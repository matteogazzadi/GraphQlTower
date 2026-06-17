using GraphQlTower.Shared.Interfaces;
using GraphQlTower.Shared.Models;
using System.Net.Http.Json;

namespace GraphQlTower.Api.Health;

public class UpstreamHealthMonitor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UpstreamHealthMonitor> _logger;
    private readonly TimeSpan _interval;

    public UpstreamHealthMonitor(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<UpstreamHealthMonitor> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(
            configuration.GetValue("HealthMonitor:IntervalSeconds", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckAllServicesAsync(stoppingToken);
            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CheckAllServicesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IServiceRegistry>();
        var services = await registry.GetAllAsync(ct);

        foreach (var service in services.Where(s => s.IsEnabled))
        {
            var status = await PingServiceAsync(service, ct);

            if (status != service.LastStatus)
            {
                _logger.LogInformation(
                    "Service '{Name}' status changed: {Old} → {New}",
                    service.Name, service.LastStatus, status);

                service.LastStatus = status;
                service.LastChecked = DateTimeOffset.UtcNow;
                await registry.UpdateAsync(service, ct);
            }
        }
    }

    private async Task<ServiceHealthStatus> PingServiceAsync(UpstreamService service, CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(service.Url);
            client.Timeout = TimeSpan.FromSeconds(5);

            foreach (var h in service.Headers)
                client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);

            var response = await client.PostAsJsonAsync("", new { query = "{ __typename }" }, ct);
            return response.IsSuccessStatusCode
                ? ServiceHealthStatus.Healthy
                : ServiceHealthStatus.Unhealthy;
        }
        catch (TaskCanceledException)
        {
            return ServiceHealthStatus.Degraded;
        }
        catch
        {
            return ServiceHealthStatus.Unhealthy;
        }
    }
}
