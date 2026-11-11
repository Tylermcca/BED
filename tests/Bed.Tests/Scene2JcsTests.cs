using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bed;
using Xunit;
namespace Bed.Tests;

public sealed class Scene2JcsTests
{
    // BORROWED factual conformance vectors: RFC8785 Appendix B, https://www.rfc-editor.org/rfc/rfc8785#appendix-B.
    [Theory]
    [InlineData("0000000000000000","0")][InlineData("8000000000000000","0")]
    [InlineData("0000000000000001","5e-324")][InlineData("8000000000000001","-5e-324")]
    [InlineData("7fefffffffffffff","1.7976931348623157e+308")][InlineData("ffefffffffffffff","-1.7976931348623157e+308")]
    [InlineData("4340000000000000","9007199254740992")][InlineData("c340000000000000","-9007199254740992")]
    [InlineData("4430000000000000","295147905179352830000")]
    [InlineData("44b52d02c7e14af5","9.999999999999997e+22")][InlineData("44b52d02c7e14af6","1e+23")][InlineData("44b52d02c7e14af7","1.0000000000000001e+23")]
    [InlineData("444b1ae4d6e2ef4e","999999999999999700000")][InlineData("444b1ae4d6e2ef4f","999999999999999900000")][InlineData("444b1ae4d6e2ef50","1e+21")]
    [InlineData("3eb0c6f7a0b5ed8c","9.999999999999997e-7")][InlineData("3eb0c6f7a0b5ed8d","0.000001")]
    [InlineData("41b3de4355555553","333333333.3333332")][InlineData("41b3de4355555554","333333333.33333325")][InlineData("41b3de4355555555","333333333.3333333")]
    [InlineData("41b3de4355555556","333333333.3333334")][InlineData("41b3de4355555557","333333333.33333343")]
    [InlineData("becbf647612f3696","-0.0000033333333333333333")][InlineData("43143ff3c1cb0959","1424953923781206.2")]
    public void Rfc_number_bit_patterns_have_exact_ecmascript_spelling(string bits,string expected)
        =>Assert.Equal(expected,Scene2Jcs.Number(BitConverter.Int64BitsToDouble(unchecked((long)ulong.Parse(bits,NumberStyles.HexNumber)))));
    [Fact]
    public void Sorting_uses_raw_utf16_units_and_strings_are_not_html_escaped_or_normalized()
    {
        const string input="{\"\\ufb33\":7,\"\\ud83d\\ude00\":6,\"\\u20ac\":5,\"\\u00f6\":4,\"\\u0080\":3,\"1\":2,\"\\r\":1}";
        Assert.Equal("{\"\\r\":1,\"1\":2,\"\u0080\":3,\"ö\":4,\"€\":5,\"😀\":6,\"דּ\":7}",Encoding.UTF8.GetString(Scene2Jcs.Canonicalize(input)));
        const string escaped="{\"é\":\"e\\u0301<&>/\\u000f\\b\\t\\n\\f\\r\"}";
        Assert.Equal("{\"é\":\"e\u0301<&>/\\u000f\\b\\t\\n\\f\\r\"}",Encoding.UTF8.GetString(Scene2Jcs.Canonicalize(escaped)));
    }
    [Theory]
    [InlineData("{\"a\":1,\"a\":2}")][InlineData("{\"nested\":{\"x\":1,\"x\":2}}")]
    [InlineData("[1e400]")][InlineData("[\"\\ud800\"]")][InlineData("{\"\\udead\":1}")][InlineData("{\"a\":1,}")]
    public void Invalid_unicode_duplicate_members_and_nonfinite_numbers_refuse(string json)
        =>Assert.Throws<DocumentFormatException>(()=>Scene2Jcs.Canonicalize(json));
    [Fact]
    public void Accepted_definition_has_exact_pre_default_revision_and_budget_refusals_are_bounded()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Bed.slnx")))root=root.Parent;
        Assert.NotNull(root);var folder=Path.Combine(root.FullName,"tests/Bed.Tests/Fixtures/Scene2");
        var source=File.ReadAllText(Path.Combine(folder,"prefab-definition.example.json"));var expected=File.ReadAllBytes(Path.Combine(folder,"example-definition.canonical.json"));
        var canonical=Scene2Jcs.Canonicalize(source);Assert.Equal(expected,canonical);
        Assert.Equal("3401412037e446e3cb76c970e49647fce19aba75aca39a02f8e43c80e1bb1c05",Convert.ToHexStringLower(SHA256.HashData(canonical)));
        Assert.Throws<DocumentFormatException>(()=>Scene2Jcs.Canonicalize("[1e20]",10)); // Output expansion also respects the byte cap.
        Assert.Throws<DocumentFormatException>(()=>Scene2Jcs.Canonicalize(new string('[',65)+"0"+new string(']',65)));
        Assert.NotEqual(Scene2Jcs.Canonicalize("{\"v\":1}"),Scene2Jcs.Canonicalize("{\"v\":1,\"default\":false}"));
    }
    [Fact]
    public void Parsed_numbers_nested_sorting_array_order_and_culture_match_the_canonical_contract()
    {
        var old=CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
            var result=Scene2Jcs.Canonicalize("{\"z\":[{\"b\":4.50,\"a\":1E30},3,2,1],\"a\":333333333.33333329}");
            Assert.Equal("{\"a\":333333333.3333333,\"z\":[{\"a\":1e+30,\"b\":4.5},3,2,1]}",Encoding.UTF8.GetString(result));
            Assert.Throws<DocumentFormatException>(()=>Scene2Jcs.Canonicalize("\"\ud800\""));
        }
        finally{CultureInfo.CurrentCulture=old;}
    }
    [Theory][InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(double.NegativeInfinity)]
    public void Invalid_direct_ieee_values_refuse_before_number_serialization(double value)
        =>Assert.Throws<DocumentFormatException>(()=>Scene2Jcs.Number(value));

}
