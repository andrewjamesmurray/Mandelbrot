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

    public static uint ARGBToUInt(byte alpha, byte r, byte g, byte b)
    {
        return (uint)(alpha << 24 | r << 16 | g << 8 | b);
    }

    private static uint ColorFromIntensity(double hue)
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
}
