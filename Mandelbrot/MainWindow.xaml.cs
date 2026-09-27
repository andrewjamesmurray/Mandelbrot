using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mandelbrot;

public sealed partial class MainWindow : Window
{
    /// <summary>The overlay is text layout on the UI thread; it does not need 60 Hz.</summary>
    const double OverlayIntervalMs = 100;

    private readonly Stopwatch _renderTimer = new();
    private readonly uint[] _palette = Palette.GenerateColorLookup();

    private IComputeAdapter _compute = null!;
    private MandelbrotState _fractalState = null!;
    private WriteableBitmap _bitmap = null!;
    private Int32Rect _rectangle;
    private int _pixelCount;

    private bool _isZooming;
    private bool _isPanning;
    private bool _frameDirty = true;
    private Point _startPanPoint;

    private double _renderMs;
    private double _frameMs;
    private TimeSpan _lastRenderingTime;
    private double _msSinceOverlay = OverlayIntervalMs;

    public MainWindow()
    {
        InitializeComponent();

        // The bitmap is sized to device pixels and tagged with the matching DPI, so it
        // lands on screen 1:1. Nearest-neighbour then skips WPF's resampling filter
        // entirely instead of running Fant over a multi-megapixel image every frame.
        RenderOptions.SetBitmapScalingMode(MandelbrotImage, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(MandelbrotImage, EdgeMode.Aliased);

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
        _fractalState = new MandelbrotState(width, height);
        _bitmap = new WriteableBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Bgra32, null);
        _rectangle = new Int32Rect(0, 0, width, height);

        _compute = GpuAdapter.Create(width, height, _palette);

        MandelbrotImage.Source = _bitmap;
        ResLabel.Text = $"{width}x{height}\n{_compute.Description}";

        // Driving from the composition clock paces frames against the display instead of
        // against Task.Delay, whose resolution is coarser than a frame to begin with.
        CompositionTarget.Rendering += RenderingHandler;
    }

    private void RenderingHandler(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs args)
        {
            if (_lastRenderingTime != TimeSpan.Zero)
                _frameMs = (args.RenderingTime - _lastRenderingTime).TotalMilliseconds;

            _lastRenderingTime = args.RenderingTime;
        }

        if (_isZooming && !_fractalState.ZoomNext())
            _isZooming = false;

        if (!_isZooming && !_frameDirty)
            return;

        _frameDirty = false;
        GenerateMandelbrotFrame();

        _msSinceOverlay += _frameMs;
        if (_msSinceOverlay >= OverlayIntervalMs)
        {
            _msSinceOverlay = 0;
            UpdateTextOverlay();
        }
    }

    private void MainWindow_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isZooming = false;
        _fractalState.Reset();
        Invalidate();
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

        _fractalState.Move(deltaX, deltaY);

        // Update the start point for the next movement calculation.
        _startPanPoint = currentPoint;

        Invalidate();
    }

    private void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Get the mouse position relative to the image.
        var mousePos = e.GetPosition(MandelbrotImage);

        _fractalState.ZoomAndMove(e.Delta, mousePos.X, mousePos.Y);

        Invalidate();
    }

    private void KeyDownHandler(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;

            case Key.Space:
                _isZooming = !_isZooming;
                if (_isZooming)
                    _fractalState.ResetForZoom();
                Invalidate();
                break;

            case Key.Up:
                _fractalState.IncreaseMaxIter();
                Invalidate();
                break;

            case Key.Down:
                _fractalState.DecreaseMaxIter();
                Invalidate();
                break;

            case Key.Enter:
                Clipboard.SetText(_fractalState.DescribeCenter());
                break;

            case Key.D1:
                _fractalState.TogglePeriodicityOptimization();
                Announce($"Periodicity Checks: {_fractalState.UsePeriodicityOptimization}");
                break;

            case Key.D2:
                _fractalState.ToggleBulbCheckOptimization();
                Announce($"Bulb Checks: {_fractalState.UseBulbCheckOptimization}");
                break;

            case Key.D3:
                _fractalState.ToggleSeriesApproximation();
                Announce($"Series Approximation: {_fractalState.UseSeriesApproximation}");
                break;

            case Key.D0:
                _fractalState.ForcedMode = _fractalState.ForcedMode is null ? RenderMode.DirectDouble : null;
                Announce(_fractalState.ForcedMode is null ? "Kernel: auto" : "Kernel: forced fp64 direct");
                break;

            case Key.G:
                SwapComputeAdapter();
                break;
        }
    }

    private void Announce(string message)
    {
        ResLabel.Text = message;
        Invalidate();
    }

    private void Invalidate() => _frameDirty = true;

    /// <summary>Switches between the GPU and the CPU renderer, which is handy for
    /// checking that the two agree on a frame.</summary>
    private void SwapComputeAdapter()
    {
        var wasGpu = _compute is GpuAdapter;
        _compute.Dispose();
        _compute = wasGpu
            ? CpuAdapter.Create(_rectangle.Width, _rectangle.Height, _palette)
            : GpuAdapter.Create(_rectangle.Width, _rectangle.Height, _palette);

        Announce(_compute.Description);
    }

    private void UpdateTextOverlay()
    {
        FpsLabel.Text =
            $"render:  {_renderMs:F1} ms\n" +
            $"frame:   {_frameMs:F1} ms\n" +
            $"maxIter: {_fractalState.MaxIter}\n" +
            $"skipped: {_fractalState.SkippedIterations}\n" +
            $"kernel:  {_fractalState.Mode}\n" +
            $"scale:   {_fractalState.Scale:E}\n";
    }

    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering -= RenderingHandler;
        _compute?.Dispose();

        base.OnClosed(e);
    }

    private void GenerateMandelbrotFrame()
    {
        _renderTimer.Restart();

        var (parameters, orbit) = _fractalState.PrepareFrame();

        _bitmap.Lock();
        try
        {
            unsafe
            {
                // Straight into the locked back buffer: no staging array, and no second
                // full-frame copy to get the pixels there.
                var destination = new Span<uint>(_bitmap.BackBuffer.ToPointer(), _pixelCount);
                _compute.Render(parameters, _fractalState.Mode, _fractalState.Optimizations, orbit, destination);
            }

            _bitmap.AddDirtyRect(_rectangle);
        }
        finally
        {
            _bitmap.Unlock();
        }

        _renderTimer.Stop();
        _renderMs = _renderTimer.Elapsed.TotalMilliseconds;
    }
}
