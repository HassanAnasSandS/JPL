using Microsoft.EntityFrameworkCore;
using JPL.Api.Models;
using JPL.Api.Services;

namespace JPL.Api.Data;

public sealed class ApiDbContext : DbContext
{
    private readonly IApiLeagueScope _league;

    public ApiDbContext(DbContextOptions<ApiDbContext> options, IApiLeagueScope league) : base(options)
    {
        _league = league;
    }

    public Guid CurrentLeagueId => _league.CurrentLeagueId;

    public DbSet<Player> Players => Set<Player>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamPlayer> TeamPlayers => Set<TeamPlayer>();
    public DbSet<SyncReceipt> SyncReceipts => Set<SyncReceipt>();
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
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<TournamentTeam> TournamentTeams => Set<TournamentTeam>();
    public DbSet<TournamentFixture> TournamentFixtures => Set<TournamentFixture>();
    public DbSet<Series> Series => Set<Series>();
    public DbSet<SeriesFixture> SeriesFixtures => Set<SeriesFixture>();
    public DbSet<NetSession> NetSessions => Set<NetSession>();
    public DbSet<NetDelivery> NetDeliveries => Set<NetDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.ImagePath).HasMaxLength(80);
            e.HasOne(x => x.Captain).WithMany().HasForeignKey(x => x.CaptainPlayerId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TeamPlayer>(e =>
        {
            e.HasKey(x => new { x.TeamId, x.PlayerId });
            e.HasOne(x => x.Team).WithMany(t => t.TeamPlayers).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Player).WithMany(p => p.TeamPlayers).HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.IsPlaying).HasDefaultValue(true);
        });

        modelBuilder.Entity<SyncReceipt>(e =>
        {
            e.HasKey(x => x.IdempotencyKey);
            e.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        modelBuilder.Entity<Format>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Kind).HasColumnName("Format");
            // Raw SQL created these as datetime2; other league tables use datetimeoffset.
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            e.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2");
            e.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<Match>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(160).IsRequired();
            e.Property(x => x.LiveStreamUrl).HasMaxLength(1000);
            e.HasIndex(x => x.FormatId);
        });

        modelBuilder.Entity<Innings>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Match).WithMany(m => m.Innings).HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.MatchId, x.InningsNumber }).IsUnique();
        });

        modelBuilder.Entity<Delivery>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Innings).WithMany(i => i.Deliveries).HasForeignKey(x => x.InningsId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.BatterId, x.BowlerId });
            e.HasIndex(x => new { x.InningsId, x.Sequence }).IsUnique();
        });

        modelBuilder.Entity<Auction>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(160).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => x.AuctionDateUtc);
        });

        modelBuilder.Entity<AuctionCaptain>(e =>
        {
            e.HasKey(x => new { x.AuctionId, x.PlayerId });
            e.HasOne(x => x.Auction).WithMany(a => a.Captains).HasForeignKey(x => x.AuctionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.WalletAmount).IsRequired();
        });

        modelBuilder.Entity<AuctionLot>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Auction).WithMany(a => a.Lots).HasForeignKey(x => x.AuctionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.AuctionId, x.PlayerId }).IsUnique();
            e.HasIndex(x => x.PlayerId);
        });

        modelBuilder.Entity<Venue>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<VenueBooking>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.BookingCost).HasPrecision(18, 2);
            e.HasIndex(x => x.BookingDateUtc);
            e.HasIndex(x => x.CreatedByUserId);
            e.HasIndex(x => x.BookedByPlayerId);
            e.HasIndex(x => x.VenueId);
        });

        modelBuilder.Entity<VenueBookingParticipant>(e =>
        {
            e.HasKey(x => new { x.VenueBookingId, x.PlayerId });
            e.HasOne(x => x.VenueBooking).WithMany(b => b.Participants)
                .HasForeignKey(x => x.VenueBookingId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasMaxLength(16).IsRequired();
            e.HasIndex(x => x.PlayerId);
        });

        modelBuilder.Entity<Tournament>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.HasIndex(x => x.CreatedAtUtc);
        });

        modelBuilder.Entity<TournamentTeam>(e =>
        {
            e.HasKey(x => new { x.TournamentId, x.TeamId });
            e.HasOne(x => x.Tournament).WithMany(t => t.Teams)
                .HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TeamId);
        });

        modelBuilder.Entity<TournamentFixture>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Label).HasMaxLength(80).IsRequired();
            e.Property(x => x.TeamASlot).HasMaxLength(40);
            e.Property(x => x.TeamBSlot).HasMaxLength(40);
            e.HasOne(x => x.Tournament).WithMany(t => t.Fixtures)
                .HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TournamentId, x.Sequence });
            e.HasIndex(x => x.MatchId);
        });

        modelBuilder.Entity<Series>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.HasIndex(x => x.CreatedAtUtc);
        });

        modelBuilder.Entity<SeriesFixture>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Label).HasMaxLength(80).IsRequired();
            e.HasOne(x => x.Series).WithMany(s => s.Fixtures)
                .HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SeriesId, x.Sequence });
            e.HasIndex(x => x.MatchId);
        });

        modelBuilder.Entity<NetSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.HasIndex(x => x.SessionDateUtc);
        });

        modelBuilder.Entity<NetDelivery>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Session).WithMany(s => s.Deliveries)
                .HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SessionId, x.Sequence }).IsUnique();
            e.HasIndex(x => new { x.BatterId, x.BowlerId });
        });

        SqlServerLocalDateTimes.Map(modelBuilder, (Database.ProviderName ?? "").Contains("SqlServer", StringComparison.OrdinalIgnoreCase));
    }
}
