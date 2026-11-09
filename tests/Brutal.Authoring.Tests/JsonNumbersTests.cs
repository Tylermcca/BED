using Brutal.Authoring.Data;
using Xunit;

namespace Brutal.Authoring.Tests;

/// <summary>A206: JSON numbers compared exactly, on their text, where a double can't tell them apart.</summary>
public sealed class JsonNumbersTests
{
    [Theory]
    [InlineData("1e400", "2e400", -1)]
    [InlineData("-1e400", "-2e400", 1)]
    [InlineData("9007199254740992", "9007199254740993", -1)]
    [InlineData("1.50", "1.5", 0)]
    [InlineData("-0", "0", 0)]
    [InlineData("0.001", "1e-3", 0)]
    [InlineData("10", "9.99", 1)]
    [InlineData("-3", "2", -1)]
    [InlineData("1E+2", "100", 0)]
    [InlineData("123.456e1", "1234.55", 1)]
    [InlineData("1e-400", "0", 1)]
    public void Numbers_compare_by_their_exact_value(string a, string b, int expected)
    {
        Assert.Equal(expected, Math.Sign(JsonNumbers.Compare(a, b)));
        Assert.Equal(-expected, Math.Sign(JsonNumbers.Compare(b, a)));
    }
}
