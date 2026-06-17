using GraphQlTower.Shared.Interfaces;
using GraphQlTower.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Reactive.Subjects;

namespace GraphQlTower.Api.Data;

public class EfServiceRegistry : IServiceRegistry
{
    private readonly ServiceRegistryDbContext _db;
    private readonly Subject<RegistryChange> _changes = new();

    public EfServiceRegistry(ServiceRegistryDbContext db)
    {
        _db = db;
    }

    public IObservable<RegistryChange> Changes => _changes;

    public async Task<IReadOnlyList<UpstreamService>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.UpstreamServices
            .Include(s => s.Headers)
            .AsNoTracking()
            .OrderBy(s => s.DisplayName)
            .ToListAsync(ct);
    }

    public async Task<UpstreamService?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.UpstreamServices
            .Include(s => s.Headers)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<UpstreamService> AddAsync(UpstreamService service, CancellationToken ct = default)
    {
        ValidateName(service.Name);
        service.CreatedAt = DateTimeOffset.UtcNow;
        service.UpdatedAt = DateTimeOffset.UtcNow;
        _db.UpstreamServices.Add(service);
        await _db.SaveChangesAsync(ct);
        _changes.OnNext(new RegistryChange(ChangeType.Added, service.Id));
        return service;
    }

    public async Task UpdateAsync(UpstreamService service, CancellationToken ct = default)
    {
        // Snapshot incoming values up front. The caller may pass the same instance that
        // EF tracks (and returns below), so reading service.* after we mutate the tracked
        // entity — or its Headers collection — would observe our own changes and lose data.
        var displayName = service.DisplayName;
        var url = service.Url;
        var isEnabled = service.IsEnabled;
        var newHeaders = service.Headers
            .Select(h => (h.Key, h.Value))
            .ToList();
        var id = service.Id;

        // Detach anything the caller may have handed us. The same instance can already be
        // tracked from a prior call (e.g. the object returned by AddAsync), and its mutated
        // navigation would otherwise corrupt change tracking. Reload a clean graph instead.
        _db.ChangeTracker.Clear();

        // Load the parent only — do NOT Include the Headers navigation. Mutating the
        // navigation collection relies on EF's orphan handling, which the InMemory
        // provider does not apply consistently. Instead we operate on the ServiceHeaders
        // set directly: delete the existing rows and insert the new ones.
        var existing = await _db.UpstreamServices
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new InvalidOperationException($"Service {id} not found.");

        existing.DisplayName = displayName;
        existing.Url = url;
        existing.IsEnabled = isEnabled;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        var oldHeaders = await _db.ServiceHeaders
            .Where(h => h.UpstreamServiceId == id)
            .ToListAsync(ct);
        _db.ServiceHeaders.RemoveRange(oldHeaders);

        foreach (var (key, value) in newHeaders)
        {
            _db.ServiceHeaders.Add(new ServiceHeader
            {
                Id = Guid.NewGuid(),
                UpstreamServiceId = id,
                Key = key,
                Value = value
            });
        }

        await _db.SaveChangesAsync(ct);
        _changes.OnNext(new RegistryChange(ChangeType.Updated, id));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var service = await _db.UpstreamServices.FindAsync(new object[] { id }, ct)
            ?? throw new InvalidOperationException($"Service {id} not found.");
        _db.UpstreamServices.Remove(service);
        await _db.SaveChangesAsync(ct);
        _changes.OnNext(new RegistryChange(ChangeType.Removed, id));
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Service name cannot be empty.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z][a-zA-Z0-9_]*$"))
            throw new ArgumentException("Service name must start with a letter and contain only letters, digits, and underscores.");
    }
}
