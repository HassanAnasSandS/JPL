using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;
using JPL.Api.Models;
using JPL.Api.Services;

namespace JPL.Api.Data;

public sealed class ApiDbContext : DbContext
{
    internal static readonly HashSet<string> SyncEntityTypes = new(StringComparer.Ordinal)
    {
        nameof(Player), nameof(Team), nameof(TeamPlayer),
        nameof(Match), nameof(Innings), nameof(Delivery),
        nameof(Auction), nameof(AuctionCaptain), nameof(AuctionLot),
        nameof(Venue), nameof(Format), nameof(VenueBooking), nameof(VenueBookingParticipant),
        nameof(Expense), nameof(ExpenseContribution),
        nameof(Tournament), nameof(TournamentTeam), nameof(TournamentFixture),
        nameof(Series), nameof(SeriesFixture), nameof(NetSession), nameof(NetDelivery)
    };
    internal static IReadOnlyList<string> SyncEntityTypeNames => SyncEntityTypes
        .OrderBy(SyncDependencyOrder)
        .ThenBy(x => x, StringComparer.Ordinal)
        .ToArray();
    private static readonly JsonSerializerOptions SyncJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
    public DbSet<SyncChange> SyncChanges => Set<SyncChange>();
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

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ChangeTracker.DetectChanges();
        if (!HasPendingSyncChanges())
            return base.SaveChanges(acceptAllChangesOnSuccess);

        if (Database.CurrentTransaction is null)
        {
            using var transaction = Database.BeginTransaction();
            AcquireSyncChangeOrderLock();
            CaptureSyncChanges();
            var saved = base.SaveChanges(acceptAllChangesOnSuccess);
            transaction.Commit();
            return saved;
        }

        AcquireSyncChangeOrderLock();
        CaptureSyncChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        if (!HasPendingSyncChanges())
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);

        if (Database.CurrentTransaction is null)
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await AcquireSyncChangeOrderLockAsync(cancellationToken).ConfigureAwait(false);
            CaptureSyncChanges();
            var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return saved;
        }

        await AcquireSyncChangeOrderLockAsync(cancellationToken).ConfigureAwait(false);
        CaptureSyncChanges();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
    }

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

        modelBuilder.Entity<SyncChange>(e =>
        {
            e.HasKey(x => x.Sequence);
            e.Property(x => x.Sequence).ValueGeneratedOnAdd();
            // Match live SQL catalogs (EntityType 128 / EntityKey 600 / Operation 32).
            e.Property(x => x.EntityType).HasMaxLength(128).IsRequired();
            e.Property(x => x.EntityKey).HasMaxLength(600).IsRequired();
            e.Property(x => x.Operation).HasMaxLength(32).IsRequired();
            e.Property(x => x.ChangedAtUtc).HasColumnType("datetime2");
            e.HasIndex(x => new { x.EntityType, x.EntityKey, x.Sequence });
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
            e.HasIndex(x => new { x.IsDeleted, x.Status, x.UpdatedAtUtc })
                .HasDatabaseName("IX_Matches_LiveList");
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
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => x.PlayerId);
        });

        modelBuilder.Entity<Expense>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            // Expense timestamps are created as datetime2 by the tenant schema bootstrap.
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            e.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2");
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

    private void CaptureSyncChanges()
    {
        ChangeTracker.DetectChanges();
        var pending = ChangeTracker.Entries()
            .Where(entry => SyncEntityTypes.Contains(entry.Metadata.ClrType.Name)
                            && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .OrderBy(entry => SyncOrder(entry.Metadata.ClrType.Name, entry.State == EntityState.Deleted))
            .ToList();
        if (pending.Count == 0)
            return;

        foreach (var entry in pending)
        {
            var properties = entry.Properties
                .Where(property => !property.Metadata.IsShadowProperty())
                .ToDictionary(
                    property => JsonNamingPolicy.CamelCase.ConvertName(property.Metadata.Name),
                    property => entry.State == EntityState.Deleted
                        ? property.OriginalValue
                        : property.CurrentValue);
            var keyProperties = entry.Metadata.FindPrimaryKey()?.Properties ?? [];
            var keyValues = keyProperties.ToDictionary(
                property => JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                property => entry.State == EntityState.Deleted
                    ? entry.Property(property.Name).OriginalValue
                    : entry.Property(property.Name).CurrentValue);
            var isSoftDeleted = entry.Metadata.FindProperty("IsDeleted") is not null
                                && entry.Property("IsDeleted").CurrentValue is true;
            var operation = entry.State == EntityState.Deleted || isSoftDeleted ? "Delete" : "Upsert";
            long? version = entry.Metadata.FindProperty("Version") is null
                ? null
                : Convert.ToInt64(entry.Property("Version").CurrentValue ?? 0);

            // Image file paths are server-local implementation details, not portable data.
            properties.Remove("imagePath");

            SyncChanges.Add(new SyncChange
            {
                EntityType = entry.Metadata.ClrType.Name,
                EntityKey = JsonSerializer.Serialize(keyValues, SyncJsonOptions),
                Operation = operation,
                EntityVersion = version,
                ChangedAtUtc = DateTime.UtcNow,
                PayloadJson = JsonSerializer.Serialize(properties, SyncJsonOptions)
            });
        }
    }

    private bool HasPendingSyncChanges()
        => ChangeTracker.Entries().Any(entry =>
            SyncEntityTypes.Contains(entry.Metadata.ClrType.Name)
            && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

    internal static int SyncDependencyOrder(string entityType) => entityType switch
    {
        nameof(Player) => 0,
        nameof(Team) => 1,
        nameof(TeamPlayer) => 2,
        nameof(Format) => 3,
        nameof(Venue) => 4,
        nameof(Auction) => 5,
        nameof(AuctionCaptain) or nameof(AuctionLot) => 6,
        nameof(Tournament) => 7,
        nameof(TournamentTeam) => 8,
        nameof(TournamentFixture) => 9,
        nameof(Series) => 10,
        nameof(SeriesFixture) => 11,
        nameof(Match) => 12,
        nameof(Innings) => 13,
        nameof(Delivery) => 14,
        nameof(NetSession) => 15,
        nameof(NetDelivery) => 16,
        nameof(VenueBooking) => 17,
        nameof(VenueBookingParticipant) => 18,
        nameof(Expense) => 19,
        nameof(ExpenseContribution) => 20,
        _ => 100
    };

    private static int SyncOrder(string entityType, bool deleting)
    {
        var order = SyncDependencyOrder(entityType);
        return deleting ? 100 - order : order;
    }

    /// <summary>
    /// IDENTITY values can be allocated in a different order from transaction commits.
    /// Serialize sync-producing transactions per league so a client cursor can never
    /// move past a lower sequence value that commits later.
    /// </summary>
    private void AcquireSyncChangeOrderLock()
    {
        if (!(Database.ProviderName ?? string.Empty).Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            return; // SQLite serializes writers for the whole write transaction.

        using var command = Database.GetDbConnection().CreateCommand();
        command.Transaction = Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock
                @Resource = N'JPL.SyncChanges.CommitOrder',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000;
            IF @lockResult < 0 THROW 51000, 'Could not acquire sync change ordering lock.', 1;
            """;
        command.ExecuteNonQuery();
    }

    private async Task AcquireSyncChangeOrderLockAsync(CancellationToken cancellationToken)
    {
        if (!(Database.ProviderName ?? string.Empty).Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            return; // SQLite serializes writers for the whole write transaction.

        await using var command = Database.GetDbConnection().CreateCommand();
        command.Transaction = Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock
                @Resource = N'JPL.SyncChanges.CommitOrder',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000;
            IF @lockResult < 0 THROW 51000, 'Could not acquire sync change ordering lock.', 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
