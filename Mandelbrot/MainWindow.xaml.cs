// Fast Mandelbrot Rendering with GPU in C#.
// Guy Fernando - i4cy (2024)
// Optimized by Andrew Murray (2025)

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.Sequencers;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

namespace Mandelbrot;

public sealed partial class MainWindow : Window
{
    private short width;
    private short height;

    private double centerX = -0.74;
    private double centerY = 0.15;
    private double scale = 2.5;

    private bool isPanning = false;
    private bool isZooming = false;
    private Point startPanPoint;

    private Context context;
    private Accelerator accelerator;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, double, double, double, short, short> kernel;
    private static readonly uint[] Gradient = Palette.GenerateColorLookup();

    public MainWindow()
    {
        InitializeComponent();

        // Initialize ILGPU context and accelerator.
        context = Context.Create(builder => builder.Cuda());
        accelerator = context.GetPreferredDevice(preferCPU: false).CreateAccelerator(context);

        // Load the kernel once during initialization.
        kernel = accelerator.LoadAutoGroupedStreamKernel
            <Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, double, double, double, short, short>(MandelbrotKernel.ComputeMandelbrotFrame);

        gradientBuffer = accelerator.Allocate1D<uint>(Gradient.Length);
        gradientBuffer.CopyFromCPU(Gradient);

        ReallocateBuffer();

        accelerator.Synchronize();

        // Add event handlers for zooming, panning, and resizing.
        this.MouseWheel += MainWindow_MouseWheel;
        this.MouseRightButtonDown += MainWindow_MouseRightButtonDown;
        this.MouseLeftButtonDown += MainWindow_MouseLeftButtonDown;
        this.MouseLeftButtonUp += MainWindow_MouseLeftButtonUp;
        this.MouseMove += MainWindow_MouseMove;
        this.SizeChanged += MainWindow_SizeChanged;
        this.KeyDown += MainWindow_KeyDown;

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

    private void ReallocateBuffer()
    {
        if (buffer != null) buffer.Dispose();
        buffer = accelerator.Allocate1D<uint>(width * height);
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Update the width and height based on the new window size.
        width = (short)e.NewSize.Width;
        height = (short)e.NewSize.Height;

        ReallocateBuffer();

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

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            if (!isZooming)
            {
                isZooming = true;
                StartAutoZoom();
            }
            else
            {
                isZooming = false;
            }
        }
    }

    private async void StartAutoZoom()
    {
        // Set fixed coordinates for auto-zoom, near a point of interest.
        centerX = -0.74335165531181;
        centerY = +0.13138323820835;

        // Zoom speed multiplier.
        const double zoomFactorIncrement = 0.95;

        var sw = new Stopwatch();
        sw.Start();
        int frames = 0;

        while (isZooming && scale > 1e-13) // Stop when zoom factor is extremely high.
        {
            // Reduce the zoom scale.
            scale *= zoomFactorIncrement;

            // Render the Mandelbrot set at the new zoom level.
            GenerateMandelbrotFrame();

            // Allow the UI to update by awaiting a small delay ensuring UI responsiveness.
            await Task.Delay(1);

            frames++;
        }

        sw.Stop();
        ZoomFactorText.Text = (frames / sw.Elapsed.TotalMilliseconds * 1000).ToString("0.00");

    }

    private void UpdateStatusBar()
    {
        CenterXText.Text = $"Center X: {centerX:F14}";
        CenterYText.Text = $"Center Y: {centerY:F14}";

        // Display zoom factor in engineering format
        string zoomFormatted = (1 / scale).ToString("F1", CultureInfo.InvariantCulture);
        ZoomFactorText.Text = $"Zoom: {zoomFormatted}";
    }

    protected override void OnClosed(EventArgs e)
    {
        if (buffer != null) buffer.Dispose();

        // Cleanup resources on window close.
        base.OnClosed(e);
        accelerator.Dispose();
        context.Dispose();
    }

    private MemoryBuffer1D<uint, Stride1D.Dense> buffer;
    private MemoryBuffer1D<uint, Stride1D.Dense> gradientBuffer;

    private void GenerateMandelbrotFrame()
    {
        if (width <= 0 || height <= 0)
            return; 

        UpdateStatusBar();

        double aspectRatio = (double)width / height;
        double adjustedScaleX = scale;
        double adjustedScaleY = scale;

        if (aspectRatio >= 1.0)
            adjustedScaleX *= aspectRatio;
        else
            adjustedScaleY /= aspectRatio;

        int pixelCount = width * height;

        //For now we're reusing a buffer (slight perf improvement)
        //using MemoryBuffer1D<uint, Stride1D.Dense> buffer = accelerator.Allocate1D<uint>(pixelCount);

        kernel(pixelCount, buffer.View, gradientBuffer.View, centerX, centerY, scale, width, height);
        //accelerator.Synchronize();

        // Retrieve the results from GPU
        var result = buffer.GetAsArray1D();

        // Set the Image control source to display the Mandelbrot set.
        MandelbrotImage.Source = CreateFrameBitmap(result);
    }

    private WriteableBitmap CreateFrameBitmap(uint[] pixels)
    {
        // Create a WriteableBitmap and fill it with the Mandelbrot set image.
        var bitmap = new WriteableBitmap(width, height, 140, 140, PixelFormats.Bgra32, null);
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
