using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using JPL.Domain;

namespace JPL.Data;

public static class DbBootstrap
{
    /// <summary>Bump when EnsureLegacyTables gains new columns/migrations.</summary>
    private const int SchemaVersion = 19;
    private const string SchemaVersionKey = "jpl.sqlite.schemaVersion";

    public static string GetDatabasePath()
    {
        var dir = FileSystem.AppDataDirectory;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "jpl.db");
    }

    public static async Task InitializeAsync(JplDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);

        // EnsureCreated does not add tables to an existing file. Phones that already
        // have a schema version skip the version-gated block, so nets/series CREATE
        // must run every launch (all statements are IF NOT EXISTS / ADD COLUMN).
        await EnsureNetSessionTablesAsync(db, ct).ConfigureAwait(false);
        await EnsureLiveOutboxTableAsync(db, ct).ConfigureAwait(false);
        await EnsureSchedulerTablesAsync(db, ct).ConfigureAwait(false);

        var dbVersion = await ReadUserVersionAsync(db, ct).ConfigureAwait(false);
        if (dbVersion < SchemaVersion)
        {
            await EnsureLegacyTablesAsync(db, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync($"PRAGMA user_version = {SchemaVersion};", ct)
                .ConfigureAwait(false);
            Preferences.Default.Set(SchemaVersionKey, SchemaVersion);
        }
        else
        {
            await EnsureNetSessionTablesAsync(db, ct).ConfigureAwait(false);
            await EnsureLiveOutboxTableAsync(db, ct).ConfigureAwait(false);
            await EnsureSchedulerTablesAsync(db, ct).ConfigureAwait(false);
        }

        // Always run: phones already at SchemaVersion skip EnsureLegacyTables, so new
        // Users columns must not wait for a version bump.
        try
        {
            await EnsureColumnAsync(db, "Users", "WhatsAppNumber",
                """ALTER TABLE "Users" ADD COLUMN "WhatsAppNumber" TEXT NULL;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* table may not exist yet */
        }

        await EnsureColumnAsync(db, "Matches", "LiveStreamUrl",
            """ALTER TABLE "Matches" ADD COLUMN "LiveStreamUrl" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Matches", "IsLiveTelecast",
            """ALTER TABLE "Matches" ADD COLUMN "IsLiveTelecast" INTEGER NOT NULL DEFAULT 1;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Matches", "StartedByUserId",
            """ALTER TABLE "Matches" ADD COLUMN "StartedByUserId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Matches", "StartedOnDeviceId",
            """ALTER TABLE "Matches" ADD COLUMN "StartedOnDeviceId" TEXT NULL;""", ct).ConfigureAwait(false);

        await EnsureColumnAsync(db, "Players", "ImagePath",
            """ALTER TABLE "Players" ADD COLUMN "ImagePath" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Players", "NickName",
            """ALTER TABLE "Players" ADD COLUMN "NickName" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Players", "PlayingRole",
            """ALTER TABLE "Players" ADD COLUMN "PlayingRole" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Players", "BattingStyle",
            """ALTER TABLE "Players" ADD COLUMN "BattingStyle" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Players", "BowlingStyle",
            """ALTER TABLE "Players" ADD COLUMN "BowlingStyle" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Players", "Bio",
            """ALTER TABLE "Players" ADD COLUMN "Bio" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Teams", "ImagePath",
            """ALTER TABLE "Teams" ADD COLUMN "ImagePath" TEXT NULL;""", ct).ConfigureAwait(false);

        await EnsureFormatsAsync(db, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=3000;", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", ct).ConfigureAwait(false);
    }

    /// <summary>Flush WAL into jpl.db so an app update/kill does not drop the latest balls.</summary>
    public static async Task CheckpointAsync(JplDbContext db, CancellationToken ct = default)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* never block UI */
        }
    }

    private static async Task<int> ReadUserVersionAsync(JplDbContext db, CancellationToken ct)
    {
        try
        {
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            var value = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return value is null or DBNull ? 0 : Convert.ToInt32(value);
        }
        catch
        {
            return Preferences.Default.Get(SchemaVersionKey, 0);
        }
    }

    private static async Task EnsureLegacyTablesAsync(JplDbContext db, CancellationToken ct)
    {
        await EnsureLeagueSchemaAsync(db, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Users" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY,
                "Username" TEXT NOT NULL,
                "PasswordHash" TEXT NOT NULL,
                "Role" INTEGER NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL
            );
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Username" ON "Users" ("Username");""",
            ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Matches" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Matches" PRIMARY KEY,
                "Title" TEXT NOT NULL,
                "TeamAId" TEXT NOT NULL,
                "TeamBId" TEXT NOT NULL,
                "TeamACaptainId" TEXT NULL,
                "TeamBCaptainId" TEXT NULL,
                "OversLimit" INTEGER NOT NULL,
                "MatchDateUtc" TEXT NOT NULL,
                "Venue" TEXT NULL,
                "Status" INTEGER NOT NULL,
                "TossWinnerTeamId" TEXT NULL,
                "TossWinnerBatsFirst" INTEGER NOT NULL,
                "Version" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "IsDeleted" INTEGER NOT NULL
            );
            """, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Innings" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Innings" PRIMARY KEY,
                "MatchId" TEXT NOT NULL,
                "InningsNumber" INTEGER NOT NULL,
                "BattingTeamId" TEXT NOT NULL,
                "BowlingTeamId" TEXT NOT NULL,
                "IsCompleted" INTEGER NOT NULL,
                "StartedAtUtc" TEXT NOT NULL,
                CONSTRAINT "FK_Innings_Matches_MatchId" FOREIGN KEY ("MatchId") REFERENCES "Matches" ("Id") ON DELETE CASCADE
            );
            """, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Deliveries" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Deliveries" PRIMARY KEY,
                "MatchId" TEXT NOT NULL,
                "InningsId" TEXT NOT NULL,
                "Sequence" INTEGER NOT NULL,
                "OverNumber" INTEGER NOT NULL,
                "BallInOver" INTEGER NOT NULL,
                "BatterId" TEXT NOT NULL,
                "NonStrikerId" TEXT NOT NULL,
                "BowlerId" TEXT NOT NULL,
                "RunsOffBat" INTEGER NOT NULL,
                "ExtraRuns" INTEGER NOT NULL,
                "ExtraType" INTEGER NOT NULL,
                "IsWicket" INTEGER NOT NULL,
                "WicketType" INTEGER NOT NULL,
                "DismissedPlayerId" TEXT NULL,
                "IsLegalDelivery" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "Version" INTEGER NOT NULL,
                CONSTRAINT "FK_Deliveries_Innings_InningsId" FOREIGN KEY ("InningsId") REFERENCES "Innings" ("Id") ON DELETE CASCADE
            );
            """, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Matches_MatchDateUtc" ON "Matches" ("MatchDateUtc");""", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Innings_MatchId_InningsNumber" ON "Innings" ("MatchId", "InningsNumber");""", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Deliveries_InningsId_Sequence" ON "Deliveries" ("InningsId", "Sequence");""", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Deliveries_BatterId_BowlerId" ON "Deliveries" ("BatterId", "BowlerId");""", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Deliveries_MatchId" ON "Deliveries" ("MatchId");""", ct).ConfigureAwait(false);

        await EnsureColumnAsync(db, "Deliveries", "FielderId",
            """ALTER TABLE "Deliveries" ADD COLUMN "FielderId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Deliveries", "ShotZone",
            """ALTER TABLE "Deliveries" ADD COLUMN "ShotZone" INTEGER NOT NULL DEFAULT 0;""", ct).ConfigureAwait(false);

        await EnsureColumnAsync(db, "Teams", "IsActive",
            """ALTER TABLE "Teams" ADD COLUMN "IsActive" INTEGER NOT NULL DEFAULT 1;""", ct).ConfigureAwait(false);

        await EnsureColumnAsync(db, "Players", "IsActive",
            """ALTER TABLE "Players" ADD COLUMN "IsActive" INTEGER NOT NULL DEFAULT 1;""", ct).ConfigureAwait(false);

        foreach (var col in new[] { "Phone", "Notes" })
            await DropColumnIfExistsAsync(db, "Players", col, ct).ConfigureAwait(false);

        await EnsureColumnAsync(db, "Players", "NickName",
            """ALTER TABLE "Players" ADD COLUMN "NickName" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Players", "PlayingRole",
            """ALTER TABLE "Players" ADD COLUMN "PlayingRole" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Players", "BattingStyle",
            """ALTER TABLE "Players" ADD COLUMN "BattingStyle" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Players", "BowlingStyle",
            """ALTER TABLE "Players" ADD COLUMN "BowlingStyle" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Players", "Bio",
            """ALTER TABLE "Players" ADD COLUMN "Bio" TEXT NULL;""", ct).ConfigureAwait(false);

        await EnsureColumnAsync(db, "Matches", "Format",
            """ALTER TABLE "Matches" ADD COLUMN "Format" INTEGER NOT NULL DEFAULT 0;""", ct).ConfigureAwait(false);

        await EnsureColumnAsync(db, "Matches", "SquadRosterJson",
            """ALTER TABLE "Matches" ADD COLUMN "SquadRosterJson" TEXT NULL;""", ct).ConfigureAwait(false);

        await EnsureColumnAsync(db, "TeamPlayers", "IsPlaying",
            """ALTER TABLE "TeamPlayers" ADD COLUMN "IsPlaying" INTEGER NOT NULL DEFAULT 1;""", ct)
            .ConfigureAwait(false);

        await EnsureColumnAsync(db, "Matches", "PlayerOfTheMatchPlayerId",
            """ALTER TABLE "Matches" ADD COLUMN "PlayerOfTheMatchPlayerId" TEXT NULL;""", ct).ConfigureAwait(false);

        await EnsureColumnAsync(db, "Matches", "LastBatsmanCanBatAsSingleWicket",
            """ALTER TABLE "Matches" ADD COLUMN "LastBatsmanCanBatAsSingleWicket" INTEGER NOT NULL DEFAULT 0;""", ct)
            .ConfigureAwait(false);

        await EnsureColumnAsync(db, "Matches", "BowlerMaxOvers",
            """ALTER TABLE "Matches" ADD COLUMN "BowlerMaxOvers" INTEGER NOT NULL DEFAULT 0;""", ct)
            .ConfigureAwait(false);

        await EnsureColumnAsync(db, "Matches", "OversMode",
            """ALTER TABLE "Matches" ADD COLUMN "OversMode" INTEGER NOT NULL DEFAULT 0;""", ct)
            .ConfigureAwait(false);

        await EnsureColumnAsync(db, "Innings", "IsFollowOn",
            """ALTER TABLE "Innings" ADD COLUMN "IsFollowOn" INTEGER NOT NULL DEFAULT 0;""", ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Auctions" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Auctions" PRIMARY KEY,
                "Title" TEXT NOT NULL,
                "AuctionDateUtc" TEXT NOT NULL,
                "Notes" TEXT NULL,
                "Version" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "IsDeleted" INTEGER NOT NULL
            );
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Auctions_AuctionDateUtc" ON "Auctions" ("AuctionDateUtc");""", ct)
            .ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "AuctionCaptains" (
                "AuctionId" TEXT NOT NULL,
                "PlayerId" TEXT NOT NULL,
                "WalletAmount" INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "PK_AuctionCaptains" PRIMARY KEY ("AuctionId", "PlayerId"),
                CONSTRAINT "FK_AuctionCaptains_Auctions_AuctionId" FOREIGN KEY ("AuctionId") REFERENCES "Auctions" ("Id") ON DELETE CASCADE
            );
            """, ct).ConfigureAwait(false);

        await EnsureColumnAsync(db, "AuctionCaptains", "WalletAmount",
            """ALTER TABLE "AuctionCaptains" ADD COLUMN "WalletAmount" INTEGER NOT NULL DEFAULT 0;""", ct)
            .ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "AuctionLots" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AuctionLots" PRIMARY KEY,
                "AuctionId" TEXT NOT NULL,
                "PlayerId" TEXT NOT NULL,
                "BasePrice" INTEGER NOT NULL,
                "SoldPrice" INTEGER NULL,
                "SoldToCaptainPlayerId" TEXT NULL,
                "IsSold" INTEGER NOT NULL,
                CONSTRAINT "FK_AuctionLots_Auctions_AuctionId" FOREIGN KEY ("AuctionId") REFERENCES "Auctions" ("Id") ON DELETE CASCADE
            );
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_AuctionLots_AuctionId_PlayerId" ON "AuctionLots" ("AuctionId", "PlayerId");""", ct)
            .ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_AuctionLots_PlayerId" ON "AuctionLots" ("PlayerId");""", ct)
            .ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Venues" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Venues" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Version" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "IsDeleted" INTEGER NOT NULL
            );
            """, ct).ConfigureAwait(false);

        await EnsureColumnAsync(db, "VenueBookings", "VenueId",
            """ALTER TABLE "VenueBookings" ADD COLUMN "VenueId" TEXT NULL;""", ct).ConfigureAwait(false);

        // Backfill VenueId from legacy VenueName when present.
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE "VenueBookings"
                SET "VenueId" = (
                    SELECT v."Id" FROM "Venues" v
                    WHERE lower(trim(v."Name")) = lower(trim("VenueBookings"."VenueName"))
                    LIMIT 1
                )
                WHERE "VenueId" IS NULL OR "VenueId" = '';
                """, ct).ConfigureAwait(false);
        }
        catch
        {
            /* VenueName column may already be gone */
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync($"""
                UPDATE "VenueBookings"
                SET "VenueId" = '{VenueSeeds.FalconId:D}'
                WHERE "VenueId" IS NULL OR "VenueId" = '';
                """, ct).ConfigureAwait(false);
        }
        catch
        {
            /* table or column may not exist yet */
        }

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "VenueBookings" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_VenueBookings" PRIMARY KEY,
                "CreatedByUserId" TEXT NOT NULL,
                "VenueId" TEXT NOT NULL,
                "BookingDateUtc" TEXT NOT NULL,
                "BookedByPlayerId" TEXT NOT NULL,
                "BookingCost" TEXT NOT NULL,
                "Version" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "IsDeleted" INTEGER NOT NULL
            );
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_VenueBookings_BookingDateUtc" ON "VenueBookings" ("BookingDateUtc");""", ct)
            .ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_VenueBookings_CreatedByUserId" ON "VenueBookings" ("CreatedByUserId");""", ct)
            .ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "VenueBookingParticipants" (
                "VenueBookingId" TEXT NOT NULL,
                "PlayerId" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                CONSTRAINT "PK_VenueBookingParticipants" PRIMARY KEY ("VenueBookingId", "PlayerId"),
                CONSTRAINT "FK_VenueBookingParticipants_VenueBookings_VenueBookingId" FOREIGN KEY ("VenueBookingId") REFERENCES "VenueBookings" ("Id") ON DELETE CASCADE
            );
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_VenueBookingParticipants_PlayerId" ON "VenueBookingParticipants" ("PlayerId");""", ct)
            .ConfigureAwait(false);

        await EnsureColumnAsync(db, "Matches", "TournamentId",
            """ALTER TABLE "Matches" ADD COLUMN "TournamentId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Matches", "TournamentFixtureId",
            """ALTER TABLE "Matches" ADD COLUMN "TournamentFixtureId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Matches", "SeriesId",
            """ALTER TABLE "Matches" ADD COLUMN "SeriesId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Matches", "SeriesFixtureId",
            """ALTER TABLE "Matches" ADD COLUMN "SeriesFixtureId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Matches", "LiveStreamUrl",
            """ALTER TABLE "Matches" ADD COLUMN "LiveStreamUrl" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Matches", "IsLiveTelecast",
            """ALTER TABLE "Matches" ADD COLUMN "IsLiveTelecast" INTEGER NOT NULL DEFAULT 1;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Matches", "StartedByUserId",
            """ALTER TABLE "Matches" ADD COLUMN "StartedByUserId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Matches", "StartedOnDeviceId",
            """ALTER TABLE "Matches" ADD COLUMN "StartedOnDeviceId" TEXT NULL;""", ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Tournaments" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Tournaments" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "GroupCount" INTEGER NOT NULL,
                "QualifyPerGroup" INTEGER NOT NULL,
                "VenueId" TEXT NOT NULL DEFAULT 'A1000001-0000-4000-8000-000000000001',
                "Format" INTEGER NOT NULL DEFAULT 0,
                "OversLimit" INTEGER NOT NULL DEFAULT 8,
                "LastBatsmanCanBatAsSingleWicket" INTEGER NOT NULL DEFAULT 0,
                "BowlerMaxOvers" INTEGER NOT NULL DEFAULT 0,
                "Status" INTEGER NOT NULL,
                "WinnerTeamId" TEXT NULL,
                "Version" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "IsDeleted" INTEGER NOT NULL
            );
            """, ct).ConfigureAwait(false);

        await EnsureColumnAsync(db, "Tournaments", "VenueId",
            """ALTER TABLE "Tournaments" ADD COLUMN "VenueId" TEXT NOT NULL DEFAULT 'A1000001-0000-4000-8000-000000000001';""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Tournaments", "Format",
            """ALTER TABLE "Tournaments" ADD COLUMN "Format" INTEGER NOT NULL DEFAULT 0;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Tournaments", "OversLimit",
            """ALTER TABLE "Tournaments" ADD COLUMN "OversLimit" INTEGER NOT NULL DEFAULT 8;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Tournaments", "LastBatsmanCanBatAsSingleWicket",
            """ALTER TABLE "Tournaments" ADD COLUMN "LastBatsmanCanBatAsSingleWicket" INTEGER NOT NULL DEFAULT 0;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Tournaments", "BowlerMaxOvers",
            """ALTER TABLE "Tournaments" ADD COLUMN "BowlerMaxOvers" INTEGER NOT NULL DEFAULT 0;""", ct).ConfigureAwait(false);

        await EnsureColumnAsync(db, "Tournaments", "OversMode",
            """ALTER TABLE "Tournaments" ADD COLUMN "OversMode" INTEGER NOT NULL DEFAULT 0;""", ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "TournamentTeams" (
                "TournamentId" TEXT NOT NULL,
                "TeamId" TEXT NOT NULL,
                "GroupNumber" INTEGER NOT NULL,
                CONSTRAINT "PK_TournamentTeams" PRIMARY KEY ("TournamentId", "TeamId"),
                CONSTRAINT "FK_TournamentTeams_Tournaments_TournamentId" FOREIGN KEY ("TournamentId") REFERENCES "Tournaments" ("Id") ON DELETE CASCADE
            );
            """, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "TournamentFixtures" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_TournamentFixtures" PRIMARY KEY,
                "TournamentId" TEXT NOT NULL,
                "Stage" INTEGER NOT NULL,
                "GroupNumber" INTEGER NULL,
                "Sequence" INTEGER NOT NULL,
                "Label" TEXT NOT NULL,
                "TeamAId" TEXT NULL,
                "TeamBId" TEXT NULL,
                "TeamASlot" TEXT NULL,
                "TeamBSlot" TEXT NULL,
                "MatchId" TEXT NULL,
                CONSTRAINT "FK_TournamentFixtures_Tournaments_TournamentId" FOREIGN KEY ("TournamentId") REFERENCES "Tournaments" ("Id") ON DELETE CASCADE
            );
            """, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Series" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Series" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "TeamAId" TEXT NOT NULL,
                "TeamBId" TEXT NOT NULL,
                "MatchCount" INTEGER NOT NULL,
                "VenueId" TEXT NOT NULL,
                "Format" INTEGER NOT NULL DEFAULT 0,
                "OversLimit" INTEGER NOT NULL DEFAULT 8,
                "OversMode" INTEGER NOT NULL DEFAULT 0,
                "LastBatsmanCanBatAsSingleWicket" INTEGER NOT NULL DEFAULT 0,
                "BowlerMaxOvers" INTEGER NOT NULL DEFAULT 0,
                "Status" INTEGER NOT NULL,
                "WinnerTeamId" TEXT NULL,
                "IsTied" INTEGER NOT NULL DEFAULT 0,
                "Version" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "IsDeleted" INTEGER NOT NULL
            );
            """, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "SeriesFixtures" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SeriesFixtures" PRIMARY KEY,
                "SeriesId" TEXT NOT NULL,
                "Sequence" INTEGER NOT NULL,
                "Label" TEXT NOT NULL,
                "TeamAId" TEXT NOT NULL,
                "TeamBId" TEXT NOT NULL,
                "MatchId" TEXT NULL,
                CONSTRAINT "FK_SeriesFixtures_Series_SeriesId" FOREIGN KEY ("SeriesId") REFERENCES "Series" ("Id") ON DELETE CASCADE
            );
            """, ct).ConfigureAwait(false);

        await EnsureNetSessionTablesAsync(db, ct).ConfigureAwait(false);
        await EnsureLiveOutboxTableAsync(db, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// League format catalog + FormatId FK on matches/series/tournaments.
    /// Existing phones skip EnsureCreated, so this runs every launch.
    /// </summary>
    public static async Task EnsureFormatsAsync(JplDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Formats" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Formats" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Format" INTEGER NOT NULL DEFAULT 0,
                "OversLimit" INTEGER NOT NULL DEFAULT 8,
                "OversMode" INTEGER NOT NULL DEFAULT 1,
                "Version" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "IsDeleted" INTEGER NOT NULL
            );
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Formats_Name" ON "Formats" ("Name");""", ct)
            .ConfigureAwait(false);

        await EnsureColumnAsync(db, "Matches", "FormatId",
            """ALTER TABLE "Matches" ADD COLUMN "FormatId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Tournaments", "FormatId",
            """ALTER TABLE "Tournaments" ADD COLUMN "FormatId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Series", "FormatId",
            """ALTER TABLE "Series" ADD COLUMN "FormatId" TEXT NULL;""", ct).ConfigureAwait(false);

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_Matches_FormatId" ON "Matches" ("FormatId");""", ct)
                .ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_Tournaments_FormatId" ON "Tournaments" ("FormatId");""", ct)
                .ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync(
                """CREATE INDEX IF NOT EXISTS "IX_Series_FormatId" ON "Series" ("FormatId");""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* index create is best-effort */
        }

        try
        {
            await BackfillFormatIdsAsync(db, ct).ConfigureAwait(false);
        }
        catch
        {
            /* best-effort — FormatId is filled on next launch or via API sync */
        }
    }

    private static async Task BackfillFormatIdsAsync(JplDbContext db, CancellationToken ct)
    {
        var known = (await db.Formats.IgnoreQueryFilters()
            .Select(f => f.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false)).ToHashSet();

        async Task<Guid> EnsureRow(MatchFormat format, OversMode mode, int oversLimit)
        {
            var stored = OversRules.StoredOversLimit(mode, oversLimit);
            var id = FormatRules.MigratedId(format, mode, stored);
            if (known.Contains(id))
                return id;

            var desired = FormatRules.DefaultName(format, mode, stored);
            var existingNames = await db.Formats.IgnoreQueryFilters()
                .Where(f => !f.IsDeleted)
                .Select(f => f.Name)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var name = desired;
            var n = 2;
            while (existingNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{desired} ({n})";
                n++;
            }

            var now = DateTime.Now;
            db.Formats.Add(new Format
            {
                Id = id,
                Name = name,
                Kind = format,
                OversLimit = stored,
                OversMode = mode,
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            known.Add(id);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return id;
        }

        foreach (var m in await db.Matches.IgnoreQueryFilters().ToListAsync(ct).ConfigureAwait(false))
        {
            var id = await EnsureRow(m.Format, m.OversMode, m.OversLimit).ConfigureAwait(false);
            if (m.FormatId is null || m.FormatId == Guid.Empty)
                m.FormatId = id;
        }

        foreach (var t in await db.Tournaments.IgnoreQueryFilters().ToListAsync(ct).ConfigureAwait(false))
        {
            var id = await EnsureRow(t.Format, t.OversMode, t.OversLimit).ConfigureAwait(false);
            if (t.FormatId is null || t.FormatId == Guid.Empty)
                t.FormatId = id;
        }

        foreach (var s in await db.Series.IgnoreQueryFilters().ToListAsync(ct).ConfigureAwait(false))
        {
            var id = await EnsureRow(s.Format, s.OversMode, s.OversLimit).ConfigureAwait(false);
            if (s.FormatId is null || s.FormatId == Guid.Empty)
                s.FormatId = id;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Live telecast queue — existing phones skip EnsureCreated for new tables.</summary>
    public static async Task EnsureLiveOutboxTableAsync(JplDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "LiveOutbox" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LiveOutbox" PRIMARY KEY,
                "IdempotencyKey" TEXT NOT NULL,
                "EntityType" TEXT NOT NULL,
                "EntityId" TEXT NOT NULL,
                "Operation" TEXT NOT NULL,
                "PayloadJson" TEXT NOT NULL,
                "Status" INTEGER NOT NULL,
                "AttemptCount" INTEGER NOT NULL,
                "LastError" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "LastAttemptAtUtc" TEXT NULL,
                "SortId" INTEGER NOT NULL
            );
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_LiveOutbox_Status_CreatedAtUtc_SortId" ON "LiveOutbox" ("Status", "CreatedAtUtc", "SortId");""",
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Create nets tables on every launch. Existing DBs skip EnsureCreated for new tables,
    /// and a failed earlier migration must not leave Nets Practice broken.
    /// </summary>
    public static async Task EnsureNetSessionTablesAsync(JplDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "NetSessions" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_NetSessions" PRIMARY KEY,
                "LeagueId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "SessionDateUtc" TEXT NOT NULL,
                "Status" INTEGER NOT NULL,
                "PlayerRosterJson" TEXT NULL,
                "Version" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "IsDeleted" INTEGER NOT NULL
            );
            """, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "NetDeliveries" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_NetDeliveries" PRIMARY KEY,
                "SessionId" TEXT NOT NULL,
                "Sequence" INTEGER NOT NULL,
                "OverNumber" INTEGER NOT NULL,
                "BallInOver" INTEGER NOT NULL,
                "BatterId" TEXT NOT NULL,
                "NonStrikerId" TEXT NOT NULL,
                "BowlerId" TEXT NOT NULL,
                "RunsOffBat" INTEGER NOT NULL,
                "ExtraRuns" INTEGER NOT NULL,
                "ExtraType" INTEGER NOT NULL,
                "IsWicket" INTEGER NOT NULL,
                "WicketType" INTEGER NOT NULL,
                "DismissedPlayerId" TEXT NULL,
                "FielderId" TEXT NULL,
                "IsLegalDelivery" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "Version" INTEGER NOT NULL,
                CONSTRAINT "FK_NetDeliveries_NetSessions_SessionId" FOREIGN KEY ("SessionId") REFERENCES "NetSessions" ("Id") ON DELETE CASCADE
            );
            """, ct).ConfigureAwait(false);

        const string jpl = "b1000001-0000-4000-8000-000000000001";
        await EnsureColumnAsync(db, "NetSessions", "LeagueId",
            $"""ALTER TABLE "NetSessions" ADD COLUMN "LeagueId" TEXT NOT NULL DEFAULT '{jpl}';""", ct)
            .ConfigureAwait(false);
        await EnsureColumnAsync(db, "NetSessions", "PlayerRosterJson",
            """ALTER TABLE "NetSessions" ADD COLUMN "PlayerRosterJson" TEXT NULL;""", ct)
            .ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_NetSessions_SessionDateUtc" ON "NetSessions" ("SessionDateUtc");""",
            ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_NetSessions_LeagueId" ON "NetSessions" ("LeagueId");""",
            ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_NetDeliveries_SessionId_Sequence" ON "NetDeliveries" ("SessionId", "Sequence");""",
            ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_NetDeliveries_BatterId_BowlerId" ON "NetDeliveries" ("BatterId", "BowlerId");""",
            ct).ConfigureAwait(false);
    }

    public static async Task EnsureSchedulerTablesAsync(JplDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "SchedulerTournaments" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SchedulerTournaments" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "GroupCount" INTEGER NOT NULL,
                "RoundRobinType" INTEGER NOT NULL,
                "QualifyPerGroup" INTEGER NOT NULL,
                "KnockoutFormat" INTEGER NOT NULL,
                "Status" INTEGER NOT NULL,
                "WinnerTeamId" TEXT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "QualificationPicksJson" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "IsDeleted" INTEGER NOT NULL
            );
            """, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "SchedulerTeams" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SchedulerTeams" PRIMARY KEY,
                "TournamentId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "GroupNumber" INTEGER NOT NULL,
                "SortOrder" INTEGER NOT NULL,
                CONSTRAINT "FK_SchedulerTeams_SchedulerTournaments_TournamentId"
                    FOREIGN KEY ("TournamentId") REFERENCES "SchedulerTournaments" ("Id") ON DELETE CASCADE
            );
            """, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "SchedulerMatches" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SchedulerMatches" PRIMARY KEY,
                "TournamentId" TEXT NOT NULL,
                "Stage" INTEGER NOT NULL,
                "GroupNumber" INTEGER NOT NULL,
                "Sequence" INTEGER NOT NULL,
                "Label" TEXT NOT NULL,
                "TeamAId" TEXT NULL,
                "TeamBId" TEXT NULL,
                "TeamASlot" TEXT NULL,
                "TeamBSlot" TEXT NULL,
                "Result" INTEGER NOT NULL,
                "WinnerFeedsMatchId" TEXT NULL,
                "WinnerFeedsIsTeamA" INTEGER NOT NULL,
                "LoserFeedsMatchId" TEXT NULL,
                "LoserFeedsIsTeamA" INTEGER NOT NULL,
                CONSTRAINT "FK_SchedulerMatches_SchedulerTournaments_TournamentId"
                    FOREIGN KEY ("TournamentId") REFERENCES "SchedulerTournaments" ("Id") ON DELETE CASCADE
            );
            """, ct).ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_SchedulerTeams_TournamentId" ON "SchedulerTeams" ("TournamentId");""",
            ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_SchedulerMatches_TournamentId" ON "SchedulerMatches" ("TournamentId");""",
            ct).ConfigureAwait(false);
    }

    private static async Task EnsureLeagueSchemaAsync(JplDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Leagues" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Leagues" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "ImageBytes" BLOB NULL,
                "IsActive" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL
            );
            """, ct).ConfigureAwait(false);

        const string jpl = "b1000001-0000-4000-8000-000000000001";
        foreach (var table in new[] { "Matches", "Users" })
        {
            var sql = table == "Users"
                ? """ALTER TABLE "Users" ADD COLUMN "LeagueId" TEXT NULL;"""
                : $"""ALTER TABLE "{table}" ADD COLUMN "LeagueId" TEXT NOT NULL DEFAULT '{jpl}';""";
            try
            {
                await EnsureColumnAsync(db, table, "LeagueId", sql, ct).ConfigureAwait(false);
            }
            catch
            {
                /* table may not exist yet on a brand-new DB */
            }
        }

        try
        {
            await EnsureColumnAsync(db, "Users", "WhatsAppNumber",
                """ALTER TABLE "Users" ADD COLUMN "WhatsAppNumber" TEXT NULL;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* table may not exist yet */
        }

        foreach (var table in new[] { "Matches" })
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"""UPDATE "{table}" SET "LeagueId" = '{jpl}' WHERE "LeagueId" IS NULL OR "LeagueId" = '';""",
                    ct).ConfigureAwait(false);
            }
            catch
            {
                /* table missing */
            }
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync($"""
                UPDATE "Users"
                SET "LeagueId" = '{jpl}'
                WHERE ("LeagueId" IS NULL OR "LeagueId" = '')
                  AND lower("Username") <> 'user'
                  AND "Role" <> 2;
                """, ct).ConfigureAwait(false);
        }
        catch
        {
            /* Users table missing */
        }
    }

    private static async Task EnsureColumnAsync(
        JplDbContext db, string table, string column, string alterSql, CancellationToken ct)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
            await cmd.Connection.OpenAsync(ct).ConfigureAwait(false);

        cmd.CommandText = $"""SELECT 1 FROM sqlite_master WHERE type='table' AND name='{table}' LIMIT 1;""";
        var tableExists = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
        if (!tableExists)
            return;

        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var exists = false;
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        if (!exists)
            await db.Database.ExecuteSqlRawAsync(alterSql, ct).ConfigureAwait(false);
    }

    private static async Task DropColumnIfExistsAsync(
        JplDbContext db, string table, string column, CancellationToken ct)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
            await cmd.Connection.OpenAsync(ct).ConfigureAwait(false);

        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var exists = false;
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        if (!exists)
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                $"""ALTER TABLE "{table}" DROP COLUMN "{column}";""", ct).ConfigureAwait(false);
        }
        catch
        {
            /* older SQLite may not support DROP COLUMN */
        }
    }

    public static DbContextOptionsBuilder ConfigureSqlite(this DbContextOptionsBuilder builder)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = GetDatabasePath(),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        return builder.UseSqlite(csb.ToString());
    }
}
