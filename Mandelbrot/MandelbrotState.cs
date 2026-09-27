namespace Mandelbrot;

public class MandelbrotState
{
    const double ZoomFactorIncrement = 0.95;

    /// <summary>
    /// Perturbation keeps working until the square of a per-pixel delta stops being a
    /// normal double, which is far past where a double centre coordinate gave out.
    /// </summary>
    const double ZoomLimit = 1e-150;

    /// <summary>
    /// Where a double coordinate stops resolving a pixel. Above this, straight iteration
    /// is both exact and the cheapest thing to do; below it, perturbation takes over.
    /// Measured against pixels iterated at arbitrary precision: direct fp64 is exact to
    /// 1e-12, 67% wrong at 1e-14 and 100% wrong at 1e-20.
    /// </summary>
    const double DirectDoubleLimit = 1e-13;

    /// <summary>Precision the centre is always carried at, so no zoom path can run short of digits.</summary>
    static readonly int CenterBits = HpReal.BitsForScale(ZoomLimit);

    const int MinIter = 350;
    const int MaxIterCeiling = 1_000_000;
    const int IterStep = 50;

    private readonly int _width;
    private readonly int _height;
    private readonly double _aspectRatio;
    private readonly ReferenceOrbit _orbit = new();

    private HpComplex _center;
    private double _adjustedScaleX;
    private double _adjustedScaleY;
    private double _scale;
    private int _maxIter;

    public bool UseBulbCheckOptimization { get; private set; } = true;
    public bool UsePeriodicityOptimization { get; private set; } = true;
    public bool UseSeriesApproximation { get; private set; } = true;

    public double Scale => _scale;
    public int MaxIter => _maxIter;
    public HpComplex Center => _center;
    public double CenterX => _center.Real.ToDouble();
    public double CenterY => _center.Imaginary.ToDouble();

    /// <summary>Iterations the series approximation skipped on the last prepared frame.</summary>
    public int SkippedIterations => _orbit.SkipIterations;

    /// <summary>Set to force one kernel regardless of depth; null follows the zoom.</summary>
    public RenderMode? ForcedMode { get; set; }

    /// <summary>
    /// Straight iteration while a double still resolves a pixel, then perturbation.
    ///
    /// The fp32 kernels are deliberately not on this ladder. A consumer GPU runs fp64 at
    /// a fraction of the fp32 rate, so they are far faster, but measured against exact
    /// arbitrary-precision pixels they are wrong wherever that speed would matter:
    /// direct fp32 is already 1% wrong at scale 1e-2 and 34% at 1e-4, and fp32
    /// perturbation holds up only while the iteration cap stays low - exact at maxIter
    /// 2000, 13% wrong at 10000, because the error compounds between rebases. They stay
    /// reachable through <see cref="ForcedMode"/> for experimenting.
    /// </summary>
    public RenderMode Mode =>
        ForcedMode ?? (_scale > DirectDoubleLimit ? RenderMode.DirectDouble : RenderMode.PerturbDouble);

    public int Optimizations
    {
        get
        {
            var flags = 0;
            if (UsePeriodicityOptimization) flags |= MandelbrotParameters.PeriodicityOptimizationFlag;
            if (UseBulbCheckOptimization) flags |= MandelbrotParameters.BulbCheckOptimizationFlag;
            return flags;
        }
    }

    public MandelbrotState(int width, int height)
    {
        _width = width;
        _height = height;
        _aspectRatio = (double)width / height;

        Reset();
    }

    private void SetCenter(double x, double y) => _center = HpComplex.FromDouble(x, y, CenterBits);

    /// <summary>
    /// Jump straight to a view. Used by the points of interest below and by the tests;
    /// called at most once per user action, never from a render loop.
    /// </summary>
    public void SetView(HpComplex center, double scale, int? maxIter = null)
    {
        SetScale(scale);
        _center = center.WithFractionBits(CenterBits);

        if (maxIter is not null)
            _maxIter = Math.Clamp(maxIter.Value, 1, MaxIterCeiling);
    }

    /// <summary>Parses a centre from decimal literals and jumps to it.</summary>
    public void SetView(string centerX, string centerY, double scale, int? maxIter = null) =>
        SetView(HpComplex.Parse(centerX, centerY, CenterBits), scale, maxIter);

    private void SetScale(double newValue)
    {
        _scale = newValue;

        if (_aspectRatio >= 1.0)
        {
            _adjustedScaleX = _scale * _aspectRatio;
            _adjustedScaleY = _scale;
        }
        else
        {
            _adjustedScaleX = _scale;
            _adjustedScaleY = _scale / _aspectRatio;
        }

        _maxIter = ComputeMaxIter(_scale);
    }

    public void Reset()
    {
        SetScale(2.5);
        SetCenter(-0.74, 0.15);
    }

    /// <summary>The working precision a centre is carried at, in fractional bits.</summary>
    public static int CenterPrecisionBits => CenterBits;

    /// <summary>
    /// Reset to a point of interest. Centres are parsed from decimal strings rather than
    /// double literals: to zoom to 1e-N around a point you have to know the point to at
    /// least N digits, and a double runs out at 16.
    /// </summary>
    public void ResetForZoom()
    {
        // BUTT
        SetView(
            "0.2652335718086577500000000000000000000000",
            "0.0030554807403595630000000000000000000000",
            scale: 10);

        // INNER MANDELBROT
        //_centerX = -1.0401309460202168;
        //_centerY = 0.3487766281539035;

        // ORIGINAL
        // _centerX = -1.2535516388693015;
        // _centerY = 0.37899272530660111;

        // SPIRALS
        //_centerX = -0.7440082435282657;
        //_centerY = 0.1481642578992491;

        // REPEATED HOLES
        //_centerX = -0.6918522229482166;
        //_centerY = 0.27323155495824253;

        // DENDRIDE THINGIE
        //_centerX = -1.1719083849552117;
        //_centerY = 0.18669829447747052;

        // STARFISH AREA
        //_centerX = -0.2262667169347128;
        //_centerY = -1.1161743860818043;

        // VERY FAST RANDOM HOLE
        //_centerX = -1.4208192303359084;
        //_centerY = -1.1046267101656378E-06;

        // recursive spirals
        //_centerX = -0.6266946049017867;
        //_centerY = 0.40126480268366255;
    }

    public void Move(double deltaX, double deltaY)
    {
        _center = new HpComplex(
            _center.Real - HpReal.FromDouble(deltaX / _width * _scale, CenterBits),
            _center.Imaginary - HpReal.FromDouble(deltaY / _height * _scale, CenterBits));
    }

    /// <summary>
    /// Dynamically set maxIter based on zoom depth to improve performance at low zooms
    /// </summary>
    private static int ComputeMaxIter(double scale)
    {
        // Logarithmic boost keeps growth manageable at deep zooms
        var divisor = Math.Max(scale, ZoomLimit); // Smaller numbers could generate a negative number
        double zoom = 1.0 / divisor;

        // Instead of log(zoom), use log(max(zoom, 1)) so log never goes negative
        double safeLog = Math.Log10(Math.Max(1.0, zoom));

        // Grow iteration count smoothly with zoom depth
        var iterations = Math.Max(MinIter, 200 * Math.Pow(safeLog, 1.5));
        return (int)Math.Min(iterations, MaxIterCeiling);
    }

    public bool ZoomNext()
    {
        if (_scale < ZoomLimit)
            return false;

        SetScale(_scale * ZoomFactorIncrement);

        return true;
    }

    public void ZoomAndMove(int zoomDelta, double positionX, double positionY)
    {
        // Normalize mouse position to [-1, 1] range in the complex plane.
        double normX = (positionX / _width - 0.5) * _scale;
        double normY = (positionY / _height - 0.5) * _scale;

        // Adjust scale based on the scroll direction
        var scaleMultiplier = zoomDelta > 0 ? ZoomFactorIncrement : 1 - (ZoomFactorIncrement - 1);

        SetScale(_scale * scaleMultiplier);

        // Adjust the center point based on the normalized mouse position.
        var normMultiplier = (1 - _scale / (_scale * scaleMultiplier));
        _center = new HpComplex(
            _center.Real - HpReal.FromDouble(normX * normMultiplier, CenterBits),
            _center.Imaginary - HpReal.FromDouble(normY * normMultiplier, CenterBits));
    }

    public void IncreaseMaxIter() => _maxIter = Math.Min(_maxIter + IterStep, MaxIterCeiling);

    public void DecreaseMaxIter() => _maxIter = Math.Max(_maxIter - IterStep, IterStep);

    public void TogglePeriodicityOptimization() => UsePeriodicityOptimization = !UsePeriodicityOptimization;

    public void ToggleBulbCheckOptimization() => UseBulbCheckOptimization = !UseBulbCheckOptimization;

    public void ToggleSeriesApproximation() => UseSeriesApproximation = !UseSeriesApproximation;

    /// <summary>Full-precision centre, for pasting back into a point-of-interest list.</summary>
    public string DescribeCenter()
    {
        var digits = (int)Math.Max(20, Math.Log10(1.0 / Math.Max(_scale, ZoomLimit)) + 8);
        return $"\"{_center.Real.ToDecimalString(digits)}\",\n\"{_center.Imaginary.ToDecimalString(digits)}\"\n";
    }

    /// <summary>
    /// Builds the kernel parameters for the next frame and, in the perturbation modes,
    /// brings the reference orbit up to date. The orbit is cached across frames: while
    /// auto-zooming the centre never moves, so it is only ever extended.
    /// </summary>
    public (MandelbrotParameters Parameters, ReferenceOrbit? Orbit) PrepareFrame()
    {
        var deltaX = _adjustedScaleX / _width;
        var deltaY = _adjustedScaleY / _height;
        var originX = -(_adjustedScaleX / 2);
        var originY = -(_adjustedScaleY / 2);

        var parameters = new MandelbrotParameters
        {
            Width = _width,
            Height = _height,
            MaxIter = _maxIter,
            DeltaX = deltaX,
            DeltaY = deltaY,
            OffsetX = originX + CenterX,
            OffsetY = originY + CenterY,
            PerturbOriginX = originX,
            PerturbOriginY = originY,
            PaletteScale = (float)Palette.NumShades / _maxIter,
            InvRadius = 1.0
        };

        var mode = Mode;
        if (mode is not (RenderMode.PerturbFloat or RenderMode.PerturbDouble))
            return (parameters, null);

        var cornerRadius = Math.Sqrt(originX * originX + originY * originY);
        _orbit.Update(_center, _scale, _maxIter, cornerRadius, deltaX, UseSeriesApproximation);

        parameters.RefLength = _orbit.Length;
        parameters.SkipIterations = _orbit.SkipIterations;
        parameters.InvRadius = 1.0 / _orbit.Radius;
        parameters.SeriesAr = _orbit.SeriesA.Real;
        parameters.SeriesAi = _orbit.SeriesA.Imaginary;
        parameters.SeriesBr = _orbit.SeriesB.Real;
        parameters.SeriesBi = _orbit.SeriesB.Imaginary;
        parameters.SeriesCr = _orbit.SeriesC.Real;
        parameters.SeriesCi = _orbit.SeriesC.Imaginary;

        return (parameters, _orbit);
    }
}
