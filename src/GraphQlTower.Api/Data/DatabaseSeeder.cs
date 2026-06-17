using GraphQlTower.Shared.Interfaces;
using GraphQlTower.Shared.Models;
using System.Text.Json;

namespace GraphQlTower.Api.Data;

public static class DatabaseSeeder
{
    /// <summary>
    /// Seeds upstream services on first boot from two sources (in priority order):
    ///   1. appsettings.json / appsettings.{env}.json  →  "InitialServices" array
    ///   2. TOWER_INITIAL_SERVICES environment variable  →  JSON array (used by K8s ConfigMap)
    ///
    /// Nothing is seeded if the registry already contains services.
    /// </summary>
    public static async Task SeedAsync(
        IServiceRegistry registry,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct = default)
    {
        var existing = await registry.GetAllAsync(ct);
        if (existing.Count > 0) return;

        var seeds = ReadFromConfiguration(configuration, logger)
                 ?? ReadFromEnvironment(logger);

        if (seeds is null || seeds.Count == 0) return;

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
                logger.LogInformation(
                    "Seeded upstream service '{Name}' ({Url})", seed.Name, seed.Url);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to seed upstream service '{Name}' — skipping.", seed.Name);
            }
        }
    }

    private static List<ServiceSeedEntry>? ReadFromConfiguration(
        IConfiguration configuration, ILogger logger)
    {
        var section = configuration.GetSection("InitialServices");
        if (!section.Exists()) return null;

        try
        {
            var entries = section.Get<List<ServiceSeedEntry>>();
            if (entries is { Count: > 0 })
                logger.LogInformation(
                    "Found {Count} initial service(s) in configuration.", entries.Count);
            return entries;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse InitialServices from configuration.");
            return null;
        }
    }

    private static List<ServiceSeedEntry>? ReadFromEnvironment(ILogger logger)
    {
        var json = Environment.GetEnvironmentVariable("TOWER_INITIAL_SERVICES");
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var entries = JsonSerializer.Deserialize<List<ServiceSeedEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (entries is { Count: > 0 })
                logger.LogInformation(
                    "Found {Count} initial service(s) in TOWER_INITIAL_SERVICES env var.", entries.Count);
            return entries;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse TOWER_INITIAL_SERVICES environment variable.");
            return null;
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
