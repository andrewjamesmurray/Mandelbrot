using System.Windows.Media;

namespace Mandelbrot;

public static class Palette
{
    public static uint[] GenerateColorLookup()
    {
        var results = new uint[MandelbrotConstants.MaxIterations + 1];

        for (var i = 0; i <= MandelbrotConstants.MaxIterations; i++)
        {
            var hsv = (double)i / MandelbrotConstants.MaxIterations * 360.0;
            var color = ColorFromHSV(hsv);
            var color32 = (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);
            results[i] = color32;
        }

        return results;
    }

    private static Color ColorFromHSV(double hue)
    {
        sbyte hi = Convert.ToSByte(Math.Floor(hue / 60) % 6);
        double f = hue / 60 - Math.Floor(hue / 60);

        byte q = Convert.ToByte(255 * (1 - f));
        byte t = Convert.ToByte(255 * (1 - (1 - f)));

        const byte v = 255;
        const byte p = 0;

        if (hi == 0)
            return Color.FromArgb(255, v, t, p);
        else if (hi == 1)
            return Color.FromArgb(255, q, v, p);
        else if (hi == 2)
            return Color.FromArgb(255, p, v, t);
        else if (hi == 3)
            return Color.FromArgb(255, p, q, v);
        else if (hi == 4)
            return Color.FromArgb(255, t, p, v);
        else
            return Color.FromArgb(255, v, p, q);
    }
}
