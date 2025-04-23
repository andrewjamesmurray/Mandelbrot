using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Mandelbrot;

public sealed partial class MainWindow : Window
{
    private GpuAdapter _gpu;
    private MandelbrotState _fractalState;
    private WriteableBitmap _bitmap;
    private uint[] StagingBuffer;
    private Int32Rect _rectangle;

    private bool isZooming = false;
    private bool isPanning = false;
    private Point startPanPoint;
    private int renderMs = 0;
    //private float fps = 0f;

    public MainWindow()
    {
        InitializeComponent();

        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Fant);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        // Add event handlers for zooming, panning, and resizing.
        this.MouseWheel += MainWindow_MouseWheel;
        this.MouseRightButtonDown += MainWindow_MouseRightButtonDown;
        this.MouseLeftButtonDown += MouseLeftButtonDownHandler;
        this.MouseLeftButtonUp += MouseLeftButtonUpHandler;
        this.MouseMove += MouseMoveHandler;
        this.KeyDown += KeyUpHandler;
        this.Loaded += LoadedHandler;
    }

    private void LoadedHandler(object sender, RoutedEventArgs e)
    {
        var width = (short)Width;
        var height = (short)Height;

        ResLabel.Text = (int)width + "x" + (int)height;

        _fractalState = new MandelbrotState(width, height);
        _bitmap = new WriteableBitmap(width, height, 140, 140, PixelFormats.Bgra32, null);
        _rectangle = new Int32Rect(0, 0, width, height);
        MandelbrotImage.Source = _bitmap;
        _gpu = GpuAdapter.Create(width, height, Palette.GenerateColorLookup2());

        // Load the kernel once during initialization.
        StagingBuffer = new uint[width * height];

        GenerateMandelbrotFrame();
    }

    private void MainWindow_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _fractalState.Reset();
        GenerateMandelbrotFrame();
    }

    /// <summary>
    /// Start panning mode
    /// </summary>
    private void MouseLeftButtonDownHandler(object sender, MouseButtonEventArgs e)
    {
        isPanning = true;
        startPanPoint = e.GetPosition(MandelbrotImage);
        MandelbrotImage.CaptureMouse();
    }

    /// <summary>
    /// End panning mode
    /// </summary>
    private void MouseLeftButtonUpHandler(object sender, MouseButtonEventArgs e)
    {
        isPanning = false;
        MandelbrotImage.ReleaseMouseCapture();
    }

    private void MouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!isPanning)
            return;

        // Calculate the movement delta.
        var currentPoint = e.GetPosition(MandelbrotImage);
        var deltaX = currentPoint.X - startPanPoint.X;
        var deltaY = currentPoint.Y - startPanPoint.Y;

        _fractalState.Move(deltaX, deltaY);

        // Update the start point for the next movement calculation.
        startPanPoint = currentPoint;

        GenerateMandelbrotFrame();
    }

    private void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Get the mouse position relative to the image.
        var mousePos = e.GetPosition(MandelbrotImage);

        _fractalState.ZoomAndMove(e.Delta, mousePos.X, mousePos.Y);

        GenerateMandelbrotFrame();
    }

    private void KeyUpHandler(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                Environment.Exit(0);
                break;

            case Key.Space:
                if (!isZooming)
                {
                    isZooming = true;
                    StartAutoZoom();
                }
                else
                {
                    isZooming = false;
                }
                break;

            case Key.Up:
                _fractalState.IncreaseMaxIter();
                GenerateMandelbrotFrame();
                break;

            case Key.Down:
                _fractalState.DecreaseMaxIter();
                GenerateMandelbrotFrame();
                break;

            case Key.Enter:
                break;
        }
    }

    private void UpdateTextOverlay()
    {
        var fps = 1000 / renderMs;

        var fpsText    = "fps:     " + fps.ToString("0");
        var renderText = "render:  " + renderMs.ToString("0") + " ms";
        var iterText   = "maxIter: " + _fractalState.MaxIter;
        var scaleText  = "scale:   " + _fractalState.Scale.ToString("E");

        FpsLabel.Text = 
            fpsText + "\n" + 
            renderText + "\n" +
            iterText + "\n" + 
            scaleText + "\n";
    }

    private async void StartAutoZoom()
    {
        const int TargetFps = 120;
        const float TargetDelay = 1000f / TargetFps;

        _fractalState.ResetForZoom();

        while (isZooming && _fractalState.ZoomNext())
        {
            GenerateMandelbrotFrame();            

            float delay = (renderMs > TargetDelay) ? 1 : (TargetDelay - renderMs);
            await Task.Delay((int)delay);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if(_gpu != null) 
            _gpu.Dispose();

        base.OnClosed(e);
    }

    private void GenerateMandelbrotFrame()
    {
        var sw = new Stopwatch();
        sw.Start();

        var parameters = _fractalState.GenerateParameters();

        _gpu.Kernel(parameters, StagingBuffer);

        CreateFrameBitmap(StagingBuffer);

        sw.Stop();

        renderMs = (int)sw.Elapsed.TotalMilliseconds;
        UpdateTextOverlay();
    }

    private WriteableBitmap CreateFrameBitmap(uint[] pixels)
    {
        _bitmap.Lock();

        var numBytes = pixels.Length * sizeof(uint);

        unsafe
        {
            Buffer.MemoryCopy(
                source: Unsafe.AsPointer(ref pixels[0]),
                destination: _bitmap.BackBuffer.ToPointer(),
                destinationSizeInBytes: numBytes,
                sourceBytesToCopy: numBytes
            );
        }

        _bitmap.AddDirtyRect(_rectangle);
        _bitmap.Unlock();

        return _bitmap;
    }
}
