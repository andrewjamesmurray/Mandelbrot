using System.Diagnostics;

namespace Mandelbrot.Tests;

/// <summary>
/// The producer side: a render thread, a bounded bank of finished frames, and
/// commands posted from the UI.
/// </summary>
public class FramePumpTests
{
    const int Width = 160;
    const int Height = 90;

    static FramePump New() => new(Width, Height, RenderFixture.IdentityPalette);

    static RenderedFrame? WaitForFrame(FramePump pump, double seconds = 30)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed.TotalSeconds < seconds)
        {
            if (pump.TryTake(out var frame) && frame is not null)
                return frame;

            Thread.Sleep(5);
        }

        return null;
    }

    static bool WaitUntil(Func<bool> condition, double seconds = 30)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed.TotalSeconds < seconds)
        {
            if (condition())
                return true;

            Thread.Sleep(5);
        }

        return false;
    }

    [Fact]
    public void ProducesAFirstFrameWithoutBeingAsked()
    {
        using var pump = New();

        var frame = WaitForFrame(pump);

        Assert.NotNull(frame);
        Assert.Equal(Width * Height, frame.Pixels.Length);
        Assert.False(frame.FromAutoZoom);
        Assert.True(frame.Scale > 0);
        Assert.Equal(Math.Log(frame.Scale), frame.LogScale, 12);
        Assert.NotEqual("starting", pump.AdapterDescription);
    }

    [Fact]
    public void PostedCommandsTakeEffect()
    {
        using var pump = New();
        var first = WaitForFrame(pump);
        Assert.NotNull(first);
        pump.Recycle(first);

        pump.Post(state => state.IncreaseMaxIter());

        Assert.True(WaitUntil(() =>
        {
            if (!pump.TryTake(out var frame) || frame is null)
                return false;

            var grew = frame.MaxIter > first.MaxIter;
            pump.Recycle(frame);
            return grew;
        }), "the posted change never showed up on a frame");
    }

    [Fact]
    public void AutoZoomProducesAStrictlyDescendingSequence()
    {
        using var pump = New();
        Assert.NotNull(WaitForFrame(pump));

        pump.StartZoom();
        Assert.True(pump.IsZooming);

        var scales = new List<double>();
        var marks = new List<bool>();
        var deadline = Stopwatch.StartNew();

        while (scales.Count < 40 && deadline.Elapsed.TotalSeconds < 30)
        {
            if (pump.TryTake(out var frame) && frame is not null)
            {
                scales.Add(frame.LogScale);
                marks.Add(frame.FromAutoZoom);
                pump.Recycle(frame);
            }
            else
            {
                Thread.Sleep(2);
            }
        }

        pump.StopZoom();

        Assert.True(scales.Count >= 40, $"only {scales.Count} frames arrived");

        // The first frame jumps to the point of interest, so it is not a continuation;
        // every one after it is, and each is exactly one zoom step deeper.
        Assert.False(marks[0]);
        Assert.All(marks.Skip(1), mark => Assert.True(mark));

        var step = Math.Log(1.0 / MandelbrotState.ZoomFactorIncrement);
        for (var i = 1; i < scales.Count; i++)
        {
            Assert.True(scales[i] < scales[i - 1], $"frame {i} did not descend");
            Assert.True(Math.Abs((scales[i - 1] - scales[i]) - step) < 1e-9, $"frame {i} took the wrong step");
        }
    }

    [Fact]
    public void BanksFramesWhileTheyAreCheapAndNeverExceedsTheQueue()
    {
        using var pump = New();
        Assert.NotNull(WaitForFrame(pump));

        pump.StartZoom();

        // Nothing is consuming, so the bank should fill and then hold.
        var filled = WaitUntil(() => pump.QueueDepth >= FramePump.QueueCapacity);
        Assert.True(filled, $"queue only reached {pump.QueueDepth} of {FramePump.QueueCapacity}");

        for (var i = 0; i < 20; i++)
        {
            Assert.True(pump.QueueDepth <= FramePump.QueueCapacity,
                $"queue overran to {pump.QueueDepth}");
            Thread.Sleep(10);
        }

        pump.StopZoom();
    }

    [Fact]
    public void StaysResponsiveWhileTheBankIsFull()
    {
        using var pump = New();
        Assert.NotNull(WaitForFrame(pump));

        pump.StartZoom();
        Assert.True(WaitUntil(() => pump.QueueDepth >= FramePump.QueueCapacity));

        // A full queue must not wedge the render thread against user input.
        pump.StopZoom();
        pump.Post(state => state.Reset());

        Assert.True(WaitUntil(() =>
        {
            while (pump.TryTake(out var frame) && frame is not null)
            {
                var reset = !frame.FromAutoZoom && frame.Scale > 1.0;
                pump.Recycle(frame);
                if (reset)
                    return true;
            }

            return false;
        }), "the reset never got through a full queue");
    }

    [Fact]
    public void RecycledBuffersAreReused()
    {
        using var pump = New();
        Assert.NotNull(WaitForFrame(pump));

        pump.StartZoom();

        // Arrays do not override Equals, so this is reference identity.
        var distinct = new HashSet<uint[]>();
        var taken = 0;
        var deadline = Stopwatch.StartNew();

        while (taken < 400 && deadline.Elapsed.TotalSeconds < 30)
        {
            if (pump.TryTake(out var frame) && frame is not null)
            {
                distinct.Add(frame.Pixels);
                pump.Recycle(frame);
                taken++;
            }
            else
            {
                Thread.Sleep(1);
            }
        }

        pump.StopZoom();

        Assert.True(taken >= 400, $"only {taken} frames arrived");
        Assert.True(distinct.Count <= FramePump.QueueCapacity + 4,
            $"{distinct.Count} distinct buffers for {taken} frames - the pool is not being reused");
    }

    [Fact]
    public void StoppingHaltsProduction()
    {
        using var pump = New();
        Assert.NotNull(WaitForFrame(pump));

        pump.StartZoom();
        Assert.True(WaitUntil(() => pump.QueueDepth > 4));

        pump.StopZoom();
        Assert.False(pump.IsZooming);

        Thread.Sleep(150);
        while (pump.TryTake(out var frame) && frame is not null)
            pump.Recycle(frame);

        Thread.Sleep(250);
        Assert.Equal(0, pump.QueueDepth);
    }

    [Fact]
    public void DisposeShutsTheThreadDownPromptly()
    {
        var pump = New();
        Assert.NotNull(WaitForFrame(pump));
        pump.StartZoom();

        var timer = Stopwatch.StartNew();
        pump.Dispose();
        timer.Stop();

        Assert.True(timer.Elapsed.TotalSeconds < 5, $"dispose took {timer.Elapsed.TotalSeconds:F1}s");
    }
}

