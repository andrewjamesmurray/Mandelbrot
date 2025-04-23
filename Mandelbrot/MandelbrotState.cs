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

    public double Scale => _scale;
    public int MaxIter => _maxIter;

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

    const double ZoomTargetX = -1.2535516388693015;
    const double ZoomTargetY = 0.37899272530660111;

    /// <summary>
    /// Reset to a point of interest
    /// </summary>
    public void ResetForZoom()
    {
        _centerX = -1.2535516388693015;
        _centerY = 0.37899272530660111;

        SetScale(3.5);
    }

    public void Move(double deltaX, double deltaY)
    {
        _centerX -= deltaX / _width * _scale;
        _centerY -= deltaY / _height * _scale;
    }

    private static int ComputeMaxIter(double scale)
    {
        // Heuristic based on the inverse of zoom scale
        // Logarithmic boost keeps growth manageable at deep zooms
        double zoom = Math.Max(1.0, 1 / Math.Max(scale, 1e-13)); // Much lower and it can return a negative number

        // Instead of log(zoom), use log(max(zoom, 1)) so log never goes negative
        double safeLog = Math.Log10(zoom);

        // Grow iteration count smoothly with zoom depth
        return (int)Math.Max(150, (60 * scale + 250 * Math.Pow(safeLog, 1.6)));
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
        _centerX -= normX * (1 - _scale / (_scale * scaleMultiplier));
        _centerY -= normY * (1 - _scale / (_scale * scaleMultiplier));
    }

    public void IncreaseMaxIter()
    {
        _maxIter += 50;
    }

    public void DecreaseMaxIter()
    {
        _maxIter = Math.Max(_maxIter - 50, 50);
    }

    public MandelbrotParameters GenerateParameters()
    {
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
            maxIter = _maxIter
        };
    }
}
