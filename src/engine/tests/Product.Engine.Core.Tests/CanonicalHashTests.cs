using System;
using Product.Engine.Core.Canonical;
using Xunit;

namespace Product.Engine.Core.Tests;

// SHA-256 over canonical JSON (document 25, sections 6.2, 8 and 9).
// The expected hashes below were computed outside .NET, with Python's hashlib over the expected canonical text.
// The same constants run on net48 and net10.0, so a pass on both proves the same input gives the same hash on every target.
public sealed class CanonicalHashTests
{
    private const string RequestText = "{\"a\":[3,1,2],\"b\":1,\"c\":{\"y\":null,\"z\":true}}";
    private const string RequestHash = "2a4b28cf0ebd27fc5e5d487bc4b7f051ee5043a09f774ecd9da630cf1e97fbd0";

    private const string SnapshotText =
        "{\"entities\":{\"setting\":[{\"key\":\"working-days\",\"value\":[\"mon\",\"tue\",\"wed\",\"thu\",\"fri\"]}],"
        + "\"tier\":[{\"key\":\"tier-1\",\"label\":\"Tier 1\",\"rank\":1},{\"key\":\"tier-2\",\"label\":\"Tier 2\",\"rank\":2}]},"
        + "\"manifest\":{\"kind\":\"bundle\",\"presetKey\":\"sample\"},\"schemaVersion\":\"1.0\"}";

    private const string SnapshotHash = "8622543ce66b398c4922d3d34eb895c30ece337675c39bbfd0f0533c4d715065";

    private const string UnicodeHash = "a68a53c12c63d084f13c29cdfa56597ae20444f39c3c46171838be8c879f5f1d";

    private const string NumbersHash = "0d18edf7f8ce95eb671be84236185544755b670c62d8122b48e18809426faacd";

    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void Sha256MatchesTheStandardTestVectors(string text, string expected)
    {
        Assert.Equal(expected, CanonicalHash.Sha256Hex(text));
    }

    [Fact]
    public void RequestHashMatchesTheGoldenValue()
    {
        string messy = "{ \"c\": { \"z\": true, \"y\": null }, \"b\": 1.0, \"a\": [3, 1, 2] }";

        Assert.Equal(RequestText, CanonicalJson.Canonicalize(messy, CanonicalProfile.Request));
        Assert.Equal(RequestHash, CanonicalHash.ComputeRequestHash(messy));
        Assert.Equal(RequestHash, CanonicalHash.ComputeRequestHash(CanonicalJson.Parse(messy)));
    }

    [Fact]
    public void ContentHashMatchesTheGoldenValue()
    {
        // Rows out of order, members out of order, a rank written as 2.0, and extra whitespace.
        string messy = "{\n  \"schemaVersion\": \"1.0\",\n"
            + "  \"manifest\": { \"presetKey\": \"sample\", \"kind\": \"bundle\" },\n"
            + "  \"entities\": {\n"
            + "    \"tier\": [\n"
            + "      { \"label\": \"Tier 2\", \"key\": \"tier-2\", \"rank\": 2.0 },\n"
            + "      { \"key\": \"tier-1\", \"rank\": 1, \"label\": \"Tier 1\" }\n"
            + "    ],\n"
            + "    \"setting\": [ { \"key\": \"working-days\", \"value\": [\"mon\", \"tue\", \"wed\", \"thu\", \"fri\"] } ]\n"
            + "  }\n"
            + "}\n";

        Assert.Equal(SnapshotText, CanonicalJson.Canonicalize(messy, CanonicalProfile.Snapshot));
        Assert.Equal(SnapshotHash, CanonicalHash.ComputeContentHash(messy));
        Assert.Equal(SnapshotHash, CanonicalHash.ComputeContentHash(CanonicalJson.Parse(messy)));
        Assert.True(CanonicalHash.VerifyContentHash(messy, SnapshotHash));
    }

    [Fact]
    public void UnicodeHashMatchesTheGoldenValue()
    {
        string escaped = "{\"tab\":\"a\\u0009b\",\"name\":\"caf\\u00e9 \\ud83d\\ude00\",\"ctl\":\"\\u0001\"}";
        string literal = "{\"name\":\"caf\u00E9 \uD83D\uDE00\",\"ctl\":\"\\u0001\",\"tab\":\"a\\tb\"}";

        Assert.Equal(UnicodeHash, CanonicalHash.ComputeRequestHash(escaped));
        Assert.Equal(UnicodeHash, CanonicalHash.ComputeRequestHash(literal));
    }

    [Fact]
    public void NumbersHashMatchesTheGoldenValue()
    {
        string messy = "[0.0, 1.000, 15e-1, -12.340, 1E2, 1e21, 1E-6, 0.0000001, 9007199254740993]";

        Assert.Equal("[0,1,1.5,-12.34,100,1e+21,0.000001,1e-7,9007199254740993]", CanonicalJson.Canonicalize(messy, CanonicalProfile.Request));
        Assert.Equal(NumbersHash, CanonicalHash.ComputeRequestHash(messy));
    }

    [Fact]
    public void KeyOrderDoesNotChangeTheHash()
    {
        string first = "{\"decisionTypeKey\":\"go-live\",\"tierKey\":\"tier-2\",\"portfolioPath\":[\"p2\",\"p1\"]}";
        string second = "{\"portfolioPath\":[\"p2\",\"p1\"],\"tierKey\":\"tier-2\",\"decisionTypeKey\":\"go-live\"}";

        Assert.Equal(CanonicalHash.ComputeRequestHash(first), CanonicalHash.ComputeRequestHash(second));
    }

    [Fact]
    public void ArrayOrderChangesTheRequestHash()
    {
        // The portfolio path is nearest first. Its order carries meaning, so a request hash must see it.
        string first = "{\"portfolioPath\":[\"p2\",\"p1\"]}";
        string second = "{\"portfolioPath\":[\"p1\",\"p2\"]}";

        Assert.NotEqual(CanonicalHash.ComputeRequestHash(first), CanonicalHash.ComputeRequestHash(second));
    }

    [Fact]
    public void AnyValueChangeChangesTheHash()
    {
        string baseline = CanonicalHash.ComputeContentHash(SnapshotText);

        Assert.NotEqual(baseline, CanonicalHash.ComputeContentHash(SnapshotText.Replace("\"rank\":2", "\"rank\":3")));
        Assert.NotEqual(baseline, CanonicalHash.ComputeContentHash(SnapshotText.Replace("Tier 1", "Tier 1 ")));
        Assert.NotEqual(baseline, CanonicalHash.ComputeContentHash(SnapshotText.Replace("\"mon\",\"tue\"", "\"tue\",\"mon\"")));
        Assert.NotEqual(baseline, CanonicalHash.ComputeContentHash(SnapshotText.Replace("\"1.0\"", "\"1.1\"")));
    }

    [Fact]
    public void HashesAreLowercaseHexOf64Characters()
    {
        string hash = CanonicalHash.ComputeRequestHash("{}");

        Assert.True(CanonicalHash.IsHash(hash));
        Assert.Equal(CanonicalHash.HexLength, hash.Length);
        Assert.False(CanonicalHash.IsHash(hash.ToUpperInvariant()));
        Assert.False(CanonicalHash.IsHash(hash.Substring(1)));
        Assert.False(CanonicalHash.IsHash(null));
        Assert.False(CanonicalHash.IsHash("sha256:" + hash.Substring(7)));
    }

    [Fact]
    public void VerifyRefusesAWrongOrMalformedHash()
    {
        Assert.False(CanonicalHash.VerifyContentHash(SnapshotText, RequestHash));
        Assert.False(CanonicalHash.VerifyContentHash(SnapshotText, SnapshotHash.ToUpperInvariant()));
        Assert.False(CanonicalHash.VerifyContentHash(SnapshotText, string.Empty));
    }

    [Fact]
    public void HashingIsRepeatable()
    {
        string first = CanonicalHash.ComputeContentHash(SnapshotText);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(first, CanonicalHash.ComputeContentHash(SnapshotText));
        }
    }

    [Fact]
    public void BadJsonIsNotHashed()
    {
        Assert.Throws<CanonicalJsonException>(() => CanonicalHash.ComputeRequestHash("{\"a\":1,\"a\":2}"));
        Assert.Throws<CanonicalJsonException>(() => CanonicalHash.ComputeContentHash("[1,]"));
        Assert.Throws<ArgumentNullException>(() => CanonicalHash.Sha256Hex((byte[])null!));
    }
}
