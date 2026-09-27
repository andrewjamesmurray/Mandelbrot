using System.Drawing;

namespace Mandelbrot.Tests;

public class PaletteTests
{
    [Fact]
    public void ArgbPacksIntoTheExpectedLayout() =>
        Assert.Equal(0xFF123456u, Palette.ARGBToUInt(0xFF, 0x12, 0x34, 0x56));

    [Fact]
    public void DefaultLookupIsFullyPopulatedAndOpaque()
    {
        var palette = Palette.GenerateColorLookup();

        Assert.Equal(Palette.NumShades, palette.Length);
        Assert.All(palette, colour => Assert.Equal(0xFFu, colour >> 24));
    }

    [Fact]
    public void ViridisIsFullyPopulatedAndOpaque()
    {
        var palette = Palette.Viridis();

        Assert.Equal(Palette.NumShades, palette.Length);
        Assert.All(palette, colour => Assert.Equal(0xFFu, colour >> 24));
    }

    [Fact]
    public void TwoStopRampInterpolatesFromEndToEnd()
    {
        var palette = Palette.GenerateColorLookup([Color.FromArgb(0, 0, 0), Color.FromArgb(255, 255, 255)]);

        Assert.Equal(Palette.ARGBToUInt(0xFF, 0, 0, 0), palette[0]);

        // t runs over [0, 1), so the ramp approaches its last stop without landing on
        // it: the final entry is one step short, which is 1/2048 of the range.
        Assert.Equal(Palette.ARGBToUInt(0xFF, 0xFE, 0xFE, 0xFE), palette[^1]);

        // Monotonically brightening, with no wrap-around anywhere in between.
        for (var i = 1; i < palette.Length; i++)
            Assert.True((palette[i] & 0xFF) >= (palette[i - 1] & 0xFF));
    }

    [Fact]
    public void RampWithASingleRepeatedStopDoesNotRunOffTheEnd()
    {
        // Exercises the upper-bound guard in the ramp rather than falling through it.
        var palette = Palette.GenerateColorLookup([Color.FromArgb(10, 20, 30), Color.FromArgb(10, 20, 30)]);

        Assert.All(palette, colour => Assert.Equal(Palette.ARGBToUInt(0xFF, 10, 20, 30), colour));
    }
}
