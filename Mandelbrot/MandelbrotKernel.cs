// Fast Mandelbrot Rendering with GPU in C#.
// Guy Fernando - i4cy (2024)

using System.Numerics;

using ILGPU;
using ILGPU.Runtime;

namespace Mandelbrot;

public static class MandelbrotKernel
{
    public static void ComputeMandelbrotFrame(
        Index1D index, ArrayView1D<int, Stride1D.Dense> output,
        double centerX, double centerY, double scale, short width, short height)
    {
        int x = index % width;
        int y = index / width;

        // Calculate aspect ratio and scaling factors.
        double aspectRatio = (double)width / height;
        double adjustedScaleX, adjustedScaleY;
        if (aspectRatio >= 1.0)
        {
            adjustedScaleX = scale * aspectRatio;
            adjustedScaleY = scale;
        }
        else
        {
            adjustedScaleX = scale;
            adjustedScaleY = scale / aspectRatio;
        }

        // Calculate the complex coordinate.

        double real = (x * adjustedScaleX / width) - (adjustedScaleX / 2) + centerX;
        double imaginary = (y * adjustedScaleY / height) - (adjustedScaleY / 2) + centerY;

        // Perform Mandelbrot iteration.
        double zx = 0.0, zy = 0.0;
        int iteration = 0;
        const int maxIter = MandelbrotConstants.MaxIterations;

        while (zx * zx + zy * zy <= 4.0 && iteration < maxIter)
        {
            double temp = zx * zx - zy * zy + real;
            zy = 2.0 * zx * zy + imaginary;
            zx = temp;
            iteration++;
        }

        // Write result to output.
        output[index] = iteration;
    }
}
