using GraphQlTower.Shared.Models;

namespace GraphQlTower.Api.Stitching;

/// <summary>
/// Provides named HttpClient instances for HotChocolate stitching HTTP executors.
/// Each upstream service gets a client named by its schema slug.
/// </summary>
public class HttpSchemaClientFactory
{
    private readonly IHttpClientFactory _factory;
    private readonly Dictionary<string, UpstreamService> _serviceMap = new();

    public HttpSchemaClientFactory(IHttpClientFactory factory)
    {
        _factory = factory;
    }

    public void Register(UpstreamService service)
    {
        _serviceMap[service.Name] = service;
    }

    public void Unregister(string name)
    {
        _serviceMap.Remove(name);
    }

    public HttpClient CreateClient(string schemaName)
    {
        if (!_serviceMap.TryGetValue(schemaName, out var service))
            throw new InvalidOperationException($"No upstream service registered with name '{schemaName}'.");

        var client = _factory.CreateClient($"upstream_{schemaName}");
        client.BaseAddress = new Uri(service.Url);
        foreach (var h in service.Headers)
            client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);
        return client;
    }
}
