using GraphQlTower.Shared.Interfaces;
using GraphQlTower.Shared.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net.Http.Json;

namespace GraphQlTower.Api.Health;

public class UpstreamServicesHealthCheck : IHealthCheck
{
    private readonly IServiceRegistry _registry;
    private readonly IHttpClientFactory _httpClientFactory;

    public UpstreamServicesHealthCheck(IServiceRegistry registry, IHttpClientFactory httpClientFactory)
    {
        _registry = registry;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var services = await _registry.GetAllAsync(cancellationToken);
        var enabled = services.Where(s => s.IsEnabled).ToList();

        if (enabled.Count == 0)
            return HealthCheckResult.Healthy("No upstream services configured.");

        var results = new Dictionary<string, object>();
        int healthy = 0, unhealthy = 0;

        foreach (var service in enabled)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(service.Url);
                client.Timeout = TimeSpan.FromSeconds(5);

                foreach (var h in service.Headers)
                    client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);

                var response = await client.PostAsJsonAsync("", new { query = "{ __typename }" }, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    results[service.Name] = "healthy";
                    healthy++;
                }
                else
                {
                    results[service.Name] = $"unhealthy (HTTP {(int)response.StatusCode})";
                    unhealthy++;
                }
            }
            catch (Exception ex)
            {
                results[service.Name] = $"unhealthy ({ex.Message})";
                unhealthy++;
            }
        }

        if (unhealthy == 0)
            return HealthCheckResult.Healthy($"All {healthy} upstream services are healthy.", results);
        if (healthy == 0)
            return HealthCheckResult.Unhealthy($"All {unhealthy} upstream services are unhealthy.", data: results);

        return HealthCheckResult.Degraded(
            $"{healthy}/{enabled.Count} upstream services are healthy.",
            data: results);
    }
}
