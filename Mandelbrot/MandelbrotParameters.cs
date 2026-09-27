namespace Mandelbrot;

/// <summary>How a frame is evaluated. Chosen from the zoom depth, see <see cref="MandelbrotState"/>.</summary>
public enum RenderMode
{
    /// <summary>Straight iteration in float. Only valid while a pixel is far wider than a float ULP.</summary>
    DirectFloat,

    /// <summary>Straight iteration in double.</summary>
    DirectDouble,

    /// <summary>Perturbation against a high-precision reference orbit, deltas in float.</summary>
    PerturbFloat,

    /// <summary>Perturbation against a high-precision reference orbit, deltas in double.</summary>
    PerturbDouble
}

public struct MandelbrotParameters
{
    public const int PeriodicityOptimizationFlag = 1;
    public const int BulbCheckOptimizationFlag = 2;

    public int Width { get; set; }
    public int Height { get; set; }
    public int MaxIter { get; set; }

    /// <summary>Complex coordinate of pixel (0, 0), for the direct kernels.</summary>
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }

    /// <summary>Complex-plane step between adjacent pixels.</summary>
    public double DeltaX { get; set; }
    public double DeltaY { get; set; }

    /// <summary>Offset of pixel (0, 0) from the reference point, for the perturbation kernels.</summary>
    public double PerturbOriginX { get; set; }
    public double PerturbOriginY { get; set; }

    /// <summary>Number of valid points in the reference orbit.</summary>
    public int RefLength { get; set; }

    /// <summary>Leading iterations the series approximation replaces.</summary>
    public int SkipIterations { get; set; }

    /// <summary>
    /// Series coefficients, each already multiplied by the corner radius to its own power,
    /// so the kernel evaluates them against a delta normalised to [-1, 1] and everything
    /// stays in range even in float.
    /// </summary>
    public double SeriesAr { get; set; }
    public double SeriesAi { get; set; }
    public double SeriesBr { get; set; }
    public double SeriesBi { get; set; }
    public double SeriesCr { get; set; }
    public double SeriesCi { get; set; }

    /// <summary>Reciprocal of the corner radius, used to normalise deltas for the series.</summary>
    public double InvRadius { get; set; }

    /// <summary>Palette.NumShades / MaxIter, so the kernel does a multiply instead of a divide.</summary>
    public float PaletteScale { get; set; }
}
