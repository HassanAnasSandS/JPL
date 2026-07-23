using Microsoft.EntityFrameworkCore;
using JPL.Domain;

namespace JPL.Data;

public sealed class JplDbContext : DbContext
{
    public JplDbContext(DbContextOptions<JplDbContext> options) : base(options)
    {
    }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamPlayer> TeamPlayers => Set<TeamPlayer>();
    public DbSet<SyncOutboxItem> SyncOutbox => Set<SyncOutboxItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Nickname).HasMaxLength(60);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.BattingStyle).HasMaxLength(40);
            e.Property(x => x.BowlingStyle).HasMaxLength(40);
            e.HasIndex(x => x.FullName);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.ShortName).HasMaxLength(20);
            e.HasIndex(x => x.Name);
            e.HasOne(x => x.Captain)
                .WithMany()
                .HasForeignKey(x => x.CaptainPlayerId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<TeamPlayer>(e =>
        {
            e.HasKey(x => new { x.TeamId, x.PlayerId });
            e.HasOne(x => x.Team)
                .WithMany(t => t.TeamPlayers)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Player)
                .WithMany(p => p.TeamPlayers)
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SyncOutboxItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            e.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
            e.Property(x => x.Operation).HasMaxLength(32).IsRequired();
        });
    }
}
