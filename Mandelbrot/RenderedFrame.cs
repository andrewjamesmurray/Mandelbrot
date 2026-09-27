namespace Mandelbrot;

/// <summary>
/// One finished frame, handed from the render thread to the UI thread. Carries the
/// view it was computed for so the UI can work out how far the display has drifted
/// past it, plus everything the overlay wants - the UI never touches the state.
/// </summary>
public sealed class RenderedFrame
{
    public required uint[] Pixels { get; init; }

    public double Scale { get; set; }

    /// <summary>ln(Scale). The zoom is geometric, so the useful arithmetic is all in logs.</summary>
    public double LogScale { get; set; }

    public int MaxIter { get; set; }
    public int SkippedIterations { get; set; }
    public RenderMode Mode { get; set; }
    public double RenderMs { get; set; }

    /// <summary>False for frames produced by a pan, zoom or reset rather than the auto-zoom.</summary>
    public bool FromAutoZoom { get; set; }
}
