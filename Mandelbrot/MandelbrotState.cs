using System.Windows.Media.Media3D;

namespace Mandelbrot;

public class MandelbrotState
{
    private readonly short _width;
    private readonly short _height;
    private readonly double _aspectRatio;

    private double _centerX;
    private double _centerY;
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

    public void Reset()
    {
        _centerX = -0.74;
        _centerY = 0.15;
        _scale = 2.5;
        _maxIter = 150;
    }

    /// <summary>
    /// Reset to a point of interest
    /// </summary>
    public void ResetForZoom()
    {
        // original
        //_centerX = -0.74335165531181;
        //_centerY = +0.13138323820835;

        _centerX = -0.73842101229341928;
        _centerY = 0.1832356822787915;
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
        double zoom = 1.0 / Math.Max(scale, 1e-13);

        // Instead of log(zoom), use log(max(zoom, 1)) so log never goes negative
        double safeLog = Math.Log10(Math.Max(zoom, 1.0));

        // Grow iteration count smoothly with zoom depth
        return (int)(50 + 200 * Math.Pow(safeLog, 2));
    }

    public bool ZoomNext()
    {
        if (_scale < 1e-13)
            return false;

        _scale *= zoomFactorIncrement;
        _maxIter = ComputeMaxIter(_scale);

        return true;
    }

    public void ZoomAndMove(int zoomDelta, double positionX, double positionY)
    {
        // Normalize mouse position to [-1, 1] range in the complex plane.
        double normX = (positionX / _width - 0.5) * _scale;
        double normY = (positionY / _height - 0.5) * _scale;

        // Adjust scale based on the scroll direction
        var scaleMultiplier = zoomDelta > 0 ? zoomFactorIncrement : 1 - (zoomFactorIncrement - 1);
        _scale *= scaleMultiplier;

        // Adjust the center point based on the normalized mouse position.
        _centerX -= normX * (1 - _scale / (_scale * scaleMultiplier));
        _centerY -= normY * (1 - _scale / (_scale * scaleMultiplier));

        _maxIter = ComputeMaxIter(_scale);
    }

    public void IncreaseMaxIter()
    {
        _maxIter += 50;
    }

    public void DecreaseMaxIter()
    {
        _maxIter = Math.Max(_maxIter - 50, 50);
    }

    public MandelbrotParameters GenerateParameters(GpuAdapter _gpu)
    {
        var adjustedScaleX = _scale;
        var adjustedScaleY = _scale;

        if (_aspectRatio >= 1.0)
        {
            adjustedScaleX = _scale * _aspectRatio;
            adjustedScaleY = _scale;
        }
        else
        {
            adjustedScaleX = _scale;
            adjustedScaleY = _scale / _aspectRatio;
        }

        return new MandelbrotParameters
        {
            CenterX = _centerX,
            CenterY = _centerY,
            Scale = _scale,
            Width = _width,
            Height = _height,
            AdjustedScaleXPerPixel = adjustedScaleX / _width,
            AdjustedScaleYPerPixel = adjustedScaleY / _height,
            OffsetX = -(adjustedScaleX / 2) + _centerX,
            OffsetY = -(adjustedScaleY / 2) + _centerY,
            maxIter = _maxIter
        };
    }


}
