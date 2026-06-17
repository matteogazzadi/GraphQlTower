using FluentAssertions;
using GraphQlTower.Api.Data;
using Xunit;
using GraphQlTower.Shared.Interfaces;
using GraphQlTower.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace GraphQlTower.Api.Tests.Data;

public class EfServiceRegistryTests : IDisposable
{
    private readonly ServiceRegistryDbContext _db;
    private readonly EfServiceRegistry _registry;

    public EfServiceRegistryTests()
    {
        var options = new DbContextOptionsBuilder<ServiceRegistryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ServiceRegistryDbContext(options);
        _registry = new EfServiceRegistry(_db);
    }

    [Fact]
    public async Task AddAsync_StoresServiceAndReturnsIt()
    {
        var service = MakeService("products");

        var result = await _registry.AddAsync(service);

        result.Id.Should().NotBeEmpty();
        result.Name.Should().Be("products");
        result.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AddAsync_PublishesAddedChange()
    {
        var changes = new List<RegistryChange>();
        _registry.Changes.Subscribe(c => changes.Add(c));

        await _registry.AddAsync(MakeService("orders"));

        changes.Should().ContainSingle(c => c.Type == ChangeType.Added);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllServices()
    {
        await _registry.AddAsync(MakeService("svc1"));
        await _registry.AddAsync(MakeService("svc2"));

        var all = await _registry.GetAllAsync();

        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsCorrectService()
    {
        var added = await _registry.AddAsync(MakeService("users"));

        var found = await _registry.GetByIdAsync(added.Id);

        found.Should().NotBeNull();
        found!.Name.Should().Be("users");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _registry.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ChangesUrlAndEnabled()
    {
        var added = await _registry.AddAsync(MakeService("catalog"));

        added.Url = "https://updated.example.com/graphql";
        added.IsEnabled = false;
        await _registry.UpdateAsync(added);

        var updated = await _registry.GetByIdAsync(added.Id);
        updated!.Url.Should().Be("https://updated.example.com/graphql");
        updated.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_PublishesUpdatedChange()
    {
        var added = await _registry.AddAsync(MakeService("reviews"));
        var changes = new List<RegistryChange>();
        _registry.Changes.Subscribe(c => changes.Add(c));

        await _registry.UpdateAsync(added);

        changes.Should().ContainSingle(c => c.Type == ChangeType.Updated && c.ServiceId == added.Id);
    }

    [Fact]
    public async Task DeleteAsync_RemovesService()
    {
        var added = await _registry.AddAsync(MakeService("inventory"));

        await _registry.DeleteAsync(added.Id);

        var all = await _registry.GetAllAsync();
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_PublishesRemovedChange()
    {
        var added = await _registry.AddAsync(MakeService("payments"));
        var changes = new List<RegistryChange>();
        _registry.Changes.Subscribe(c => changes.Add(c));

        await _registry.DeleteAsync(added.Id);

        changes.Should().ContainSingle(c => c.Type == ChangeType.Removed && c.ServiceId == added.Id);
    }

    [Fact]
    public async Task AddAsync_ThrowsOnInvalidName()
    {
        var service = MakeService("invalid-name-with-hyphens");

        var act = async () => await _registry.AddAsync(service);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*name*");
    }

    [Fact]
    public async Task AddAsync_ThrowsOnEmptyName()
    {
        var service = MakeService("");

        var act = async () => await _registry.AddAsync(service);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AddAsync_IncludesHeaders()
    {
        var service = MakeService("secure_svc");
        service.Headers.Add(new ServiceHeader { Key = "Authorization", Value = "Bearer abc" });

        var result = await _registry.AddAsync(service);
        var fetched = await _registry.GetByIdAsync(result.Id);

        fetched!.Headers.Should().HaveCount(1);
        fetched.Headers[0].Key.Should().Be("Authorization");
    }

    [Fact]
    public async Task UpdateAsync_ReplacesHeaders()
    {
        var service = MakeService("svc_with_headers");
        service.Headers.Add(new ServiceHeader { Key = "X-Old-Header", Value = "old" });
        var added = await _registry.AddAsync(service);

        added.Headers = new List<ServiceHeader>
        {
            new() { Key = "X-New-Header", Value = "new" }
        };
        await _registry.UpdateAsync(added);

        var updated = await _registry.GetByIdAsync(added.Id);
        updated!.Headers.Should().HaveCount(1);
        updated.Headers[0].Key.Should().Be("X-New-Header");
    }

    public void Dispose() => _db.Dispose();

    private static UpstreamService MakeService(string name) => new()
    {
        Name = name,
        DisplayName = name,
        Url = $"https://{name}.example.com/graphql"
    };
}
