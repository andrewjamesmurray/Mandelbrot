namespace Mandelbrot.Tests;

/// <summary>
/// Compares rendered pixels against the same pixels iterated entirely in HpReal.
///
/// This is the test that decides whether the renderer is right. Everything else checks
/// that two implementations agree with each other, which is no help when both are wrong
/// the same way - and that is exactly what happened with the fp32 kernels, which agreed
/// nicely with one another and disagreed with reality by up to 34%.
/// </summary>
[Collection("render")]
public class GroundTruthTests(RenderFixture fixture)
{
    const int Samples = 48;
    const int Seed = 20260927;

    /// <param name="scale">Zoom level.</param>
    /// <param name="maxIter">Iteration cap, held fixed so the test stays quick.</param>
    /// <param name="tolerancePercent">
    /// Direct fp64 is not quite exact even where the ladder still uses it; perturbation is.
    /// </param>
    [Theory]
    [InlineData(2.5, 400, RenderMode.DirectDouble, 2.0)]
    [InlineData(1e-2, 600, RenderMode.DirectDouble, 2.0)]
    [InlineData(1e-8, 900, RenderMode.DirectDouble, 2.0)]
    [InlineData(1e-20, 9000, RenderMode.PerturbDouble, 0.0)]
    [InlineData(1e-40, 9000, RenderMode.PerturbDouble, 0.0)]
    [InlineData(1e-80, 9000, RenderMode.PerturbDouble, 0.0)]
    public void RenderedPixelsMatchExactArithmetic(double scale, int maxIter, RenderMode expectedMode, double tolerancePercent)
    {
        var state = RenderFixture.At(scale, maxIter);
        Assert.Equal(expectedMode, state.Mode);

        var (parameters, _) = state.PrepareFrame();
        var pixels = RenderFixture.Render(fixture.Gpu, state);

        var bits = HpReal.BitsForScale(scale) + 64;
        var centre = HpComplex.Parse(RenderFixture.DeepX, RenderFixture.DeepY, bits);

        var random = new Random(Seed);
        var xs = new int[Samples];
        var ys = new int[Samples];
        for (var s = 0; s < Samples; s++)
        {
            xs[s] = random.Next(RenderFixture.Width);
            ys[s] = random.Next(RenderFixture.Height);
        }

        var wrong = 0;
        Parallel.For(0, Samples, s =>
        {
            var cr = centre.Real + HpReal.FromDouble(xs[s] * parameters.DeltaX + parameters.PerturbOriginX, bits);
            var ci = centre.Imaginary + HpReal.FromDouble(ys[s] * parameters.DeltaY + parameters.PerturbOriginY, bits);

            var exact = RenderFixture.ExactIterations(cr, ci, maxIter, bits);
            var expected = RenderFixture.ExpectedShade(exact, maxIter);
            var actual = RenderFixture.ShadeOf(pixels[ys[s] * RenderFixture.Width + xs[s]]);

            if (actual != expected)
                Interlocked.Increment(ref wrong);
        });

        var percent = 100.0 * wrong / Samples;
        Assert.True(percent <= tolerancePercent,
            $"{wrong} of {Samples} sampled pixels disagree with exact arithmetic at scale {scale:E0} ({percent:F2}%)");
    }

    [Fact]
    public void SeriesApproximationStaysExactAtDepth()
    {
        const double Scale = 1e-40;
        const int MaxIter = 9000;

        var state = RenderFixture.At(Scale, MaxIter);
        var (parameters, _) = state.PrepareFrame();
        Assert.True(parameters.SkipIterations > 0, "expected the series to skip something here");

        var pixels = RenderFixture.Render(fixture.Gpu, state);

        var bits = HpReal.BitsForScale(Scale) + 64;
        var centre = HpComplex.Parse(RenderFixture.DeepX, RenderFixture.DeepY, bits);

        var random = new Random(Seed);
        var wrong = 0;

        for (var s = 0; s < Samples; s++)
        {
            var x = random.Next(RenderFixture.Width);
            var y = random.Next(RenderFixture.Height);

            var cr = centre.Real + HpReal.FromDouble(x * parameters.DeltaX + parameters.PerturbOriginX, bits);
            var ci = centre.Imaginary + HpReal.FromDouble(y * parameters.DeltaY + parameters.PerturbOriginY, bits);

            if (RenderFixture.ShadeOf(pixels[y * RenderFixture.Width + x]) !=
                RenderFixture.ExpectedShade(RenderFixture.ExactIterations(cr, ci, MaxIter, bits), MaxIter))
                wrong++;
        }

        Assert.Equal(0, wrong);
    }

    [Fact]
    public void DirectDoubleHasGivenUpByTheTimeTheLadderSwitchesAway()
    {
        // Justifies the 1e-13 handover: past it, straight fp64 is not merely imprecise,
        // it is wrong about most of the picture.
        const double Scale = 1e-20;
        const int MaxIter = 9000;

        var state = RenderFixture.At(Scale, MaxIter, RenderMode.DirectDouble);
        var (parameters, _) = state.PrepareFrame();
        var pixels = RenderFixture.Render(fixture.Gpu, state);

        var bits = HpReal.BitsForScale(Scale) + 64;
        var centre = HpComplex.Parse(RenderFixture.DeepX, RenderFixture.DeepY, bits);

        var random = new Random(Seed);
        var wrong = 0;

        for (var s = 0; s < Samples; s++)
        {
            var x = random.Next(RenderFixture.Width);
            var y = random.Next(RenderFixture.Height);

            var cr = centre.Real + HpReal.FromDouble(x * parameters.DeltaX + parameters.PerturbOriginX, bits);
            var ci = centre.Imaginary + HpReal.FromDouble(y * parameters.DeltaY + parameters.PerturbOriginY, bits);

            if (RenderFixture.ShadeOf(pixels[y * RenderFixture.Width + x]) !=
                RenderFixture.ExpectedShade(RenderFixture.ExactIterations(cr, ci, MaxIter, bits), MaxIter))
                wrong++;
        }

        Assert.True(wrong > Samples / 2, $"expected fp64 to be badly wrong at {Scale:E0}, only {wrong} of {Samples} were");
    }
}
