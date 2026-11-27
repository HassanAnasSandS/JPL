using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using JPL.Domain;

namespace JPL.Data;

public static class DbBootstrap
{
    /// <summary>Bump when EnsureLegacyTables gains new columns/migrations.</summary>
    private const int SchemaVersion = 42;
    private const string SchemaVersionKey = "jpl.sqlite.schemaVersion";
    private static readonly string[] LeagueScopedTables =
    [
        "Players", "Teams", "TeamPlayers", "Matches", "Innings", "Deliveries",
        "Auctions", "AuctionCaptains", "AuctionLots", "Venues", "Formats",
        "VenueBookings", "VenueBookingParticipants", "Expenses", "ExpenseContributions",
        "Tournaments", "TournamentTeams", "TournamentFixtures", "Series", "SeriesFixtures",
        "NetSessions", "NetDeliveries",
        "SyncOutbox", "LiveOutbox", "SyncCursors", "SyncDeferredPulls", "SyncLogs",
        "LocalStatsCache"
    ];

    public static string GetDatabasePath(Guid? userId = null)
    {
        var dir = FileSystem.AppDataDirectory;
        Directory.CreateDirectory(dir);
        return userId is Guid id && id != Guid.Empty
            ? Path.Combine(dir, $"jpl-user-{id:N}.db")
            : Path.Combine(dir, "jpl.db");
    }

    public static async Task InitializeAsync(JplDbContext db, CancellationToken ct = default)
    {
        var path = db.Database.GetDbConnection().DataSource;
        if (!File.Exists(path))
            await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
        else
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

        var dbVersion = await ReadUserVersionAsync(db, ct).ConfigureAwait(false);
        if (dbVersion < SchemaVersion)
        {
            await DropLegacyTournamentSchedulerTablesAsync(db, ct).ConfigureAwait(false);
            await EnsureNetSessionTablesAsync(db, ct).ConfigureAwait(false);
            await EnsureLiveOutboxTableAsync(db, ct).ConfigureAwait(false);
            await EnsureLocalStatsCacheTableAsync(db, ct).ConfigureAwait(false);
            await EnsureLegacyTablesAsync(db, ct).ConfigureAwait(false);
            await EnsureLeagueScopedColumnsAsync(db, ct).ConfigureAwait(false);

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

            await db.Database.ExecuteSqlRawAsync($"PRAGMA user_version = {SchemaVersion};", ct)
                .ConfigureAwait(false);
            Preferences.Default.Set(SchemaVersionKey, SchemaVersion);
        }

        // Always idempotent — version gate alone left older DBs without these columns
        // while EF already selected them (Nets/Matches pages then fail with "no such column").
        await EnsureSyncSchemaAsync(db, ct).ConfigureAwait(false);
        await EnsureSyncLogTableAsync(db, ct).ConfigureAwait(false);
        await DropLegacyVenueBookingVenueNameAsync(db, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"GroundBookingRequestOutbox\";", ct)
            .ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"LocalGroundCache\";", ct)
            .ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=8000;", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", ct).ConfigureAwait(false);
    }

    private static async Task DropLegacyTournamentSchedulerTablesAsync(JplDbContext db, CancellationToken ct)
    {
        // These tables belonged only to the removed standalone Tournament Scheduler module.
        foreach (var table in new[]
        {
            "SchedulerSyncOutbox", "SchedulerMatches", "SchedulerTeams", "SchedulerTournaments"
        })
        {
            await db.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS \"{table}\";", ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// SyncOutbox / deferred-pull columns required by the current EF model.
    /// Safe to call on every startup; skips ALTER when the column already exists.
    /// </summary>
    public static Task EnsureSyncSchemaAsync(JplDbContext db, CancellationToken ct = default)
        => EnsureSyncSchemaCoreAsync(db, ct);

    private static async Task EnsureSyncSchemaCoreAsync(JplDbContext db, CancellationToken ct)
    {
        // Create SyncOutbox if an older install somehow never got it.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "SyncOutbox" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SyncOutbox" PRIMARY KEY,
                "LeagueId" TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
                "IdempotencyKey" TEXT NOT NULL,
                "EntityType" TEXT NOT NULL,
                "EntityKeyJson" TEXT NOT NULL DEFAULT '{{}}',
                "BasePayloadJson" TEXT NULL,
                "ServerPayloadJson" TEXT NULL,
                "EntityId" TEXT NOT NULL,
                "Operation" TEXT NOT NULL,
                "PayloadJson" TEXT NOT NULL,
                "Status" INTEGER NOT NULL,
                "AttemptCount" INTEGER NOT NULL,
                "LastError" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "LastAttemptAtUtc" TEXT NULL
            );
            """, ct).ConfigureAwait(false);

        // Prefer ALTER+ignore over PRAGMA-only detection — older installs hit
        // "no such column: s.BasePayloadJson" when EF mapped columns before repair ran.
        await TryAddColumnAsync(db,
            """ALTER TABLE "SyncOutbox" ADD COLUMN "EntityKeyJson" TEXT NOT NULL DEFAULT '{{}}';""", ct)
            .ConfigureAwait(false);
        await TryAddColumnAsync(db,
            """ALTER TABLE "SyncOutbox" ADD COLUMN "BasePayloadJson" TEXT NULL;""", ct)
            .ConfigureAwait(false);
        await TryAddColumnAsync(db,
            """ALTER TABLE "SyncOutbox" ADD COLUMN "ServerPayloadJson" TEXT NULL;""", ct)
            .ConfigureAwait(false);
        await TryAddColumnAsync(db,
            """ALTER TABLE "SyncOutbox" ADD COLUMN "LeagueId" TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';""", ct)
            .ConfigureAwait(false);
        await EnsureSyncDeferredPullsAsync(db, ct).ConfigureAwait(false);

        // SQLite Guid TEXT compares case-sensitively. Legacy rows with uppercase hex
        // break EF lookups (lowercase params) → duplicate PK inserts on cache/sync.
        await NormalizeGuidTextColumnsAsync(db, ct).ConfigureAwait(false);
        await DeduplicateSyncCursorsAsync(db, ct).ConfigureAwait(false);
        await RemapSentinelLeagueRowsAsync(db, ct).ConfigureAwait(false);
        await DropLegacyVenueBookingVenueNameAsync(db, ct).ConfigureAwait(false);
        await EnsureLocalSyncCursorsAsync(db, ct).ConfigureAwait(false);
        await EnsureSyncLogTableAsync(db, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// SyncCursors PK is LeagueId TEXT — uppercase + lowercase Guid variants can coexist.
    /// Merge them so EF Find/UPDATE never hits 0 rows (DbUpdateConcurrencyException).
    /// </summary>
    public static async Task DeduplicateSyncCursorsAsync(JplDbContext db, CancellationToken ct = default)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "SyncCursors_merge" (
                    "LeagueId" TEXT NOT NULL PRIMARY KEY,
                    "LastSequence" INTEGER NOT NULL DEFAULT 0,
                    "IsBootstrapped" INTEGER NOT NULL DEFAULT 0,
                    "LastSuccessfulSyncUtc" TEXT NULL
                );
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync(
                """DELETE FROM "SyncCursors_merge";""", ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "SyncCursors_merge" ("LeagueId", "LastSequence", "IsBootstrapped", "LastSuccessfulSyncUtc")
                SELECT lower("LeagueId"),
                       MAX("LastSequence"),
                       MAX("IsBootstrapped"),
                       MAX("LastSuccessfulSyncUtc")
                FROM "SyncCursors"
                WHERE "LeagueId" IS NOT NULL AND length(trim("LeagueId")) = 36
                GROUP BY lower("LeagueId");
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync(
                """DELETE FROM "SyncCursors";""", ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "SyncCursors" ("LeagueId", "LastSequence", "IsBootstrapped", "LastSuccessfulSyncUtc")
                SELECT "LeagueId", "LastSequence", "IsBootstrapped", "LastSuccessfulSyncUtc"
                FROM "SyncCursors_merge";
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync(
                """DROP TABLE IF EXISTS "SyncCursors_merge";""", ct).ConfigureAwait(false);
        }
        catch
        {
            /* table may not exist yet on brand-new installs */
        }
    }

    public static async Task EnsureSyncLogTableAsync(JplDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "SyncLogs" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SyncLogs" PRIMARY KEY,
                "LeagueId" TEXT NOT NULL,
                "AtUtc" TEXT NOT NULL,
                "Level" INTEGER NOT NULL DEFAULT 0,
                "Phase" TEXT NOT NULL,
                "Message" TEXT NOT NULL,
                "Detail" TEXT NULL
            );
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_SyncLogs_LeagueId_AtUtc"
                ON "SyncLogs" ("LeagueId", "AtUtc");
            """, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Domain/API use VenueId only. Legacy installs still have VenueName TEXT NOT NULL —
    /// EF inserts omit that column → Sync dies with NOT NULL constraint failed.
    /// </summary>
    private static async Task DropLegacyVenueBookingVenueNameAsync(JplDbContext db, CancellationToken ct)
    {
        if (!await ColumnExistsAsync(db, "VenueBookings", "VenueName", ct).ConfigureAwait(false))
            return;

        try
        {
            await EnsureColumnAsync(db, "VenueBookings", "VenueId",
                    """ALTER TABLE "VenueBookings" ADD COLUMN "VenueId" TEXT NULL;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* already present */
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE "VenueBookings"
                SET "VenueId" = (
                    SELECT v."Id" FROM "Venues" v
                    WHERE lower(trim(v."Name")) = lower(trim("VenueBookings"."VenueName"))
                    LIMIT 1
                )
                WHERE ("VenueId" IS NULL OR "VenueId" = '')
                  AND "VenueName" IS NOT NULL AND trim("VenueName") != '';
                """, ct).ConfigureAwait(false);
        }
        catch
        {
            /* ignore */
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
            /* ignore */
        }

        // Prefer DROP COLUMN (SQLite 3.35+). Fall back to table rebuild.
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "VenueBookings" DROP COLUMN "VenueName";""", ct)
                .ConfigureAwait(false);
            return;
        }
        catch
        {
            /* older SQLite — rebuild */
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "VenueBookings_v40" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_VenueBookings" PRIMARY KEY,
                    "LeagueId" TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
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
                INSERT INTO "VenueBookings_v40" (
                    "Id", "LeagueId", "CreatedByUserId", "VenueId", "BookingDateUtc",
                    "BookedByPlayerId", "BookingCost", "Version", "CreatedAtUtc", "UpdatedAtUtc", "IsDeleted")
                SELECT
                    "Id",
                    IFNULL("LeagueId", '00000000-0000-0000-0000-000000000000'),
                    "CreatedByUserId",
                    IFNULL(NULLIF("VenueId", ''), '00000000-0000-0000-0000-000000000001'),
                    "BookingDateUtc",
                    "BookedByPlayerId",
                    "BookingCost",
                    "Version",
                    "CreatedAtUtc",
                    "UpdatedAtUtc",
                    "IsDeleted"
                FROM "VenueBookings";
                DROP TABLE "VenueBookings";
                ALTER TABLE "VenueBookings_v40" RENAME TO "VenueBookings";
                CREATE INDEX IF NOT EXISTS "IX_VenueBookings_BookingDateUtc" ON "VenueBookings" ("BookingDateUtc");
                CREATE INDEX IF NOT EXISTS "IX_VenueBookings_CreatedByUserId" ON "VenueBookings" ("CreatedByUserId");
                """, ct).ConfigureAwait(false);
        }
        catch
        {
            // Last resort: keep column but allow NULL so EF inserts succeed.
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                        """UPDATE "VenueBookings" SET "VenueName" = '' WHERE "VenueName" IS NULL;""", ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                /* give up — Sync will surface the real error */
            }
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        JplDbContext db, string table, string column, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != System.Data.ConnectionState.Open;
        if (closeWhenDone)
            await connection.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\");";
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var name = reader.GetString(1);
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Rows stamped with legacy <see cref="LeagueIds.Jpl"/> (b1000001…) are invisible under a
    /// real home-league filter — remap onto the selected league from Preferences.
    /// </summary>
    private static async Task RemapSentinelLeagueRowsAsync(JplDbContext db, CancellationToken ct)
    {
        var homeText = Preferences.Default.Get("cric360.league.currentId", string.Empty);
        if (!Guid.TryParse(homeText, out var home) || home == Guid.Empty || home == LeagueIds.Jpl)
            homeText = Preferences.Default.Get("jpl.session.leagueId", string.Empty);
        if (!Guid.TryParse(homeText, out home) || home == Guid.Empty || home == LeagueIds.Jpl)
            return;

        var homeId = home.ToString("D");
        var legacy = LeagueIds.Jpl.ToString("D");
        foreach (var table in new[]
                 {
                     "Teams", "Players", "TeamPlayers", "Matches", "Formats", "Venues",
                     "Auctions", "AuctionCaptains", "AuctionLots",
                     "Tournaments", "TournamentTeams", "TournamentFixtures",
                     "Series", "SeriesFixtures", "Innings", "Deliveries",
                     "NetSessions", "NetDeliveries", "Expenses", "ExpenseContributions"
                 })
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE \"" + table + "\" SET \"LeagueId\" = {0} WHERE lower(\"LeagueId\") = lower({1});",
                    homeId, legacy).ConfigureAwait(false);
            }
            catch
            {
                /* table may not exist yet */
            }
        }
    }

    /// <summary>
    /// Lowercase every TEXT value that looks like a Guid so EF parameters match SQLite rows.
    /// Idempotent — safe on every startup and before Sync pull.
    /// </summary>
    public static async Task NormalizeGuidTextColumnsForSyncAsync(JplDbContext db, CancellationToken ct = default)
    {
        await NormalizeGuidTextColumnsAsync(db, ct).ConfigureAwait(false);
        await DeduplicateSyncCursorsAsync(db, ct).ConfigureAwait(false);
    }

    private static async Task NormalizeGuidTextColumnsAsync(JplDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != System.Data.ConnectionState.Open;
        if (closeWhenDone)
            await connection.OpenAsync(ct).ConfigureAwait(false);

        // Parent Id + child FK columns are lowercased in separate statements; with FK enforcement on,
        // lowercasing a parent first aborts the whole UPDATE and leaves mixed-case Guids behind.
        var restoreForeignKeys = false;
        var foreignKeysOff = false;
        try
        {
            await using var fkRead = connection.CreateCommand();
            fkRead.CommandText = "PRAGMA foreign_keys;";
            var fkState = await fkRead.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (Convert.ToInt64(fkState ?? 0L, System.Globalization.CultureInfo.InvariantCulture) == 1)
            {
                await using var fkOff = connection.CreateCommand();
                fkOff.CommandText = "PRAGMA foreign_keys=OFF;";
                await fkOff.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                restoreForeignKeys = true;
                // PRAGMA is a no-op inside a transaction — re-read to be sure.
                var after = await fkRead.ExecuteScalarAsync(ct).ConfigureAwait(false);
                foreignKeysOff = Convert.ToInt64(after ?? 1L, System.Globalization.CultureInfo.InvariantCulture) == 0;
            }
            else
            {
                foreignKeysOff = true;
            }
        }
        catch
        {
            /* inside a transaction / unsupported — keep going */
        }

        try
        {
            var tables = new List<string>();
            await using (var list = connection.CreateCommand())
            {
                list.CommandText = """
                    SELECT name FROM sqlite_master
                    WHERE type = 'table' AND name NOT LIKE 'sqlite_%';
                    """;
                await using var reader = await list.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    tables.Add(reader.GetString(0));
            }

            foreach (var table in tables)
            {
                // Only with FK enforcement off: never let a twin DELETE cascade into child rows.
                if (foreignKeysOff)
                    await DeleteLowercaseTwinsAsync(connection, table, ct).ConfigureAwait(false);

                var columns = new List<string>();
                await using (var pragma = connection.CreateCommand())
                {
                    pragma.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\");";
                    await using var reader = await pragma.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var colType = reader.IsDBNull(2) ? "" : reader.GetString(2);
                        if (colType.Contains("TEXT", StringComparison.OrdinalIgnoreCase)
                            || colType.Length == 0)
                            columns.Add(reader.GetString(1));
                    }
                }

                foreach (var column in columns)
                {
                    var safeTable = table.Replace("\"", "\"\"", StringComparison.Ordinal);
                    var safeColumn = column.Replace("\"", "\"\"", StringComparison.Ordinal);

                    // If lowercasing a PK/unique column would collide with an existing
                    // lowercase row, drop the uppercase duplicate first.
                    if (string.Equals(column, "Id", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            await using var dedupe = connection.CreateCommand();
                            dedupe.CommandText = $"""
                                DELETE FROM "{safeTable}"
                                WHERE typeof("Id") = 'text'
                                  AND length("Id") = 36
                                  AND "Id" != lower("Id")
                                  AND EXISTS (
                                      SELECT 1 FROM "{safeTable}" AS keep
                                      WHERE keep."Id" = lower("{safeTable}"."Id")
                                  );
                                """;
                            await dedupe.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }
                        catch
                        {
                            /* ignore — table may lack Id or block DELETE */
                        }
                    }

                    await using var update = connection.CreateCommand();
                    // 36-char dashed Guid values only — leave other TEXT alone.
                    // OR IGNORE: one row that would collide on a UNIQUE index (e.g. LocalStatsCache
                    // LeagueId+CacheKey written in both cases) must not abort the whole column.
                    update.CommandText = $"""
                        UPDATE OR IGNORE "{safeTable}"
                        SET "{safeColumn}" = lower("{safeColumn}")
                        WHERE typeof("{safeColumn}") = 'text'
                          AND length("{safeColumn}") = 36
                          AND substr("{safeColumn}", 9, 1) = '-'
                          AND substr("{safeColumn}", 14, 1) = '-'
                          AND substr("{safeColumn}", 19, 1) = '-'
                          AND substr("{safeColumn}", 24, 1) = '-'
                          AND "{safeColumn}" != lower("{safeColumn}");
                        """;
                    try
                    {
                        await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        /* table/column may be virtual, locked, or unique — ignore */
                    }
                }
            }

        }
        finally
        {
            if (restoreForeignKeys)
            {
                try
                {
                    await using var fkOn = connection.CreateCommand();
                    fkOn.CommandText = "PRAGMA foreign_keys=ON;";
                    await fkOn.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    /* connection closing */
                }
            }

            if (closeWhenDone)
                await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A row that EF could not find (lowercase on disk, uppercase EF parameter) was re-inserted in
    /// UPPERCASE, so the same key exists twice ("twins") and lowercasing hits a UNIQUE index and
    /// is skipped forever. The UPPERCASE twin is always the newer write (pull / local edit after
    /// the last normalize), so drop the stale lowercase twin; the column pass then lowercases it.
    /// </summary>
    private static async Task DeleteLowercaseTwinsAsync(
        System.Data.Common.DbConnection connection, string table, CancellationToken ct)
    {
        static string Q(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        static string Norm(string alias, string column) =>
            $"(CASE WHEN typeof({alias}.{column}) = 'text' AND length({alias}.{column}) = 36 " +
            $"AND substr({alias}.{column}, 9, 1) = '-' THEN lower({alias}.{column}) ELSE {alias}.{column} END)";

        try
        {
            var uniqueIndexes = new List<string>();
            await using (var list = connection.CreateCommand())
            {
                list.CommandText = $"PRAGMA index_list({Q(table)});";
                await using var reader = await list.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    // seq, name, unique, origin, partial
                    if (Convert.ToInt64(reader.GetValue(2), System.Globalization.CultureInfo.InvariantCulture) == 1)
                        uniqueIndexes.Add(reader.GetString(1));
                }
            }

            foreach (var index in uniqueIndexes)
            {
                var columns = new List<string>();
                await using (var info = connection.CreateCommand())
                {
                    info.CommandText = $"PRAGMA index_info({Q(index)});";
                    await using var reader = await info.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        if (!reader.IsDBNull(2))
                            columns.Add(reader.GetString(2));
                    }
                }

                if (columns.Count == 0)
                    continue;

                var quoted = columns.Select(Q).ToList();
                var match = string.Join(" AND ", quoted.Select(c => $"keep.{c} = {Norm("up", c)}"));
                var needsLower = string.Join(" OR ", quoted.Select(c => $"up.{c} != {Norm("up", c)}"));
                await using var delete = connection.CreateCommand();
                delete.CommandText = $"""
                    DELETE FROM {Q(table)}
                    WHERE rowid IN (
                        SELECT keep.rowid
                        FROM {Q(table)} AS up
                        JOIN {Q(table)} AS keep ON keep.rowid != up.rowid AND {match}
                        WHERE {needsLower});
                    """;
                try
                {
                    await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    /* WITHOUT ROWID / virtual table — leave as is */
                }
            }
        }
        catch
        {
            /* never block startup / sync on repair */
        }
    }

    /// <summary>
    /// Ensure a SyncCursor row exists for every league that already has local data,
    /// so PreferLocal / offline modules do not hammer a dead server. Does not mark
    /// IsBootstrapped — full snapshot still runs when sync reaches the server.
    /// </summary>
    private static async Task EnsureLocalSyncCursorsAsync(JplDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "SyncCursors" (
                "LeagueId" TEXT NOT NULL CONSTRAINT "PK_SyncCursors" PRIMARY KEY,
                "LastSequence" INTEGER NOT NULL DEFAULT 0,
                "IsBootstrapped" INTEGER NOT NULL DEFAULT 0,
                "LastSuccessfulSyncUtc" TEXT NULL
            );
            """, ct).ConfigureAwait(false);

        // Seed cursor rows from known league-scoped tables (ignore missing tables).
        foreach (var source in new[] { "Teams", "Players", "Matches", "NetSessions", "Tournaments", "Series" })
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync($"""
                    INSERT OR IGNORE INTO "SyncCursors" ("LeagueId", "LastSequence", "IsBootstrapped", "LastSuccessfulSyncUtc")
                    SELECT DISTINCT lower("LeagueId"), 0, 0, NULL
                    FROM "{source}"
                    WHERE "LeagueId" IS NOT NULL AND length("LeagueId") = 36;
                    """, ct).ConfigureAwait(false);
            }
            catch
            {
                /* table may not exist */
            }
        }
    }

    /// <summary>Adds a column; treats "duplicate column" as success so repair is always safe.</summary>
    private static async Task EnsureColumnOrIgnoreAsync(
        JplDbContext db, string table, string column, string alterSql, CancellationToken ct)
    {
        try
        {
            await EnsureColumnAsync(db, table, column, alterSql, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)
            || ex.InnerException?.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase) == true)
        {
            /* already present */
        }
    }

    /// <summary>Unconditional ALTER — ignores duplicate / missing-table failures.</summary>
    private static async Task TryAddColumnAsync(JplDbContext db, string alterSql, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(alterSql, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
            || ex.InnerException?.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase) == true)
        {
            /* already present or table not created yet */
        }
    }

    private static async Task EnsureLeagueScopedColumnsAsync(JplDbContext db, CancellationToken ct)
    {
        const string defaultLeague = "b1000001-0000-4000-8000-000000000001";
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = connection.State != System.Data.ConnectionState.Open;
        if (closeWhenDone)
            await connection.OpenAsync(ct).ConfigureAwait(false);

        try
        {
            await using (var cursorTable = connection.CreateCommand())
            {
                cursorTable.CommandText = """
                    CREATE TABLE IF NOT EXISTS "SyncCursors" (
                        "LeagueId" TEXT NOT NULL CONSTRAINT "PK_SyncCursors" PRIMARY KEY,
                        "LastSequence" INTEGER NOT NULL DEFAULT 0,
                        "IsBootstrapped" INTEGER NOT NULL DEFAULT 0,
                        "LastSuccessfulSyncUtc" TEXT NULL
                    );
                    """;
                await cursorTable.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            foreach (var table in LeagueScopedTables)
            {
                await using (var exists = connection.CreateCommand())
                {
                    exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name;";
                    var parameter = exists.CreateParameter();
                    parameter.ParameterName = "$name";
                    parameter.Value = table;
                    exists.Parameters.Add(parameter);
                    if (await exists.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
                        continue;
                }

                var hasLeagueId = false;
                await using (var columns = connection.CreateCommand())
                {
                    columns.CommandText = $"PRAGMA table_info(\"{table}\");";
                    await using var reader = await columns.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        if (string.Equals(reader.GetString(1), "LeagueId", StringComparison.OrdinalIgnoreCase))
                        {
                            hasLeagueId = true;
                            break;
                        }
                    }
                }

                if (!hasLeagueId)
                {
                    await using var alter = connection.CreateCommand();
                    alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"LeagueId\" TEXT NOT NULL DEFAULT '{defaultLeague}';";
                    await alter.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using var backfill = connection.CreateCommand();
                backfill.CommandText = $"UPDATE \"{table}\" SET \"LeagueId\" = '{defaultLeague}' WHERE \"LeagueId\" IS NULL OR \"LeagueId\" = '' OR \"LeagueId\" = '00000000-0000-0000-0000-000000000000';";
                await backfill.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync().ConfigureAwait(false);
        }
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
        await EnsureColumnAsync(db, "Deliveries", "IsDroppedCatch",
            """ALTER TABLE "Deliveries" ADD COLUMN "IsDroppedCatch" INTEGER NOT NULL DEFAULT 0;""", ct).ConfigureAwait(false);
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

        await DropLegacyVenueBookingVenueNameAsync(db, ct).ConfigureAwait(false);

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
                "Amount" TEXT NOT NULL DEFAULT '0',
                CONSTRAINT "PK_VenueBookingParticipants" PRIMARY KEY ("VenueBookingId", "PlayerId"),
                CONSTRAINT "FK_VenueBookingParticipants_VenueBookings_VenueBookingId" FOREIGN KEY ("VenueBookingId") REFERENCES "VenueBookings" ("Id") ON DELETE CASCADE
            );
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_VenueBookingParticipants_PlayerId" ON "VenueBookingParticipants" ("PlayerId");""", ct)
            .ConfigureAwait(false);
        await EnsureColumnAsync(db, "VenueBookingParticipants", "Amount",
            """ALTER TABLE "VenueBookingParticipants" ADD COLUMN "Amount" TEXT NOT NULL DEFAULT '0';""", ct)
            .ConfigureAwait(false);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Expenses" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Expenses" PRIMARY KEY,
                "CreatedByUserId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "PurchasedByPlayerId" TEXT NOT NULL,
                "Amount" TEXT NOT NULL,
                "Version" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "IsDeleted" INTEGER NOT NULL
            );
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "ExpenseContributions" (
                "ExpenseId" TEXT NOT NULL,
                "PlayerId" TEXT NOT NULL,
                "Amount" TEXT NOT NULL,
                "Status" TEXT NOT NULL DEFAULT 'Pending',
                CONSTRAINT "PK_ExpenseContributions" PRIMARY KEY ("ExpenseId", "PlayerId"),
                CONSTRAINT "FK_ExpenseContributions_Expenses_ExpenseId" FOREIGN KEY ("ExpenseId") REFERENCES "Expenses" ("Id") ON DELETE CASCADE
            );
            """, ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "ExpenseContributions", "Status",
            """ALTER TABLE "ExpenseContributions" ADD COLUMN "Status" TEXT NOT NULL DEFAULT 'Paid';""", ct)
            .ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Expenses_CreatedAtUtc" ON "Expenses" ("CreatedAtUtc");""", ct)
            .ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Expenses_CreatedByUserId" ON "Expenses" ("CreatedByUserId");""", ct)
            .ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Expenses_PurchasedByPlayerId" ON "Expenses" ("PurchasedByPlayerId");""", ct)
            .ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_ExpenseContributions_PlayerId" ON "ExpenseContributions" ("PlayerId");""", ct)
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
        await EnsureLocalStatsCacheTableAsync(db, ct).ConfigureAwait(false);
    }

    private static async Task EnsureLocalStatsCacheTableAsync(JplDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "LocalStatsCache" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_LocalStatsCache" PRIMARY KEY,
                "LeagueId" TEXT NOT NULL,
                "CacheKey" TEXT NOT NULL,
                "PayloadJson" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_LocalStatsCache_LeagueId_CacheKey" ON "LocalStatsCache" ("LeagueId", "CacheKey");""",
            ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_LocalStatsCache_LeagueId_UpdatedAtUtc" ON "LocalStatsCache" ("LeagueId", "UpdatedAtUtc");""",
            ct).ConfigureAwait(false);
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
        await EnsureColumnAsync(db, "Tournaments", "RoundRobinType",
            """ALTER TABLE "Tournaments" ADD COLUMN "RoundRobinType" INTEGER NOT NULL DEFAULT 0;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Tournaments", "MatchesPerTeam",
            """ALTER TABLE "Tournaments" ADD COLUMN "MatchesPerTeam" INTEGER NOT NULL DEFAULT 0;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Tournaments", "CustomPairsJson",
            """ALTER TABLE "Tournaments" ADD COLUMN "CustomPairsJson" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Tournaments", "KnockoutFormat",
            """ALTER TABLE "Tournaments" ADD COLUMN "KnockoutFormat" INTEGER NOT NULL DEFAULT 2;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Tournaments", "PlayerOfTheTournamentPlayerId",
            """ALTER TABLE "Tournaments" ADD COLUMN "PlayerOfTheTournamentPlayerId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Tournaments", "BestBatsmanPlayerId",
            """ALTER TABLE "Tournaments" ADD COLUMN "BestBatsmanPlayerId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Tournaments", "BestBowlerPlayerId",
            """ALTER TABLE "Tournaments" ADD COLUMN "BestBowlerPlayerId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Tournaments", "BestFielderPlayerId",
            """ALTER TABLE "Tournaments" ADD COLUMN "BestFielderPlayerId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Series", "PlayerOfTheSeriesPlayerId",
            """ALTER TABLE "Series" ADD COLUMN "PlayerOfTheSeriesPlayerId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Series", "BestBatsmanPlayerId",
            """ALTER TABLE "Series" ADD COLUMN "BestBatsmanPlayerId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Series", "BestBowlerPlayerId",
            """ALTER TABLE "Series" ADD COLUMN "BestBowlerPlayerId" TEXT NULL;""", ct).ConfigureAwait(false);
        await EnsureColumnAsync(db, "Series", "BestFielderPlayerId",
            """ALTER TABLE "Series" ADD COLUMN "BestFielderPlayerId" TEXT NULL;""", ct).ConfigureAwait(false);
        try
        {
            // Backfill legacy group and qualification settings onto supported knockout formats.
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "Tournaments" SET "KnockoutFormat" = 4 WHERE "GroupCount" = 1 AND "QualifyPerGroup" = 1 AND "KnockoutFormat" = 2;
                UPDATE "Tournaments" SET "KnockoutFormat" = 3 WHERE "GroupCount" = 1 AND "QualifyPerGroup" = 2 AND "KnockoutFormat" = 2;
                UPDATE "Tournaments" SET "KnockoutFormat" = 3 WHERE "GroupCount" = 2 AND "QualifyPerGroup" = 1 AND "KnockoutFormat" = 2;
                """, ct).ConfigureAwait(false);
        }
        catch { /* column / dialect */ }
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

        await EnsureColumnAsync(db, "NetDeliveries", "IsDroppedCatch",
            """ALTER TABLE "NetDeliveries" ADD COLUMN "IsDroppedCatch" INTEGER NOT NULL DEFAULT 0;""", ct)
            .ConfigureAwait(false);
    }

    private static async Task EnsureSyncDeferredPullsAsync(JplDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "SyncDeferredPulls" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SyncDeferredPulls" PRIMARY KEY,
                "LeagueId" TEXT NOT NULL,
                "Sequence" INTEGER NOT NULL,
                "EntityType" TEXT NOT NULL,
                "EntityKeyJson" TEXT NOT NULL,
                "Operation" TEXT NOT NULL,
                "PayloadJson" TEXT NOT NULL,
                "DeferredAtUtc" TEXT NOT NULL
            );
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_SyncDeferredPulls_LeagueId_Sequence"
            ON "SyncDeferredPulls" ("LeagueId", "Sequence");
            """, ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS "IX_SyncDeferredPulls_LeagueId_EntityType_EntityKeyJson"
            ON "SyncDeferredPulls" ("LeagueId", "EntityType", "EntityKeyJson");
            """, ct).ConfigureAwait(false);
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

        var exists = false;
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        // Close the reader before ALTER — SQLite rejects a second command on an open reader.
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
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

        var exists = false;
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
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
