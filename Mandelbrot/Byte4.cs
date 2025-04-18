// Fast Mandelbrot Rendering with GPU in C#.
// Guy Fernando - i4cy (2024)
namespace Mandelbrot;

public readonly struct Byte4
{
    public readonly byte X; // Blue
    public readonly byte Y; // Green
    public readonly byte Z; // Red
    public readonly byte W; // Alpha

    public Byte4(byte x, byte y, byte z, byte w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public int ToPackedBGRA()
    {
        return (W << 24) | (Z << 16) | (Y << 8) | X;
    }
}
