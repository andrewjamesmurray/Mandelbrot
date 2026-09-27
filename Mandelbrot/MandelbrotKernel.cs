using System.Runtime.CompilerServices;
using ILGPU;
using ILGPU.Runtime;

namespace Mandelbrot;

public static class MandelbrotKernel
{
    public const uint BulbColor = 0xFF000000;
    public const double PeriodicityLimit = 1e-24;
    public const float PeriodicityLimitFloat = 1e-12f;
    public const int PeriodicityInterval = 20;
    public const double EscapeRadiusSquared = 4.0;

    // ---------------------------------------------------------------- direct iteration

    /// <summary>
    /// Straight iteration. <paramref name="periodicity"/> and <paramref name="bulbCheck"/>
    /// are compile-time constants at every call site (a SpecializedValue on the GPU, a
    /// literal on the CPU), so neither costs a branch inside the loop.
    ///
    /// The squares of z are carried across iterations rather than recomputed at the top
    /// of each pass, and the periodicity test is hoisted to the chunk boundary it was
    /// already implicitly running on.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IterateDouble(double cr, double ci, int maxIter, bool periodicity, bool bulbCheck)
    {
        if (bulbCheck && InCardioidOrBulb(cr, ci))
            return maxIter;

        double zr = 0.0, zi = 0.0, zr2 = 0.0, zi2 = 0.0;
        double prevZr = 0.0, prevZi = 0.0;
        var iter = 0;

        while (iter < maxIter)
        {
            var chunkEnd = maxIter;
            if (periodicity)
            {
                var limit = iter + PeriodicityInterval;
                chunkEnd = limit < maxIter ? limit : maxIter;
            }

            while (iter < chunkEnd)
            {
                zi = 2.0 * zr * zi + ci;
                zr = zr2 - zi2 + cr;
                zr2 = zr * zr;
                zi2 = zi * zi;
                iter++;

                if (zr2 + zi2 > EscapeRadiusSquared)
                    return iter;
            }

            if (periodicity)
            {
                if (Math.Abs(zr - prevZr) < PeriodicityLimit && Math.Abs(zi - prevZi) < PeriodicityLimit)
                    return maxIter;

                prevZr = zr;
                prevZi = zi;
            }
        }

        return maxIter;
    }

    /// <summary>
    /// Float twin of <see cref="IterateDouble"/>, for zoom levels where a pixel is still
    /// far wider than a float ULP. Consumer GPUs run fp64 at a fraction of the fp32 rate,
    /// so this is worth a great deal more than the 2x in width suggests.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IterateFloat(float cr, float ci, int maxIter, bool periodicity, bool bulbCheck)
    {
        if (bulbCheck)
        {
            var ciSquared = ci * ci;

            var shifted = cr + 1.0f;
            if (shifted * shifted + ciSquared < 0.0625f)
                return maxIter;

            var offset = cr - 0.25f;
            var q = offset * offset + ciSquared;
            if (q * (q + offset) < 0.25f * ciSquared)
                return maxIter;
        }

        float zr = 0.0f, zi = 0.0f, zr2 = 0.0f, zi2 = 0.0f;
        float prevZr = 0.0f, prevZi = 0.0f;
        var iter = 0;

        while (iter < maxIter)
        {
            var chunkEnd = maxIter;
            if (periodicity)
            {
                var limit = iter + PeriodicityInterval;
                chunkEnd = limit < maxIter ? limit : maxIter;
            }

            while (iter < chunkEnd)
            {
                zi = 2.0f * zr * zi + ci;
                zr = zr2 - zi2 + cr;
                zr2 = zr * zr;
                zi2 = zi * zi;
                iter++;

                if (zr2 + zi2 > 4.0f)
                    return iter;
            }

            if (periodicity)
            {
                if (MathF.Abs(zr - prevZr) < PeriodicityLimitFloat && MathF.Abs(zi - prevZi) < PeriodicityLimitFloat)
                    return maxIter;

                prevZr = zr;
                prevZi = zi;
            }
        }

        return maxIter;
    }

    /// <summary>Main cardioid and period-2 bulb, the two regions worth rejecting analytically.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InCardioidOrBulb(double cr, double ci)
    {
        var ciSquared = ci * ci;

        var shifted = cr + 1.0;
        if (shifted * shifted + ciSquared < 0.0625) // 1/16
            return true;

        var offset = cr - 0.25;
        var q = offset * offset + ciSquared;
        return q * (q + offset) < 0.25 * ciSquared;
    }

    // ------------------------------------------------------------------- perturbation

    /// <summary>
    /// Iterates a pixel as an offset from a high-precision reference orbit:
    /// eps' = 2*Z*eps + eps^2 + delta, where only the reference ever needed precision
    /// beyond a double. This is what lifts the zoom ceiling.
    ///
    /// Rebasing restarts the offset against Z[0] whenever the true orbit falls below the
    /// offset or the reference runs out. That keeps the perturbation well conditioned and
    /// makes separate glitch detection unnecessary.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IteratePerturbDouble(
        double dr, double di, in MandelbrotParameters p,
        ArrayView1D<double, Stride1D.Dense> reference)
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

            if (magnitude > EscapeRadiusSquared)
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

    /// <summary>
    /// Float twin of <see cref="IteratePerturbDouble"/>. Everything the loop touches is
    /// relative to the reference, so float carries as many significant digits per pixel
    /// here as it would at zoom 1 - the depth lives in the reference orbit, not the loop.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int IteratePerturbFloat(
        float dr, float di, in MandelbrotParameters p,
        ArrayView1D<float, Stride1D.Dense> reference)
    {
        var maxIter = p.MaxIter;
        var refLength = p.RefLength;

        var n = p.SkipIterations;
        var iter = n;

        float er = 0.0f, ei = 0.0f;
        if (n > 0)
        {
            var invRadius = (float)p.InvRadius;
            var hr = dr * invRadius;
            var hi = di * invRadius;
            var h2r = hr * hr - hi * hi;
            var h2i = 2.0f * hr * hi;
            var h3r = h2r * hr - h2i * hi;
            var h3i = h2r * hi + h2i * hr;

            float ar = (float)p.SeriesAr, ai = (float)p.SeriesAi;
            float br = (float)p.SeriesBr, bi = (float)p.SeriesBi;
            float cr = (float)p.SeriesCr, ci = (float)p.SeriesCi;

            er = ar * hr - ai * hi + br * h2r - bi * h2i + cr * h3r - ci * h3i;
            ei = ar * hi + ai * hr + br * h2i + bi * h2r + cr * h3i + ci * h3r;
        }

        while (iter < maxIter)
        {
            var zr = reference[2 * n] + er;
            var zi = reference[2 * n + 1] + ei;
            var magnitude = zr * zr + zi * zi;

            if (magnitude > 4.0f)
                return iter;

            if (magnitude < er * er + ei * ei || n + 1 >= refLength)
            {
                er = zr;
                ei = zi;
                n = 0;
            }

            var refR = reference[2 * n];
            var refI = reference[2 * n + 1];

            var nextR = 2.0f * (refR * er - refI * ei) + (er * er - ei * ei) + dr;
            var nextI = 2.0f * (refR * ei + refI * er) + 2.0f * er * ei + di;

            er = nextR;
            ei = nextI;
            n++;
            iter++;
        }

        return maxIter;
    }

    // ------------------------------------------------------------------------- colour

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Colorize(int iterations, int maxIter, float paletteScale, ArrayView1D<uint, Stride1D.Dense> palette)
    {
        if (iterations >= maxIter)
            return BulbColor;

        var shade = (int)(iterations * paletteScale);
        return palette[shade < Palette.NumShades ? shade : Palette.NumShades - 1];
    }

    // ------------------------------------------------------------------------ kernels

    public static void DirectDoubleKernel(
        Index2D index, MandelbrotParameters p, BufferParameters buffers, SpecializedValue<int> optimizations)
    {
        var cr = index.X * p.DeltaX + p.OffsetX;
        var ci = index.Y * p.DeltaY + p.OffsetY;

        var iterations = IterateDouble(cr, ci, p.MaxIter,
            (optimizations.Value & MandelbrotParameters.PeriodicityOptimizationFlag) != 0,
            (optimizations.Value & MandelbrotParameters.BulbCheckOptimizationFlag) != 0);

        buffers.Output[index.Y * p.Width + index.X] = Colorize(iterations, p.MaxIter, p.PaletteScale, buffers.Palette);
    }

    public static void DirectFloatKernel(
        Index2D index, MandelbrotParameters p, BufferParameters buffers, SpecializedValue<int> optimizations)
    {
        // The coordinate is formed in double and only then narrowed, so the pixel grid
        // stays exact; only the iteration itself runs in float.
        var cr = (float)(index.X * p.DeltaX + p.OffsetX);
        var ci = (float)(index.Y * p.DeltaY + p.OffsetY);

        var iterations = IterateFloat(cr, ci, p.MaxIter,
            (optimizations.Value & MandelbrotParameters.PeriodicityOptimizationFlag) != 0,
            (optimizations.Value & MandelbrotParameters.BulbCheckOptimizationFlag) != 0);

        buffers.Output[index.Y * p.Width + index.X] = Colorize(iterations, p.MaxIter, p.PaletteScale, buffers.Palette);
    }

    public static void PerturbDoubleKernel(Index2D index, MandelbrotParameters p, BufferParameters buffers)
    {
        var dr = index.X * p.DeltaX + p.PerturbOriginX;
        var di = index.Y * p.DeltaY + p.PerturbOriginY;

        var iterations = IteratePerturbDouble(dr, di, p, buffers.ReferenceDouble);

        buffers.Output[index.Y * p.Width + index.X] = Colorize(iterations, p.MaxIter, p.PaletteScale, buffers.Palette);
    }

    public static void PerturbFloatKernel(Index2D index, MandelbrotParameters p, BufferParameters buffers)
    {
        var dr = (float)(index.X * p.DeltaX + p.PerturbOriginX);
        var di = (float)(index.Y * p.DeltaY + p.PerturbOriginY);

        var iterations = IteratePerturbFloat(dr, di, p, buffers.ReferenceFloat);

        buffers.Output[index.Y * p.Width + index.X] = Colorize(iterations, p.MaxIter, p.PaletteScale, buffers.Palette);
    }
}
