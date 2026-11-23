using System;
using System.Linq;
using Product.Engine.Core.Canonical;
using Xunit;

namespace Product.Engine.Core.Tests;

// Canonical JSON form (document 25, sections 6.2 and 9): key order, whitespace, unicode and strict input.
// Source files stay ASCII, so non-ASCII text is written with C# \u escapes.
public sealed class CanonicalJsonTests
{
    private static string Request(string json)
    {
        return CanonicalJson.Canonicalize(json, CanonicalProfile.Request);
    }

    private static string Snapshot(string json)
    {
        return CanonicalJson.Canonicalize(json, CanonicalProfile.Snapshot);
    }

    // ---- key order ----

    [Theory]
    [InlineData("{\"b\":1,\"a\":2}", "{\"a\":2,\"b\":1}")]
    [InlineData("{\"ab\":1,\"a\":2}", "{\"a\":2,\"ab\":1}")]
    [InlineData("{\"a\":1,\"B\":2,\"_\":3}", "{\"B\":2,\"_\":3,\"a\":1}")]
    [InlineData("{\"z\":{\"d\":1,\"c\":2},\"y\":[{\"q\":1,\"p\":2}]}", "{\"y\":[{\"p\":2,\"q\":1}],\"z\":{\"c\":2,\"d\":1}}")]
    [InlineData("{\"10\":1,\"9\":2,\"1\":3}", "{\"1\":3,\"10\":1,\"9\":2}")]
    [InlineData("{}", "{}")]
    public void MembersAreSortedByOrdinalName(string input, string expected)
    {
        Assert.Equal(expected, Request(input));
    }

    [Fact]
    public void MemberOrderUsesUtf16CodeUnitsNotCodePoints()
    {
        // U+1F600 is the surrogate pair D83D DE00. In UTF-16 order it comes before U+FFFD,
        // although its code point is higher. RFC 8785 uses the same UTF-16 order.
        string input = "{\"\uFFFD\":1,\"\uD83D\uDE00\":2,\"\u00E9\":3,\"z\":4}";
        string expected = "{\"z\":4,\"\u00E9\":3,\"\uD83D\uDE00\":2,\"\uFFFD\":1}";

        Assert.Equal(expected, Request(input));
    }

    [Fact]
    public void MemberOrderInTheInputDoesNotChangeTheCanonicalText()
    {
        string first = "{\"name\":\"Sample app\",\"tier\":\"tier-2\",\"owners\":{\"technical\":\"t1\",\"business\":\"b1\"}}";
        string second = "{\"owners\":{\"business\":\"b1\",\"technical\":\"t1\"},\"tier\":\"tier-2\",\"name\":\"Sample app\"}";

        Assert.Equal(Request(first), Request(second));
    }

    // ---- whitespace and literals ----

    [Fact]
    public void InsignificantWhitespaceIsRemoved()
    {
        string input = " \r\n\t{ \"a\" : [ 1 , 2 ] ,\n\t\"b\" : { } , \"c\" : [ ] , \"d\" : \" x y \" }\r\n ";

        Assert.Equal("{\"a\":[1,2],\"b\":{},\"c\":[],\"d\":\" x y \"}", Request(input));
    }

    [Theory]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("null", "null")]
    [InlineData("\"text\"", "\"text\"")]
    [InlineData("[true,false,null]", "[true,false,null]")]
    public void ScalarsAtTheRootAreAllowed(string input, string expected)
    {
        Assert.Equal(expected, Request(input));
    }

    [Fact]
    public void ArrayOrderIsKeptInTheRequestProfile()
    {
        Assert.Equal("[3,1,2,{\"key\":\"b\"},{\"key\":\"a\"}]", Request("[3, 1, 2, {\"key\":\"b\"}, {\"key\":\"a\"}]"));
    }

    // ---- unicode ----

    [Fact]
    public void EscapedAndLiteralCharactersGiveTheSameText()
    {
        string escaped = "{\"name\":\"caf\\u00e9 \\ud83d\\ude00\"}";
        string upperHex = "{\"name\":\"caf\\u00E9 \\uD83D\\uDE00\"}";
        string literal = "{\"name\":\"caf\u00E9 \uD83D\uDE00\"}";

        Assert.Equal(literal, Request(escaped));
        Assert.Equal(literal, Request(upperHex));
        Assert.Equal(literal, Request(literal));
    }

    [Theory]
    [InlineData("\"\\u0001\\u001f\"", "\"\\u0001\\u001f\"")]
    [InlineData("\"\\u0008\\u000c\\u000a\\u000d\\u0009\"", "\"\\b\\f\\n\\r\\t\"")]
    [InlineData("\"\\b\\f\\n\\r\\t\"", "\"\\b\\f\\n\\r\\t\"")]
    [InlineData("\"\\u0000\"", "\"\\u0000\"")]
    [InlineData("\"\\u001F\"", "\"\\u001f\"")]
    [InlineData("\"a\\\"b\\\\c\"", "\"a\\\"b\\\\c\"")]
    [InlineData("\"\\/\"", "\"/\"")]
    [InlineData("\"\\u002F\"", "\"/\"")]
    [InlineData("\"\\u0041\"", "\"A\"")]
    public void OnlyQuoteBackslashAndControlCharactersAreEscaped(string input, string expected)
    {
        Assert.Equal(expected, Request(input));
    }

    [Fact]
    public void DeleteAndLineSeparatorAreWrittenAsThemselves()
    {
        // RFC 8785 escapes neither U+007F nor U+2028. Neither does this form.
        Assert.Equal("\"a\u007Fb\u2028c\"", Request("\"a\\u007fb\\u2028c\""));
    }

    [Fact]
    public void NoUnicodeNormalizationIsApplied()
    {
        // Precomposed e-acute (NFC) and e plus a combining acute (NFD) look the same but are different text.
        // Normalizing would tie the hash to the runtime's Unicode tables, so the form keeps code points as they are.
        string composed = Request("\"\u00E9\"");
        string decomposed = Request("\"e\u0301\"");

        Assert.NotEqual(composed, decomposed);
        Assert.NotEqual(CanonicalHash.ComputeRequestHash("\"\u00E9\""), CanonicalHash.ComputeRequestHash("\"e\u0301\""));
    }

    [Fact]
    public void Utf8BytesHaveNoByteOrderMark()
    {
        byte[] bytes = CanonicalJson.ToUtf8("caf\u00E9 \uD83D\uDE00");

        Assert.Equal(new byte[] { 99, 97, 102, 0xC3, 0xA9, 32, 0xF0, 0x9F, 0x98, 0x80 }, bytes);
    }

    [Theory]
    [InlineData("\"\\ud83d\"")]
    [InlineData("\"\\ude00\"")]
    [InlineData("\"\\ude00\\ud83d\"")]
    [InlineData("\"a\\ud83dx\"")]
    [InlineData("{\"\\ud83d\":1}")]
    public void EscapedLoneSurrogatesAreRefused(string input)
    {
        CanonicalJsonException e = Assert.Throws<CanonicalJsonException>(() => Request(input));
        Assert.Equal("input.json-invalid", e.ReasonCode);
    }

    [Fact]
    public void LiteralLoneSurrogatesAreRefused()
    {
        Assert.Throws<CanonicalJsonException>(() => Request("\"a\uD83Db\""));
        Assert.Throws<CanonicalJsonException>(() => JsonTreeNode.FromString("\uDE00"));
    }

    // ---- strict input ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("[1,]")]
    [InlineData("[,1]")]
    [InlineData("{\"a\":1,}")]
    [InlineData("{'a':1}")]
    [InlineData("{a:1}")]
    [InlineData("{\"a\" 1}")]
    [InlineData("[1 2]")]
    [InlineData("{} {}")]
    [InlineData("// note\n1")]
    [InlineData("/* note */ 1")]
    [InlineData("tru")]
    [InlineData("True")]
    [InlineData("nul")]
    [InlineData("nulls")]
    [InlineData("\"open")]
    [InlineData("\"tab\there\"")]
    [InlineData("\"line\nbreak\"")]
    [InlineData("\"\\x41\"")]
    [InlineData("\"\\u12\"")]
    [InlineData("\"\\u12G4\"")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("\uFEFF{}")]
    [InlineData("\u00A0{}")]
    public void InvalidJsonIsRefused(string input)
    {
        CanonicalJsonException e = Assert.Throws<CanonicalJsonException>(() => CanonicalJson.Parse(input));
        Assert.Equal("input.json-invalid", e.ReasonCode);
    }

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"a\":1,\"\\u0061\":2}")]
    [InlineData("{\"x\":{\"b\":1,\"b\":1}}")]
    public void RepeatedMemberNamesAreRefused(string input)
    {
        CanonicalJsonException e = Assert.Throws<CanonicalJsonException>(() => CanonicalJson.Parse(input));
        Assert.True(e.Position >= 0);
    }

    [Fact]
    public void ErrorsReportTheCharacterPosition()
    {
        CanonicalJsonException e = Assert.Throws<CanonicalJsonException>(() => CanonicalJson.Parse("[1,2,x]"));

        Assert.Equal(5, e.Position);
    }

    [Fact]
    public void TryParseReturnsAnErrorInsteadOfThrowing()
    {
        Assert.False(CanonicalJson.TryParse("[1,", out JsonTreeNode? node, out string? error));
        Assert.Null(node);
        Assert.False(string.IsNullOrEmpty(error));

        Assert.True(CanonicalJson.TryParse("[1]", out node, out error));
        Assert.NotNull(node);
        Assert.Null(error);
    }

    [Fact]
    public void NestingUpToTheLimitIsAccepted()
    {
        string json = new string('[', CanonicalJson.MaxDepth) + new string(']', CanonicalJson.MaxDepth);

        Assert.Equal(json, Request(json));
    }

    [Fact]
    public void NestingBeyondTheLimitIsRefused()
    {
        int depth = CanonicalJson.MaxDepth + 1;
        string json = new string('[', depth) + new string(']', depth);

        Assert.Throws<CanonicalJsonException>(() => CanonicalJson.Parse(json));
    }

    // ---- snapshot profile ----

    [Fact]
    public void SnapshotProfileSortsEntityRowsByKey()
    {
        string input = "{\"schemaVersion\":\"1.0\",\"entities\":{\"tier\":["
            + "{\"key\":\"tier-3\",\"rank\":3},{\"key\":\"tier-1\",\"rank\":1},{\"key\":\"tier-2\",\"rank\":2}]}}";
        string expected = "{\"entities\":{\"tier\":["
            + "{\"key\":\"tier-1\",\"rank\":1},{\"key\":\"tier-2\",\"rank\":2},{\"key\":\"tier-3\",\"rank\":3}]},\"schemaVersion\":\"1.0\"}";

        Assert.Equal(expected, Snapshot(input));
    }

    [Fact]
    public void SnapshotProfileKeepsArraysInsideRowsInOrder()
    {
        // Conditions Json is ordered: OP-12 reports failed conditions by index.
        string input = "{\"entities\":{\"standard-change-model\":[{\"key\":\"m1\",\"conditions\":["
            + "{\"key\":\"z\",\"kind\":\"stated\"},{\"key\":\"a\",\"kind\":\"stated\"}]}]}}";

        Assert.Contains("[{\"key\":\"z\",\"kind\":\"stated\"},{\"key\":\"a\",\"kind\":\"stated\"}]", Snapshot(input), StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotProfileKeepsArraysOutsideEntitiesInOrder()
    {
        // Patch operations are ordered and carry a key too. They are not entity rows.
        string input = "{\"operations\":[{\"op\":\"upsert\",\"key\":\"b\"},{\"op\":\"retire\",\"key\":\"a\"}]}";

        Assert.Equal("{\"operations\":[{\"key\":\"b\",\"op\":\"upsert\"},{\"key\":\"a\",\"op\":\"retire\"}]}", Snapshot(input));
    }

    [Fact]
    public void SnapshotProfileSortsOnlyTheTopLevelEntitiesObject()
    {
        string input = "{\"nested\":{\"entities\":{\"tier\":[{\"key\":\"b\"},{\"key\":\"a\"}]}}}";

        Assert.Equal("{\"nested\":{\"entities\":{\"tier\":[{\"key\":\"b\"},{\"key\":\"a\"}]}}}", Snapshot(input));
    }

    [Fact]
    public void SnapshotProfileLeavesMixedArraysInOrder()
    {
        // When one element has no string key, the array is not a row list and keeps its order.
        string input = "{\"entities\":{\"x\":[{\"key\":\"b\"},{\"label\":\"no key\"},{\"key\":\"a\"}],\"y\":[{\"key\":\"b\"},{\"key\":1}]}}";

        Assert.Equal("{\"entities\":{\"x\":[{\"key\":\"b\"},{\"label\":\"no key\"},{\"key\":\"a\"}],\"y\":[{\"key\":\"b\"},{\"key\":1}]}}", Snapshot(input));
    }

    [Fact]
    public void SnapshotProfileBreaksKeyTiesByRowText()
    {
        // Child rows share a key under different parents (document 21, section 13.5).
        string first = "{\"entities\":{\"tier-answer-option\":[{\"key\":\"low\",\"questionKey\":\"q2\"},{\"key\":\"low\",\"questionKey\":\"q1\"}]}}";
        string second = "{\"entities\":{\"tier-answer-option\":[{\"key\":\"low\",\"questionKey\":\"q1\"},{\"key\":\"low\",\"questionKey\":\"q2\"}]}}";

        Assert.Equal(Snapshot(first), Snapshot(second));
        Assert.Equal(second, Snapshot(first));
    }

    [Fact]
    public void RowOrderChangesTheRequestFormButNotTheSnapshotForm()
    {
        string first = "{\"entities\":{\"tier\":[{\"key\":\"tier-1\"},{\"key\":\"tier-2\"}]}}";
        string second = "{\"entities\":{\"tier\":[{\"key\":\"tier-2\"},{\"key\":\"tier-1\"}]}}";

        Assert.Equal(Snapshot(first), Snapshot(second));
        Assert.NotEqual(Request(first), Request(second));
    }

    // ---- dates ----

    [Fact]
    public void UtcDatesUseOneIso8601Form()
    {
        var whole = new DateTime(2026, 11, 22, 9, 30, 0, DateTimeKind.Utc);

        Assert.Equal("2026-11-22T09:30:00Z", CanonicalJson.FormatUtc(whole));
        Assert.Equal("2026-11-22T09:30:00.25Z", CanonicalJson.FormatUtc(whole.AddMilliseconds(250)));
        Assert.Equal("2026-11-22T09:30:00.0000001Z", CanonicalJson.FormatUtc(whole.AddTicks(1)));
        Assert.Equal("0001-01-01T00:00:00Z", CanonicalJson.FormatUtc(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void DatesThatAreNotUtcAreRefused(DateTimeKind kind)
    {
        var value = new DateTime(2026, 11, 22, 9, 30, 0, kind);

        Assert.Throws<ArgumentException>(() => CanonicalJson.FormatUtc(value));
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("de-DE")]
    [InlineData("fa-IR")]
    public void TheCurrentCultureChangesNothing(string cultureName)
    {
        var date = new DateTime(2026, 11, 22, 9, 30, 0, DateTimeKind.Utc);
        string json = "{\"b\":1.50,\"a\":\"I\",\"i\":-0.000001}";
        string expectedJson = "{\"a\":\"I\",\"b\":1.5,\"i\":-0.000001}";

        System.Globalization.CultureInfo saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(cultureName);

            Assert.Equal("2026-11-22T09:30:00Z", CanonicalJson.FormatUtc(date));
            Assert.Equal(expectedJson, Request(json));
            Assert.Equal("1.5", JsonTreeNode.FromDecimal(1.5m).GetNumberText());
            Assert.Equal("-12", JsonTreeNode.FromInt64(-12).GetNumberText());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void TheTurkishCultureDoesNotChangeMemberOrder()
    {
        // Culture-aware comparison treats "i" and "I" specially in Turkish. Ordinal order never does.
        System.Globalization.CultureInfo saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");

            Assert.Equal("{\"I\":1,\"i\":2,\"\u0130\":3,\"\u0131\":4}", Request("{\"\u0131\":4,\"i\":2,\"\u0130\":3,\"I\":1}"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void ParsedTreeAndBuiltTreeGiveTheSameText()
    {
        JsonTreeNode built = JsonTreeNode.CreateObject(
            Member("tier", JsonTreeNode.FromString("tier-2")),
            Member("count", JsonTreeNode.FromInt64(3)),
            Member("ratio", JsonTreeNode.FromDecimal(0.50m)),
            Member("on", JsonTreeNode.FromUtcDateTime(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc))),
            Member("tags", JsonTreeNode.CreateArray(JsonTreeNode.FromString("b"), JsonTreeNode.FromString("a"))),
            Member("none", JsonTreeNode.Null),
            Member("yes", JsonTreeNode.FromBoolean(true)));

        JsonTreeNode parsed = CanonicalJson.Parse(
            "{\"yes\":true,\"none\":null,\"tags\":[\"b\",\"a\"],\"on\":\"2026-01-02T03:04:05Z\",\"ratio\":0.5,\"count\":3.0,\"tier\":\"tier-2\"}");

        Assert.Equal(parsed.ToString(), built.ToString());
        Assert.Equal(parsed, built);
        Assert.Equal(parsed.GetHashCode(), built.GetHashCode());
    }

    [Fact]
    public void MemberNamesAreFoundByBinarySearch()
    {
        JsonTreeNode node = JsonTreeNode.CreateObject(
            Enumerable.Range(0, 50).Reverse().Select(i => Member("m" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), JsonTreeNode.FromInt64(i))));

        for (int i = 0; i < 50; i++)
        {
            Assert.True(node.TryGetMember("m" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), out JsonTreeNode? value));
            Assert.True(value.TryGetInt64(out long number));
            Assert.Equal(i, number);
        }

        Assert.False(node.TryGetMember("m50", out _));
        Assert.False(node.TryGetMember("M01", out _));
    }

    internal static System.Collections.Generic.KeyValuePair<string, JsonTreeNode> Member(string name, JsonTreeNode value)
    {
        return new System.Collections.Generic.KeyValuePair<string, JsonTreeNode>(name, value);
    }
}
