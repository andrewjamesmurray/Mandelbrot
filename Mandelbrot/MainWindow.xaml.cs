using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mandelbrot;

public sealed partial class MainWindow : Window
{
    /// <summary>The overlay is text layout on the UI thread; it does not need 60 Hz.</summary>
    const double OverlayIntervalMs = 100;

    private readonly uint[] _palette = Palette.GenerateColorLookup();
    private readonly DisplayDescent _descent = new();
    private readonly ScaleTransform _magnification = new(1.0, 1.0);

    private FramePump _pump = null!;
    private WriteableBitmap _bitmap = null!;
    private Int32Rect _rectangle;
    private int _pixelCount;

    private bool _isPanning;
    private Point _startPanPoint;

    private RenderedFrame? _latest;
    private TimeSpan _lastRenderingTime;
    private double _frameMs;
    private double _msSinceOverlay = OverlayIntervalMs;
    private string _announcement = "";

    public MainWindow()
    {
        InitializeComponent();

        // The bitmap is sized to device pixels and tagged with the matching DPI, so it
        // lands on screen 1:1. Nearest-neighbour then skips WPF's resampling filter
        // instead of running Fant over a multi-megapixel image every frame.
        RenderOptions.SetBitmapScalingMode(MandelbrotImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(MandelbrotImage, EdgeMode.Aliased);
        MandelbrotImage.RenderTransform = _magnification;

        this.MouseWheel += MainWindow_MouseWheel;
        this.MouseRightButtonDown += MainWindow_MouseRightButtonDown;
        this.MouseLeftButtonDown += MouseLeftButtonDownHandler;
        this.MouseLeftButtonUp += MouseLeftButtonUpHandler;
        this.MouseMove += MouseMoveHandler;
        this.KeyDown += KeyDownHandler;
        this.Loaded += LoadedHandler;
    }

    private void LoadedHandler(object sender, RoutedEventArgs e)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));

        _pixelCount = width * height;
        _bitmap = new WriteableBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Bgra32, null);
        _rectangle = new Int32Rect(0, 0, width, height);
        MandelbrotImage.Source = _bitmap;

        _pump = new FramePump(width, height, _palette);
        ResLabel.Text = $"{width}x{height}";

        // Driving from the composition clock paces the display against the monitor.
        // All the actual work happens on the pump's thread, so a 300 ms frame no
        // longer freezes the window.
        CompositionTarget.Rendering += RenderingHandler;
    }

    private void RenderingHandler(object? sender, EventArgs e)
    {
        var elapsedSeconds = 1.0 / 60.0;
        if (e is RenderingEventArgs args)
        {
            if (_lastRenderingTime != TimeSpan.Zero)
            {
                _frameMs = (args.RenderingTime - _lastRenderingTime).TotalMilliseconds;
                elapsedSeconds = Math.Clamp(_frameMs / 1000.0, 1e-4, 0.25);
            }

            _lastRenderingTime = args.RenderingTime;
        }

        if (_pump.TryTake(out var frame) && frame is not null)
            Present(frame);

        AdvanceDisplayScale(elapsedSeconds);

        _msSinceOverlay += _frameMs;
        if (_msSinceOverlay >= OverlayIntervalMs)
        {
            _msSinceOverlay = 0;
            UpdateTextOverlay();
        }
    }

    private void Present(RenderedFrame frame)
    {
        _bitmap.Lock();
        try
        {
            unsafe
            {
                var destination = new Span<uint>(_bitmap.BackBuffer.ToPointer(), _pixelCount);
                frame.Pixels.AsSpan(0, _pixelCount).CopyTo(destination);
            }

            _bitmap.AddDirtyRect(_rectangle);
        }
        finally
        {
            _bitmap.Unlock();
        }

        _descent.OnFrame(frame.LogScale, frame.FromAutoZoom);

        var previous = _latest;
        _latest = frame;
        if (previous is not null)
            _pump.Recycle(previous);
    }

    /// <summary>
    /// Advances the displayed scale and magnifies the newest frame to cover whatever
    /// the render thread has not caught up with. See <see cref="DisplayDescent"/>.
    /// </summary>
    private void AdvanceDisplayScale(double elapsedSeconds)
    {
        _descent.Advance(elapsedSeconds, _pump.IsZooming);

        var factor = _descent.Magnification;
        _magnification.ScaleX = factor;
        _magnification.ScaleY = factor;
    }

    private void MainWindow_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pump.StopZoom();
        _pump.Post(state => state.Reset());
    }

    /// <summary>
    /// Start panning mode
    /// </summary>
    private void MouseLeftButtonDownHandler(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _startPanPoint = e.GetPosition(MandelbrotImage);
        MandelbrotImage.CaptureMouse();
    }

    /// <summary>
    /// End panning mode
    /// </summary>
    private void MouseLeftButtonUpHandler(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        MandelbrotImage.ReleaseMouseCapture();
    }

    private void MouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
            return;

        // Calculate the movement delta.
        var currentPoint = e.GetPosition(MandelbrotImage);
        var deltaX = currentPoint.X - _startPanPoint.X;
        var deltaY = currentPoint.Y - _startPanPoint.Y;

        // Update the start point for the next movement calculation.
        _startPanPoint = currentPoint;

        _pump.StopZoom();
        _pump.Post(state => state.Move(deltaX, deltaY));
    }

    private void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Get the mouse position relative to the image.
        var mousePos = e.GetPosition(MandelbrotImage);
        var delta = e.Delta;

        _pump.StopZoom();
        _pump.Post(state => state.ZoomAndMove(delta, mousePos.X, mousePos.Y));
    }

    private void KeyDownHandler(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;

            case Key.Space:
                if (_pump.IsZooming)
                    _pump.StopZoom();
                else
                    _pump.StartZoom();
                break;

            case Key.Up:
                _pump.Post(state => state.IncreaseMaxIter());
                break;

            case Key.Down:
                _pump.Post(state => state.DecreaseMaxIter());
                break;

            case Key.Enter:
                CopyCentreToClipboard();
                break;

            case Key.D1:
                Toggle(state => state.TogglePeriodicityOptimization(),
                    state => $"Periodicity Checks: {state.UsePeriodicityOptimization}");
                break;

            case Key.D2:
                Toggle(state => state.ToggleBulbCheckOptimization(),
                    state => $"Bulb Checks: {state.UseBulbCheckOptimization}");
                break;

            case Key.D3:
                Toggle(state => state.ToggleSeriesApproximation(),
                    state => $"Series Approximation: {state.UseSeriesApproximation}");
                break;

            case Key.D0:
                Toggle(state => state.ForcedMode = state.ForcedMode is null ? RenderMode.DirectDouble : null,
                    state => state.ForcedMode is null ? "Kernel: auto" : "Kernel: forced fp64 direct");
                break;

            case Key.G:
                _pump.SwapAdapter();
                break;
        }
    }

    /// <summary>Applies a toggle on the render thread and reports the result back.</summary>
    private void Toggle(Action<MandelbrotState> change, Func<MandelbrotState, string> describe)
    {
        _pump.Post(state =>
        {
            change(state);
            var message = describe(state);
            Dispatcher.BeginInvoke(() => _announcement = message);
        });
    }

    private void CopyCentreToClipboard() =>
        _pump.Post(state =>
        {
            var description = state.DescribeCenter();
            Dispatcher.BeginInvoke(() =>
            {
                Clipboard.SetText(description);
                _announcement = "centre copied";
            });
        });

    private void UpdateTextOverlay()
    {
        var frame = _latest;
        if (frame is null)
            return;

        FpsLabel.Text =
            $"render:  {frame.RenderMs:F1} ms\n" +
            $"frame:   {_frameMs:F1} ms\n" +
            $"queued:  {_pump.QueueDepth} / {FramePump.QueueCapacity}\n" +
            $"descent: {_descent.DecadesPerSecond:F2} decades/s\n" +
            $"maxIter: {frame.MaxIter}\n" +
            $"skipped: {frame.SkippedIterations}\n" +
            $"kernel:  {frame.Mode}\n" +
            $"scale:   {Math.Exp(_descent.DisplayLogScale):E}\n";

        ResLabel.Text = _announcement.Length > 0
            ? $"{_rectangle.Width}x{_rectangle.Height}\n{_pump.AdapterDescription}\n{_announcement}"
            : $"{_rectangle.Width}x{_rectangle.Height}\n{_pump.AdapterDescription}";
    }

    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering -= RenderingHandler;
        _pump?.Dispose();

        base.OnClosed(e);
    }
}
