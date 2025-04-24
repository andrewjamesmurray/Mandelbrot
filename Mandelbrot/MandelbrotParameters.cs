using ILGPU;
using ILGPU.Runtime;

namespace Mandelbrot;

public struct MandelbrotParameters
{
    public const byte PeriodicityOptimizationEnum = 1;
    public const byte BulbCheckOptimizationEnum = 2;

    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Scale { get; set; }
    public short Width { get; set; }
    public short Height { get; set; }

    public double AdjustedScaleXPerPixel { get; set; }
    public double AdjustedScaleYPerPixel { get; set; }

    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public int MaxIter { get; set; }

    public byte Optimizations { get; set; }
}
