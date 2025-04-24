namespace Mandelbrot;

public sealed class CpuAdapter : IComputeAdapter
{
    private readonly uint[] _palette;

    public CpuAdapter(uint[] palette)
    {
        _palette = palette;
    }

    public void Dispose()
    {
    }

    public void Render(MandelbrotParameters parameters, uint[] outputBuffer)
    {
        var width = parameters.Width;
        var height = parameters.Height;
        var maxIter = parameters.MaxIter;
        var optimizations = parameters.Optimizations;

        Parallel.For(0, height, y => 
        {
            var rowOffset = y * width;
            for (var x = 0; x < width; x++)
            {
                // Calculate the complex coordinate.
                var cr = (x * parameters.AdjustedScaleXPerPixel) + parameters.OffsetX;
                var ci = (y * parameters.AdjustedScaleYPerPixel) + parameters.OffsetY;

                var iterations = MandelbrotKernel.Mandelbrot(cr, ci, maxIter, optimizations);

                uint color;
                if (iterations < maxIter)
                {
                    var normalized = (byte)((float)iterations / maxIter * Palette.NumShades);
                    color = _palette[normalized];
                }
                else
                {
                    color = MandelbrotKernel.BulbColor;
                }

                outputBuffer[rowOffset + x] = color;
            }
        });
    }

    public static CpuAdapter Create(short width, short height, uint[] palette)
    {
        return new CpuAdapter(palette);
    }
}