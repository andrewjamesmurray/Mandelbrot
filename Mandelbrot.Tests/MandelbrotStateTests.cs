namespace Mandelbrot.Tests;

public class MandelbrotStateTests
{
    const string DeepX = "0.26523357180865775";
    const string DeepY = "0.003055480740359563";

    static MandelbrotState At(double scale, int? maxIter = null)
    {
        var state = new MandelbrotState(1920, 1080);
        state.SetView(DeepX, DeepY, scale, maxIter);
        return state;
    }

    [Theory]
    [InlineData(2.5, RenderMode.DirectDouble)]
    [InlineData(1e-6, RenderMode.DirectDouble)]
    [InlineData(1e-12, RenderMode.DirectDouble)]
    [InlineData(1e-14, RenderMode.PerturbDouble)]
    [InlineData(1e-40, RenderMode.PerturbDouble)]
    public void ModeFollowsTheZoomDepth(double scale, RenderMode expected) =>
        Assert.Equal(expected, At(scale).Mode);

    [Fact]
    public void AutomaticLadderNeverSelectsAFloatKernel()
    {
        // Measured against exact arithmetic, direct fp32 is 1% wrong at 1e-2 and 34% at
        // 1e-4, and fp32 perturbation degrades as the iteration cap rises (exact at
        // maxIter 2000, 13% wrong at 10000). They are far faster on a consumer GPU and
        // must still stay off the automatic path.
        for (var exponent = 1; exponent >= -150; exponent -= 1)
        {
            var mode = At(Math.Pow(10, exponent)).Mode;

            Assert.True(mode is RenderMode.DirectDouble or RenderMode.PerturbDouble,
                $"scale 1e{exponent} selected {mode}");
        }
    }

    [Fact]
    public void ForcedModeOverridesTheLadderAndClearsAgain()
    {
        var state = At(2.5);

        state.ForcedMode = RenderMode.PerturbFloat;
        Assert.Equal(RenderMode.PerturbFloat, state.Mode);

        state.ForcedMode = null;
        Assert.Equal(RenderMode.DirectDouble, state.Mode);
    }

    [Fact]
    public void ZoomingStopsAtTheLimitRatherThanRunningForever()
    {
        var state = At(1e-149);

        var steps = 0;
        while (state.ZoomNext() && steps < 1000)
            steps++;

        Assert.InRange(steps, 1, 999);
        Assert.False(state.ZoomNext());
    }

    [Fact]
    public void OptimisationFlagsReflectTheToggles()
    {
        var state = At(2.5);
        Assert.Equal(
            MandelbrotParameters.PeriodicityOptimizationFlag | MandelbrotParameters.BulbCheckOptimizationFlag,
            state.Optimizations);

        state.TogglePeriodicityOptimization();
        Assert.Equal(MandelbrotParameters.BulbCheckOptimizationFlag, state.Optimizations);

        state.ToggleBulbCheckOptimization();
        Assert.Equal(0, state.Optimizations);
    }

    [Fact]
    public void FrameParametersAreSelfConsistent()
    {
        var state = At(1e-6);
        var (parameters, orbit) = state.PrepareFrame();

        Assert.Null(orbit);
        Assert.Equal(1920, parameters.Width);
        Assert.Equal(1080, parameters.Height);
        Assert.Equal((float)Palette.NumShades / parameters.MaxIter, parameters.PaletteScale);

        // The direct offset is the perturbation origin shifted onto the centre.
        Assert.True(Math.Abs(parameters.OffsetX - (parameters.PerturbOriginX + state.CenterX)) < 1e-18);
        Assert.True(Math.Abs(parameters.OffsetY - (parameters.PerturbOriginY + state.CenterY)) < 1e-18);

        // The pixel grid spans exactly the view.
        Assert.True(Math.Abs(parameters.DeltaX * parameters.Width / (-2 * parameters.PerturbOriginX) - 1.0) < 1e-12);
        Assert.True(Math.Abs(parameters.DeltaY * parameters.Height / (-2 * parameters.PerturbOriginY) - 1.0) < 1e-12);
    }

    [Fact]
    public void PerturbationFramesCarryAReferenceOrbit()
    {
        var state = At(1e-20);
        var (parameters, orbit) = state.PrepareFrame();

        Assert.NotNull(orbit);
        Assert.Equal(orbit.Length, parameters.RefLength);
        Assert.True(parameters.RefLength > 1);
        Assert.True(parameters.InvRadius > 0);
    }

    [Fact]
    public void PanningAtDepthKeepsPrecisionADoubleWouldHaveLost()
    {
        var state = new MandelbrotState(1920, 1080);
        state.SetView("0.25", "0.0", 1e-40);

        // The same move applied to a double simply vanishes.
        var step = 1.0 / 1920 * 1e-40;
        Assert.Equal(0.25, 0.25 - step);

        var before = state.Center.Real;
        state.Move(1.0, 0.0);
        var shift = (before - state.Center.Real).ToDouble();

        Assert.True(Math.Abs(shift / step - 1.0) < 1e-9, $"shift was {shift}, expected {step}");
        Assert.NotEqual("0.25", state.DescribeCenter());
    }

    [Fact]
    public void CentreIsCarriedAtEnoughPrecisionForTheDeepestZoom()
    {
        // 1e-150 needs about 500 bits; anything less and deep zooms quietly degrade.
        Assert.True(MandelbrotState.CenterPrecisionBits >= HpReal.BitsForScale(1e-150));
    }

    [Fact]
    public void DescribedCentreRoundTripsBackIntoTheSameView()
    {
        var state = new MandelbrotState(1920, 1080);
        state.SetView("-1.25355163886930157734567890123456789", "0.37899272530660111222333444555666777", 1e-30);

        var description = state.DescribeCenter();
        var parts = description.Replace("\"", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var restored = new MandelbrotState(1920, 1080);
        restored.SetView(parts[0].TrimEnd(','), parts[1].TrimEnd(','), 1e-30);

        Assert.True((state.Center.Real - restored.Center.Real).IsZero);
        Assert.True((state.Center.Imaginary - restored.Center.Imaginary).IsZero);
    }

    [Fact]
    public void IterationCapStaysWithinBounds()
    {
        var state = At(2.5);

        for (var i = 0; i < 100; i++)
            state.DecreaseMaxIter();
        Assert.True(state.MaxIter > 0);

        var deep = At(1e-149);
        Assert.True(deep.MaxIter <= 1_000_000);
    }
}
