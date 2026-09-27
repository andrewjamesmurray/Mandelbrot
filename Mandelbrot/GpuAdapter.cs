using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

namespace Mandelbrot;

public sealed class GpuAdapter : IComputeAdapter
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    private readonly MemoryBuffer1D<uint, Stride1D.Dense> _outputBuffer;
    private readonly MemoryBuffer1D<uint, Stride1D.Dense> _paletteBuffer;
    private readonly Index2D _extent;

    private readonly Action<Index2D, MandelbrotParameters, BufferParameters, SpecializedValue<int>> _directDouble;
    private readonly Action<Index2D, MandelbrotParameters, BufferParameters, SpecializedValue<int>> _directFloat;
    private readonly Action<Index2D, MandelbrotParameters, BufferParameters> _perturbDouble;
    private readonly Action<Index2D, MandelbrotParameters, BufferParameters> _perturbFloat;

    private MemoryBuffer1D<double, Stride1D.Dense> _referenceDouble;
    private MemoryBuffer1D<float, Stride1D.Dense> _referenceFloat;
    private int _uploadedDoubleVersion;
    private int _uploadedFloatVersion;
    private int _uploadedDoubleLength;
    private int _uploadedFloatLength;

    public string Description { get; }

    private GpuAdapter(Context context, Accelerator accelerator, int width, int height, uint[] palette)
    {
        _context = context;
        _accelerator = accelerator;
        _extent = new Index2D(width, height);

        Description = $"{accelerator.Name} [{accelerator.AcceleratorType}]";

        _paletteBuffer = accelerator.Allocate1D<uint>(palette.Length);
        _paletteBuffer.CopyFromCPU(palette);
        _outputBuffer = accelerator.Allocate1D<uint>((long)width * height);

        // Kept valid at all times: a kernel that does not read them still gets the views.
        _referenceDouble = accelerator.Allocate1D<double>(2);
        _referenceFloat = accelerator.Allocate1D<float>(2);

        _directDouble = accelerator.LoadAutoGroupedStreamKernel<Index2D, MandelbrotParameters, BufferParameters, SpecializedValue<int>>(MandelbrotKernel.DirectDoubleKernel);
        _directFloat = accelerator.LoadAutoGroupedStreamKernel<Index2D, MandelbrotParameters, BufferParameters, SpecializedValue<int>>(MandelbrotKernel.DirectFloatKernel);
        _perturbDouble = accelerator.LoadAutoGroupedStreamKernel<Index2D, MandelbrotParameters, BufferParameters>(MandelbrotKernel.PerturbDoubleKernel);
        _perturbFloat = accelerator.LoadAutoGroupedStreamKernel<Index2D, MandelbrotParameters, BufferParameters>(MandelbrotKernel.PerturbFloatKernel);

        accelerator.Synchronize();
    }

    public static GpuAdapter Create(int width, int height, uint[] palette)
    {
        var context = Context.CreateDefault();
        var accelerator = SelectDevice(context).CreateAccelerator(context);

        return new GpuAdapter(context, accelerator, width, height, palette);
    }

    /// <summary>
    /// Picks the fastest real GPU. ILGPU's GetPreferredDevice does not rank across
    /// backends the way you would hope: on a machine with both an integrated Radeon and
    /// a discrete GeForce it hands back the OpenCL iGPU, which is one to two orders of
    /// magnitude slower here. Prefer CUDA, then the widest OpenCL device, then the CPU.
    /// </summary>
    private static Device SelectDevice(Context context)
    {
        var cuda = context.Devices
            .OfType<CudaDevice>()
            .OrderByDescending(d => d.NumMultiprocessors)
            .FirstOrDefault();

        if (cuda is not null)
            return cuda;

        var openCl = context.Devices
            .Where(d => d.AcceleratorType == AcceleratorType.OpenCL)
            .OrderByDescending(d => d.NumMultiprocessors)
            .FirstOrDefault();

        return openCl ?? context.GetPreferredDevice(preferCPU: true);
    }

    public void Render(in MandelbrotParameters parameters, RenderMode mode, int optimizations, ReferenceOrbit? orbit, Span<uint> destination)
    {
        var buffers = new BufferParameters(
            _outputBuffer.View,
            _paletteBuffer.View,
            UploadReferenceDouble(mode, orbit),
            UploadReferenceFloat(mode, orbit));

        switch (mode)
        {
            case RenderMode.DirectFloat:
                _directFloat(_extent, parameters, buffers, SpecializedValue.New(optimizations));
                break;
            case RenderMode.PerturbFloat:
                _perturbFloat(_extent, parameters, buffers);
                break;
            case RenderMode.PerturbDouble:
                _perturbDouble(_extent, parameters, buffers);
                break;
            default:
                _directDouble(_extent, parameters, buffers, SpecializedValue.New(optimizations));
                break;
        }

        _outputBuffer.View.AsContiguous().CopyToCPU(destination);
    }

    private ArrayView1D<double, Stride1D.Dense> UploadReferenceDouble(RenderMode mode, ReferenceOrbit? orbit)
    {
        if (mode != RenderMode.PerturbDouble || orbit is null || orbit.Length == 0)
            return _referenceDouble.View;

        var needed = 2 * orbit.Length;
        if (_referenceDouble.Length < needed)
        {
            _referenceDouble.Dispose();
            _referenceDouble = _accelerator.Allocate1D<double>(needed);
            _uploadedDoubleVersion = 0;
        }

        // The orbit only changes when the centre moves or the iteration cap grows, which
        // is far less often than once a frame while auto-zooming.
        if (_uploadedDoubleVersion != orbit.Version || _uploadedDoubleLength != orbit.Length)
        {
            _referenceDouble.View.SubView(0, needed).AsContiguous().CopyFromCPU(orbit.PointsDouble.AsSpan(0, needed));
            _uploadedDoubleVersion = orbit.Version;
            _uploadedDoubleLength = orbit.Length;
        }

        return _referenceDouble.View;
    }

    private ArrayView1D<float, Stride1D.Dense> UploadReferenceFloat(RenderMode mode, ReferenceOrbit? orbit)
    {
        if (mode != RenderMode.PerturbFloat || orbit is null || orbit.Length == 0)
            return _referenceFloat.View;

        var needed = 2 * orbit.Length;
        if (_referenceFloat.Length < needed)
        {
            _referenceFloat.Dispose();
            _referenceFloat = _accelerator.Allocate1D<float>(needed);
            _uploadedFloatVersion = 0;
        }

        if (_uploadedFloatVersion != orbit.Version || _uploadedFloatLength != orbit.Length)
        {
            _referenceFloat.View.SubView(0, needed).AsContiguous().CopyFromCPU(orbit.PointsFloat.AsSpan(0, needed));
            _uploadedFloatVersion = orbit.Version;
            _uploadedFloatLength = orbit.Length;
        }

        return _referenceFloat.View;
    }

    public void Dispose()
    {
        _referenceFloat.Dispose();
        _referenceDouble.Dispose();
        _paletteBuffer.Dispose();
        _outputBuffer.Dispose();
        _accelerator.Dispose();
        _context.Dispose();
    }
}
