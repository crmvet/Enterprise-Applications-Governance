using Product.Engine.Core.Canonical;
using Xunit;

namespace Product.Engine.Core.Tests;

// Number formats in canonical JSON. Numbers are worked out from their decimal text, never through a double,
// so the same text gives the same canonical form on net48 and net10.0.
public sealed class CanonicalNumberTests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData("-0", "0")]
    [InlineData("0.0", "0")]
    [InlineData("-0.0e5", "0")]
    [InlineData("0e-10", "0")]
    [InlineData("1", "1")]
    [InlineData("1.0", "1")]
    [InlineData("1.000", "1")]
    [InlineData("10e-1", "1")]
    [InlineData("0.1E1", "1")]
    [InlineData("1e0", "1")]
    [InlineData("1E+0", "1")]
    [InlineData("1.50", "1.5")]
    [InlineData("-12.340", "-12.34")]
    [InlineData("12e-1", "1.2")]
    [InlineData("123e-2", "1.23")]
    [InlineData("100", "100")]
    [InlineData("1e2", "100")]
    [InlineData("1E+2", "100")]
    [InlineData("0.1", "0.1")]
    [InlineData("0.000001", "0.000001")]
    [InlineData("1E-6", "0.000001")]
    [InlineData("0.00012300", "0.000123")]
    [InlineData("0.0000001", "1e-7")]
    [InlineData("1.5e-7", "1.5e-7")]
    [InlineData("-5e-324", "-5e-324")]
    [InlineData("1e20", "100000000000000000000")]
    [InlineData("123456789012345678901", "123456789012345678901")]
    [InlineData("1e21", "1e+21")]
    [InlineData("1234567890123456789012", "1.234567890123456789012e+21")]
    [InlineData("-1.5e300", "-1.5e+300")]
    [InlineData("1e400", "1e+400")]
    [InlineData("9007199254740993", "9007199254740993")]
    [InlineData("3.14159265358979323846264338327950288", "3.14159265358979323846264338327950288")]
    [InlineData("1e000000000000002", "100")]
    public void NumbersHaveOneCanonicalForm(string input, string expected)
    {
        Assert.Equal(expected, JsonTreeNode.FromNumberText(input).GetNumberText());
        Assert.Equal(expected, CanonicalJson.Canonicalize(input, CanonicalProfile.Request));
        Assert.Equal("[" + expected + "]", CanonicalJson.Canonicalize("[ " + input + " ]", CanonicalProfile.Request));
    }

    [Theory]
    [InlineData("1", "1.0")]
    [InlineData("1", "10e-1")]
    [InlineData("100", "1e2")]
    [InlineData("0", "-0")]
    [InlineData("0.5", "5E-1")]
    public void EqualValuesGiveEqualHashes(string first, string second)
    {
        Assert.Equal(CanonicalHash.ComputeRequestHash("{\"n\":" + first + "}"), CanonicalHash.ComputeRequestHash("{\"n\":" + second + "}"));
    }

    [Fact]
    public void DigitsBeyondDoublePrecisionAreKept()
    {
        // As doubles, these two are the same number. As decimal text they differ, and so do their hashes.
        Assert.NotEqual(
            CanonicalHash.ComputeRequestHash("9007199254740993"),
            CanonicalHash.ComputeRequestHash("9007199254740992"));
    }

    [Theory]
    [InlineData("01")]
    [InlineData("-01")]
    [InlineData("00")]
    [InlineData("+1")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("1.e5")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("1e-")]
    [InlineData("-")]
    [InlineData("--1")]
    [InlineData("1.2.3")]
    [InlineData("1e5e5")]
    [InlineData("0x10")]
    [InlineData("1_000")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("")]
    [InlineData("1e1234567890")]
    public void BadNumberTextIsRefused(string input)
    {
        Assert.Throws<CanonicalJsonException>(() => JsonTreeNode.FromNumberText(input));
    }

    [Theory]
    [InlineData("[01]")]
    [InlineData("[+1]")]
    [InlineData("[.5]")]
    [InlineData("[1.]")]
    [InlineData("[1e1234567890]")]
    [InlineData("[0x10]")]
    public void BadNumbersInsideJsonAreRefused(string input)
    {
        Assert.Throws<CanonicalJsonException>(() => CanonicalJson.Parse(input));
    }

    [Fact]
    public void BuiltNumbersMatchParsedNumbers()
    {
        Assert.Equal("-9223372036854775808", JsonTreeNode.FromInt64(long.MinValue).GetNumberText());
        Assert.Equal("9223372036854775807", JsonTreeNode.FromInt64(long.MaxValue).GetNumberText());
        Assert.Equal("1.5", JsonTreeNode.FromDecimal(1.50m).GetNumberText());
        Assert.Equal("0", JsonTreeNode.FromDecimal(-0.000m).GetNumberText());
        Assert.Equal("7.9228162514264337593543950335e+28", JsonTreeNode.FromDecimal(decimal.MaxValue).GetNumberText());
        Assert.Equal("1e-28", JsonTreeNode.FromDecimal(0.0000000000000000000000000001m).GetNumberText());
    }

    [Theory]
    [InlineData("123", true, 123L)]
    [InlineData("-7", true, -7L)]
    [InlineData("1.0", true, 1L)]
    [InlineData("1e2", true, 100L)]
    [InlineData("1.5", false, 0L)]
    [InlineData("1e21", false, 0L)]
    [InlineData("9223372036854775808", false, 0L)]
    public void WholeNumbersReadAsInt64(string input, bool expectedOk, long expectedValue)
    {
        bool ok = JsonTreeNode.FromNumberText(input).TryGetInt64(out long value);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedValue, value);
    }

    [Fact]
    public void NumbersReadAsDecimalOnlyWhenExact()
    {
        Assert.True(JsonTreeNode.FromNumberText("0.1").TryGetDecimal(out decimal tenth));
        Assert.Equal(0.1m, tenth);
        Assert.True(JsonTreeNode.FromNumberText("1e-7").TryGetDecimal(out decimal small));
        Assert.Equal(0.0000001m, small);
        Assert.False(JsonTreeNode.FromNumberText("1e400").TryGetDecimal(out _));
        Assert.False(JsonTreeNode.FromNumberText("3.14159265358979323846264338327950288").TryGetDecimal(out _));
        Assert.False(JsonTreeNode.FromString("1").TryGetDecimal(out _));
    }
}
