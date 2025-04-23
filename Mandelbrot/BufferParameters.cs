using ILGPU;
using ILGPU.Runtime;

namespace Mandelbrot;

public readonly struct BufferParameters
{
    public ArrayView1D<uint, Stride1D.Dense> Output { get; init; }
    public ArrayView1D<uint, Stride1D.Dense> Palette { get; init; }

    public BufferParameters(ArrayView1D<uint, Stride1D.Dense> output, ArrayView1D<uint, Stride1D.Dense> palette)
    {
        Output = output;
        Palette = palette;
    }
}
