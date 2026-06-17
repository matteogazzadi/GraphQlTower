namespace GraphQlTower.Shared.Models;

public class UpstreamService
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public List<ServiceHeader> Headers { get; set; } = new();
    public ServiceHealthStatus LastStatus { get; set; } = ServiceHealthStatus.Unknown;
    public DateTimeOffset LastChecked { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ServiceHeader
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UpstreamServiceId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public enum ServiceHealthStatus
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy
}
