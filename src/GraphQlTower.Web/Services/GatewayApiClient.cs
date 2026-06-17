using GraphQlTower.Shared.DTOs;
using System.Net.Http.Json;

namespace GraphQlTower.Web.Services;

public class GatewayApiClient
{
    private readonly HttpClient _http;

    public GatewayApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<UpstreamServiceDto>> GetServicesAsync(CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<List<UpstreamServiceDto>>("api/services", ct)
            ?? new();
    }

    public async Task<UpstreamServiceDto?> GetServiceAsync(Guid id, CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<UpstreamServiceDto>($"api/services/{id}", ct);
    }

    public async Task<UpstreamServiceDto> CreateServiceAsync(
        CreateUpstreamServiceRequest request,
        CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/services", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UpstreamServiceDto>(ct))!;
    }

    public async Task<UpstreamServiceDto> UpdateServiceAsync(
        Guid id,
        UpdateUpstreamServiceRequest request,
        CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"api/services/{id}", request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UpstreamServiceDto>(ct))!;
    }

    public async Task DeleteServiceAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"api/services/{id}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<UpstreamServiceDto> ToggleServiceAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"api/services/{id}/toggle", null, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UpstreamServiceDto>(ct))!;
    }

    public async Task<string?> GetServiceSchemaAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"api/services/{id}/schema", ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<object?> CheckServiceHealthAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"api/services/{id}/health", ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<object>(ct);
    }
}
