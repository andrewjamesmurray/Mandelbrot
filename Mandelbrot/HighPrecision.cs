using System.Globalization;
using System.Numerics;
using System.Text;

namespace Mandelbrot;

/// <summary>
/// A fixed-point real number: the value is <c>Raw / 2^FractionBits</c>.
///
/// Every quantity that needs more than a double here (the view centre, the reference
/// orbit) is bounded in magnitude by about 4, so a fixed-point representation is both
/// simpler and faster than a general arbitrary-precision float. Precision is chosen
/// from the zoom depth, see <see cref="BitsForScale"/>.
/// </summary>
public readonly struct HpReal
{
    public static readonly HpReal Zero = default;

    public BigInteger Raw { get; }
    public int FractionBits { get; }

    public HpReal(BigInteger raw, int fractionBits)
    {
        Raw = raw;
        FractionBits = fractionBits;
    }

    public bool IsZero => Raw.IsZero;

    /// <summary>
    /// Working precision for a given zoom: enough bits to resolve the scale, plus a
    /// generous guard band for the error the reference orbit accumulates.
    /// </summary>
    public static int BitsForScale(double scale)
    {
        var depth = scale > 0 && double.IsFinite(scale) ? Math.Log2(1.0 / scale) : 0.0;
        return 96 + (int)Math.Max(0.0, depth);
    }

    public HpReal WithFractionBits(int bits)
    {
        if (bits == FractionBits)
            return this;

        var shift = bits - FractionBits;
        var raw = shift >= 0 ? Raw << shift : Raw >> -shift;
        return new HpReal(raw, bits);
    }

    public static HpReal FromDouble(double value, int fractionBits)
    {
        if (value == 0.0 || !double.IsFinite(value))
            return new HpReal(BigInteger.Zero, fractionBits);

        var bits = BitConverter.DoubleToInt64Bits(value);
        var negative = bits < 0;
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xF_FFFF_FFFF_FFFFL;

        if (exponent == 0)
            exponent = 1;                   // subnormal
        else
            mantissa |= 1L << 52;           // restore the implicit bit

        // value == mantissa * 2^(exponent - 1075)
        var shift = exponent - 1075 + fractionBits;
        var raw = shift >= 0 ? (BigInteger)mantissa << shift : (BigInteger)mantissa >> -shift;

        return new HpReal(negative ? -raw : raw, fractionBits);
    }

    public double ToDouble()
    {
        if (Raw.IsZero)
            return 0.0;

        // Keep the top 54 bits, then apply the binary exponent with ScaleB so that
        // neither the numerator nor 2^FractionBits has to be representable on its own.
        var magnitude = BigInteger.Abs(Raw);
        var shift = (int)magnitude.GetBitLength() - 54;
        var top = shift > 0 ? magnitude >> shift : magnitude << -shift;

        var result = Math.ScaleB((double)top, shift - FractionBits);
        return Raw.Sign < 0 ? -result : result;
    }

    public static HpReal operator +(HpReal a, HpReal b)
    {
        var bits = Math.Max(a.FractionBits, b.FractionBits);
        return new HpReal(a.WithFractionBits(bits).Raw + b.WithFractionBits(bits).Raw, bits);
    }

    public static HpReal operator -(HpReal a, HpReal b)
    {
        var bits = Math.Max(a.FractionBits, b.FractionBits);
        return new HpReal(a.WithFractionBits(bits).Raw - b.WithFractionBits(bits).Raw, bits);
    }

    public static HpReal operator -(HpReal a) => new(-a.Raw, a.FractionBits);

    public static HpReal operator *(HpReal a, HpReal b)
    {
        var bits = Math.Max(a.FractionBits, b.FractionBits);
        var product = a.WithFractionBits(bits).Raw * b.WithFractionBits(bits).Raw;
        return new HpReal(product >> bits, bits);
    }

    public static bool operator >(HpReal a, HpReal b) => Compare(a, b) > 0;
    public static bool operator <(HpReal a, HpReal b) => Compare(a, b) < 0;

    private static int Compare(HpReal a, HpReal b)
    {
        var bits = Math.Max(a.FractionBits, b.FractionBits);
        return a.WithFractionBits(bits).Raw.CompareTo(b.WithFractionBits(bits).Raw);
    }

    /// <summary>
    /// Parses a plain decimal literal such as "-1.2535516388693015773". Accepts more
    /// digits than a double can hold, which is the point: a centre has to be known to
    /// at least as many digits as the zoom depth you intend to reach.
    /// </summary>
    public static HpReal Parse(string text, int fractionBits)
    {
        text = text.Trim();
        var negative = text.StartsWith('-');
        if (negative || text.StartsWith('+'))
            text = text[1..];

        var point = text.IndexOf('.');
        var digits = point < 0 ? text : string.Concat(text.AsSpan(0, point), text.AsSpan(point + 1));
        var decimals = point < 0 ? 0 : text.Length - point - 1;

        var numerator = BigInteger.Parse(digits.Length == 0 ? "0" : digits, CultureInfo.InvariantCulture);
        var raw = (numerator << fractionBits) / BigInteger.Pow(10, decimals);

        return new HpReal(negative ? -raw : raw, fractionBits);
    }

    /// <summary>Formats as a plain decimal literal with the requested number of decimals.</summary>
    public string ToDecimalString(int decimals)
    {
        var scaled = BigInteger.Abs(Raw) * BigInteger.Pow(10, decimals);
        var half = BigInteger.One << (FractionBits - 1);
        var digits = ((scaled + half) >> FractionBits).ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');

        var text = new StringBuilder();
        if (Raw.Sign < 0)
            text.Append('-');

        text.Append(digits.AsSpan(0, digits.Length - decimals));
        if (decimals > 0)
            text.Append('.').Append(digits.AsSpan(digits.Length - decimals));

        return text.ToString();
    }

    public override string ToString() => ToDecimalString(20);
}

/// <summary>A complex number built from two <see cref="HpReal"/> components.</summary>
public readonly struct HpComplex
{
    public HpReal Real { get; }
    public HpReal Imaginary { get; }

    public HpComplex(HpReal real, HpReal imaginary)
    {
        Real = real;
        Imaginary = imaginary;
    }

    public static HpComplex FromDouble(double real, double imaginary, int fractionBits) =>
        new(HpReal.FromDouble(real, fractionBits), HpReal.FromDouble(imaginary, fractionBits));

    public static HpComplex Parse(string real, string imaginary, int fractionBits) =>
        new(HpReal.Parse(real, fractionBits), HpReal.Parse(imaginary, fractionBits));

    public HpComplex WithFractionBits(int bits) =>
        new(Real.WithFractionBits(bits), Imaginary.WithFractionBits(bits));

    public static HpComplex operator +(HpComplex a, HpComplex b) =>
        new(a.Real + b.Real, a.Imaginary + b.Imaginary);

    public static HpComplex operator -(HpComplex a, HpComplex b) =>
        new(a.Real - b.Real, a.Imaginary - b.Imaginary);
}
