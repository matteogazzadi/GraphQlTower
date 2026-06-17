using GraphQlTower.Shared.Interfaces;
using GraphQlTower.Shared.Models;
using System.Text.Json;

namespace GraphQlTower.Api.Data;

public static class DatabaseSeeder
{
    /// <summary>
    /// Seeds initial upstream services from TOWER_INITIAL_SERVICES environment variable (JSON array).
    /// Runs only if the registry is empty on first boot.
    /// </summary>
    public static async Task SeedFromEnvironmentAsync(IServiceRegistry registry, ILogger logger)
    {
        var existing = await registry.GetAllAsync();
        if (existing.Count > 0) return;

        var json = Environment.GetEnvironmentVariable("TOWER_INITIAL_SERVICES");
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var seeds = JsonSerializer.Deserialize<List<ServiceSeedEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (seeds is null) return;

            foreach (var seed in seeds)
            {
                if (string.IsNullOrWhiteSpace(seed.Name) || string.IsNullOrWhiteSpace(seed.Url))
                    continue;

                var service = new UpstreamService
                {
                    Name = seed.Name,
                    DisplayName = seed.DisplayName ?? seed.Name,
                    Url = seed.Url,
                    IsEnabled = seed.Enabled
                };

                await registry.AddAsync(service);
                logger.LogInformation("Seeded upstream service '{Name}' ({Url})", seed.Name, seed.Url);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to seed initial services from TOWER_INITIAL_SERVICES.");
        }
    }

    private class ServiceSeedEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string Url { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
    }
}
