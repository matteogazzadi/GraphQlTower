using GraphQlTower.Shared.Interfaces;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Stitching.SchemaDefinitions;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GraphQlTower.Api.Stitching;

/// <summary>
/// Fetches SDL from all enabled upstream services at executor-build time.
/// When TypesChanged is raised, HotChocolate evicts and lazily rebuilds the executor,
/// calling this module again with fresh data from the registry.
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

        // Wire registry changes → HC executor eviction
        _registry.Changes.Subscribe(_ => TypesChanged?.Invoke(this, EventArgs.Empty));
    }

    public async ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken)
    {
        var services = await _registry.GetAllAsync(cancellationToken);
        var enabledServices = services.Where(s => s.IsEnabled).ToList();

        var types = new List<ITypeSystemMember>();

        foreach (var service in enabledServices)
        {
            try
            {
                var sdl = await FetchSdlAsync(service, cancellationToken);
                if (sdl is null) continue;

                var schemaDoc = Utf8GraphQLParser.Parse(sdl);
                var schemaDefinition = new RemoteSchemaDefinition(service.Name, schemaDoc);
                types.Add(schemaDefinition);
                _logger.LogInformation("Loaded schema for upstream service '{Name}' ({Url})", service.Name, service.Url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load schema for upstream service '{Name}' ({Url})", service.Name, service.Url);
            }
        }

        return types;
    }

    private async Task<string?> FetchSdlAsync(Shared.Models.UpstreamService service, CancellationToken ct)
    {
        // Try SDL endpoint first, fall back to introspection
        using var client = CreateHttpClient(service);

        // Try ?sdl endpoint (HotChocolate native)
        try
        {
            var sdlResponse = await client.GetAsync("?sdl", ct);
            if (sdlResponse.IsSuccessStatusCode)
            {
                return await sdlResponse.Content.ReadAsStringAsync(ct);
            }
        }
        catch { /* fall through to introspection */ }

        // Fall back to introspection query
        var introspectionBody = new
        {
            query = IntrospectionQuery
        };

        var response = await client.PostAsJsonAsync("", introspectionBody, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return ConvertIntrospectionToSdl(json);
    }

    private HttpClient CreateHttpClient(Shared.Models.UpstreamService service)
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(service.Url.TrimEnd('/') + "/");
        foreach (var header in service.Headers)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
        return client;
    }

    private static string? ConvertIntrospectionToSdl(string introspectionJson)
    {
        // Parse the introspection result and convert to SDL
        using var doc = JsonDocument.Parse(introspectionJson);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("__schema", out _))
        {
            return null;
        }

        // Use HotChocolate's built-in conversion
        var schemaDoc = HotChocolate.Utilities.Introspection.IntrospectionDeserializer.Deserialize(introspectionJson);
        return schemaDoc is not null
            ? HotChocolate.Language.SchemaSyntaxSerializer.Serialize(schemaDoc, true)
            : null;
    }

    private const string IntrospectionQuery = """
        query IntrospectionQuery {
          __schema {
            queryType { name }
            mutationType { name }
            subscriptionType { name }
            types {
              ...FullType
            }
            directives {
              name
              description
              locations
              args {
                ...InputValue
              }
            }
          }
        }
        fragment FullType on __Type {
          kind
          name
          description
          fields(includeDeprecated: true) {
            name
            description
            args {
              ...InputValue
            }
            type {
              ...TypeRef
            }
            isDeprecated
            deprecationReason
          }
          inputFields {
            ...InputValue
          }
          interfaces {
            ...TypeRef
          }
          enumValues(includeDeprecated: true) {
            name
            description
            isDeprecated
            deprecationReason
          }
          possibleTypes {
            ...TypeRef
          }
        }
        fragment InputValue on __InputValue {
          name
          description
          type { ...TypeRef }
          defaultValue
        }
        fragment TypeRef on __Type {
          kind
          name
          ofType {
            kind
            name
            ofType {
              kind
              name
              ofType {
                kind
                name
                ofType {
                  kind
                  name
                }
              }
            }
          }
        }
        """;
}
