using ILGPU;

namespace Mandelbrot;

public static class MandelbrotKernel
{
    public const uint BulbColor = 0xFF000000;

    private static int Mandelbrot(double cr, double ci, int maxIter)
    {
        // Pre-iteration Bulb Checking
        if ((cr + 1) * (cr + 1) + ci * ci < 1.0 / 16.0)
            return maxIter;

        // Main cardioid
        double q = (cr - 0.25) * (cr - 0.25) + ci * ci;
        if (q * (q + (cr - 0.25)) < 0.25 * ci * ci)
            return maxIter;

        double zr = 0.0, zi = 0.0;
        int iterations = 0, period = 0;
        double prevZr = 0, prevZi = 0;

        while (iterations < maxIter)
        {
            var zr2 = zr * zr;
            var zi2 = zi * zi;

            if ((zr2 + zi2) > 4.0) break;

            double temp = zr2 - zi2 + cr;
            zi = 2.0 * zr * zi + ci;
            zr = temp;

            iterations++;

            // Periodicity Checking optimization
            if (++period > 10)
            {
                if (Math.Abs(zr - prevZr) < (double)1e-24 && Math.Abs(zi - prevZi) < (double)1e-24)
                {
                    return maxIter;                 
                }

                prevZr = zr;
                prevZi = zi;

                period = 0;
            }
        }

        return iterations;
    }

    public static void ComputeMandelbrotFrame(Index1D index, MandelbrotParameters parameters)
    {
        int x = index % parameters.Width;
        int y = index / parameters.Width;

        // Calculate the complex coordinate.
        double cr = (x * parameters.AdjustedScaleXPerPixel) - parameters.HalfAdjustedScaleX + parameters.CenterX;
        double ci = (y * parameters.AdjustedScaleYPerPixel) - parameters.HalfAdjustedScaleY + parameters.CenterY;

        const int maxIter = MandelbrotConstants.MaxIterations;

        var iterations = Mandelbrot(cr, ci, maxIter);

        uint color = iterations < maxIter ? parameters.Gradient[iterations] : BulbColor;

        parameters.Output[index] = color;
    }
}
