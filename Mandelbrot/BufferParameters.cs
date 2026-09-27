using ILGPU;
using ILGPU.Runtime;

namespace Mandelbrot;

public readonly struct BufferParameters
{
    public ArrayView1D<uint, Stride1D.Dense> Output { get; init; }
    public ArrayView1D<uint, Stride1D.Dense> Palette { get; init; }

    /// <summary>Interleaved (re, im) reference orbit for the double perturbation kernel.</summary>
    public ArrayView1D<double, Stride1D.Dense> ReferenceDouble { get; init; }

    /// <summary>The same orbit narrowed to float. Only the dynamics are perturbed by the
    /// narrowing, and by a relative amount comparable to the float arithmetic itself.</summary>
    public ArrayView1D<float, Stride1D.Dense> ReferenceFloat { get; init; }

    public BufferParameters(
        ArrayView1D<uint, Stride1D.Dense> output,
        ArrayView1D<uint, Stride1D.Dense> palette,
        ArrayView1D<double, Stride1D.Dense> referenceDouble,
        ArrayView1D<float, Stride1D.Dense> referenceFloat)
    {
        Output = output;
        Palette = palette;
        ReferenceDouble = referenceDouble;
        ReferenceFloat = referenceFloat;
    }
}
