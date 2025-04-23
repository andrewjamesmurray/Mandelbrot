using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mandelbrot;

public sealed partial class MainWindow : Window
{
    private readonly GpuAdapter _gpu;
    private readonly MandelbrotState _fractalState;
    private readonly WriteableBitmap bitmap;
    private readonly uint[] StagingBuffer;
    private readonly Int32Rect _rectangle;

    private bool isZooming = false;
    private bool isPanning = false;
    private Point startPanPoint;
    private float fps = 0f;

    public MainWindow()
    {
        InitializeComponent();

        width = 2560; // (short)SystemParameters.MaximizedPrimaryScreenWidth;
        height = 1080; // (short)SystemParameters.MaximizedPrimaryScreenHeight;

        this.Width = width;
        this.Height = height;


        _fractalState = new MandelbrotState(width, height);

        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Fant);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        _gpu = GpuAdapter.Create(width, height, Palette.GenerateColorLookup2());

        // Load the kernel once during initialization.
        StagingBuffer = new uint[width * height];

        // Add event handlers for zooming, panning, and resizing.
        this.MouseWheel += MainWindow_MouseWheel;
        this.MouseRightButtonDown += MainWindow_MouseRightButtonDown;
        this.MouseLeftButtonDown += MouseLeftButtonDownHandler;
        this.MouseLeftButtonUp += MouseLeftButtonUpHandler;
        this.MouseMove += MouseMoveHandler;
        this.KeyDown += KeyUpHandler;

        bitmap = new WriteableBitmap(width, height, 140, 140, PixelFormats.Bgra32, null);
        _rectangle = new Int32Rect(0, 0, width, height);
        MandelbrotImage.Source = bitmap;

        _gpu.Synchronize();

        // Generate the initial Mandelbrot set.
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
        }
    }

    private void UpdateTextOverlay()
    {
        var fpsText = fps.ToString("0") + " fps";
        var scaleText = "scale: " + _fractalState.Scale.ToString("E");
        var iterText = "maxIter: " + _fractalState.MaxIter;

        FpsLabel.Text = fpsText + ", " + iterText + ", " + scaleText;
    }

    private async void StartAutoZoom()
    {
        _fractalState.ResetForZoom();

        while (isZooming && _fractalState.ZoomNext())
        {
            GenerateMandelbrotFrame();
            await Task.Delay(1);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _gpu.Dispose();
        base.OnClosed(e);
    }

    private void GenerateMandelbrotFrame()
    {
        var sw = new Stopwatch();
        sw.Start();

        var parameters = _fractalState.GenerateParameters(_gpu);

        _gpu.Kernel(parameters, StagingBuffer);

        CreateFrameBitmap(StagingBuffer);

        sw.Stop();

        fps = (float)(1000 / sw.Elapsed.TotalMilliseconds);
        UpdateTextOverlay();
    }

    private WriteableBitmap CreateFrameBitmap(uint[] pixels)
    {
        bitmap.Lock();

        var numBytes = pixels.Length * sizeof(uint);

        unsafe
        {
            Buffer.MemoryCopy(
                source: Unsafe.AsPointer(ref pixels[0]),
                destination: bitmap.BackBuffer.ToPointer(),
                destinationSizeInBytes: numBytes,
                sourceBytesToCopy: numBytes
            );
        }

        bitmap.AddDirtyRect(_rectangle);
        bitmap.Unlock();

        return bitmap;
    }
}
