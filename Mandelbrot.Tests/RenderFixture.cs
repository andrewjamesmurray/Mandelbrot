using System.Numerics;

namespace Mandelbrot.Tests;

/// <summary>
/// Shares one accelerator and one set of compiled kernels across every test that
/// renders, and keeps those tests from running concurrently against it.
/// </summary>
public sealed class RenderFixture : IDisposable
{
    public const int Width = 240;
    public const int Height = 135;

    public const string DeepX = "0.26523357180865775";
    public const string DeepY = "0.003055480740359563";

    /// <summary>
    /// A palette whose entry k is the number k, so a rendered pixel decodes straight
    /// back to the shade the kernel picked.
    /// </summary>
    public static uint[] IdentityPalette { get; } = BuildIdentity();

    public IComputeAdapter Gpu { get; }
    public IComputeAdapter Cpu { get; }

    public RenderFixture()
    {
        Gpu = GpuAdapter.Create(Width, Height, IdentityPalette);
        Cpu = CpuAdapter.Create(Width, Height, IdentityPalette);
    }

    static uint[] BuildIdentity()
    {
        var palette = new uint[Palette.NumShades];
        for (var i = 0; i < palette.Length; i++)
            palette[i] = (uint)i;
        return palette;
    }

    public static MandelbrotState At(double scale, int? maxIter = null, RenderMode? mode = null)
    {
        var state = new MandelbrotState(Width, Height);
        state.SetView(DeepX, DeepY, scale, maxIter);
        state.ForcedMode = mode;
        return state;
    }

    public static uint[] Render(IComputeAdapter adapter, MandelbrotState state)
    {
        var (parameters, orbit) = state.PrepareFrame();
        var buffer = new uint[Width * Height];
        adapter.Render(parameters, state.Mode, state.Optimizations, orbit, buffer);
        return buffer;
    }

    public static double PercentDiffering(uint[] a, uint[] b)
    {
        var differing = 0;
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                differing++;

        return 100.0 * differing / a.Length;
    }

    public static int DistinctShades(uint[] pixels) => new HashSet<uint>(pixels).Count;

    /// <summary>
    /// The iteration count for one point, computed entirely in HpReal. Nothing in the
    /// renderer takes part, so this is an independent answer to compare against.
    /// </summary>
    public static int ExactIterations(HpReal cr, HpReal ci, int maxIter, int bits)
    {
        var four = HpReal.FromDouble(4.0, bits);
        HpReal zr = new(BigInteger.Zero, bits), zi = new(BigInteger.Zero, bits);

        for (var iter = 0; iter < maxIter; iter++)
        {
            var zrSquared = zr * zr;
            var ziSquared = zi * zi;
            var cross = zr * zi;

            zr = zrSquared - ziSquared + cr;
            zi = cross + cross + ci;

            if (zr * zr + zi * zi > four)
                return iter + 1;
        }

        return maxIter;
    }

    /// <summary>The shade the renderer should produce for an exact iteration count.</summary>
    public static int ExpectedShade(int iterations, int maxIter)
    {
        if (iterations >= maxIter)
            return -1;

        var shade = (int)(iterations * ((float)Palette.NumShades / maxIter));
        return shade < Palette.NumShades ? shade : Palette.NumShades - 1;
    }

    /// <summary>Shade the kernel chose for a pixel, or -1 for the interior colour.</summary>
    public static int ShadeOf(uint pixel) => pixel == MandelbrotKernel.BulbColor ? -1 : (int)pixel;

    public void Dispose()
    {
        Cpu.Dispose();
        Gpu.Dispose();
    }
}

[CollectionDefinition("render")]
public sealed class RenderCollection : ICollectionFixture<RenderFixture>;
