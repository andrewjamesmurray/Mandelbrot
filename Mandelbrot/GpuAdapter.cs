using ILGPU;
using ILGPU.Runtime;

namespace Mandelbrot;

public sealed class GpuAdapter : IDisposable
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    private readonly MemoryBuffer1D<uint, Stride1D.Dense> _outputBuffer;
    private readonly MemoryBuffer1D<uint, Stride1D.Dense> _paletteCache;
    private readonly Action<Index1D, MandelbrotParameters> _kernel;

    public ArrayView1D<uint, Stride1D.Dense> PaletteView => _paletteCache.View;
    public ArrayView1D<uint, Stride1D.Dense> OutputBufferView => _outputBuffer.View;

    private readonly Index1D _pixelIndex;

    internal GpuAdapter(
        Context context, 
        Accelerator accelerator, 
        Action<Index1D, MandelbrotParameters> kernel,
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
    }

    public static GpuAdapter Create(short width, short height, uint[] palette)
    {
        var context = Context.Create(builder =>
        {
            builder.Default().EnableAlgorithms();
        });

        var accelerator = context.GetPreferredDevice(preferCPU: false).CreateAccelerator(context);

        // Load the kernel once during initialization.
        var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, MandelbrotParameters>(MandelbrotKernel.ComputeMandelbrotFrame);

        var paletteCache = accelerator.Allocate1D<uint>(palette.Length);
        paletteCache.CopyFromCPU(palette);

        var buffer = accelerator.Allocate1D<uint>(width * height);

        return new GpuAdapter(context, accelerator, kernel, buffer, paletteCache, width, height);
    }

    public void Kernel(MandelbrotParameters parameters, uint[] outputBuffer)
    {
        _kernel(_pixelIndex, parameters);
        _outputBuffer.CopyToCPU(outputBuffer);
    }

    public void Synchronize()
    {
        _accelerator.Synchronize();
    }

    public void Dispose()
    {
        _paletteCache.Dispose();
        _outputBuffer.Dispose();
        _accelerator.Dispose();
        _context.Dispose();
    }
}
