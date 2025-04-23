using ILGPU;
using ILGPU.Runtime;

namespace Mandelbrot;

public struct MandelbrotParameters
{
    public ArrayView1D<uint, Stride1D.Dense> Output { get; set; }
    public ArrayView1D<uint, Stride1D.Dense> Gradient { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Scale { get; set; }
    public short Width { get; set; }
    public short Height { get; set; }

    public double AdjustedScaleXPerPixel { get; set; }
    public double AdjustedScaleYPerPixel { get; set; }

    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public int maxIter { get; set; }
}
