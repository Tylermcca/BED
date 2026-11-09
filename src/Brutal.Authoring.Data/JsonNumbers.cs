using System.Globalization;
using System.Numerics;

namespace Brutal.Authoring.Data;

/// <summary>JSON numbers compared exactly, on their text: a double makes 1e400 and 2e400 both infinity, and 9007199254740993 equal to
/// 9007199254740992 (found by A202 and A206). INVENTED: digits and a power of ten, compared by size first so no huge power is built.</summary>
public static class JsonNumbers
{
    /// <summary>Below zero when <paramref name="a"/> is less than <paramref name="b"/>, zero when they're the same value, above zero when
    /// it's more. Both are JSON number text.</summary>
    public static int Compare(string a, string b)
    {
        var (na, ma, ea) = Parse(a);
        var (nb, mb, eb) = Parse(b);
        if (ma.IsZero && mb.IsZero) return 0;
        var sa = ma.IsZero ? 0 : na ? -1 : 1;
        var sb = mb.IsZero ? 0 : nb ? -1 : 1;
        if (sa != sb) return sa.CompareTo(sb);
        var magnitude = Magnitude(ma, ea, mb, eb);
        return sa < 0 ? -magnitude : magnitude;
    }

    // Compares |a| and |b|: by how many digits come before the point first, then digit by digit.
    private static int Magnitude(BigInteger ma, BigInteger ea, BigInteger mb, BigInteger eb)
    {
        var size = (ma.ToString(CultureInfo.InvariantCulture).Length + ea).CompareTo(mb.ToString(CultureInfo.InvariantCulture).Length + eb);
        if (size != 0) return size;
        // The same size: line the digits up (the difference in exponent is now below the longer number's digit count).
        var shift = ea - eb;
        return shift >= 0 ? (ma * BigInteger.Pow(10, (int)shift)).CompareTo(mb) : ma.CompareTo(mb * BigInteger.Pow(10, (int)-shift));
    }

    // A number as its sign, its digits with no zeros at either end, and a power of ten.
    private static (bool Negative, BigInteger Digits, BigInteger Exponent) Parse(string number)
    {
        var negative = number.StartsWith('-');
        var body = negative ? number[1..] : number;
        var e = body.IndexOfAny(['e', 'E']);
        var exponent = e < 0 ? BigInteger.Zero : BigInteger.Parse(body[(e + 1)..].TrimStart('+'), CultureInfo.InvariantCulture);
        var mantissa = e < 0 ? body : body[..e];
        var dot = mantissa.IndexOf('.');
        var fraction = dot < 0 ? "" : mantissa[(dot + 1)..];
        var digits = ((dot < 0 ? mantissa : mantissa[..dot]) + fraction).TrimStart('0');
        exponent -= fraction.Length;
        var kept = digits.TrimEnd('0');
        exponent += digits.Length - kept.Length;
        return (negative, kept.Length == 0 ? BigInteger.Zero : BigInteger.Parse(kept, CultureInfo.InvariantCulture), exponent);
    }
}
