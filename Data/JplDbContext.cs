using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Linq.Expressions;
using JPL.Domain;
using JPL.Services;

namespace JPL.Data;

public sealed class JplDbContext : DbContext
{
    private readonly ILeagueContext _league;

    public JplDbContext(DbContextOptions<JplDbContext> options, ILeagueContext league) : base(options)
    {
        _league = league;
    }

    /// <summary>Used by global query filters; Empty means "no league selected" (return none).</summary>
    public Guid CurrentLeagueId => _league.CurrentLeagueId ?? Guid.Empty;

    /// <summary>
    /// SQLite keeps Guid as TEXT. EF Core's default writes/compares UPPERCASE text, but
    /// <see cref="DbBootstrap"/> lowercases every Guid column before each sync (and all raw SQL uses
    /// lowercase). That mismatch made tracked UPDATE/DELETE hit 0 rows (DbUpdateConcurrencyException
    /// on the 2nd sync after a login), missed SyncCursor lookups (re-bootstrap every run) and created
    /// duplicate cache rows. Always store and compare Guids in lowercase "D" format.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Guid>().HaveConversion<LowercaseGuidConverter>();
        configurationBuilder.Properties<Guid?>().HaveConversion<LowercaseGuidConverter>();
    }

    public DbSet<League> Leagues => Set<League>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamPlayer> TeamPlayers => Set<TeamPlayer>();
    public DbSet<SyncOutboxItem> SyncOutbox => Set<SyncOutboxItem>();
    public DbSet<LiveOutboxItem> LiveOutbox => Set<LiveOutboxItem>();
    public DbSet<SyncCursor> SyncCursors => Set<SyncCursor>();
    public DbSet<SyncDeferredPullItem> SyncDeferredPulls => Set<SyncDeferredPullItem>();
    public DbSet<SyncLogEntry> SyncLogs => Set<SyncLogEntry>();
    public DbSet<LocalStatsCacheItem> LocalStatsCache => Set<LocalStatsCacheItem>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Innings> Innings => Set<Innings>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<AuctionCaptain> AuctionCaptains => Set<AuctionCaptain>();
    public DbSet<AuctionLot> AuctionLots => Set<AuctionLot>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Format> Formats => Set<Format>();
    public DbSet<VenueBooking> VenueBookings => Set<VenueBooking>();
    public DbSet<VenueBookingParticipant> VenueBookingParticipants => Set<VenueBookingParticipant>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseContribution> ExpenseContributions => Set<ExpenseContribution>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<TournamentTeam> TournamentTeams => Set<TournamentTeam>();
    public DbSet<TournamentFixture> TournamentFixtures => Set<TournamentFixture>();
    public DbSet<Series> Series => Set<Series>();
    public DbSet<SeriesFixture> SeriesFixtures => Set<SeriesFixture>();
    public DbSet<NetSession> NetSessions => Set<NetSession>();
    public DbSet<NetDelivery> NetDeliveries => Set<NetDelivery>();
    public DbSet<SchedulerTournament> SchedulerTournaments => Set<SchedulerTournament>();
    public DbSet<SchedulerTeam> SchedulerTeams => Set<SchedulerTeam>();
    public DbSet<SchedulerMatch> SchedulerMatches => Set<SchedulerMatch>();
    public DbSet<SchedulerSyncOutboxItem> SchedulerSyncOutbox => Set<SchedulerSyncOutboxItem>();
    public DbSet<LocalGroundCacheItem> LocalGroundCache => Set<LocalGroundCacheItem>();
    public DbSet<GroundBookingRequestOutboxItem> GroundBookingRequestOutbox => Set<GroundBookingRequestOutboxItem>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampLeagueId();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampLeagueId();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampLeagueId()
    {
        var leagueId = CurrentLeagueId;
        if (leagueId == Guid.Empty)
            return;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
                continue;
            if (entry.Entity is ILeagueScoped scoped && scoped.LeagueId == Guid.Empty)
                scoped.LeagueId = leagueId;
            if (entry.Entity is AppUser user && user.LeagueId is null
                && user.Role != AppRole.SuperAdmin
                && user.Role != AppRole.GroundOwner
                && user.Role != AppRole.Guest)
                user.LeagueId = leagueId;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<League>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
        });

        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).HasMaxLength(64).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            e.Property(x => x.WhatsAppNumber).HasMaxLength(30);
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.LeagueId);
            e.Ignore(x => x.LeagueName);
        });

        modelBuilder.Entity<Player>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(120).IsRequired();
            e.Property(x => x.NickName).HasMaxLength(40);
            e.Property(x => x.PlayingRole).HasMaxLength(40);
            e.Property(x => x.BattingStyle).HasMaxLength(40);
            e.Property(x => x.BowlingStyle).HasMaxLength(60);
            e.Property(x => x.Bio).HasMaxLength(500);
            e.Property(x => x.ImagePath).HasMaxLength(80);
            e.HasIndex(x => x.FullName);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.ImagePath).HasMaxLength(80);
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
            e.Property(x => x.IsPlaying).HasDefaultValue(true);
        });

        modelBuilder.Entity<SyncOutboxItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            e.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
            e.Property(x => x.Operation).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<LiveOutboxItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Status, x.CreatedAtUtc, x.SortId });
            e.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
            e.Property(x => x.Operation).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<SyncCursor>(e =>
        {
            e.HasKey(x => x.LeagueId);
        });

        modelBuilder.Entity<SyncDeferredPullItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
            e.Property(x => x.Operation).HasMaxLength(32).IsRequired();
            e.Property(x => x.EntityKeyJson).IsRequired();
            e.Property(x => x.PayloadJson).IsRequired();
            e.HasIndex(x => new { x.LeagueId, x.Sequence }).IsUnique();
            e.HasIndex(x => new { x.LeagueId, x.EntityType, x.EntityKeyJson });
        });

        modelBuilder.Entity<SyncLogEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Phase).HasMaxLength(40).IsRequired();
            e.Property(x => x.Message).HasMaxLength(280).IsRequired();
            e.Property(x => x.Detail).HasMaxLength(1200);
            e.HasIndex(x => new { x.LeagueId, x.AtUtc });
        });

        modelBuilder.Entity<LocalStatsCacheItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CacheKey).HasMaxLength(80).IsRequired();
            e.Property(x => x.PayloadJson).IsRequired();
            e.HasIndex(x => new { x.LeagueId, x.CacheKey }).IsUnique();
            e.HasIndex(x => new { x.LeagueId, x.UpdatedAtUtc });
        });

        modelBuilder.Entity<Match>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(160).IsRequired();
            e.Property(x => x.Venue).HasMaxLength(120);
            e.Property(x => x.SquadRosterJson);
            e.Property(x => x.LiveStreamUrl).HasMaxLength(1000);
            e.HasIndex(x => x.MatchDateUtc);
            e.HasIndex(x => x.LeagueId);
            e.HasIndex(x => x.FormatId);
            e.HasQueryFilter(x => !x.IsDeleted && x.LeagueId == CurrentLeagueId);
        });

        modelBuilder.Entity<Innings>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Match)
                .WithMany(m => m.Innings)
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.MatchId, x.InningsNumber }).IsUnique();
        });

        modelBuilder.Entity<Delivery>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Innings)
                .WithMany(i => i.Deliveries)
                .HasForeignKey(x => x.InningsId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.InningsId, x.Sequence }).IsUnique();
            e.HasIndex(x => new { x.BatterId, x.BowlerId });
            e.HasIndex(x => x.MatchId);
        });

        modelBuilder.Entity<Auction>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(160).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => x.AuctionDateUtc);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<AuctionCaptain>(e =>
        {
            e.HasKey(x => new { x.AuctionId, x.PlayerId });
            e.HasOne(x => x.Auction)
                .WithMany(a => a.Captains)
                .HasForeignKey(x => x.AuctionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.WalletAmount).IsRequired();
        });

        modelBuilder.Entity<AuctionLot>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Auction)
                .WithMany(a => a.Lots)
                .HasForeignKey(x => x.AuctionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.AuctionId, x.PlayerId }).IsUnique();
            e.HasIndex(x => x.PlayerId);
        });

        modelBuilder.Entity<Venue>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.HasIndex(x => x.Name);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Format>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Kind).HasColumnName("Format");
            e.HasIndex(x => x.Name);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<VenueBooking>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.BookingCost).HasPrecision(18, 2);
            e.HasIndex(x => x.BookingDateUtc);
            e.HasIndex(x => x.CreatedByUserId);
            e.HasIndex(x => x.VenueId);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<VenueBookingParticipant>(e =>
        {
            e.HasKey(x => new { x.VenueBookingId, x.PlayerId });
            e.HasOne(x => x.VenueBooking)
                .WithMany(b => b.Participants)
                .HasForeignKey(x => x.VenueBookingId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasMaxLength(16).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => x.PlayerId);
        });

        modelBuilder.Entity<Expense>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => x.CreatedByUserId);
            e.HasIndex(x => x.PurchasedByPlayerId);
        });

        modelBuilder.Entity<ExpenseContribution>(e =>
        {
            e.HasKey(x => new { x.ExpenseId, x.PlayerId });
            e.HasOne(x => x.Expense).WithMany(x => x.Contributions)
                .HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Status).HasMaxLength(16).IsRequired().HasDefaultValue(ExpenseContributionStatus.Pending);
            e.HasIndex(x => x.PlayerId);
        });

        modelBuilder.Entity<Tournament>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.HasIndex(x => x.FormatId);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<TournamentTeam>(e =>
        {
            e.HasKey(x => new { x.TournamentId, x.TeamId });
            e.HasOne(x => x.Tournament).WithMany(t => t.Teams)
                .HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TournamentFixture>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Label).HasMaxLength(80).IsRequired();
            e.HasOne(x => x.Tournament).WithMany(t => t.Fixtures)
                .HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.MatchId);
        });

        modelBuilder.Entity<Series>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.HasIndex(x => x.FormatId);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<SeriesFixture>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Label).HasMaxLength(80).IsRequired();
            e.HasOne(x => x.Series).WithMany(s => s.Fixtures)
                .HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.MatchId);
        });

        modelBuilder.Entity<NetSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.PlayerRosterJson);
            e.HasIndex(x => x.SessionDateUtc);
            e.HasIndex(x => x.LeagueId);
            e.HasQueryFilter(x => !x.IsDeleted && x.LeagueId == CurrentLeagueId);
        });

        modelBuilder.Entity<NetDelivery>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Session)
                .WithMany(s => s.Deliveries)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SessionId, x.Sequence }).IsUnique();
            e.HasIndex(x => new { x.BatterId, x.BowlerId });
        });

        modelBuilder.Entity<SchedulerTournament>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
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

        modelBuilder.Entity<SchedulerSyncOutboxItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.LeagueId, x.TournamentId, x.CreatedAtUtc });
            e.HasIndex(x => x.OperationId).IsUnique();
        });

        modelBuilder.Entity<LocalGroundCacheItem>(e =>
        {
            e.HasKey(x => x.CacheKey);
            e.Property(x => x.CacheKey).HasMaxLength(128);
            e.Property(x => x.PayloadJson).IsRequired();
            e.HasIndex(x => x.UpdatedAtUtc);
        });

        modelBuilder.Entity<GroundBookingRequestOutboxItem>(e =>
        {
            e.HasKey(x => x.RequestId);
            e.Property(x => x.GroundName).HasMaxLength(160).IsRequired();
            e.Property(x => x.PayloadJson).IsRequired();
            e.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            e.HasIndex(x => x.Status);
        });

        ApplyLeagueQueryFilters(modelBuilder);
    }

    private void ApplyLeagueQueryFilters(ModelBuilder modelBuilder)
    {
        var scopedTypes = modelBuilder.Model.GetEntityTypes()
            .Where(type => typeof(ILeagueScoped).IsAssignableFrom(type.ClrType))
            .ToArray();

        foreach (var type in scopedTypes)
        {
            var parameter = Expression.Parameter(type.ClrType, "row");
            var leagueId = Expression.Property(parameter, nameof(ILeagueScoped.LeagueId));
            var currentLeagueId = Expression.Property(Expression.Constant(this), nameof(CurrentLeagueId));
            Expression body = Expression.Equal(leagueId, currentLeagueId);
            var isDeleted = type.FindProperty("IsDeleted");
            if (isDeleted?.ClrType == typeof(bool))
                body = Expression.AndAlso(body, Expression.Not(Expression.Property(parameter, "IsDeleted")));

            modelBuilder.Entity(type.ClrType).HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }
}

/// <summary>Guid &lt;-&gt; lowercase TEXT so EF parameters always match normalized SQLite rows.</summary>
public sealed class LowercaseGuidConverter : ValueConverter<Guid, string>
{
    public LowercaseGuidConverter()
        : base(value => value.ToString("D"), text => Guid.Parse(text))
    {
    }
}
