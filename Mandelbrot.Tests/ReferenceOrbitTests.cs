using System.Numerics;

namespace Mandelbrot.Tests;

public class ReferenceOrbitTests
{
    const int Bits = 256;

    static HpComplex Centre(string real, string imaginary) => HpComplex.Parse(real, imaginary, Bits);

    static ReferenceOrbit Built(HpComplex centre, int maxIter, bool series = false)
    {
        var orbit = new ReferenceOrbit();
        orbit.Update(centre, scale: 1e-20, maxIter, cornerRadius: 1e-20, pixelSpacing: 1e-23, useSeries: series);
        return orbit;
    }

    [Fact]
    public void OrbitMatchesPlainDoubleIterationWhileADoubleIsStillAccurate()
    {
        var orbit = Built(Centre("-0.5", "0.25"), maxIter: 200);

        double zr = 0.0, zi = 0.0;
        for (var n = 0; n < 60; n++)
        {
            Assert.True(Math.Abs(orbit.PointsDouble[2 * n] - zr) < 1e-12);
            Assert.True(Math.Abs(orbit.PointsDouble[2 * n + 1] - zi) < 1e-12);

            var next = zr * zr - zi * zi - 0.5;
            zi = 2.0 * zr * zi + 0.25;
            zr = next;
        }
    }

    [Fact]
    public void FloatCopyTracksTheDoubleCopy()
    {
        var orbit = Built(Centre("-0.5", "0.25"), maxIter: 200);

        for (var i = 0; i < 2 * orbit.Length; i++)
            Assert.Equal((float)orbit.PointsDouble[i], orbit.PointsFloat[i]);
    }

    [Fact]
    public void OrbitStartsAtTheOrigin()
    {
        var orbit = Built(Centre("-0.5", "0.25"), maxIter: 50);

        Assert.Equal(0.0, orbit.PointsDouble[0]);
        Assert.Equal(0.0, orbit.PointsDouble[1]);
    }

    [Fact]
    public void AnEscapingReferenceStopsAtEscapeRatherThanRunningToTheCap()
    {
        var orbit = Built(Centre("2", "0"), maxIter: 1000);

        Assert.True(orbit.Length < 10, $"length was {orbit.Length}");
    }

    [Fact]
    public void AnInteriorReferenceRunsToTheCap()
    {
        var orbit = Built(Centre("-0.5", "0"), maxIter: 500);

        Assert.Equal(501, orbit.Length);
    }

    [Fact]
    public void SameCentreAndCapIsServedFromTheCache()
    {
        var centre = Centre("-0.5", "0");
        var orbit = new ReferenceOrbit();

        orbit.Update(centre, 1e-20, 500, 1e-20, 1e-23, false);
        var version = orbit.Version;

        orbit.Update(centre, 1e-20, 500, 1e-20, 1e-23, false);

        Assert.Equal(version, orbit.Version);
    }

    [Fact]
    public void RaisingTheCapExtendsTheOrbitInPlace()
    {
        var centre = Centre("-0.5", "0");
        var orbit = new ReferenceOrbit();

        orbit.Update(centre, 1e-20, 500, 1e-20, 1e-23, false);
        var version = orbit.Version;
        var length = orbit.Length;
        var head = orbit.PointsDouble[100];

        orbit.Update(centre, 1e-20, 900, 1e-20, 1e-23, false);

        Assert.NotEqual(version, orbit.Version);
        Assert.True(orbit.Length > length);
        Assert.Equal(head, orbit.PointsDouble[100]);   // the existing prefix is untouched
    }

    [Fact]
    public void MovingTheCentreRebuilds()
    {
        var orbit = new ReferenceOrbit();

        orbit.Update(Centre("-0.5", "0"), 1e-20, 300, 1e-20, 1e-23, false);
        Assert.True(orbit.Matches(Centre("-0.5", "0"), Bits));

        Assert.False(orbit.Matches(Centre("-0.5000000000000000000000001", "0"), Bits));
    }

    [Fact]
    public void SeriesCoefficientsFollowTheRecurrence()
    {
        var state = new MandelbrotState(1920, 1080);
        state.SetView("0.26523357180865775", "0.003055480740359563", 1e-20);

        var (parameters, orbit) = state.PrepareFrame();
        Assert.NotNull(orbit);
        Assert.True(parameters.SkipIterations > 0, "expected the series to skip something at this depth");

        // A' = 2ZA + 1,  B' = 2ZB + A^2,  C' = 2ZC + 2AB, from eps = A.d + B.d^2 + C.d^3
        Complex a = Complex.Zero, b = Complex.Zero, c = Complex.Zero;
        for (var n = 0; n < orbit.SkipIterations; n++)
        {
            var twoZ = new Complex(2.0 * orbit.PointsDouble[2 * n], 2.0 * orbit.PointsDouble[2 * n + 1]);
            (a, b, c) = (twoZ * a + Complex.One, twoZ * b + a * a, twoZ * c + 2.0 * a * b);
        }

        var radius = orbit.Radius;
        AssertRelative(a * radius, orbit.SeriesA);
        AssertRelative(b * radius * radius, orbit.SeriesB);
        AssertRelative(c * radius * radius * radius, orbit.SeriesC);
    }

    [Fact]
    public void FirstCoefficientStepIsExactlyOne()
    {
        // A_1 = 2*Z_0*A_0 + 1 with Z_0 = A_0 = 0.
        var orbit = Built(Centre("-0.5", "0"), maxIter: 50);
        Complex a = Complex.Zero;
        var twoZ = new Complex(2.0 * orbit.PointsDouble[0], 2.0 * orbit.PointsDouble[1]);

        Assert.Equal(Complex.One, twoZ * a + Complex.One);
    }

    [Fact]
    public void DisablingTheSeriesSkipsNothing()
    {
        var state = new MandelbrotState(1920, 1080);
        state.SetView("0.26523357180865775", "0.003055480740359563", 1e-20);
        state.ToggleSeriesApproximation();

        var (parameters, _) = state.PrepareFrame();

        Assert.False(state.UseSeriesApproximation);
        Assert.Equal(0, parameters.SkipIterations);
    }

    [Fact]
    public void SeriesNeverSkipsPastTheReferenceOrTheCap()
    {
        foreach (var scale in new[] { 1e-14, 1e-20, 1e-40, 1e-80 })
        {
            var state = new MandelbrotState(1920, 1080);
            state.SetView("0.26523357180865775", "0.003055480740359563", scale);

            var (parameters, orbit) = state.PrepareFrame();

            Assert.NotNull(orbit);
            Assert.InRange(parameters.SkipIterations, 0, Math.Min(orbit.Length - 1, parameters.MaxIter));
        }
    }

    static void AssertRelative(Complex expected, Complex actual)
    {
        var scale = Math.Max(expected.Magnitude, 1e-300);
        Assert.True((expected - actual).Magnitude / scale < 1e-9,
            $"expected {expected}, got {actual}");
    }
}
