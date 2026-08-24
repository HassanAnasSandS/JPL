using Microsoft.EntityFrameworkCore;
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

    public DbSet<League> Leagues => Set<League>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamPlayer> TeamPlayers => Set<TeamPlayer>();
    public DbSet<SyncOutboxItem> SyncOutbox => Set<SyncOutboxItem>();
    public DbSet<LiveOutboxItem> LiveOutbox => Set<LiveOutboxItem>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Innings> Innings => Set<Innings>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<AuctionCaptain> AuctionCaptains => Set<AuctionCaptain>();
    public DbSet<AuctionLot> AuctionLots => Set<AuctionLot>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<VenueBooking> VenueBookings => Set<VenueBooking>();
    public DbSet<VenueBookingParticipant> VenueBookingParticipants => Set<VenueBookingParticipant>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<TournamentTeam> TournamentTeams => Set<TournamentTeam>();
    public DbSet<TournamentFixture> TournamentFixtures => Set<TournamentFixture>();
    public DbSet<Series> Series => Set<Series>();
    public DbSet<SeriesFixture> SeriesFixtures => Set<SeriesFixture>();
    public DbSet<NetSession> NetSessions => Set<NetSession>();
    public DbSet<NetDelivery> NetDeliveries => Set<NetDelivery>();

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
            if (entry.Entity is AppUser user && user.LeagueId is null && user.Role != AppRole.SuperAdmin)
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
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.LeagueId);
            e.Ignore(x => x.LeagueName);
        });

        modelBuilder.Entity<Player>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.FullName);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
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

        modelBuilder.Entity<Match>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(160).IsRequired();
            e.Property(x => x.Venue).HasMaxLength(120);
            e.Property(x => x.SquadRosterJson);
            e.Property(x => x.LiveStreamUrl).HasMaxLength(1000);
            e.HasIndex(x => x.MatchDateUtc);
            e.HasIndex(x => x.LeagueId);
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
            e.HasIndex(x => x.PlayerId);
        });

        modelBuilder.Entity<Tournament>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
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
    }
}
