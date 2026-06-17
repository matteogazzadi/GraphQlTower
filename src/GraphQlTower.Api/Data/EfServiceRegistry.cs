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
        var existing = await _db.UpstreamServices
            .Include(s => s.Headers)
            .FirstOrDefaultAsync(s => s.Id == service.Id, ct)
            ?? throw new InvalidOperationException($"Service {service.Id} not found.");

        existing.DisplayName = service.DisplayName;
        existing.Url = service.Url;
        existing.IsEnabled = service.IsEnabled;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        // Replace headers
        _db.ServiceHeaders.RemoveRange(existing.Headers);
        existing.Headers = service.Headers.Select(h => new ServiceHeader
        {
            Id = Guid.NewGuid(),
            UpstreamServiceId = existing.Id,
            Key = h.Key,
            Value = h.Value
        }).ToList();

        await _db.SaveChangesAsync(ct);
        _changes.OnNext(new RegistryChange(ChangeType.Updated, existing.Id));
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
