using System.Numerics;

namespace Axis.Expressions.Evaluation;

/// <summary>
/// Exact decimal arithmetic. <see cref="decimal"/> operators round a result with too many digits,
/// so these compute the exact result first and throw <see cref="OverflowException"/> when
/// <see cref="decimal"/> cannot hold it. Only division rounds, as the reference says.
/// </summary>
internal static class DecimalMath
{
    /// <summary>The most digits after the point that a <see cref="decimal"/> holds.</summary>
    private const int MaxScale = 28;

    /// <summary>The digits after the point that a quotient is rounded to when it has room.</summary>
    private const int DivisionScale = 20;

    private static readonly BigInteger _maxMantissa = (BigInteger.One << 96) - 1;

    public static decimal Add(decimal left, decimal right)
    {
        var (a, scaleA) = Split(left);
        var (b, scaleB) = Split(right);
        var scale = Math.Max(scaleA, scaleB);
        return Build(Align(a, scaleA, scale) + Align(b, scaleB, scale), scale);
    }

    public static decimal Subtract(decimal left, decimal right) => Add(left, -right);

    public static decimal Multiply(decimal left, decimal right)
    {
        var (a, scaleA) = Split(left);
        var (b, scaleB) = Split(right);
        return Build(a * b, scaleA + scaleB);
    }

    /// <summary>
    /// Rounds the quotient half away from zero to 20 digits after the point, or to as many as fit
    /// when its whole-number part leaves no room for 20. Each scale is rounded from the exact
    /// operands, so the result is never rounded twice.
    /// </summary>
    public static decimal Divide(decimal left, decimal right)
    {
        var (a, scaleA) = Split(left);
        var (b, scaleB) = Split(right);
        if (b.IsZero)
        {
            throw new DivideByZeroException();
        }

        for (var scale = DivisionScale; scale >= 0; scale--)
        {
            // left / right = (a / 10^scaleA) / (b / 10^scaleB), so the quotient at this scale is
            // a * 10^(scale + scaleB - scaleA) / b.
            var exponent = scale + scaleB - scaleA;
            var numerator = exponent >= 0 ? a * BigInteger.Pow(10, exponent) : a;
            var denominator = exponent >= 0 ? b : b * BigInteger.Pow(10, -exponent);
            var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
            if (BigInteger.Abs(remainder) * 2 >= BigInteger.Abs(denominator))
            {
                quotient += numerator.Sign == denominator.Sign ? 1 : -1;
            }

            if (BigInteger.Abs(quotient) <= _maxMantissa)
            {
                return Build(quotient, scale);
            }
        }

        throw new OverflowException();
    }

    private static (BigInteger Mantissa, int Scale) Split(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        var magnitude = ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        return (value < 0 ? -magnitude : magnitude, value.Scale);
    }

    private static BigInteger Align(BigInteger mantissa, int scale, int target) =>
        mantissa * BigInteger.Pow(10, target - scale);

    /// <summary>
    /// Makes the decimal <c>mantissa / 10^scale</c>, dropping trailing zeros until it fits. Throws
    /// <see cref="OverflowException"/> when fitting it would drop a digit that is not zero.
    /// </summary>
    private static decimal Build(BigInteger mantissa, int scale)
    {
        while (scale > MaxScale || BigInteger.Abs(mantissa) > _maxMantissa)
        {
            if (scale == 0)
            {
                throw new OverflowException();
            }

            mantissa = BigInteger.DivRem(mantissa, 10, out var remainder);
            if (!remainder.IsZero)
            {
                throw new OverflowException();
            }

            scale--;
        }

        var magnitude = BigInteger.Abs(mantissa);
        return new decimal(
            unchecked((int)(uint)(magnitude & uint.MaxValue)),
            unchecked((int)(uint)((magnitude >> 32) & uint.MaxValue)),
            unchecked((int)(uint)(magnitude >> 64)),
            mantissa.Sign < 0,
            (byte)scale);
    }
}
