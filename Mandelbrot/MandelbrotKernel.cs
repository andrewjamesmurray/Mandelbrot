using ILGPU;
using ILGPU.Runtime;

namespace Mandelbrot;

public static class MandelbrotKernel
{
    public static void ComputeMandelbrotFrame(Index1D index, MandelbrotParameters parameters)
    {
        int x = index % parameters.Width;
        int y = index / parameters.Width;

        // Calculate aspect ratio and scaling factors.
        double aspectRatio = (double)parameters.Width / parameters.Height;
        double adjustedScaleX, adjustedScaleY;
        if (aspectRatio >= 1.0)
        {
            adjustedScaleX = parameters.Scale * aspectRatio;
            adjustedScaleY = parameters.Scale;
        }
        else
        {
            adjustedScaleX = parameters.Scale;
            adjustedScaleY = parameters.Scale / aspectRatio;
        }

        // Calculate the complex coordinate.
        double real = (x * adjustedScaleX / parameters.Width) - (adjustedScaleX / 2) + parameters.CenterX;
        double imaginary = (y * adjustedScaleY / parameters.Height) - (adjustedScaleY / 2) + parameters.CenterY;

        // Perform Mandelbrot iteration.
        double zx = 0.0, zy = 0.0;
        int iteration = 0;

        const int maxIter = MandelbrotConstants.MaxIterations;

        while (iteration < maxIter && (zx * zx + zy * zy) <= 4.0)
        {
            double temp = zx * zx - zy * zy + real;
            zy = 2.0 * zx * zy + imaginary;
            zx = temp;
            iteration++;
        }

        uint color = (iteration >= maxIter) ? 0 : parameters.Gradient[iteration];

        parameters.Output[index] = color;
    }
}
