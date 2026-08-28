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
    public DbSet<SchedulerTournament> SchedulerTournaments => Set<SchedulerTournament>();
    public DbSet<SchedulerTeam> SchedulerTeams => Set<SchedulerTeam>();
    public DbSet<SchedulerMatch> SchedulerMatches => Set<SchedulerMatch>();

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
            e.Property(x => x.WhatsAppNumber).HasMaxLength(30);
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.LeagueId);
        });

        modelBuilder.Entity<LeagueLiveIndex>(e =>
        {
            e.HasKey(x => x.LeagueId);
        });

        modelBuilder.Entity<SchedulerTournament>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.QualificationPicksJson);
            // Raw SQL created these as datetime2; other master tables use datetimeoffset.
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            e.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2");
            e.HasIndex(x => x.CreatedByUserId);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SchedulerTeam>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasOne(x => x.Tournament).WithMany(t => t.Teams)
                .HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TournamentId);
        });

        modelBuilder.Entity<SchedulerMatch>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Label).HasMaxLength(80).IsRequired();
            e.Property(x => x.TeamASlot).HasMaxLength(80);
            e.Property(x => x.TeamBSlot).HasMaxLength(80);
            e.HasOne(x => x.Tournament).WithMany(t => t.Matches)
                .HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TournamentId);
        });

        SqlServerLocalDateTimes.Map(modelBuilder, (Database.ProviderName ?? "").Contains("SqlServer", StringComparison.OrdinalIgnoreCase));
    }
}
