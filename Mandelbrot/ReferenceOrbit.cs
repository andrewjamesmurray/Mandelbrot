using System.Numerics;

namespace Mandelbrot;

/// <summary>
/// The high-precision orbit of a single reference point, plus the series-approximation
/// coefficients derived from it.
///
/// Perturbation rendering iterates every pixel as a small offset from this one orbit:
/// only the reference needs precision beyond a double, so the per-pixel work stays in
/// hardware float/double and the zoom depth stops being bounded by the mantissa.
///
/// The orbit values themselves are stored as plain float/double because they are O(1);
/// what needs the precision is <em>computing</em> them without accumulating error.
/// </summary>
public sealed class ReferenceOrbit
{
    /// <summary>How far past the last provably-accurate term to pull the series skip back.</summary>
    private const int SeriesSafetyMargin = 8;

    /// <summary>Series error budget, as a fraction of one pixel.</summary>
    private const double SeriesPixelTolerance = 1e-4;

    /// <summary>Precision is raised in chunks so that zooming does not force a rebuild every few frames.</summary>
    private const int PrecisionQuantum = 128;

    private static int _nextVersion = 1;

    private HpComplex _requestedCenter;
    private HpComplex _center;
    private HpComplex _z;
    private int _fractionBits;
    private bool _escaped;
    private int _computed;

    private double[] _pointsDouble = [];
    private float[] _pointsFloat = [];

    /// <summary>Interleaved (re, im) orbit points, for the double-precision kernel.</summary>
    public double[] PointsDouble => _pointsDouble;

    /// <summary>The same points narrowed to float, for the float kernel.</summary>
    public float[] PointsFloat => _pointsFloat;

    /// <summary>Number of valid orbit points, Z[0] .. Z[Length - 1].</summary>
    public int Length { get; private set; }

    /// <summary>Changes whenever the point data changes, so uploads can be skipped.</summary>
    public int Version { get; private set; }

    /// <summary>Iterations the series approximation lets every pixel skip.</summary>
    public int SkipIterations { get; private set; }

    public Complex SeriesA { get; private set; }
    public Complex SeriesB { get; private set; }
    public Complex SeriesC { get; private set; }

    /// <summary>|delta| at the corner of the view: the worst case the series must hold for.</summary>
    public double Radius { get; private set; } = 1.0;

    /// <summary>
    /// True when the cached orbit was built for this centre at sufficient precision.
    /// Compares against the centre as handed in, not the working copy, which has been
    /// narrowed to the orbit's own precision.
    /// </summary>
    public bool Matches(HpComplex center, int fractionBits) =>
        _fractionBits >= fractionBits &&
        _computed > 0 &&
        (_requestedCenter.Real - center.Real).IsZero &&
        (_requestedCenter.Imaginary - center.Imaginary).IsZero;

    /// <summary>
    /// Brings the orbit up to <paramref name="maxIter"/> points for the given centre,
    /// rebuilding from scratch only when the centre moved or the precision is no longer
    /// sufficient, and recomputing the (cheap, scale-dependent) series coefficients.
    /// </summary>
    public void Update(HpComplex center, double scale, int maxIter, double cornerRadius, double pixelSpacing, bool useSeries)
    {
        var bits = RoundUpPrecision(HpReal.BitsForScale(scale));

        if (!Matches(center, bits))
        {
            _fractionBits = Math.Max(bits, _fractionBits);
            _requestedCenter = center;
            _center = center.WithFractionBits(_fractionBits);
            _z = HpComplex.FromDouble(0.0, 0.0, _fractionBits);
            _escaped = false;
            _computed = 0;
            Length = 0;
        }

        Extend(maxIter + 1);
        ComputeSeries(cornerRadius, pixelSpacing, useSeries);
    }

    private static int RoundUpPrecision(int bits) =>
        (bits + PrecisionQuantum - 1) / PrecisionQuantum * PrecisionQuantum;

    private void Extend(int wanted)
    {
        if (_escaped || _computed >= wanted)
        {
            Length = _computed;
            return;
        }

        EnsureCapacity(wanted);

        var four = HpReal.FromDouble(4.0, _fractionBits);
        var zr = _z.Real;
        var zi = _z.Imaginary;

        var n = _computed;
        for (; n < wanted; n++)
        {
            var zrSquared = zr * zr;
            var ziSquared = zi * zi;

            var re = zr.ToDouble();
            var im = zi.ToDouble();
            _pointsDouble[2 * n] = re;
            _pointsDouble[2 * n + 1] = im;
            _pointsFloat[2 * n] = (float)re;
            _pointsFloat[2 * n + 1] = (float)im;

            if (zrSquared + ziSquared > four)
            {
                _escaped = true;
                n++;
                break;
            }

            // Z <- Z^2 + c
            var cross = zr * zi;
            var nextR = zrSquared - ziSquared + _center.Real;
            var nextI = cross + cross + _center.Imaginary;
            zr = nextR;
            zi = nextI;
        }

        _z = new HpComplex(zr, zi);
        _computed = n;
        Length = n;
        Version = _nextVersion++;
    }

    private void EnsureCapacity(int points)
    {
        if (_pointsDouble.Length >= 2 * points)
            return;

        var capacity = Math.Max(2 * points, _pointsDouble.Length * 2);
        Array.Resize(ref _pointsDouble, capacity);
        Array.Resize(ref _pointsFloat, capacity);
    }

    /// <summary>
    /// Finds how many leading iterations the cubic series eps ~ A*d + B*d^2 + C*d^3 can
    /// replace, then re-runs the (double-only) recurrence to that point and pre-scales the
    /// coefficients by the corner radius so the kernel can evaluate them in float.
    /// </summary>
    private void ComputeSeries(double cornerRadius, double pixelSpacing, bool useSeries)
    {
        Radius = cornerRadius > 0.0 ? cornerRadius : 1.0;
        SkipIterations = 0;
        SeriesA = Complex.Zero;
        SeriesB = Complex.Zero;
        SeriesC = Complex.Zero;

        if (!useSeries || Length < SeriesSafetyMargin + 2)
            return;

        var radiusSquared = Radius * Radius;
        var radiusCubed = radiusSquared * Radius;
        var budget = pixelSpacing * SeriesPixelTolerance;

        Complex a = Complex.Zero, b = Complex.Zero, c = Complex.Zero;
        var valid = 0;

        for (var n = 0; n < Length - 1; n++)
        {
            // The cubic term stands in for the first omitted one: once it alone could
            // move the pixel by a measurable fraction of a pixel, the series is spent.
            if (n > 0 && (c.Magnitude * radiusCubed > budget ||
                          b.Magnitude * radiusSquared > a.Magnitude * Radius))
                break;

            Step(n, ref a, ref b, ref c);

            if (!double.IsFinite(a.Real) || !double.IsFinite(b.Real) || !double.IsFinite(c.Real))
                break;

            valid = n + 1;
        }

        var skip = valid - SeriesSafetyMargin;
        if (skip <= 0)
            return;

        a = b = c = Complex.Zero;
        for (var n = 0; n < skip; n++)
            Step(n, ref a, ref b, ref c);

        SkipIterations = skip;
        SeriesA = a * Radius;
        SeriesB = b * radiusSquared;
        SeriesC = c * radiusCubed;
    }

    /// <summary>One step of the coefficient recurrence for eps ~ A*d + B*d^2 + C*d^3.</summary>
    private void Step(int n, ref Complex a, ref Complex b, ref Complex c)
    {
        var twoZ = new Complex(2.0 * _pointsDouble[2 * n], 2.0 * _pointsDouble[2 * n + 1]);

        var nextA = twoZ * a + Complex.One;
        var nextB = twoZ * b + a * a;
        var nextC = twoZ * c + 2.0 * a * b;

        a = nextA;
        b = nextB;
        c = nextC;
    }
}
