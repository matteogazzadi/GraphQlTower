using GraphQlTower.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace GraphQlTower.Api.Data;

public class ServiceRegistryDbContext : DbContext
{
    public ServiceRegistryDbContext(DbContextOptions<ServiceRegistryDbContext> options)
        : base(options) { }

    public DbSet<UpstreamService> UpstreamServices => Set<UpstreamService>();
    public DbSet<ServiceHeader> ServiceHeaders => Set<ServiceHeader>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UpstreamService>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Url).HasMaxLength(2048).IsRequired();
            e.HasMany(x => x.Headers)
             .WithOne()
             .HasForeignKey(x => x.UpstreamServiceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServiceHeader>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(200).IsRequired();
            e.Property(x => x.Value).HasMaxLength(4096).IsRequired();
        });
    }
}
