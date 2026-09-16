using System.Text.Json;
using System.Text.Json.Nodes;

namespace SaveLocker.Shared;

/// <summary>
/// Sorts an OpenAPI document's <c>components.schemas</c> keys alphabetically, as raw JSON text
/// rather than through the ASP.NET Core OpenAPI document model. A document transformer that
/// reorders <c>OpenApiDocument.Components.Schemas</c> in place does not actually change the
/// served JSON's key order — Microsoft.OpenApi's writer emits schemas by reference-discovery
/// order during serialization, not by the dictionary's own enumeration order — confirmed by
/// testing that exact approach live and finding the served document unaffected. Post-processing
/// the final JSON text is the only place this project found that reliably controls it.
///
/// Schema order otherwise reflects .NET's endpoint/type reflection order, which differs between
/// Windows and Linux (and between builds), which is what repeatedly made a Windows-generated
/// <c>agent-ui/src/api-types.ts</c> fail Linux CI's <c>gen:api -- --check</c> on a pure ordering
/// diff with no actual schema change. Sorting makes the document byte-for-byte identical no
/// matter which OS or build generated it.
/// </summary>
public static class OpenApiSchemaSorter
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string SortSchemasAlphabetically(string documentJson)
    {
        var node = JsonNode.Parse(documentJson) ?? throw new InvalidOperationException("empty OpenAPI document");
        if (node["components"]?["schemas"] is JsonObject schemas)
        {
            var ordered = schemas.ToList().OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            schemas.Clear();
            foreach (var kv in ordered) schemas.Add(kv.Key, kv.Value);
        }
        return node.ToJsonString(WriteOptions);
    }
}
