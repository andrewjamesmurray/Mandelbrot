namespace Mandelbrot;

public interface IComputeAdapter : IDisposable
{
    string Description { get; }

    /// <summary>
    /// Renders one frame straight into <paramref name="destination"/>, which is the
    /// bitmap's locked back buffer. Writing there directly avoids a second full-frame
    /// copy through an intermediate staging array.
    /// </summary>
    void Render(in MandelbrotParameters parameters, RenderMode mode, int optimizations, ReferenceOrbit? orbit, Span<uint> destination);
}
