namespace Mandelbrot;

/// <summary>
/// Decides what scale the screen is showing, given the frames that have actually
/// finished.
///
/// The auto-zoom cannot produce 60 frames a second at depth - one frame at 1e-16
/// costs about twenty frame budgets - so rather than stutter, the displayed scale
/// glides continuously and the newest finished frame is magnified to cover whatever
/// the render thread has not reached yet.
///
/// The displayed scale descends at the rate frames are observed to arrive, held
/// between two bounds:
///
///   - never shallower than the newest frame, because a frame cannot be shrunk past
///     its own edges;
///   - never more than one zoom step deeper, so the magnification stays under the
///     zoom ratio (about 5%) and the picture never looks soft for long.
///
/// No descent rate is configured anywhere. It follows the frames: full speed while
/// the pump keeps up or has frames banked, tapering smoothly as frames get expensive.
/// </summary>
public sealed class DisplayDescent
{
    private readonly double _velocitySmoothing;
    private readonly double _bootstrapSeconds;

    private double _clock;
    private double _lastArrival;
    private double _velocity;
    private bool _haveVelocity;

    public DisplayDescent()
        : this(Math.Log(1.0 / MandelbrotState.ZoomFactorIncrement), 0.3, 0.25)
    {
    }

    public DisplayDescent(double maxLagEFolds, double velocitySmoothing, double bootstrapSeconds)
    {
        MaxLagEFolds = maxLagEFolds;
        _velocitySmoothing = velocitySmoothing;
        _bootstrapSeconds = bootstrapSeconds;
    }

    /// <summary>How far the display may run past the newest finished frame.</summary>
    public double MaxLagEFolds { get; }

    /// <summary>ln(scale) of the frame currently on screen.</summary>
    public double RenderedLogScale { get; private set; }

    /// <summary>ln(scale) the viewer is actually being shown.</summary>
    public double DisplayLogScale { get; private set; }

    public bool HasFrame { get; private set; }

    /// <summary>
    /// Enlargement to apply to the current frame. Always at least 1, and never more
    /// than one zoom step's worth.
    /// </summary>
    public double Magnification => HasFrame ? Math.Exp(RenderedLogScale - DisplayLogScale) : 1.0;

    /// <summary>Descent over the last step, for the overlay.</summary>
    public double EFoldsPerSecond { get; private set; }

    public double DecadesPerSecond => EFoldsPerSecond / Math.Log(10.0);

    /// <summary>
    /// A frame finished. Frames from a pan, zoom or reset are the view the user asked
    /// for, so the display snaps onto them rather than gliding.
    /// </summary>
    public void OnFrame(double logScale, bool fromAutoZoom)
    {
        if (!fromAutoZoom || !HasFrame)
        {
            RenderedLogScale = logScale;
            DisplayLogScale = logScale;
            _velocity = 0.0;
            _haveVelocity = false;
            _lastArrival = _clock;
            HasFrame = true;
            return;
        }

        var interval = _clock - _lastArrival;
        if (interval > 0.0)
        {
            var instant = (logScale - RenderedLogScale) / interval;
            _velocity = _haveVelocity ? _velocity + (instant - _velocity) * _velocitySmoothing : instant;
            _haveVelocity = true;
        }

        _lastArrival = _clock;
        RenderedLogScale = logScale;
    }

    public void Advance(double elapsedSeconds, bool zooming)
    {
        _clock += elapsedSeconds;

        if (!HasFrame)
            return;

        if (!zooming)
        {
            DisplayLogScale = RenderedLogScale;
            EFoldsPerSecond = 0.0;
            _haveVelocity = false;
            return;
        }

        // Until two frames have been seen there is no measured rate; creep forward at
        // a rate that would use the whole lag allowance in bootstrapSeconds, which the
        // first real measurement then replaces.
        var velocity = _haveVelocity ? _velocity : -MaxLagEFolds / _bootstrapSeconds;

        var before = DisplayLogScale;
        var next = before + velocity * elapsedSeconds;

        // Never deeper than one zoom step past the newest frame...
        next = Math.Max(next, RenderedLogScale - MaxLagEFolds);

        // ...and never shallower than it, or the frame would have to be shrunk past
        // its own edges. If the pump has outrun us, this snaps forward by at most one
        // step, which is what keeps the magnification at or above 1.
        next = Math.Min(next, RenderedLogScale);

        // The zoom only ever goes one way.
        DisplayLogScale = Math.Min(next, before);
        EFoldsPerSecond = elapsedSeconds > 0.0 ? (before - DisplayLogScale) / elapsedSeconds : 0.0;
    }
}
