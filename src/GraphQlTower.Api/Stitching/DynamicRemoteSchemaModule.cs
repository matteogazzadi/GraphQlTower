using GraphQlTower.Shared.Interfaces;
using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Stitching;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using System.Net.Http.Json;
using System.Text.Json;

namespace GraphQlTower.Api.Stitching;

/// <summary>
/// Fetches SDL from all enabled upstream services at executor-build time.
/// When TypesChanged is raised HotChocolate evicts the executor and rebuilds lazily,
/// calling this module again with fresh data — enabling runtime add/remove of services.
/// </summary>
public class DynamicRemoteSchemaModule : ITypeModule
{
    private readonly IServiceRegistry _registry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DynamicRemoteSchemaModule> _logger;

    public event EventHandler<EventArgs>? TypesChanged;

    public DynamicRemoteSchemaModule(
        IServiceRegistry registry,
        IHttpClientFactory httpClientFactory,
        ILogger<DynamicRemoteSchemaModule> logger)
    {
        _registry = registry;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // Registry changes → HC executor eviction
        _registry.Changes.Subscribe(_ => TypesChanged?.Invoke(this, EventArgs.Empty));
    }

    public async ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken)
    {
        var services = await _registry.GetAllAsync(cancellationToken);
        var types = new List<ITypeSystemMember>();

        foreach (var service in services.Where(s => s.IsEnabled))
        {
            try
            {
                var sdl = await FetchSdlAsync(service, cancellationToken);
                if (sdl is null)
                {
                    _logger.LogWarning(
                        "Could not fetch SDL for '{Name}' ({Url}) — skipping.",
                        service.Name, service.Url);
                    continue;
                }

                var document = Utf8GraphQLParser.Parse(sdl);
                types.Add(new RemoteSchemaDefinition(service.Name, document));

                _logger.LogInformation(
                    "Loaded schema for '{Name}' ({Url})", service.Name, service.Url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to load schema for '{Name}' ({Url})", service.Name, service.Url);
            }
        }

        return types;
    }

    private async Task<string?> FetchSdlAsync(
        Shared.Models.UpstreamService service,
        CancellationToken ct)
    {
        using var client = BuildHttpClient(service);

        // 1. Try HotChocolate-native SDL endpoint (?sdl)
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
            _logger.LogDebug(ex, "SDL endpoint not available for '{Name}', trying introspection.", service.Name);
        }

        // 2. Fall back to introspection and convert to SDL
        try
        {
            var introspectionResponse = await client.PostAsJsonAsync(
                "",
                new { query = MinimalIntrospectionQuery },
                ct);

            introspectionResponse.EnsureSuccessStatusCode();
            var json = await introspectionResponse.Content.ReadAsStringAsync(ct);
            return ConvertIntrospectionToSdl(json);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Introspection failed for '{Name}'.", service.Name);
            return null;
        }
    }

    private HttpClient BuildHttpClient(Shared.Models.UpstreamService service)
    {
        var client = _httpClientFactory.CreateClient();
        var baseUrl = service.Url.TrimEnd('/') + "/";
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
        foreach (var header in service.Headers)
            client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        return client;
    }

    /// <summary>
    /// Converts an introspection JSON response into an SDL string using the
    /// GraphQL spec's __schema structure — no HC internal APIs required.
    /// </summary>
    private static string? ConvertIntrospectionToSdl(string introspectionJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(introspectionJson);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("__schema", out var schema))
                return null;

            var sb = new System.Text.StringBuilder();

            // Emit types
            if (schema.TryGetProperty("types", out var types))
            {
                foreach (var type in types.EnumerateArray())
                {
                    if (!type.TryGetProperty("name", out var nameEl)) continue;
                    var name = nameEl.GetString() ?? "";

                    // Skip built-in introspection types
                    if (name.StartsWith("__") || IsBuiltIn(name)) continue;

                    var kind = type.TryGetProperty("kind", out var k) ? k.GetString() : null;

                    switch (kind)
                    {
                        case "OBJECT":
                            EmitObjectType(sb, type, name);
                            break;
                        case "INTERFACE":
                            EmitInterfaceType(sb, type, name);
                            break;
                        case "ENUM":
                            EmitEnumType(sb, type, name);
                            break;
                        case "INPUT_OBJECT":
                            EmitInputType(sb, type, name);
                            break;
                        case "SCALAR":
                            if (!IsBuiltInScalar(name))
                                sb.AppendLine($"scalar {name}");
                            break;
                        case "UNION":
                            EmitUnionType(sb, type, name);
                            break;
                    }
                }
            }

            var result = sb.ToString().Trim();
            return result.Length == 0 ? null : result;
        }
        catch
        {
            return null;
        }
    }

    private static void EmitObjectType(System.Text.StringBuilder sb, JsonElement type, string name)
    {
        var description = GetDescription(type);
        if (description != null) sb.AppendLine($"\"\"\"{description}\"\"\"");
        sb.AppendLine($"type {name} {{");
        EmitFields(sb, type);
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitInterfaceType(System.Text.StringBuilder sb, JsonElement type, string name)
    {
        sb.AppendLine($"interface {name} {{");
        EmitFields(sb, type);
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitEnumType(System.Text.StringBuilder sb, JsonElement type, string name)
    {
        sb.AppendLine($"enum {name} {{");
        if (type.TryGetProperty("enumValues", out var values))
        {
            foreach (var val in values.EnumerateArray())
            {
                var valName = val.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (valName != null) sb.AppendLine($"  {valName}");
            }
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitInputType(System.Text.StringBuilder sb, JsonElement type, string name)
    {
        sb.AppendLine($"input {name} {{");
        if (type.TryGetProperty("inputFields", out var fields))
        {
            foreach (var field in fields.EnumerateArray())
            {
                var fname = field.TryGetProperty("name", out var fn) ? fn.GetString() : null;
                var ftype = field.TryGetProperty("type", out var ft) ? GetTypeName(ft) : null;
                if (fname != null && ftype != null)
                    sb.AppendLine($"  {fname}: {ftype}");
            }
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitUnionType(System.Text.StringBuilder sb, JsonElement type, string name)
    {
        if (!type.TryGetProperty("possibleTypes", out var possibleTypes)) return;
        var members = possibleTypes.EnumerateArray()
            .Select(t => t.TryGetProperty("name", out var n) ? n.GetString() : null)
            .Where(n => n != null)
            .ToList();
        if (members.Count > 0)
        {
            sb.AppendLine($"union {name} = {string.Join(" | ", members)}");
            sb.AppendLine();
        }
    }

    private static void EmitFields(System.Text.StringBuilder sb, JsonElement type)
    {
        if (!type.TryGetProperty("fields", out var fields)) return;
        foreach (var field in fields.EnumerateArray())
        {
            var fname = field.TryGetProperty("name", out var fn) ? fn.GetString() : null;
            var ftype = field.TryGetProperty("type", out var ft) ? GetTypeName(ft) : null;
            if (fname != null && ftype != null)
                sb.AppendLine($"  {fname}: {ftype}");
        }
    }

    private static string GetTypeName(JsonElement typeEl)
    {
        var kind = typeEl.TryGetProperty("kind", out var k) ? k.GetString() : null;
        var name = typeEl.TryGetProperty("name", out var n) ? n.GetString() : null;

        if (kind == "NON_NULL")
        {
            var inner = typeEl.TryGetProperty("ofType", out var ot) ? GetTypeName(ot) : "Unknown";
            return $"{inner}!";
        }
        if (kind == "LIST")
        {
            var inner = typeEl.TryGetProperty("ofType", out var ot) ? GetTypeName(ot) : "Unknown";
            return $"[{inner}]";
        }
        return name ?? "Unknown";
    }

    private static string? GetDescription(JsonElement el) =>
        el.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;

    private static bool IsBuiltIn(string name) =>
        name is "String" or "Boolean" or "Int" or "Float" or "ID"
               or "Query" or "Mutation" or "Subscription";

    private static bool IsBuiltInScalar(string name) =>
        name is "String" or "Boolean" or "Int" or "Float" or "ID";

    private const string MinimalIntrospectionQuery = """
        {
          __schema {
            types {
              kind
              name
              description
              fields(includeDeprecated: true) {
                name
                description
                type { ...TypeRef }
                args { name type { ...TypeRef } }
              }
              inputFields { name type { ...TypeRef } }
              interfaces { name kind ofType { name kind } }
              enumValues(includeDeprecated: true) { name description }
              possibleTypes { name kind }
            }
          }
        }
        fragment TypeRef on __Type {
          kind name
          ofType { kind name ofType { kind name ofType { kind name } } }
        }
        """;
}
