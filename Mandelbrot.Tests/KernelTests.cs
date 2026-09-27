namespace Mandelbrot.Tests;

/// <summary>
/// The escape-time loop. The production version carries z's squares across iterations
/// and hoists the periodicity test to a chunk boundary, so it is checked against a
/// plain transcription of the textbook recurrence.
/// </summary>
public class KernelTests
{
    /// <summary>z -> z^2 + c, written the obvious way, with no optimisations at all.</summary>
    static int Textbook(double cr, double ci, int maxIter)
    {
        double zr = 0.0, zi = 0.0;

        for (var iter = 0; iter < maxIter; iter++)
        {
            if (zr * zr + zi * zi > 4.0)
                return iter;

            var next = zr * zr - zi * zi + cr;
            zi = 2.0 * zr * zi + ci;
            zr = next;
        }

        return maxIter;
    }

    /// <summary>A grid over the region the renderer actually shows.</summary>
    public static IEnumerable<(double Cr, double Ci)> Grid(int steps = 60)
    {
        for (var y = 0; y < steps; y++)
        for (var x = 0; x < steps; x++)
            yield return (-2.2 + 3.0 * x / (steps - 1), -1.3 + 2.6 * y / (steps - 1));
    }

    [Theory]
    [InlineData(0.0, 0.0)]      // origin: never escapes
    [InlineData(-1.0, 0.0)]     // period 2
    [InlineData(-1.5, 0.0)]     // on the real axis, still inside
    [InlineData(0.3, 0.0)]      // just outside the cardioid
    [InlineData(2.0, 0.0)]      // escapes almost at once
    [InlineData(-0.75, 0.25)]   // near the boundary
    public void MatchesTheTextbookRecurrence(double cr, double ci)
    {
        const int MaxIter = 5000;
        Assert.Equal(Textbook(cr, ci, MaxIter),
            MandelbrotKernel.IterateDouble(cr, ci, MaxIter, periodicity: false, bulbCheck: false));
    }

    [Fact]
    public void MatchesTheTextbookRecurrenceAcrossTheWholeView()
    {
        const int MaxIter = 400;

        foreach (var (cr, ci) in Grid())
            Assert.Equal(Textbook(cr, ci, MaxIter),
                MandelbrotKernel.IterateDouble(cr, ci, MaxIter, periodicity: false, bulbCheck: false));
    }

    [Fact]
    public void KnownPointsEscapeWhenTheyShould()
    {
        const int MaxIter = 1000;

        Assert.Equal(MaxIter, MandelbrotKernel.IterateDouble(0.0, 0.0, MaxIter, false, false));
        Assert.Equal(MaxIter, MandelbrotKernel.IterateDouble(-1.0, 0.0, MaxIter, false, false));
        Assert.Equal(2, MandelbrotKernel.IterateDouble(2.0, 0.0, MaxIter, false, false));
        Assert.True(MandelbrotKernel.IterateDouble(1.0, 1.0, MaxIter, false, false) < 10);
    }

    [Fact]
    public void BulbCheckIsLossless()
    {
        const int MaxIter = 400;

        foreach (var (cr, ci) in Grid())
            Assert.Equal(
                MandelbrotKernel.IterateDouble(cr, ci, MaxIter, periodicity: false, bulbCheck: false),
                MandelbrotKernel.IterateDouble(cr, ci, MaxIter, periodicity: false, bulbCheck: true));
    }

    [Fact]
    public void PeriodicityCheckIsLossless()
    {
        const int MaxIter = 400;

        foreach (var (cr, ci) in Grid())
            Assert.Equal(
                MandelbrotKernel.IterateDouble(cr, ci, MaxIter, periodicity: false, bulbCheck: false),
                MandelbrotKernel.IterateDouble(cr, ci, MaxIter, periodicity: true, bulbCheck: false));
    }

    [Fact]
    public void OptimisationFlagsAreIndependent()
    {
        const int MaxIter = 400;

        foreach (var (cr, ci) in Grid())
        {
            var expected = MandelbrotKernel.IterateDouble(cr, ci, MaxIter, false, false);
            Assert.Equal(expected, MandelbrotKernel.IterateDouble(cr, ci, MaxIter, true, true));
        }
    }

    [Fact]
    public void CardioidAndBulbTestOnlyClaimsPointsThatNeverEscape()
    {
        const int MaxIter = 20000;

        foreach (var (cr, ci) in Grid(40))
        {
            if (!MandelbrotKernel.InCardioidOrBulb(cr, ci))
                continue;

            Assert.Equal(MaxIter, MandelbrotKernel.IterateDouble(cr, ci, MaxIter, false, false));
        }
    }

    [Fact]
    public void FloatKernelTracksTheDoubleKernelAtShallowZoom()
    {
        const int MaxIter = 200;

        var differing = 0;
        var total = 0;

        foreach (var (cr, ci) in Grid())
        {
            total++;
            if (MandelbrotKernel.IterateFloat((float)cr, (float)ci, MaxIter, false, true) !=
                MandelbrotKernel.IterateDouble(cr, ci, MaxIter, false, true))
                differing++;
        }

        // Measured against exact arithmetic, fp32 is already ~1% wrong at scale 1e-2,
        // which is why it is not on the automatic ladder. This only asserts it is not
        // broken outright.
        Assert.True(differing < total / 20, $"{differing} of {total} differ");
    }

    [Theory]
    [InlineData(350)]
    [InlineData(2048)]
    [InlineData(8313)]
    [InlineData(200000)]
    public void ShadeIndexNeverLeavesThePalette(int maxIter)
    {
        // The CPU adapter used to narrow this to a byte, wrapping 87.5% of iteration
        // counts onto the wrong entry of a 2048-colour table.
        var paletteScale = (float)Palette.NumShades / maxIter;

        for (var iterations = 0; iterations < maxIter; iterations++)
        {
            var shade = (int)(iterations * paletteScale);
            Assert.InRange(shade, 0, Palette.NumShades - 1);
        }
    }
}
