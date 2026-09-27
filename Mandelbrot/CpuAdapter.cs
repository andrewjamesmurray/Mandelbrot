using System.Numerics;

namespace Mandelbrot;

/// <summary>
/// CPU fallback. Direct modes run <c>Vector&lt;double&gt;</c> lanes wide - the widest the
/// hardware offers, so AVX-512 gets 8 pixels per step and AVX2 gets 4. Perturbation runs
/// scalar; it is the deep-zoom path and the GPU is the one that matters there.
/// </summary>
public sealed class CpuAdapter : IComputeAdapter
{
    private readonly uint[] _palette;

    public string Description => $"CPU [{Environment.ProcessorCount} threads, {Vector<double>.Count} lanes]";

    public CpuAdapter(uint[] palette)
    {
        _palette = palette;
    }

    public static CpuAdapter Create(int width, int height, uint[] palette) => new(palette);

    public void Dispose()
    {
    }

    public unsafe void Render(in MandelbrotParameters parameters, RenderMode mode, int optimizations, ReferenceOrbit? orbit, Span<uint> destination)
    {
        // Everything the row loop reads is hoisted out of the closure: reaching through
        // the captured struct once per pixel is a measurable cost in a loop this tight.
        var width = parameters.Width;
        var height = parameters.Height;
        var maxIter = parameters.MaxIter;
        var deltaX = parameters.DeltaX;
        var deltaY = parameters.DeltaY;
        var offsetX = parameters.OffsetX;
        var offsetY = parameters.OffsetY;
        var perturbOriginX = parameters.PerturbOriginX;
        var perturbOriginY = parameters.PerturbOriginY;
        var paletteScale = parameters.PaletteScale;
        var periodicity = (optimizations & MandelbrotParameters.PeriodicityOptimizationFlag) != 0;
        var bulbCheck = (optimizations & MandelbrotParameters.BulbCheckOptimizationFlag) != 0;

        var perturbing = mode is RenderMode.PerturbDouble or RenderMode.PerturbFloat;
        var reference = orbit?.PointsDouble ?? [];
        var snapshot = parameters;

        fixed (uint* outputPin = destination)
        fixed (uint* palettePin = _palette)
        fixed (double* referencePin = reference)
        {
            // Pointers cannot be captured by a lambda, so they cross into the row body
            // as native ints.
            var output = (nint)outputPin;
            var palette = (nint)palettePin;
            var points = (nint)referencePin;

            Parallel.For(0, height, y =>
            {
                var row = (uint*)output + (long)y * width;
                var shades = (uint*)palette;

                if (perturbing)
                {
                    var di = y * deltaY + perturbOriginY;
                    for (var x = 0; x < width; x++)
                    {
                        var iterations = PerturbScalar(x * deltaX + perturbOriginX, di, snapshot, (double*)points);
                        row[x] = Shade(iterations, maxIter, paletteScale, shades);
                    }

                    return;
                }

                var ci = y * deltaY + offsetY;
                RenderRowVectorized(row, width, offsetX, deltaX, ci, maxIter, periodicity, bulbCheck, paletteScale, shades);
            });
        }
    }

    private static unsafe uint Shade(int iterations, int maxIter, float paletteScale, uint* palette)
    {
        if (iterations >= maxIter)
            return MandelbrotKernel.BulbColor;

        var index = (int)(iterations * paletteScale);
        return palette[index < Palette.NumShades ? index : Palette.NumShades - 1];
    }

    /// <summary>
    /// One scanline, <c>Vector&lt;double&gt;.Count</c> pixels at a time. Lanes retire
    /// independently; the row is done when every lane has either escaped or been proved
    /// interior.
    /// </summary>
    private static unsafe void RenderRowVectorized(
        uint* row, int width, double offsetX, double deltaX, double ci,
        int maxIter, bool periodicity, bool bulbCheck, float paletteScale, uint* palette)
    {
        var lanes = Vector<double>.Count;

        var two = new Vector<double>(2.0);
        var four = new Vector<double>(4.0);
        var one = Vector<double>.One;
        var maxIterVector = new Vector<double>((double)maxIter);
        var limit = new Vector<double>(MandelbrotKernel.PeriodicityLimit);
        var vci = new Vector<double>(ci);

        Span<double> coordinates = stackalloc double[lanes];
        Span<double> results = stackalloc double[lanes];

        for (var x0 = 0; x0 < width; x0 += lanes)
        {
            for (var lane = 0; lane < lanes; lane++)
            {
                var x = x0 + lane < width ? x0 + lane : width - 1;
                coordinates[lane] = x * deltaX + offsetX;
            }

            var vcr = new Vector<double>((ReadOnlySpan<double>)coordinates);

            var interior = Vector<long>.Zero;
            if (bulbCheck)
            {
                var ciSquared = vci * vci;

                var shifted = vcr + one;
                interior = Vector.LessThan(shifted * shifted + ciSquared, new Vector<double>(0.0625));

                var offset = vcr - new Vector<double>(0.25);
                var q = offset * offset + ciSquared;
                interior |= Vector.LessThan(q * (q + offset), new Vector<double>(0.25) * ciSquared);
            }

            Vector<double> zr = Vector<double>.Zero, zi = Vector<double>.Zero;
            Vector<double> zr2 = Vector<double>.Zero, zi2 = Vector<double>.Zero;
            Vector<double> prevZr = Vector<double>.Zero, prevZi = Vector<double>.Zero;
            var counts = Vector<double>.Zero;
            var active = ~interior;
            var untilPeriodicityCheck = MandelbrotKernel.PeriodicityInterval;

            for (var iter = 0; iter < maxIter; iter++)
            {
                active &= ~Vector.GreaterThan(zr2 + zi2, four);
                if (Vector.EqualsAll(active, Vector<long>.Zero))
                    break;

                counts += Vector.ConditionalSelect(active, one, Vector<double>.Zero);

                zi = two * zr * zi + vci;
                zr = zr2 - zi2 + vcr;
                zr2 = zr * zr;
                zi2 = zi * zi;

                if (periodicity && --untilPeriodicityCheck == 0)
                {
                    untilPeriodicityCheck = MandelbrotKernel.PeriodicityInterval;

                    var settled = Vector.LessThan(Vector.Abs(zr - prevZr), limit) &
                                  Vector.LessThan(Vector.Abs(zi - prevZi), limit);

                    counts = Vector.ConditionalSelect(settled & active, maxIterVector, counts);
                    active &= ~settled;

                    prevZr = zr;
                    prevZi = zi;
                }
            }

            Vector.ConditionalSelect(interior, maxIterVector, counts).CopyTo(results);

            var span = x0 + lanes <= width ? lanes : width - x0;
            for (var lane = 0; lane < span; lane++)
                row[x0 + lane] = Shade((int)results[lane], maxIter, paletteScale, palette);
        }
    }

    private static unsafe int PerturbScalar(double dr, double di, in MandelbrotParameters p, double* reference)
    {
        var maxIter = p.MaxIter;
        var refLength = p.RefLength;

        var n = p.SkipIterations;
        var iter = n;

        double er = 0.0, ei = 0.0;
        if (n > 0)
        {
            var hr = dr * p.InvRadius;
            var hi = di * p.InvRadius;
            var h2r = hr * hr - hi * hi;
            var h2i = 2.0 * hr * hi;
            var h3r = h2r * hr - h2i * hi;
            var h3i = h2r * hi + h2i * hr;

            er = p.SeriesAr * hr - p.SeriesAi * hi + p.SeriesBr * h2r - p.SeriesBi * h2i + p.SeriesCr * h3r - p.SeriesCi * h3i;
            ei = p.SeriesAr * hi + p.SeriesAi * hr + p.SeriesBr * h2i + p.SeriesBi * h2r + p.SeriesCr * h3i + p.SeriesCi * h3r;
        }

        while (iter < maxIter)
        {
            var zr = reference[2 * n] + er;
            var zi = reference[2 * n + 1] + ei;
            var magnitude = zr * zr + zi * zi;

            if (magnitude > MandelbrotKernel.EscapeRadiusSquared)
                return iter;

            if (magnitude < er * er + ei * ei || n + 1 >= refLength)
            {
                er = zr;
                ei = zi;
                n = 0;
            }

            var refR = reference[2 * n];
            var refI = reference[2 * n + 1];

            var nextR = 2.0 * (refR * er - refI * ei) + (er * er - ei * ei) + dr;
            var nextI = 2.0 * (refR * ei + refI * er) + 2.0 * er * ei + di;

            er = nextR;
            ei = nextI;
            n++;
            iter++;
        }

        return maxIter;
    }
}
