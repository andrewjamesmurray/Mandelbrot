using ILGPU;
using ILGPU.Algorithms;

namespace Mandelbrot;

public static class MandelbrotKernel
{
    const uint BulbColor = 0xFF000000;
    const double PeriodicityLimit = 1e-24;
    const float log2 = 0.30102999566f;

    private static int Mandelbrot(double cr, double ci, int maxIter)
    {
        // Period-2 bulb check
        var ci2 = ci * ci;
        var crp = cr + 1;
        if (crp * crp + ci2 < 0.0625) // 1/16
            return maxIter;

        // Main cardioid bulb check
        var crm = cr - 0.25;
        var q = crm * crm + ci2;
        if (q * (q + crm) < 0.25 * ci2)
            return maxIter;

        double zr = 0.0, zi = 0.0;
        double prevZr = 0.0, prevZi = 0.0;
        int period = 0;
        int iter = 0;

        while (iter < maxIter)
        {
            var zr2 = zr * zr;
            var zi2 = zi * zi;

            if ((zr2 + zi2) > 4.0) 
                break;

            var temp = zr2 - zi2 + cr;
            zi = 2.0 * zr * zi + ci;
            zr = temp;

            iter++;

            // Periodicity Checking optimization
            if (++period > 20)
            {
                if (Math.Abs(zr - prevZr) < PeriodicityLimit && Math.Abs(zi - prevZi) < PeriodicityLimit)
                    return maxIter;

                prevZr = zr;
                prevZi = zi;

                period = 0;
            }
        }

        return iter;
    }

    public static void ComputeMandelbrotFrame(Index1D index, MandelbrotParameters parameters)
    {
        int x = index % parameters.Width;
        int y = index / parameters.Width;

        // Calculate the complex coordinate.
        var cr = (x * parameters.AdjustedScaleXPerPixel) + parameters.OffsetX;
        var ci = (y * parameters.AdjustedScaleYPerPixel) + parameters.OffsetY;

        var maxIter = parameters.maxIter;

        var iterations = Mandelbrot(cr, ci, maxIter);

        uint color;
        if (iterations < maxIter)
        {
            float mag = x * x + y * y;
            float log_zn = XMath.Log(mag) / 2f;
            float nu = XMath.Log(log_zn / log2) / log2;
            var smooth = iterations + 1 - nu;
            var normalized = (byte)(smooth / maxIter * Palette.NumShades);

            color = parameters.Palette[normalized];
        }
        else
        {
            color = BulbColor;
        }

        parameters.Output[index] = color;
    }
}
