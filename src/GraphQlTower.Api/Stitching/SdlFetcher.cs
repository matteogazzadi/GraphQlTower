using GraphQlTower.Shared.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace GraphQlTower.Api.Stitching;

/// <summary>
/// Fetches SDL from an upstream GraphQL service.
/// Tries the HotChocolate ?sdl endpoint first; falls back to introspection.
/// </summary>
public static class SdlFetcher
{
    public static async Task<string?> FetchAsync(
        UpstreamService service,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken ct = default)
    {
        using var client = BuildClient(service, httpClientFactory);

        try
        {
            var sdlResponse = await client.GetAsync("?sdl", ct);
            if (sdlResponse.IsSuccessStatusCode)
            {
                var sdl = await sdlResponse.Content.ReadAsStringAsync(ct);
                if (!string.IsNullOrWhiteSpace(sdl))
                    return sdl;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "SDL endpoint not available for '{Name}', trying introspection.", service.Name);
        }

        try
        {
            var resp = await client.PostAsJsonAsync("", new { query = IntrospectionQuery }, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            return ConvertToSdl(json);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Introspection failed for '{Name}'.", service.Name);
            return null;
        }
    }

    private static HttpClient BuildClient(UpstreamService service, IHttpClientFactory factory)
    {
        var client = factory.CreateClient();
        client.BaseAddress = new Uri(service.Url.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(30);
        foreach (var h in service.Headers)
            client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);
        return client;
    }

    private static string? ConvertToSdl(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("__schema", out var schema))
                return null;

            var sb = new System.Text.StringBuilder();

            if (schema.TryGetProperty("types", out var types))
            {
                foreach (var type in types.EnumerateArray())
                {
                    if (!type.TryGetProperty("name", out var nameEl)) continue;
                    var name = nameEl.GetString() ?? "";
                    if (name.StartsWith("__") || IsBuiltIn(name)) continue;

                    var kind = type.TryGetProperty("kind", out var k) ? k.GetString() : null;
                    switch (kind)
                    {
                        case "OBJECT": EmitObject(sb, type, name); break;
                        case "INTERFACE": EmitInterface(sb, type, name); break;
                        case "ENUM": EmitEnum(sb, type, name); break;
                        case "INPUT_OBJECT": EmitInput(sb, type, name); break;
                        case "SCALAR":
                            if (!IsBuiltInScalar(name)) sb.AppendLine($"scalar {name}");
                            break;
                        case "UNION": EmitUnion(sb, type, name); break;
                    }
                }
            }

            var result = sb.ToString().Trim();
            return result.Length == 0 ? null : result;
        }
        catch { return null; }
    }

    private static void EmitObject(System.Text.StringBuilder sb, JsonElement t, string name)
    {
        var desc = Desc(t);
        if (desc != null) sb.AppendLine($"\"\"\"{desc}\"\"\"");
        sb.AppendLine($"type {name} {{");
        EmitFields(sb, t);
        sb.AppendLine("}"); sb.AppendLine();
    }

    private static void EmitInterface(System.Text.StringBuilder sb, JsonElement t, string name)
    {
        sb.AppendLine($"interface {name} {{");
        EmitFields(sb, t);
        sb.AppendLine("}"); sb.AppendLine();
    }

    private static void EmitEnum(System.Text.StringBuilder sb, JsonElement t, string name)
    {
        sb.AppendLine($"enum {name} {{");
        if (t.TryGetProperty("enumValues", out var vals))
            foreach (var v in vals.EnumerateArray())
            {
                var vn = v.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (vn != null) sb.AppendLine($"  {vn}");
            }
        sb.AppendLine("}"); sb.AppendLine();
    }

    private static void EmitInput(System.Text.StringBuilder sb, JsonElement t, string name)
    {
        sb.AppendLine($"input {name} {{");
        if (t.TryGetProperty("inputFields", out var fields))
            foreach (var f in fields.EnumerateArray())
            {
                var fn = f.TryGetProperty("name", out var n) ? n.GetString() : null;
                var ft = f.TryGetProperty("type", out var tp) ? TypeName(tp) : null;
                if (fn != null && ft != null) sb.AppendLine($"  {fn}: {ft}");
            }
        sb.AppendLine("}"); sb.AppendLine();
    }

    private static void EmitUnion(System.Text.StringBuilder sb, JsonElement t, string name)
    {
        if (!t.TryGetProperty("possibleTypes", out var pt)) return;
        var members = pt.EnumerateArray()
            .Select(x => x.TryGetProperty("name", out var n) ? n.GetString() : null)
            .Where(n => n != null).ToList();
        if (members.Count > 0) { sb.AppendLine($"union {name} = {string.Join(" | ", members)}"); sb.AppendLine(); }
    }

    private static void EmitFields(System.Text.StringBuilder sb, JsonElement t)
    {
        if (!t.TryGetProperty("fields", out var fields)) return;
        foreach (var f in fields.EnumerateArray())
        {
            var fn = f.TryGetProperty("name", out var n) ? n.GetString() : null;
            var ft = f.TryGetProperty("type", out var tp) ? TypeName(tp) : null;
            if (fn != null && ft != null) sb.AppendLine($"  {fn}: {ft}");
        }
    }

    private static string TypeName(JsonElement t)
    {
        var kind = t.TryGetProperty("kind", out var k) ? k.GetString() : null;
        var name = t.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (kind == "NON_NULL") return $"{(t.TryGetProperty("ofType", out var ot) ? TypeName(ot) : "Unknown")}!";
        if (kind == "LIST") return $"[{(t.TryGetProperty("ofType", out var ot) ? TypeName(ot) : "Unknown")}]";
        return name ?? "Unknown";
    }

    private static string? Desc(JsonElement el) =>
        el.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;

    private static bool IsBuiltIn(string n) =>
        n is "String" or "Boolean" or "Int" or "Float" or "ID" or "Query" or "Mutation" or "Subscription";

    private static bool IsBuiltInScalar(string n) =>
        n is "String" or "Boolean" or "Int" or "Float" or "ID";

    private const string IntrospectionQuery = """
        {
          __schema {
            types {
              kind name description
              fields(includeDeprecated: true) {
                name type { ...TypeRef }
              }
              inputFields { name type { ...TypeRef } }
              enumValues(includeDeprecated: true) { name }
              possibleTypes { name }
            }
          }
        }
        fragment TypeRef on __Type {
          kind name
          ofType { kind name ofType { kind name ofType { kind name } } }
        }
        """;
}
