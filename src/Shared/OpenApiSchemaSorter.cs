using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;

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

/// <summary>
/// The ASP.NET Core middleware that applies <see cref="OpenApiSchemaSorter"/> to a live response.
/// Shared between the server (<c>Server/Program.cs</c>) and the agent's local API
/// (<c>Agent.Core/AgentApiServer.cs</c>) — both serve their own OpenAPI document and both need the
/// same Windows-vs-Linux ordering fix, so this used to be copy-pasted between them.
/// </summary>
public static class OpenApiSchemaSorterMiddleware
{
    /// <summary>Buffers the response for <c>/openapi/*.json</c> and rewrites it with
    /// <see cref="OpenApiSchemaSorter"/> before sending it on. Every other route, and every
    /// non-200 response on a matched route (e.g. a 404 for a document name nothing registered),
    /// passes straight through untouched — a 404's empty/non-JSON body would otherwise reach
    /// <see cref="OpenApiSchemaSorter.SortSchemasAlphabetically"/>, which throws on it, turning a
    /// clean 404 into an unhandled-exception 500.</summary>
    public static async Task SortOpenApiSchemasAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/openapi") ||
            !context.Request.Path.Value!.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        buffer.Seek(0, SeekOrigin.Begin);

        if (context.Response.StatusCode != StatusCodes.Status200OK)
        {
            await buffer.CopyToAsync(originalBody);
            return;
        }

        var json = await new StreamReader(buffer).ReadToEndAsync();
        var sorted = OpenApiSchemaSorter.SortSchemasAlphabetically(json);
        var bytes = System.Text.Encoding.UTF8.GetBytes(sorted);
        context.Response.ContentLength = bytes.Length;
        await originalBody.WriteAsync(bytes);
    }
}
