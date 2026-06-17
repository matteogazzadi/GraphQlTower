using GraphQlTower.Shared.Models;

namespace GraphQlTower.Shared.Interfaces;

public interface IServiceRegistry
{
    Task<IReadOnlyList<UpstreamService>> GetAllAsync(CancellationToken ct = default);
    Task<UpstreamService?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UpstreamService> AddAsync(UpstreamService service, CancellationToken ct = default);
    Task UpdateAsync(UpstreamService service, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    IObservable<RegistryChange> Changes { get; }
}

public record RegistryChange(ChangeType Type, Guid ServiceId);

public enum ChangeType { Added, Updated, Removed }
