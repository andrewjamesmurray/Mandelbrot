namespace Mandelbrot.Tests;

[Collection("render")]
public class RendererTests(RenderFixture fixture)
{
    [Theory]
    [InlineData(2.5)]
    [InlineData(1e-6)]
    [InlineData(1e-20)]
    public void EveryPixelIsAValidShadeOrTheInteriorColour(double scale)
    {
        var pixels = RenderFixture.Render(fixture.Gpu, RenderFixture.At(scale, maxIter: 900));

        Assert.All(pixels, pixel =>
            Assert.True(pixel == MandelbrotKernel.BulbColor || pixel < Palette.NumShades,
                $"pixel {pixel:X8} is neither the interior colour nor a palette index"));
    }

    [Fact]
    public void RealPaletteProducesFullyOpaquePixels()
    {
        using var adapter = CpuAdapter.Create(RenderFixture.Width, RenderFixture.Height, Palette.GenerateColorLookup());
        var pixels = RenderFixture.Render(adapter, RenderFixture.At(2.5));

        Assert.All(pixels, pixel => Assert.Equal(0xFFu, pixel >> 24));
    }

    [Theory]
    [InlineData(2.5)]
    [InlineData(1e-8)]
    [InlineData(1e-20)]
    [InlineData(1e-40)]
    public void CpuAndGpuAgree(double scale)
    {
        var gpu = RenderFixture.Render(fixture.Gpu, RenderFixture.At(scale, maxIter: 900));
        var cpu = RenderFixture.Render(fixture.Cpu, RenderFixture.At(scale, maxIter: 900));
        var differing = RenderFixture.PercentDiffering(gpu, cpu);

        // Both are IEEE double, but the GPU contracts multiply-adds into FMAs, so a
        // pixel on the boundary can land one iteration apart.
        Assert.True(differing < 1.0, $"{differing:F3}% of pixels differ at scale {scale:E0}");
    }

    [Fact]
    public void PerturbationAgreesWithDirectIterationWhereBothAreValid()
    {
        var direct = RenderFixture.Render(fixture.Gpu, RenderFixture.At(1e-8, 900, RenderMode.DirectDouble));
        var perturbed = RenderFixture.Render(fixture.Gpu, RenderFixture.At(1e-8, 900, RenderMode.PerturbDouble));
        var differing = RenderFixture.PercentDiffering(direct, perturbed);

        Assert.True(differing < 1.0, $"{differing:F3}% of pixels differ");
    }

    [Theory]
    [InlineData(1e-20)]
    [InlineData(1e-40)]
    public void SeriesApproximationDoesNotChangeTheImage(double scale)
    {
        var withSeries = RenderFixture.At(scale, maxIter: 9000);
        var on = RenderFixture.Render(fixture.Gpu, withSeries);
        Assert.True(withSeries.SkippedIterations > 0, "expected the series to skip something here");

        var withoutSeries = RenderFixture.At(scale, maxIter: 9000);
        withoutSeries.ToggleSeriesApproximation();
        var off = RenderFixture.Render(fixture.Gpu, withoutSeries);

        var differing = RenderFixture.PercentDiffering(on, off);
        Assert.True(differing < 0.5, $"{differing:F3}% of pixels differ with the series on");
    }

    [Fact]
    public void OptimisationFlagsDoNotChangeTheImage()
    {
        // Each flag combination compiles to its own specialised kernel, and all four
        // must still produce the same picture.
        var state = RenderFixture.At(2.5);
        var (parameters, orbit) = state.PrepareFrame();

        var reference = new uint[RenderFixture.Width * RenderFixture.Height];
        fixture.Gpu.Render(parameters, state.Mode, 3, orbit, reference);

        foreach (var flags in new[] { 0, 1, 2, 3 })
        {
            var buffer = new uint[reference.Length];
            fixture.Gpu.Render(parameters, state.Mode, flags, orbit, buffer);

            Assert.Equal(0.0, RenderFixture.PercentDiffering(reference, buffer));
        }
    }

    [Fact]
    public void DeepZoomProducesStructureRatherThanAFlatField()
    {
        // Guards against a broken rebase quietly returning the same value everywhere.
        var pixels = RenderFixture.Render(fixture.Gpu, RenderFixture.At(1e-20, maxIter: 9000));
        var shades = RenderFixture.DistinctShades(pixels);

        Assert.True(shades > 20, $"only {shades} distinct shades at 1e-20");
    }

    [Fact]
    public void RenderWritesEveryPixelOfTheDestination()
    {
        var state = RenderFixture.At(2.5);
        var (parameters, orbit) = state.PrepareFrame();

        var buffer = new uint[RenderFixture.Width * RenderFixture.Height];
        Array.Fill(buffer, 0xDEADBEEFu);

        fixture.Gpu.Render(parameters, state.Mode, state.Optimizations, orbit, buffer);

        Assert.DoesNotContain(0xDEADBEEFu, buffer);
    }
}
