using ILGPU;

namespace Mandelbrot;

public static class MandelbrotKernel
{
    public static void ComputeMandelbrotFrame(Index1D index, MandelbrotParameters parameters)
    {
        int x = index % parameters.Width;
        int y = index / parameters.Width;

        // Calculate the complex coordinate.
        double real = (x * parameters.AdjustedScaleXPerPixel) - parameters.HalfAdjustedScaleX + parameters.CenterX;
        double imaginary = (y * parameters.AdjustedScaleYPerPixel) - parameters.HalfAdjustedScaleY + parameters.CenterY;

        const int maxIter = MandelbrotConstants.MaxIterations;

        double zx = 0.0, zy = 0.0;
        int iteration = 0;

        while (iteration < maxIter)
        {
            var zx2 = zx * zx;
            var zy2 = zy * zy;

            if ((zx2 + zy2) > 4.0) break;

            double temp = zx2 - zy2 + real;
            zy = 2.0 * zx * zy + imaginary; 
            zx = temp;

            iteration++;
        }

        uint color = (iteration >= maxIter) ? 0 : parameters.Gradient[iteration];

        parameters.Output[index] = color;
    }
}
