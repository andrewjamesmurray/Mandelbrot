using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ILGPU;
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

    private Action<Index1D, MandelbrotParameters> kernel;

    private MemoryBuffer1D<uint, Stride1D.Dense> buffer;
    private MemoryBuffer1D<uint, Stride1D.Dense> gradientBuffer;

    private WriteableBitmap bitmap;

    private static readonly uint[] Gradient = Palette.GenerateColorLookup();
    private readonly uint[] StagingBuffer;

    public MainWindow()
    {
        InitializeComponent();

        width = 5120; // (short)SystemParameters.MaximizedPrimaryScreenWidth;
        height = 2160; // (short)SystemParameters.MaximizedPrimaryScreenHeight;

        this.Width = width;
        this.Height = height;

        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Fant);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);

        // Initialize ILGPU context and accelerator.
        context = Context.CreateDefault();
        accelerator = context.CreateCudaAccelerator(0);

        // Load the kernel once during initialization.
        kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, MandelbrotParameters>(MandelbrotKernel.ComputeMandelbrotFrame);

        gradientBuffer = accelerator.Allocate1D<uint>(Gradient.Length);
        gradientBuffer.CopyFromCPU(Gradient);

        StagingBuffer = new uint[width * height];

        // Add event handlers for zooming, panning, and resizing.
        this.MouseWheel += MainWindow_MouseWheel;
        this.MouseRightButtonDown += MainWindow_MouseRightButtonDown;
        this.MouseLeftButtonDown += MainWindow_MouseLeftButtonDown;
        this.MouseLeftButtonUp += MainWindow_MouseLeftButtonUp;
        this.MouseMove += MainWindow_MouseMove;
        //this.SizeChanged += MainWindow_SizeChanged;
        this.KeyDown += KeyUpHandler;

        ReallocateBuffer();
        accelerator.Synchronize();

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

    // To be called whenever width or height are initialized or changed
    private void ReallocateBuffer()
    {
        if (height == 0 || width == 0) return;

        if (buffer != null) buffer.Dispose();

        buffer = accelerator.Allocate1D<uint>(width * height);
        bitmap = new WriteableBitmap(width, height, 140, 140, PixelFormats.Bgra32, null);
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

    private void KeyUpHandler(object sender, KeyEventArgs e)
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
        else if (e.Key == Key.Escape)
        {
            Close();
            Environment.Exit(0);
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

            FpsLabel.Text = (frames / sw.Elapsed.TotalMilliseconds * 1000).ToString("0") + " fps";

            // Allow the UI to update by awaiting a small delay ensuring UI responsiveness.
            await Task.Delay(1);

            frames++;
        }

        sw.Stop();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (buffer != null) buffer.Dispose();

        // Cleanup resources on window close.
        base.OnClosed(e);
        accelerator.Dispose();
        context.Dispose();
    }

    private void GenerateMandelbrotFrame()
    {
        if (width <= 0 || height <= 0)
            return; 

        double aspectRatio = (double)width / height;
        double adjustedScaleX = scale;
        double adjustedScaleY = scale;

        if (aspectRatio >= 1.0)
            adjustedScaleX *= aspectRatio;
        else
            adjustedScaleY /= aspectRatio;

        int pixelCount = width * height;

        var p = new MandelbrotParameters
        { 
            Output = buffer,
            Gradient = gradientBuffer.View,
            CenterX = centerX,
            CenterY = centerY,
            Scale = scale,
            Width = width,
            Height = height,            
        };

        kernel(pixelCount, p);

        // Retrieve the results from GPU
        buffer.CopyToCPU(StagingBuffer); 

        // Set the Image control source to display the Mandelbrot set.
        MandelbrotImage.Source = CreateFrameBitmap(StagingBuffer); // don't reset the bitmap each time
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
