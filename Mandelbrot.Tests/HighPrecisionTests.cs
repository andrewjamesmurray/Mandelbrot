namespace Mandelbrot.Tests;

/// <summary>
/// The fixed-point type the reference orbit is built on. If this is wrong, every deep
/// zoom is wrong, and nothing else in the renderer would tell you.
/// </summary>
public class HighPrecisionTests
{
    const int Bits = 256;

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(0.5)]
    [InlineData(-3.75)]
    [InlineData(0.26523357180865775)]
    [InlineData(-1.2535516388693015)]
    [InlineData(3.9999999999999996)]
    [InlineData(1e-30)]
    [InlineData(-1e-30)]
    public void RoundTripThroughADoubleIsExact(double value) =>
        Assert.Equal(value, HpReal.FromDouble(value, Bits).ToDouble());

    [Fact]
    public void ZeroStaysZero()
    {
        Assert.True(HpReal.Zero.IsZero);
        Assert.Equal(0.0, HpReal.Zero.ToDouble());
        Assert.True((HpReal.FromDouble(0.25, Bits) - HpReal.FromDouble(0.25, Bits)).IsZero);
    }

    [Fact]
    public void CarriesInformationADoubleWouldLose()
    {
        // The double does not even notice this addition.
        Assert.Equal(1.0, 1.0 + 1e-30);

        var one = HpReal.FromDouble(1.0, Bits);
        var tiny = HpReal.Parse("0.000000000000000000000000000001", Bits);
        var sum = one + tiny;

        Assert.Equal(1.0, sum.ToDouble());
        Assert.True(Math.Abs((sum - one).ToDouble() / 1e-30 - 1.0) < 1e-12);
    }

    [Fact]
    public void ParseAndPrintRoundTripPastDoublePrecision()
    {
        const string text = "-1.25355163886930157734567890123456789012345";
        Assert.Equal(text, HpReal.Parse(text, 512).ToDecimalString(41));
    }

    [Fact]
    public void PrintingRoundsRatherThanTruncating()
    {
        Assert.Equal("-0.5", HpReal.FromDouble(-0.5, Bits).ToDecimalString(1));
        Assert.Equal("2", HpReal.FromDouble(1.6, Bits).ToDecimalString(0));
        Assert.Equal("0.3333", HpReal.Parse("0.33333333", Bits).ToDecimalString(4));
    }

    [Fact]
    public void MultiplicationMatchesTheAlgebraicIdentity()
    {
        var a = HpReal.Parse("0.31415926535897932384626433832795", Bits);
        var b = HpReal.Parse("0.00000000000000000000000271828182", Bits);

        var square = (a + b) * (a + b);
        var expanded = a * a + (a * b + a * b) + b * b;

        // Each fixed-point multiply truncates below 2^-256; four of them cannot add up
        // to anything a double could resolve.
        Assert.True(Math.Abs((square - expanded).ToDouble()) < 1e-60);
    }

    [Fact]
    public void ArithmeticPromotesToTheWiderOperand()
    {
        var coarse = HpReal.FromDouble(0.5, 64);
        var fine = HpReal.Parse("0.50000000000000000000000001", 512);

        Assert.True(fine > coarse);
        Assert.True(coarse < fine);
        Assert.Equal(512, (coarse + fine).FractionBits);
        Assert.True(Math.Abs((fine - coarse).ToDouble() / 1e-26 - 1.0) < 1e-9);
    }

    [Fact]
    public void RaisingPrecisionIsLosslessAndLoweringTruncates()
    {
        var value = HpReal.Parse("0.1", 300);

        Assert.Equal(value.ToDouble(), value.WithFractionBits(600).ToDouble());
        Assert.Equal(0.0, value.WithFractionBits(1).ToDouble());
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1e-10)]
    [InlineData(1e-50)]
    [InlineData(1e-150)]
    public void PrecisionChosenForAScaleCanStillResolveThatScale(double scale)
    {
        var bits = HpReal.BitsForScale(scale);
        var one = HpReal.FromDouble(1.0, bits);
        var step = HpReal.FromDouble(scale, bits);

        // A step of one scale unit must remain visible beside a coordinate of order 1.
        var moved = (one + step) - one;
        Assert.False(moved.IsZero);
        Assert.True(Math.Abs(moved.ToDouble() / scale - 1.0) < 1e-6);
    }

    [Fact]
    public void ComplexArithmeticIsComponentwise()
    {
        var a = HpComplex.FromDouble(1.5, -2.5, Bits);
        var b = HpComplex.FromDouble(0.25, 0.75, Bits);

        var sum = a + b;
        Assert.Equal(1.75, sum.Real.ToDouble());
        Assert.Equal(-1.75, sum.Imaginary.ToDouble());

        var difference = sum - b;
        Assert.Equal(1.5, difference.Real.ToDouble());
        Assert.Equal(-2.5, difference.Imaginary.ToDouble());
    }
}
