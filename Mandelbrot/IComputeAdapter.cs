namespace Mandelbrot;

interface IComputeAdapter : IDisposable 
{
    void Render(MandelbrotParameters mandelbrotParameters, uint[] outputBuffer);
}