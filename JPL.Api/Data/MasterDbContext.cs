using Microsoft.EntityFrameworkCore;
using JPL.Api.Models;
using JPL.Api.Services;

namespace JPL.Api.Data;

/// <summary>Cric360 platform database: users and league directory only.</summary>
public sealed class MasterDbContext : DbContext
{
    public MasterDbContext(DbContextOptions<MasterDbContext> options) : base(options)
    {
    }

    public DbSet<League> Leagues => Set<League>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<LeagueLiveIndex> LeagueLiveIndexes => Set<LeagueLiveIndex>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<League>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.DatabaseName).HasMaxLength(LeagueDatabaseManager.NameMaxLength);
        });

        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(64).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.LeagueId);
        });

        modelBuilder.Entity<LeagueLiveIndex>(e =>
        {
            e.HasKey(x => x.LeagueId);
        });

        SqlServerLocalDateTimes.Map(modelBuilder, (Database.ProviderName ?? "").Contains("SqlServer", StringComparison.OrdinalIgnoreCase));
    }
}
