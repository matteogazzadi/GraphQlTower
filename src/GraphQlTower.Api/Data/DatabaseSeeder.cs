using GraphQlTower.Shared.Interfaces;
using GraphQlTower.Shared.Models;
using System.Text.Json;

namespace GraphQlTower.Api.Data;

public static class DatabaseSeeder
{
    /// <summary>
    /// Seeds upstream services on first boot from the TOWER_INITIAL_SERVICES environment variable.
    /// This is set via a Kubernetes ConfigMap (see Helm values.yaml → initialServices).
    /// Nothing is seeded if the registry already contains services, or if the env var is absent.
    /// All ongoing management is done through the Admin UI at /services.
    /// </summary>
    public static async Task SeedAsync(
        IServiceRegistry registry,
        ILogger logger,
        CancellationToken ct = default)
    {
        var existing = await registry.GetAllAsync(ct);
        if (existing.Count > 0) return;

        var json = Environment.GetEnvironmentVariable("TOWER_INITIAL_SERVICES");
        if (string.IsNullOrWhiteSpace(json)) return;

        List<ServiceSeedEntry>? seeds;
        try
        {
            seeds = JsonSerializer.Deserialize<List<ServiceSeedEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse TOWER_INITIAL_SERVICES — skipping seed.");
            return;
        }

        if (seeds is null || seeds.Count == 0) return;

        logger.LogInformation("Seeding {Count} initial service(s) from TOWER_INITIAL_SERVICES.", seeds.Count);

        foreach (var seed in seeds)
        {
            if (string.IsNullOrWhiteSpace(seed.Name) || string.IsNullOrWhiteSpace(seed.Url))
            {
                logger.LogWarning("Skipping seed entry with missing Name or Url.");
                continue;
            }

            try
            {
                var service = new UpstreamService
                {
                    Name = seed.Name,
                    DisplayName = string.IsNullOrWhiteSpace(seed.DisplayName) ? seed.Name : seed.DisplayName,
                    Url = seed.Url,
                    IsEnabled = seed.Enabled,
                    Headers = seed.Headers?.Select(h => new ServiceHeader
                    {
                        Key = h.Key,
                        Value = h.Value
                    }).ToList() ?? new()
                };

                await registry.AddAsync(service, ct);
                logger.LogInformation("Seeded service '{Name}' ({Url})", seed.Name, seed.Url);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to seed service '{Name}' — skipping.", seed.Name);
            }
        }
    }

    private class ServiceSeedEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string Url { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public List<HeaderEntry>? Headers { get; set; }
    }

    private class HeaderEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
