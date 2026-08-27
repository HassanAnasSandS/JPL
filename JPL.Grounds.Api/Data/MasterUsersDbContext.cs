using Microsoft.EntityFrameworkCore;
using JPL.Grounds.Api.Models;

namespace JPL.Grounds.Api.Data;

/// <summary>Cric360 master Users table only — token validation, no league sports data.</summary>
public sealed class MasterUsersDbContext : DbContext
{
    public MasterUsersDbContext(DbContextOptions<MasterUsersDbContext> options) : base(options)
    {
    }

    public DbSet<MasterUser> Users => Set<MasterUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MasterUser>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(64).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
        });

        SqlServerLocalDateTimes.Map(modelBuilder, (Database.ProviderName ?? "").Contains("SqlServer", StringComparison.OrdinalIgnoreCase));
    }
}
