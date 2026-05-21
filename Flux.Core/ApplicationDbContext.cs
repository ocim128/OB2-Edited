using Microsoft.EntityFrameworkCore;
using Flux.Core.Entities;

namespace Flux.Core;

/// <summary>
/// The <see cref="DbContext"/> for the Flux core domain.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<ProxyEntity> Proxies { get; set; }
    public DbSet<ProxyGroupEntity> ProxyGroups { get; set; }
    public DbSet<WordlistEntity> Wordlists { get; set; }
    public DbSet<JobEntity> Jobs { get; set; }
    public DbSet<RecordEntity> Records { get; set; }
    public DbSet<HitEntity> Hits { get; set; }
    public DbSet<GuestEntity> Guests { get; set; }
    public DbSet<UserEntity> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProxyGroupEntity>()
            .HasMany(g => g.Proxies)
            .WithOne(u => u.Group)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProxyGroupEntity>()
            .HasOne(g => g.Owner)
            .WithMany(u => u.ProxyGroups)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<UserEntity>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<HitEntity>()
            .HasIndex(h => new { h.OwnerId, h.Type, h.Date });

        modelBuilder.Entity<HitEntity>()
            .HasIndex(h => new { h.OwnerId, h.ConfigName, h.Date });

        modelBuilder.Entity<HitEntity>()
            .HasIndex(h => new { h.ConfigName, h.Date });

        modelBuilder.Entity<HitEntity>()
            .HasIndex(h => new { h.Type, h.Date });

        modelBuilder.Entity<HitEntity>()
            .HasIndex(h => h.Date);

        modelBuilder.Entity<RecordEntity>()
            .HasIndex(r => new { r.ConfigId, r.WordlistId });

        modelBuilder.Entity<ProxyEntity>()
            .HasIndex("GroupId", nameof(ProxyEntity.Status));

        modelBuilder.Entity<ProxyEntity>()
            .HasIndex("GroupId", nameof(ProxyEntity.LastChecked));

        modelBuilder.Entity<ProxyEntity>()
            .HasIndex(p => new { p.Status, p.Ping });

        base.OnModelCreating(modelBuilder);
    }
}
