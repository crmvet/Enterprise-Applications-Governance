using System;
using System.Linq;
using Product.Engine.Configuration.Schema;

namespace Product.Engine.Testing;

// The fixture schema (config/schema/fixture.schema.json) with the configuration schemas it refers to.
public static class FixtureSchemas
{
    public const string FixtureId = "fixture.schema.json";

    private static readonly Lazy<JsonSchemaSet> LazySet = new(() => JsonSchemaSet.Create(
        ConfigurationSchemas.Documents.Concat(new[] { ConfigurationSchemas.LoadEmbedded(typeof(FixtureSchemas).Assembly, FixtureId) })));

    public static JsonSchemaSet Set => LazySet.Value;
}
