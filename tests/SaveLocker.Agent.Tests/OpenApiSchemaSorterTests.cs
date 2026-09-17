using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Coverage for <see cref="OpenApiSchemaSorter"/> — previously exercised only indirectly through
/// CI's cross-OS <c>gen:api -- --check</c>, which proves the end result is byte-identical but not
/// that this specific function behaves correctly on its own. Extracted alongside
/// <see cref="OpenApiSchemaSorterMiddleware"/> (tasks/playnite-plugin/plan.md, Group 6 review) into
/// a single shared home for the Server and the agent's local API — this is the one piece of that
/// pair that is a pure function and cheap to test directly, without standing up either host.
/// </summary>
public sealed class OpenApiSchemaSorterTests
{
    [Fact]
    public void SortSchemasAlphabetically_OrdersKeysAndPreservesValues()
    {
        const string doc = """
            {
              "components": {
                "schemas": {
                  "Zebra": { "type": "string" },
                  "Apple": { "type": "object", "properties": { "n": { "type": "integer" } } },
                  "Mango": { "$ref": "#/components/schemas/Apple" }
                }
              }
            }
            """;

        var sorted = OpenApiSchemaSorter.SortSchemasAlphabetically(doc);

        var appleIndex = sorted.IndexOf("\"Apple\"", StringComparison.Ordinal);
        var mangoIndex = sorted.IndexOf("\"Mango\"", StringComparison.Ordinal);
        var zebraIndex = sorted.IndexOf("\"Zebra\"", StringComparison.Ordinal);

        Assert.True(appleIndex < mangoIndex);
        Assert.True(mangoIndex < zebraIndex);
        // Values, including a $ref pointer, must survive the reorder untouched.
        Assert.Contains("\"$ref\": \"#/components/schemas/Apple\"", sorted);
        Assert.Contains("\"type\": \"integer\"", sorted);
    }

    [Fact]
    public void SortSchemasAlphabetically_NoSchemas_ReturnsDocumentUnchanged()
    {
        const string doc = """{ "openapi": "3.1.1", "paths": {} }""";

        var result = OpenApiSchemaSorter.SortSchemasAlphabetically(doc);

        Assert.Contains("\"openapi\"", result);
        Assert.DoesNotContain("components", result);
    }

    [Fact]
    public void SortSchemasAlphabetically_EmptySchemas_DoesNotThrow()
    {
        const string doc = """{ "components": { "schemas": {} } }""";

        var result = OpenApiSchemaSorter.SortSchemasAlphabetically(doc);

        Assert.Contains("\"schemas\"", result);
    }
}
