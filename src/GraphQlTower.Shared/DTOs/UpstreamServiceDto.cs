using GraphQlTower.Shared.Models;

namespace GraphQlTower.Shared.DTOs;

public class UpstreamServiceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public List<ServiceHeaderDto> Headers { get; set; } = new();
    public ServiceHealthStatus LastStatus { get; set; }
    public DateTimeOffset LastChecked { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class ServiceHeaderDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class CreateUpstreamServiceRequest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public List<ServiceHeaderDto> Headers { get; set; } = new();
}

public class UpdateUpstreamServiceRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public List<ServiceHeaderDto> Headers { get; set; } = new();
}
