namespace Mandelbrot;

public class MandelbrotState
{
    private readonly short _width;
    private readonly short _height;
    private readonly double _aspectRatio;

    private double _centerX;
    private double _centerY;
    private double _adjustedScaleX;
    private double _adjustedScaleY;
    private double _scale;
    private int _maxIter;

    const double zoomFactorIncrement = 0.95;

    public bool UseBulbCheckOptimization { get; private set; } = true;
    public bool UsePeriodicityOptimization { get; private set; } = true;

    public double Scale => _scale;
    public int MaxIter => _maxIter;

    public double CenterX => _centerX;
    public double CenterY => _centerY;

    public MandelbrotState(short width, short height)
    {
        _width = width;
        _height = height;
        _aspectRatio = (double)width / height;

        Reset();
    }

    private void SetScale(double newValue)
    {
        _scale = newValue;

        if (_aspectRatio >= 1.0)
        {
            _adjustedScaleX = _scale * _aspectRatio;
            _adjustedScaleY = _scale;
        }
        else
        {
            _adjustedScaleX = _scale;
            _adjustedScaleY = _scale / _aspectRatio;
        }

        _maxIter = ComputeMaxIter(_scale);
    }

    public void Reset()
    {
        _centerX = -0.74;
        _centerY = 0.15;

        SetScale(2.5);
    }

    /// <summary>
    /// Reset to a point of interest
    /// </summary>
    public void ResetForZoom()
    {
        // BUTT
        _centerX = 0.26523357180865775;
        _centerY = 0.003055480740359563;

        // INNER MANDELBROT
        //_centerX = -1.0401309460202168;
        //_centerY = 0.3487766281539035;

        // ORIGINAL
        // _centerX = -1.2535516388693015;
        // _centerY = 0.37899272530660111;

        // SPIRALS
        //_centerX = -0.7440082435282657;
        //_centerY = 0.1481642578992491;

        // REPEATED HOLES
        //_centerX = -0.6918522229482166;
        //_centerY = 0.27323155495824253;

        // DENDRIDE THINGIE
        //_centerX = -1.1719083849552117;
        //_centerY = 0.18669829447747052;

        // STARFISH AREA
        //_centerX = -0.2262667169347128;
        //_centerY = -1.1161743860818043;

        // VERY FAST RANDOM HOLE
        //_centerX = -1.4208192303359084;
        //_centerY = -1.1046267101656378E-06;

        SetScale(10);
    }

    public void Move(double deltaX, double deltaY)
    {
        _centerX -= deltaX / _width * _scale;
        _centerY -= deltaY / _height * _scale;
    }

    /// <summary>
    /// Dynamically set maxIter based on zoom depth to improve performance at low zooms
    /// </summary>
    private static int ComputeMaxIter(double scale)
    {
        // Logarithmic boost keeps growth manageable at deep zooms
        var divisor = Math.Max(scale, 1e-13); // Smaller numbers could generate a negative number
        double zoom = 1.0 / divisor;

        // Instead of log(zoom), use log(max(zoom, 1)) so log never goes negative
        double safeLog = Math.Log10(Math.Max(1.0, zoom));

        // Grow iteration count smoothly with zoom depth
        return (int)Math.Max(350, (200 * Math.Pow(safeLog, 1.5)));
    }

    public bool ZoomNext()
    {
        if (_scale < 1e-12) // With 64-bit doubles, quality degrades too much beyond this
            return false;

        SetScale(_scale * zoomFactorIncrement);

        return true;
    }

    public void ZoomAndMove(int zoomDelta, double positionX, double positionY)
    {
        // Normalize mouse position to [-1, 1] range in the complex plane.
        double normX = (positionX / _width - 0.5) * _scale;
        double normY = (positionY / _height - 0.5) * _scale;

        // Adjust scale based on the scroll direction
        var scaleMultiplier = zoomDelta > 0 ? zoomFactorIncrement : 1 - (zoomFactorIncrement - 1);

        SetScale(_scale * scaleMultiplier);

        // Adjust the center point based on the normalized mouse position.
        var normMultiplier = (1 - _scale / (_scale * scaleMultiplier));
        _centerX -= normX * normMultiplier;
        _centerY -= normY * normMultiplier;
    }

    public void IncreaseMaxIter()
    {
        _maxIter += 50;
    }

    public void DecreaseMaxIter()
    {
        _maxIter = Math.Max(_maxIter - 50, 50);
    }
    public void TogglePeriodicityOptimization()
    { 
        UsePeriodicityOptimization = !UsePeriodicityOptimization;
    }

    public void ToggleBulbCheckOptimization()
    { 
        UseBulbCheckOptimization = !UseBulbCheckOptimization;
    }

    public MandelbrotParameters GenerateParameters()
    {
        byte optimizations = 0;
        if (UsePeriodicityOptimization) optimizations |= MandelbrotParameters.PeriodicityOptimizationEnum;
        if (UseBulbCheckOptimization) optimizations |= MandelbrotParameters.BulbCheckOptimizationEnum;

        return new MandelbrotParameters
        {
            CenterX = _centerX,
            CenterY = _centerY,
            Scale = _scale,
            Width = _width,
            Height = _height,
            AdjustedScaleXPerPixel = _adjustedScaleX / _width,
            AdjustedScaleYPerPixel = _adjustedScaleY / _height,
            OffsetX = -(_adjustedScaleX / 2) + _centerX,
            OffsetY = -(_adjustedScaleY / 2) + _centerY,
            MaxIter = _maxIter,
            Optimizations = optimizations
        };
    }
}
