using System.Drawing;
using System.Windows.Media;

namespace Mandelbrot;

public static class Palette
{
    public static uint[] GenerateColorLookup()
    {
        var results = new uint[MandelbrotConstants.MaxIterations + 1];

        for (var i = 0; i <= MandelbrotConstants.MaxIterations; i++)
        {
            var intensity = (double)i / MandelbrotConstants.MaxIterations;
            results[i] = ColorFromIntensity(intensity);
        }

        return results;
    }

    private static uint ARGBToUInt(byte alpha, byte r, byte g, byte b)
    {
        return (uint)(alpha << 24 | r << 16 | g << 8 | b);
    }

    private static uint ColorFromIntensity(double hue)
    {
        var hue6 = hue * 6.0;
        var intHue6 = Math.Floor(hue6);
        var f = hue6 - intHue6; // fraction part only

        byte q = (byte)(255 * (1 - f));
        byte t = (byte)(255 * f);

        var segment = intHue6 % 6;
        switch (segment)
        {
            case 0:
                return ARGBToUInt(0xFF, 0xFF, t, 0);
            case 1:
                return ARGBToUInt(0xFF, q, 0xFF, 0);
            case 2:
                return ARGBToUInt(0xFF, 0, 0xFF, t);
            case 3:
                return ARGBToUInt(0xFF, 0, q, 0xFF);
            case 4:
                return ARGBToUInt(0xFF, t, 0, 0xFF);
            default:
                return ARGBToUInt(0xFF, 0xFF, 0, q);
        }
    }
}
