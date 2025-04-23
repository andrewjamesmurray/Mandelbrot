using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mandelbrot;

public sealed partial class MainWindow : Window
{
    private short width;
    private short height;

    private double centerX = -0.74;
    private double centerY = 0.15;
    private double scale = 2.5;
    private int maxIter = 50;
    private double aspectRatio = 1.25;
    private bool isPanning = false;
    private bool isZooming = false;
    private Point startPanPoint;
    private WriteableBitmap bitmap;
    private float fps = 0f;
    private readonly uint[] StagingBuffer;

    private readonly GpuAdapter _gpu;

    public MainWindow()
    {
        InitializeComponent();

        width = 2560; // (short)SystemParameters.MaximizedPrimaryScreenWidth;
        height = 1080; // (short)SystemParameters.MaximizedPrimaryScreenHeight;

        this.Width = width;
        this.Height = height;
        this.aspectRatio = (double)width / height;

        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Fant);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        _gpu = GpuAdapter.Create(width, height, Palette.GenerateColorLookup2());

        // Load the kernel once during initialization.
        StagingBuffer = new uint[width * height];

        // Add event handlers for zooming, panning, and resizing.
        this.MouseWheel += MainWindow_MouseWheel;
        this.MouseRightButtonDown += MainWindow_MouseRightButtonDown;
        this.MouseLeftButtonDown += MainWindow_MouseLeftButtonDown;
        this.MouseLeftButtonUp += MainWindow_MouseLeftButtonUp;
        this.MouseMove += MainWindow_MouseMove;
        this.KeyDown += KeyUpHandler;

        bitmap = new WriteableBitmap(width, height, 140, 140, PixelFormats.Bgra32, null);
        MandelbrotImage.Source = bitmap;

        _gpu.Synchronize();

        // Generate the initial Mandelbrot set.
        GenerateMandelbrotFrame();
    }


    private void MainWindow_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Reset zoom and panning.
        centerX = -0.5;
        centerY = 0.0;
        scale = 3.5;

        // Regenerate the Mandelbrot set with the updated dimensions.
        GenerateMandelbrotFrame();
    }

    private void MainWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Start panning mode.
        isPanning = true;
        startPanPoint = e.GetPosition(MandelbrotImage);
        MandelbrotImage.CaptureMouse();
    }

    private void MainWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // End panning mode.
        isPanning = false;
        MandelbrotImage.ReleaseMouseCapture();
    }

    private void MainWindow_MouseMove(object sender, MouseEventArgs e)
    {
        if (isPanning)
        {
            // Calculate the movement delta.
            Point currentPoint = e.GetPosition(MandelbrotImage);
            double deltaX = (currentPoint.X - startPanPoint.X) / width * scale;
            double deltaY = (currentPoint.Y - startPanPoint.Y) / height * scale;

            // Adjust the center point based on the movement.
            centerX -= deltaX;
            centerY -= deltaY;

            // Update the start point for the next movement calculation.
            startPanPoint = currentPoint;

            // Regenerate the Mandelbrot set with the new parameters.
            GenerateMandelbrotFrame();
        }
    }

    private void MainWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Get the mouse position relative to the image.
        Point mousePos = e.GetPosition(MandelbrotImage);

        // Normalize mouse position to [-1, 1] range in the complex plane.
        double normX = (mousePos.X / width - 0.5) * scale;
        double normY = (mousePos.Y / height - 0.5) * scale;

        // Adjust scale based on the scroll direction
        scale *= e.Delta > 0 ? 0.95 : 1.05;

        // Adjust the center point based on the normalized mouse position.
        centerX += normX * (1 - scale / (scale * (e.Delta > 0 ? 0.9 : 1.1)));
        centerY += normY * (1 - scale / (scale * (e.Delta > 0 ? 0.9 : 1.1)));

        // Regenerate the Mandelbrot set with the new parameters.
        GenerateMandelbrotFrame();
    }

    private void KeyUpHandler(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
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
            case Key.Escape:
                Close();
                Environment.Exit(0);
                break;
            case Key.Up:
                maxIter += 50;
                GenerateMandelbrotFrame();
                break;
            case Key.Down:
                maxIter = Math.Max(maxIter - 50, 0);
                GenerateMandelbrotFrame();
                break;
        }
    }

    private void UpdateTextOverlay()
    {
        var fpsText = fps.ToString("0") + " fps";
        var scaleText = "scale: " + scale.ToString("E");
        var iterText = "maxIter: " + maxIter;

        FpsLabel.Text = fpsText + ", " + iterText + ", " + scaleText;
    }

    private async void StartAutoZoom()
    {
        // Set fixed coordinates for auto-zoom, near a point of interest.
        centerX = -0.74335165531181;
        centerY = +0.13138323820835;

        // Zoom speed multiplier.
        const double zoomFactorIncrement = 0.975;

        var sw = new Stopwatch();
        sw.Start();
        int frames = 0;

        while (isZooming && scale > 1e-13) // Stop when zoom factor is extremely high.
        {
            // Reduce the zoom scale.
            scale *= zoomFactorIncrement;
            maxIter = ComputeMaxIter(scale);

            // Render the Mandelbrot set at the new zoom level.
            GenerateMandelbrotFrame();

            // Allow the UI to update by awaiting a small delay ensuring UI responsiveness.
            await Task.Delay(1);

            frames++;
        }

        sw.Stop();
    }

    protected override void OnClosed(EventArgs e)
    {
        _gpu.Dispose();

        // Cleanup resources on window close.
        base.OnClosed(e);
    }

    private static int ComputeMaxIter(double scale)
    {
        // Heuristic based on the inverse of zoom scale
        // Logarithmic boost keeps growth manageable at deep zooms
        double zoom = 1.0 / Math.Max(scale, 1e-13);

        // Instead of log(zoom), use log(max(zoom, 1)) so log never goes negative
        double safeLog = Math.Log10(Math.Max(zoom, 1.0));

        // Grow iteration count smoothly with zoom depth
        return (int)(50 + 200 * Math.Pow(safeLog, 2));
    }

    private void GenerateMandelbrotFrame()
    {
        var sw = new Stopwatch();
        sw.Start();

        var adjustedScaleX = scale;
        var adjustedScaleY = scale;

        if (aspectRatio >= 1.0)
        {
            adjustedScaleX = scale * aspectRatio;
            adjustedScaleY = scale;
        }
        else
        {
            adjustedScaleX = scale;
            adjustedScaleY = scale / aspectRatio;
        }

        var parameters = new MandelbrotParameters
        { 
            Output = _gpu.OutputBufferView,
            Palette = _gpu.PaletteView,
            CenterX = centerX,
            CenterY = centerY,
            Scale = scale,
            Width = width,
            Height = height,            
            AdjustedScaleXPerPixel = adjustedScaleX / width,    
            AdjustedScaleYPerPixel = adjustedScaleY / height,   
            OffsetX = -(adjustedScaleX / 2) + centerX,
            OffsetY = -(adjustedScaleY / 2) + centerY,
            maxIter = maxIter
        };

        _gpu.Kernel(parameters, StagingBuffer);

        CreateFrameBitmap(StagingBuffer);

        sw.Stop();
        fps = (float)(1000 / sw.Elapsed.TotalMilliseconds);
        UpdateTextOverlay();
    }

    private WriteableBitmap CreateFrameBitmap(uint[] pixels)
    {
        bitmap.Lock();

        unsafe
        {
            Buffer.MemoryCopy(
                source: Unsafe.AsPointer(ref pixels[0]),
                destination: bitmap.BackBuffer.ToPointer(),
                destinationSizeInBytes: pixels.Length * sizeof(uint),
                sourceBytesToCopy: pixels.Length * sizeof(uint)
            );
        }

        bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        bitmap.Unlock();

        return bitmap;
    }
}
