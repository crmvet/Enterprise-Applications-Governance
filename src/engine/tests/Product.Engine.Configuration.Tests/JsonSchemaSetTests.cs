using System;
using System.IO;
using System.Linq;
using Product.Engine.Configuration.Json;
using Product.Engine.Configuration.Schema;
using Product.Engine.Core.Canonical;
using Product.Engine.Testing;
using Xunit;

namespace Product.Engine.Configuration.Tests;

// The draft 2020-12 checker behind V01. It must agree with the repo's JSON check (ajv) on the product's schemas.
public sealed class JsonSchemaSetTests
{
    private static JsonSchemaSet Set(string schemaJson)
    {
        return JsonSchemaSet.Create(new[] { CanonicalJson.Parse(schemaJson) });
    }

    private static SchemaResult Check(string schemaJson, string instanceJson)
    {
        return Set(schemaJson).Evaluate(CanonicalJson.Parse(instanceJson), "t.json");
    }

    [Fact]
    public void TheEmbeddedSchemasCompile()
    {
        Assert.Contains(ConfigurationSchemas.BundleId, ConfigurationSchemas.Set.DocumentIds);
        Assert.Contains(ConfigurationSchemas.ClientBindingPatchId, ConfigurationSchemas.Set.DocumentIds);
        Assert.Contains(FixtureSchemas.FixtureId, FixtureSchemas.Set.DocumentIds);
        Assert.True(ConfigurationSchemas.Set.Contains(ConfigurationSchemas.DraftPatch));
        Assert.True(ConfigurationSchemas.Set.Contains(ConfigurationSchemas.ValidationReport));
    }

    [Theory]
    [InlineData("doc21-balanced.bundle.json")]
    [InlineData("doc21-draft.patch.json")]
    public void TheDocument21ExamplesMatchTheRootSchema(string file)
    {
        Assert.True(JsonText.TryReadFile(Path.Combine(TestData.Root, "config", "schema", "examples", file), out JsonTreeNode? document, out string? error), error);
        SchemaResult result = ConfigurationSchemas.Set.Evaluate(document!, ConfigurationSchemas.Root);
        Assert.True(result.IsValid, string.Join("\n", result.Issues));
    }

    [Fact]
    public void AnUnknownKeywordIsRefused()
    {
        var e = Assert.Throws<InvalidOperationException>(() => Set("{\"$id\":\"t.json\",\"type\":\"object\",\"propertiez\":{}}"));
        Assert.Contains("propertiez", e.Message);
    }

    [Fact]
    public void AnUnknownFormatIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => Set("{\"$id\":\"t.json\",\"type\":\"string\",\"format\":\"hostname\"}"));
    }

    [Fact]
    public void ARefThatDoesNotResolveIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => Set("{\"$id\":\"t.json\",\"$ref\":\"#/$defs/missing\"}"));
    }

    [Fact]
    public void UnevaluatedPropertiesSeesFieldsFromAllOfAndRef()
    {
        const string schema = "{\"$id\":\"t.json\",\"allOf\":[{\"$ref\":\"#/$defs/a\"},{\"properties\":{\"b\":{\"type\":\"integer\"}}}],"
            + "\"unevaluatedProperties\":false,\"$defs\":{\"a\":{\"properties\":{\"a\":{\"type\":\"string\"}}}}}";

        Assert.True(Check(schema, "{\"a\":\"x\",\"b\":1}").IsValid);
        SchemaResult bad = Check(schema, "{\"a\":\"x\",\"c\":true}");
        Assert.False(bad.IsValid);
        Assert.Contains(bad.Issues, i => i.Message.Contains("unknown field \"c\""));
    }

    [Fact]
    public void UnevaluatedPropertiesSeesOnlyTheBranchThatApplied()
    {
        const string schema = "{\"$id\":\"t.json\",\"if\":{\"properties\":{\"k\":{\"const\":\"a\"}}},"
            + "\"then\":{\"properties\":{\"k\":true,\"x\":true}},\"else\":{\"properties\":{\"k\":true,\"y\":true}},\"unevaluatedProperties\":false}";

        Assert.True(Check(schema, "{\"k\":\"a\",\"x\":1}").IsValid);
        Assert.False(Check(schema, "{\"k\":\"a\",\"y\":1}").IsValid);
        Assert.True(Check(schema, "{\"k\":\"b\",\"y\":1}").IsValid);
    }

    [Fact]
    public void OneOfNeedsExactlyOneMatch()
    {
        const string schema = "{\"$id\":\"t.json\",\"oneOf\":[{\"required\":[\"a\"]},{\"required\":[\"b\"]}]}";
        Assert.True(Check(schema, "{\"a\":1}").IsValid);

        SchemaResult both = Check(schema, "{\"a\":1,\"b\":2}");
        Assert.False(both.IsValid);
        Assert.Contains("sets \"a\" and \"b\"", both.Issues.Single().Message);

        SchemaResult none = Check(schema, "{}");
        Assert.False(none.IsValid);
        Assert.Contains("fits none of the allowed shapes", none.Issues.Single().Message);
    }

    [Theory]
    [InlineData("\"tier-1\"", true)]
    [InlineData("\"Tier-1\"", false)]
    [InlineData("\"tier-1\\n\"", false)]
    [InlineData("\"\"", false)]
    public void PatternsAnchorAtTheEndOfTheText(string value, bool valid)
    {
        // In JSON Schema "$" matches only at the very end, so a key with a trailing newline fails.
        Assert.Equal(valid, Check("{\"$id\":\"t.json\",\"type\":\"string\",\"pattern\":\"^[a-z][a-z0-9-]{0,63}$\"}", value).IsValid);
    }

    [Theory]
    [InlineData("date", "\"2026-11-22\"", true)]
    [InlineData("date", "\"2026-02-30\"", false)]
    [InlineData("date-time", "\"2026-11-22T09:00:00Z\"", true)]
    [InlineData("date-time", "\"2026-11-22T09:00:00.25+01:00\"", true)]
    [InlineData("date-time", "\"2026-11-22 09:00\"", false)]
    [InlineData("uuid", "\"00000000-0000-4000-8000-000000000001\"", true)]
    [InlineData("uuid", "\"not-a-uuid\"", false)]
    [InlineData("uri", "\"https://learn.microsoft.com/power-platform/\"", true)]
    [InlineData("uri", "\"/relative/path\"", false)]
    [InlineData("regex", "\"^[a-z]+$\"", true)]
    [InlineData("regex", "\"[a-z\"", false)]
    public void FormatsAreChecked(string format, string value, bool valid)
    {
        Assert.Equal(valid, Check("{\"$id\":\"t.json\",\"type\":\"string\",\"format\":\"" + format + "\"}", value).IsValid);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("1.0", true)]
    [InlineData("1.5", false)]
    [InlineData("\"1\"", false)]
    public void IntegersAreWholeNumbers(string value, bool valid)
    {
        Assert.Equal(valid, Check("{\"$id\":\"t.json\",\"type\":\"integer\"}", value).IsValid);
    }

    [Fact]
    public void ArrayAndNumberAssertionsApply()
    {
        const string schema = "{\"$id\":\"t.json\",\"type\":\"array\",\"minItems\":1,\"maxItems\":3,\"uniqueItems\":true,\"items\":{\"type\":\"integer\",\"minimum\":1}}";
        Assert.True(Check(schema, "[1,2,3]").IsValid);
        Assert.False(Check(schema, "[]").IsValid);
        Assert.False(Check(schema, "[1,2,3,4]").IsValid);
        Assert.False(Check(schema, "[1,1]").IsValid);
        Assert.False(Check(schema, "[0]").IsValid);
    }

    [Fact]
    public void AnnotationsComeOnlyFromBranchesThatApplied()
    {
        const string schema = "{\"$id\":\"t.json\",\"properties\":{\"a\":{\"x-references\":\"tier\"}},"
            + "\"if\":{\"properties\":{\"k\":{\"const\":\"on\"}}},\"then\":{\"properties\":{\"b\":{\"x-references\":\"role\"}}}}";

        SchemaResult on = Check(schema, "{\"a\":\"t\",\"b\":\"r\",\"k\":\"on\"}");
        Assert.Contains(on.Annotations, a => a.InstancePath == "/a" && a.Value.GetString() == "tier");
        Assert.Contains(on.Annotations, a => a.InstancePath == "/b" && a.Value.GetString() == "role");

        SchemaResult off = Check(schema, "{\"a\":\"t\",\"b\":\"r\",\"k\":\"off\"}");
        Assert.Contains(off.Annotations, a => a.InstancePath == "/a");
        Assert.DoesNotContain(off.Annotations, a => a.InstancePath == "/b");
    }

    [Fact]
    public void RefsResolveAcrossSchemasById()
    {
        JsonSchemaSet set = JsonSchemaSet.Create(new[]
        {
            CanonicalJson.Parse("{\"$id\":\"a.json\",\"$defs\":{\"key\":{\"type\":\"string\",\"minLength\":1}}}"),
            CanonicalJson.Parse("{\"$id\":\"b.json\",\"properties\":{\"k\":{\"$ref\":\"a.json#/$defs/key\"}}}"),
        });
        Assert.True(set.Evaluate(CanonicalJson.Parse("{\"k\":\"x\"}"), "b.json").IsValid);
        Assert.False(set.Evaluate(CanonicalJson.Parse("{\"k\":\"\"}"), "b.json").IsValid);
    }
}
