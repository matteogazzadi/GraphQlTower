using GraphQlTower.Shared.DTOs;
using GraphQlTower.Shared.Interfaces;
using GraphQlTower.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;

namespace GraphQlTower.Api.Controllers;

[ApiController]
[Route("api/services")]
public class UpstreamServicesController : ControllerBase
{
    private readonly IServiceRegistry _registry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UpstreamServicesController> _logger;

    public UpstreamServicesController(
        IServiceRegistry registry,
        IHttpClientFactory httpClientFactory,
        ILogger<UpstreamServicesController> logger)
    {
        _registry = registry;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UpstreamServiceDto>>> GetAll(CancellationToken ct)
    {
        var services = await _registry.GetAllAsync(ct);
        return Ok(services.Select(MapToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UpstreamServiceDto>> GetById(Guid id, CancellationToken ct)
    {
        var service = await _registry.GetByIdAsync(id, ct);
        if (service is null) return NotFound();
        return Ok(MapToDto(service));
    }

    [HttpPost]
    public async Task<ActionResult<UpstreamServiceDto>> Create(
        [FromBody] CreateUpstreamServiceRequest request,
        CancellationToken ct)
    {
        try
        {
            var service = new UpstreamService
            {
                Name = request.Name,
                DisplayName = request.DisplayName,
                Url = request.Url,
                IsEnabled = request.IsEnabled,
                Headers = request.Headers.Select(h => new ServiceHeader
                {
                    Key = h.Key,
                    Value = h.Value
                }).ToList()
            };

            var created = await _registry.AddAsync(service, ct);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, MapToDto(created));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UpstreamServiceDto>> Update(
        Guid id,
        [FromBody] UpdateUpstreamServiceRequest request,
        CancellationToken ct)
    {
        var existing = await _registry.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();

        existing.DisplayName = request.DisplayName;
        existing.Url = request.Url;
        existing.IsEnabled = request.IsEnabled;
        existing.Headers = request.Headers.Select(h => new ServiceHeader
        {
            Key = h.Key,
            Value = h.Value
        }).ToList();

        await _registry.UpdateAsync(existing, ct);
        var updated = await _registry.GetByIdAsync(id, ct);
        return Ok(MapToDto(updated!));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var existing = await _registry.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();
        await _registry.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<ActionResult<UpstreamServiceDto>> Toggle(Guid id, CancellationToken ct)
    {
        var service = await _registry.GetByIdAsync(id, ct);
        if (service is null) return NotFound();
        service.IsEnabled = !service.IsEnabled;
        await _registry.UpdateAsync(service, ct);
        var updated = await _registry.GetByIdAsync(id, ct);
        return Ok(MapToDto(updated!));
    }

    [HttpGet("{id:guid}/schema")]
    public async Task<ActionResult<string>> GetSchema(Guid id, CancellationToken ct)
    {
        var service = await _registry.GetByIdAsync(id, ct);
        if (service is null) return NotFound();

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(service.Url.TrimEnd('/') + "/");
            foreach (var h in service.Headers)
                client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);

            // Try SDL endpoint first
            var sdlResponse = await client.GetAsync("?sdl", ct);
            if (sdlResponse.IsSuccessStatusCode)
            {
                return Ok(await sdlResponse.Content.ReadAsStringAsync(ct));
            }

            // Fall back to __schema query
            var introspectionResponse = await client.PostAsJsonAsync("",
                new { query = "{ __schema { types { name kind description } } }" }, ct);
            introspectionResponse.EnsureSuccessStatusCode();
            return Ok(await introspectionResponse.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch schema for service {Id}", id);
            return StatusCode(502, new { error = "Failed to reach upstream service.", detail = ex.Message });
        }
    }

    [HttpGet("{id:guid}/health")]
    public async Task<ActionResult> CheckHealth(Guid id, CancellationToken ct)
    {
        var service = await _registry.GetByIdAsync(id, ct);
        if (service is null) return NotFound();

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(service.Url);
            client.Timeout = TimeSpan.FromSeconds(5);
            foreach (var h in service.Headers)
                client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);

            var response = await client.PostAsJsonAsync("", new { query = "{ __typename }" }, ct);
            return Ok(new
            {
                serviceId = id,
                url = service.Url,
                status = response.IsSuccessStatusCode ? "healthy" : "unhealthy",
                httpStatus = (int)response.StatusCode,
                checkedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                serviceId = id,
                url = service.Url,
                status = "unhealthy",
                error = ex.Message,
                checkedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private static UpstreamServiceDto MapToDto(UpstreamService s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        DisplayName = s.DisplayName,
        Url = s.Url,
        IsEnabled = s.IsEnabled,
        LastStatus = s.LastStatus,
        LastChecked = s.LastChecked,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt,
        Headers = s.Headers.Select(h => new ServiceHeaderDto
        {
            Id = h.Id,
            Key = h.Key,
            // Mask header values in responses for security
            Value = h.Key.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                    h.Key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                    h.Key.Contains("secret", StringComparison.OrdinalIgnoreCase)
                ? "***"
                : h.Value
        }).ToList()
    };
}
