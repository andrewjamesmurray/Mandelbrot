using System.Drawing;

namespace Mandelbrot;

public static class Palette
{
    public const int NumShades = 2048;

    public static uint ARGBToUInt(byte alpha, byte r, byte g, byte b)
    {
        return (uint)(alpha << 24 | r << 16 | g << 8 | b);
    }

    public static uint[] GenerateColorLookup(Color[] palette)
    {
        var results = new uint[NumShades];

        // Clamp between 0 and 1
        for (int gradient = 0; gradient < NumShades; gradient++)
        {
            var t = Math.Max(0.0, Math.Min(1.0, (double)gradient / NumShades));

            // Define Viridis color stops (RGB in 0–255)
            var n = palette.Length - 1;
            var scaledT = t * n;
            var i = (int)scaledT;
            var frac = scaledT - i;

            if (i >= n)
            {
                // t < 1 keeps i <= n - 1, so this is only a guard against a future
                // change to the ramp; without the continue it would fall through and
                // read palette[n + 1].
                var v = palette[n];
                results[gradient] = ARGBToUInt(0xFF, v.R, v.G, v.B);
                continue;
            }

            var c1 = palette[i];
            var c2 = palette[i + 1];

            var r = (byte)(c1.R + (c2.R - c1.R) * frac);
            var g = (byte)(c1.G + (c2.G - c1.G) * frac);
            var b = (byte)(c1.B + (c2.B - c1.B) * frac);
            var a = (byte)0xFF;

            results[gradient] = ARGBToUInt(a, r, g, b);
        }

        return results;
    }

    public static uint[] GenerateColorLookup()
    {
        Color[] stolenPalette =
        [
            Color.FromArgb(  0,  0,   0),
            Color.FromArgb( 13, 49,  37),
            Color.FromArgb(196,185, 179),
            Color.FromArgb(255,255, 255),
            Color.FromArgb(243,139, 226),
            Color.FromArgb( 51,110,   6),
            Color.FromArgb(  0,  0,   0),
            Color.FromArgb( 16, 37,  32),
            Color.FromArgb(125,  4, 200),
            Color.FromArgb(255,255, 255),
            Color.FromArgb(136, 73, 199),
            Color.FromArgb( 24, 82,  69),
            Color.FromArgb(  0,  0,   0),
            Color.FromArgb( 76, 89,  78),
            Color.FromArgb( 12,102, 129),
            Color.FromArgb(255,255, 255),
            Color.FromArgb(219, 91, 175),
            Color.FromArgb( 98,118,  92),
            Color.FromArgb(  0,  0,   0),
            Color.FromArgb( 41, 77,  48),
            Color.FromArgb( 16, 13,  14),
            Color.FromArgb(255,255, 255),
            Color.FromArgb( 47,232, 216),
            Color.FromArgb( 46, 90,  56),
            Color.FromArgb(  0,  0,   0),
            Color.FromArgb(  0,  1,   0),
            Color.FromArgb(197,180, 212),
            Color.FromArgb(255,255, 255),
            Color.FromArgb(195, 91,  51),
            Color.FromArgb( 63,126, 106),
            Color.FromArgb(  0,  0,   0),
        ];

        return GenerateColorLookup(stolenPalette);
    }

    public static uint[] Viridis()
    {
        Color[] palette =
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

        return GenerateColorLookup(palette);
    }
}
