namespace Mandelbrot.Tests;

/// <summary>
/// The rule that keeps the auto-zoom smooth when frames stop arriving at 60 Hz.
/// Its two safety properties are that the frame is never shrunk past its own edges
/// (magnification below 1) and never stretched further than one zoom step.
/// </summary>
public class DisplayDescentTests
{
    const double Vsync = 1.0 / 60.0;
    static readonly double Step = Math.Log(1.0 / MandelbrotState.ZoomFactorIncrement);

    static DisplayDescent Started(double logScale = 0.0)
    {
        var descent = new DisplayDescent();
        descent.OnFrame(logScale, fromAutoZoom: false);
        return descent;
    }

    [Fact]
    public void DoesNothingBeforeTheFirstFrame()
    {
        var descent = new DisplayDescent();
        descent.Advance(Vsync, zooming: true);

        Assert.False(descent.HasFrame);
        Assert.Equal(1.0, descent.Magnification);
    }

    [Fact]
    public void FirstFrameIsShownExactly()
    {
        var descent = Started(-3.0);

        Assert.Equal(-3.0, descent.DisplayLogScale);
        Assert.Equal(1.0, descent.Magnification);
    }

    [Fact]
    public void MagnificationStaysBetweenOneAndOneZoomStep()
    {
        var descent = Started();
        var logScale = 0.0;

        // Frames arriving far slower than the display refreshes.
        for (var tick = 0; tick < 2000; tick++)
        {
            if (tick % 17 == 0)
            {
                logScale -= Step;
                descent.OnFrame(logScale, fromAutoZoom: true);
            }

            descent.Advance(Vsync, zooming: true);

            Assert.True(descent.Magnification >= 1.0, $"magnification fell to {descent.Magnification}");
            Assert.True(descent.Magnification <= Math.Exp(Step) + 1e-9,
                $"magnification reached {descent.Magnification}, above the one-step cap {Math.Exp(Step)}");
        }
    }

    [Fact]
    public void DisplayedScaleNeverGoesBackwards()
    {
        var descent = Started();
        var logScale = 0.0;
        var previous = descent.DisplayLogScale;

        for (var tick = 0; tick < 2000; tick++)
        {
            if (tick % 7 == 0)
            {
                logScale -= Step;
                descent.OnFrame(logScale, fromAutoZoom: true);
            }

            descent.Advance(Vsync, zooming: true);

            Assert.True(descent.DisplayLogScale <= previous + 1e-12, "the zoom went backwards");
            previous = descent.DisplayLogScale;
        }
    }

    [Fact]
    public void KeepsUpWhenFramesArriveEveryRefresh()
    {
        var descent = Started();
        var logScale = 0.0;

        for (var tick = 0; tick < 600; tick++)
        {
            logScale -= Step;
            descent.OnFrame(logScale, fromAutoZoom: true);
            descent.Advance(Vsync, zooming: true);
        }

        // One zoom step per refresh, give or take the lag the rule deliberately holds.
        Assert.True(Math.Abs(descent.DisplayLogScale - logScale) <= Step + 1e-9);
    }

    [Fact]
    public void StallsAtTheLagBoundWhenNoFramesArrive()
    {
        var descent = Started();
        descent.OnFrame(-Step, fromAutoZoom: true);

        for (var tick = 0; tick < 600; tick++)
            descent.Advance(Vsync, zooming: true);

        Assert.True(Math.Abs(descent.DisplayLogScale - (-Step - Step)) < 1e-6,
            "the display should settle exactly one step past the newest frame");
        Assert.Equal(0.0, descent.EFoldsPerSecond, 9);
    }

    [Fact]
    public void ResumesDescendingWhenAFrameFinallyArrives()
    {
        var descent = Started();
        descent.OnFrame(-Step, fromAutoZoom: true);

        for (var tick = 0; tick < 100; tick++)
            descent.Advance(Vsync, zooming: true);

        var stalled = descent.DisplayLogScale;
        Assert.Equal(0.0, descent.EFoldsPerSecond, 9);

        descent.OnFrame(-2 * Step, fromAutoZoom: true);
        descent.Advance(Vsync, zooming: true);

        Assert.True(descent.DisplayLogScale < stalled);
        Assert.True(descent.EFoldsPerSecond > 0.0);
    }

    [Fact]
    public void DescentRateTracksTheRateFramesArrive()
    {
        // Half the frame rate should give half the descent, without anything being
        // told what the frame rate is.
        static double Measure(int ticksPerFrame)
        {
            var descent = Started();
            var logScale = 0.0;

            for (var tick = 0; tick < 4000; tick++)
            {
                if (tick % ticksPerFrame == 0)
                {
                    logScale -= Step;
                    descent.OnFrame(logScale, fromAutoZoom: true);
                }

                descent.Advance(Vsync, zooming: true);
            }

            return -descent.DisplayLogScale / (4000 * Vsync);
        }

        var fast = Measure(4);
        var slow = Measure(8);

        Assert.True(Math.Abs(fast / slow - 2.0) < 0.05, $"fast {fast:F3} vs slow {slow:F3} e-folds/s");
    }

    [Fact]
    public void AnInteractiveFrameSnapsTheDisplayOntoIt()
    {
        var descent = Started();
        descent.OnFrame(-Step, fromAutoZoom: true);
        for (var tick = 0; tick < 50; tick++)
            descent.Advance(Vsync, zooming: true);

        Assert.True(descent.Magnification > 1.0);

        // A pan or mouse-wheel zoom is the view the user asked for.
        descent.OnFrame(-4.2, fromAutoZoom: false);

        Assert.Equal(-4.2, descent.DisplayLogScale);
        Assert.Equal(1.0, descent.Magnification);
    }

    [Fact]
    public void NotZoomingHoldsTheDisplayOnTheRenderedFrame()
    {
        var descent = Started();
        descent.OnFrame(-Step, fromAutoZoom: true);

        descent.Advance(Vsync, zooming: false);

        Assert.Equal(-Step, descent.DisplayLogScale);
        Assert.Equal(1.0, descent.Magnification);
        Assert.Equal(0.0, descent.EFoldsPerSecond);
    }
}
