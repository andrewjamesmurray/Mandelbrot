using ILGPU;

namespace Mandelbrot;

public static class MandelbrotKernel
{
    const uint BulbColor = 0xFF000000;
    const double PeriodicityLimit = 1e-24;

    private static int Mandelbrot(double cr, double ci, int maxIter)
    {
        // Pre-iteration Bulb Checking
        double ci2 = ci * ci;
        var crp = cr + 1;
        if (crp * crp + ci2 < 1.0 / 16.0)
            return maxIter;

        // Main cardioid
        var crm = cr - 0.25;
        double q = crm * crm + ci2;
        if (q * (q + crm) < 0.25 * ci2)
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
            if (++period > 20)
            {
                if (Math.Abs(zr - prevZr) < PeriodicityLimit && Math.Abs(zi - prevZi) < PeriodicityLimit)
                    return maxIter;                 

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
