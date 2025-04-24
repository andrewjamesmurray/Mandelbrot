using ILGPU;
using ILGPU.Runtime;

namespace Mandelbrot;

public sealed class GpuAdapter : IDisposable
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    private readonly MemoryBuffer1D<uint, Stride1D.Dense> _outputBuffer;
    private readonly MemoryBuffer1D<uint, Stride1D.Dense> _paletteCache;
    private readonly Action<Index1D, MandelbrotParameters, BufferParameters> _kernel;
    private readonly Index1D _pixelIndex;
    private readonly BufferParameters _bufferParameters;

    internal GpuAdapter(
        Context context, 
        Accelerator accelerator, 
        Action<Index1D, MandelbrotParameters, BufferParameters> kernel,
        MemoryBuffer1D<uint, Stride1D.Dense> outputBuffer,
        MemoryBuffer1D<uint, Stride1D.Dense> gradientBuffer,
        short width,
        short height)
    {
        _context = context;
        _accelerator = accelerator;
        _kernel = kernel;
        _outputBuffer = outputBuffer;
        _paletteCache = gradientBuffer;

        _pixelIndex = width * height;
        _bufferParameters = new BufferParameters(_outputBuffer.View, _paletteCache.View);
    }

    public static GpuAdapter Create(short width, short height, uint[] palette)
    {
        var context = Context.Create(builder =>
        {
            builder.Default().EnableAlgorithms();
        });

        var accelerator = context.GetPreferredDevice(preferCPU: false).CreateAccelerator(context);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, MandelbrotParameters, BufferParameters>(MandelbrotKernel.ComputeMandelbrotFrame);

        var paletteCache = accelerator.Allocate1D<uint>(palette.Length);
        paletteCache.CopyFromCPU(palette);

        var buffer = accelerator.Allocate1D<uint>(width * height);

        accelerator.Synchronize();

        return new GpuAdapter(context, accelerator, kernel, buffer, paletteCache, width, height);
    }

    public void Render(MandelbrotParameters mandelbrotParameters, uint[] outputBuffer)
    {
        _kernel(_pixelIndex, mandelbrotParameters, _bufferParameters);
        _outputBuffer.CopyToCPU(outputBuffer);
    }

    public void Dispose()
    {
        _paletteCache.Dispose();
        _outputBuffer.Dispose();
        _accelerator.Dispose();
        _context.Dispose();
    }
}
