using ILGPU;

namespace Mandelbrot;

public static class MandelbrotKernel
{
    public const uint DefaultColor = 0xFF000000;

    public static void ComputeMandelbrotFrame(Index1D index, MandelbrotParameters parameters)
    {
        int x = index % parameters.Width;
        int y = index / parameters.Width;

        // Calculate the complex coordinate.
        double cr = (x * parameters.AdjustedScaleXPerPixel) - parameters.HalfAdjustedScaleX + parameters.CenterX;
        double ci = (y * parameters.AdjustedScaleYPerPixel) - parameters.HalfAdjustedScaleY + parameters.CenterY;

        const int maxIter = MandelbrotConstants.MaxIterations;

        double zr = 0.0, zi = 0.0;
        int iteration = 0, period = 0;
        double prevZr = 0, prevZi = 0;

        while (iteration < maxIter)
        {
            var zr2 = zr * zr;
            var zi2 = zi * zi;

            if ((zr2 + zi2) > 4.0) break;

            double temp = zr2 - zi2 + cr;
            zi = 2.0 * zr * zi + ci; 
            zr = temp;

            iteration++;

            if (++period > 10)
            {
                if (Math.Abs(zr - prevZr) < (double)1e-24 && Math.Abs(zi - prevZi) < (double)1e-24)
                {
                    iteration = maxIter;
                    break;
                }

                prevZr = zr;
                prevZi = zi;

                period = 0;
            }
        }

        uint color = (iteration >= maxIter) ? DefaultColor : parameters.Gradient[iteration];

        parameters.Output[index] = color;
    }
}
