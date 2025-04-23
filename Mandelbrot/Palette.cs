using System.Drawing;

namespace Mandelbrot;

public static class Palette
{
    public const int NumShades = 256;
    public static uint ARGBToUInt(byte alpha, byte r, byte g, byte b)
    {
        return (uint)(alpha << 24 | r << 16 | g << 8 | b);
    }

    public static uint[] GenerateColorLookup()
    {
        static uint ColorFromIntensity(double hue)
        {
            var hue6 = hue * 6.0;
            var intHue6 = Math.Floor(hue6);
            var segment = intHue6 % 6;
            var f = hue6 - intHue6; // fraction part only

            byte q = (byte)(255 * (1 - f));
            byte t = (byte)(255 * f);
            byte h = (byte)(127 * f);

            switch (segment)
            {
                case 0:
                    return ARGBToUInt(0xFF, 0, t, t);
                case 1:
                    return ARGBToUInt(0xFF, q, 0xFF, q);
                case 2:
                    return ARGBToUInt(0xFF, 0, h, t);
                case 3:
                    return ARGBToUInt(0xFF, 0, h, q);
                case 4:
                    return ARGBToUInt(0xFF, t, 0, h);
                default:
                    return ARGBToUInt(0xFF, h, 0, q);
            }
        }

        var results = new uint[NumShades];

        for (var i = 0; i < NumShades; i++)
        {
            var intensity = (double)i / NumShades;
            results[i] = ColorFromIntensity(intensity);
        }

        return results;
    }

    public static uint[] GenerateColorLookup2()
    {
        Color[] viridis =
        [
            Color.FromArgb(68, 1, 84),
            Color.FromArgb(71, 44, 122),
            Color.FromArgb(59, 81, 139),
            Color.FromArgb(44, 113, 142),
            Color.FromArgb(33, 144, 141),
            Color.FromArgb(39, 173, 129),
            Color.FromArgb(92, 200, 99),
            Color.FromArgb(170, 220, 50),
            Color.FromArgb(253, 231, 37),
        ];

        var results = new uint[NumShades];

        // Clamp between 0 and 1
        for (int gradient = 0; gradient < NumShades; gradient++)
        {
            var t = Math.Max(0.0, Math.Min(1.0, (double)gradient / NumShades));

            // Define Viridis color stops (RGB in 0–255)
            var n = viridis.Length - 1;
            var scaledT = t * n;
            var i = (int)scaledT;
            var frac = scaledT - i;

            if (i >= n)
            {
                var v = viridis[n];
                results[gradient] = ARGBToUInt(0xFF, v.R, v.G, v.B);
            }

            var c1 = viridis[i];
            var c2 = viridis[i + 1];

            var r = (byte)(c1.R + (c2.R - c1.R) * frac);
            var g = (byte)(c1.G + (c2.G - c1.G) * frac);
            var b = (byte)(c1.B + (c2.B - c1.B) * frac);
            var a = (byte)0xFF;

            results[gradient] = ARGBToUInt(a, r, g, b);
        }

        return results;
    }
}
