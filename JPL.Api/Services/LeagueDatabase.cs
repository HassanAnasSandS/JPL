using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Data;
using JPL.Api.Models;

namespace JPL.Api.Services;

public interface IApiDbFactory
{
    /// <summary>Cric360 master: users + league list only.</summary>
    MasterDbContext CreateMaster();

    /// <summary>Players/teams/matches/balls for one league (dedicated database).</summary>
    ApiDbContext CreateForLeague(Guid leagueId);

    /// <summary>Current request league data. Requires a selected league.</summary>
    ApiDbContext CreateForRequest();
}

/// <summary>
/// Cric360 master database (users + leagues) plus one SQL catalog / SQLite file per league.
/// Ball/match data never stays in the shared master tables.
/// </summary>
public sealed class LeagueDatabaseManager
{
    public const int NameMaxLength = 128;
    public const string MasterCatalogName = "Cric360";
    private static readonly Regex SafeName = new("^[A-Za-z][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled);

    private readonly IWebHostEnvironment _env;
    private readonly ConcurrentDictionary<Guid, string> _databaseNames = new();
    private readonly bool _sqlServer;
    private readonly string _defaultConnection;

    public LeagueDatabaseManager(IConfiguration config, IWebHostEnvironment env)
    {
        _env = env;
        _sqlServer = string.Equals(config["DatabaseProvider"], "SqlServer", StringComparison.OrdinalIgnoreCase);
        if (_sqlServer)
        {
            var raw = config.GetConnectionString("Default")
                      ?? throw new InvalidOperationException("ConnectionStrings:Default is required for SQL Server.");
            var builder = new SqlConnectionStringBuilder(raw);
            builder.InitialCatalog = MasterCatalogName;
            _defaultConnection = builder.ConnectionString;
        }
        else
        {
            _defaultConnection = BuildSqliteConnection(config["SqlitePath"] ?? "cric360-master.db");
        }
    }

    public static string NameFor(Guid leagueId, string? leagueName = null)
        => CatalogName(MasterCatalogName + "_", leagueId, leagueName);

    private static string LegacyLeagueCatalogName(Guid leagueId, string? leagueName)
        => CatalogName("League_", leagueId, leagueName);

    private static string CatalogName(string prefix, Guid leagueId, string? leagueName)
    {
        var slug = new string((leagueName ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        if (slug.Length == 0)
            slug = leagueId.ToString("N")[..12];
        if (slug.Equals(MasterCatalogName, StringComparison.OrdinalIgnoreCase))
            slug = "Named" + slug;
        var name = prefix + slug;
        return name.Length <= NameMaxLength ? name : name[..NameMaxLength];
    }

    public void Remember(Guid leagueId, string? databaseName)
    {
        if (leagueId == Guid.Empty || string.IsNullOrWhiteSpace(databaseName))
            return;
        _databaseNames[leagueId] = databaseName.Trim();
    }

    public void LoadFrom(IEnumerable<League> leagues)
    {
        foreach (var league in leagues)
            Remember(league.Id, league.DatabaseName);
    }

    public MasterDbContext CreateMaster()
    {
        var builder = new DbContextOptionsBuilder<MasterDbContext>();
        if (_sqlServer)
            builder.UseSqlServer(_defaultConnection);
        else
            builder.UseSqlite(_defaultConnection);
        return new MasterDbContext(builder.Options);
    }

    public ApiDbContext CreateForLeague(Guid leagueId)
        => CreateContext(ResolveConnection(leagueId), leagueId);

    public ApiDbContext CreateForRequest(IApiLeagueScope scope)
        => CreateContext(ResolveConnection(scope.CurrentLeagueId), scope);

    public string ResolveConnection(Guid leagueId)
    {
        if (leagueId == Guid.Empty)
            return _defaultConnection;
        if (_databaseNames.TryGetValue(leagueId, out var name) && !string.IsNullOrWhiteSpace(name))
            return ToConnection(name);
        return _defaultConnection;
    }

    /// <summary>Creates the Cric360 master catalog if the API account can CREATE DATABASE.</summary>
    public async Task EnsureMasterCatalogAsync(CancellationToken ct = default)
    {
        if (_sqlServer)
            await CreateSqlServerDatabaseAsync(MasterCatalogName, ct).ConfigureAwait(false);
    }

    /// <summary>Every league gets its own database; existing ball data is moved out of master.</summary>
    public async Task EnsureAllLeagueDatabasesAsync(MasterDbContext master, CancellationToken ct = default)
    {
        var leagues = await master.Leagues.ToListAsync(ct).ConfigureAwait(false);
        foreach (var league in leagues)
        {
            if (!league.IsActive)
                continue;

            league.DatabaseName = await EnsureLeagueCatalogAsync(league, ct).ConfigureAwait(false);
        }

        await master.SaveChangesAsync(ct).ConfigureAwait(false);
        LoadFrom(leagues);
    }

    /// <summary>Creates the league database/file and schema. No SSMS step.</summary>
    public Task<string> ProvisionAsync(Guid leagueId, CancellationToken ct = default)
        => ProvisionAsync(leagueId, leagueName: null, ct);

    private async Task<string> EnsureLeagueCatalogAsync(League league, CancellationToken ct)
    {
        var target = NameFor(league.Id, league.Name);
        if (!SafeName.IsMatch(target))
            throw new InvalidOperationException("Generated database name is not valid.");

        if (_sqlServer)
        {
            var current = league.DatabaseName?.Trim();
            var legacy = LegacyLeagueCatalogName(league.Id, league.Name);
            if (await CatalogExistsAsync(target, ct).ConfigureAwait(false))
            {
                // already the right catalog
            }
            else if (!string.IsNullOrWhiteSpace(current)
                     && !current.Equals(target, StringComparison.OrdinalIgnoreCase)
                     && !current.Equals(MasterCatalogName, StringComparison.OrdinalIgnoreCase)
                     && await CatalogExistsAsync(current, ct).ConfigureAwait(false))
            {
                await RenameSqlCatalogAsync(current, target, ct).ConfigureAwait(false);
            }
            else if (!legacy.Equals(target, StringComparison.OrdinalIgnoreCase)
                     && await CatalogExistsAsync(legacy, ct).ConfigureAwait(false))
            {
                await RenameSqlCatalogAsync(legacy, target, ct).ConfigureAwait(false);
            }
            else
            {
                await CreateSqlServerDatabaseAsync(target, ct).ConfigureAwait(false);
            }
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SqlitePath(target))!);
        }

        Remember(league.Id, target);
        await EnsureLeagueSchemaAsync(league.Id, target, ct).ConfigureAwait(false);
        return target;
    }

    public async Task<string> ProvisionAsync(Guid leagueId, string? leagueName, CancellationToken ct = default)
    {
        if (leagueId == Guid.Empty)
            throw new ArgumentException("League id is required.", nameof(leagueId));

        var dbName = NameFor(leagueId, leagueName);
        if (!SafeName.IsMatch(dbName))
            throw new InvalidOperationException("Generated database name is not valid.");

        if (_sqlServer)
            await CreateSqlServerDatabaseAsync(dbName, ct).ConfigureAwait(false);
        else
            Directory.CreateDirectory(Path.GetDirectoryName(SqlitePath(dbName))!);

        await EnsureLeagueSchemaAsync(leagueId, dbName, ct).ConfigureAwait(false);

        Remember(leagueId, dbName);
        return dbName;
    }

    private async Task EnsureLeagueSchemaAsync(Guid leagueId, string dbName, CancellationToken ct)
    {
        var connection = ToConnection(dbName);
        await using var db = CreateContext(connection, leagueId);
        await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
        await EnsureMatchSquadColumnAsync(dbName, ct).ConfigureAwait(false);
        await EnsureTeamPlayerPlayingColumnAsync(dbName, ct).ConfigureAwait(false);
        await EnsureOversModeColumnsAsync(dbName, ct).ConfigureAwait(false);
        await EnsureTournamentRoundRobinColumnsAsync(dbName, ct).ConfigureAwait(false);
        await EnsureEventAwardColumnsAsync(dbName, ct).ConfigureAwait(false);
        await BackfillEventAwardsAsync(leagueId, dbName, ct).ConfigureAwait(false);
        await EnsureLiveStreamUrlColumnAsync(dbName, ct).ConfigureAwait(false);
        await EnsureIsLiveTelecastColumnAsync(dbName, ct).ConfigureAwait(false);
        await EnsureScoringOwnerColumnsAsync(dbName, ct).ConfigureAwait(false);
        await EnsureShotZoneColumnAsync(dbName, ct).ConfigureAwait(false);
        await EnsureNetSessionTablesAsync(dbName, ct).ConfigureAwait(false);
        await EnsureDroppedCatchColumnAsync(dbName, ct).ConfigureAwait(false);
        await EnsureSeriesTablesAsync(dbName, ct).ConfigureAwait(false);
        await EnsureFormatsAsync(dbName, ct).ConfigureAwait(false);
        await EnsurePlayerImagePathColumnAsync(dbName, ct).ConfigureAwait(false);
        await EnsurePlayerIdentityColumnsAsync(dbName, ct).ConfigureAwait(false);
        await EnsureTeamImagePathColumnAsync(dbName, ct).ConfigureAwait(false);
        await EnsureVenueBookingParticipantAmountColumnAsync(dbName, ct).ConfigureAwait(false);
        await DropSportsLeagueIdColumnsAsync(dbName, ct).ConfigureAwait(false);
        await DropLeagueDirectoryTablesAsync(dbName, ct).ConfigureAwait(false);
    }

    private async Task EnsureShotZoneColumnAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF COL_LENGTH(N'dbo.Deliveries', N'ShotZone') IS NULL
                    ALTER TABLE dbo.Deliveries ADD ShotZone tinyint NOT NULL CONSTRAINT DF_Deliveries_ShotZone DEFAULT 0;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Deliveries" ADD COLUMN "ShotZone" INTEGER NOT NULL DEFAULT 0;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* already exists */
        }
    }

    private async Task EnsureDroppedCatchColumnAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF OBJECT_ID(N'dbo.Deliveries', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Deliveries', N'IsDroppedCatch') IS NULL
                    ALTER TABLE dbo.Deliveries ADD IsDroppedCatch bit NOT NULL
                        CONSTRAINT DF_Deliveries_IsDroppedCatch DEFAULT 0;
                IF OBJECT_ID(N'dbo.NetDeliveries', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.NetDeliveries', N'IsDroppedCatch') IS NULL
                    ALTER TABLE dbo.NetDeliveries ADD IsDroppedCatch bit NOT NULL
                        CONSTRAINT DF_NetDeliveries_IsDroppedCatch DEFAULT 0;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                        """ALTER TABLE "Deliveries" ADD COLUMN "IsDroppedCatch" INTEGER NOT NULL DEFAULT 0;""", ct)
                    .ConfigureAwait(false);
            }
            catch { /* already exists */ }

            try
            {
                await db.Database.ExecuteSqlRawAsync(
                        """ALTER TABLE "NetDeliveries" ADD COLUMN "IsDroppedCatch" INTEGER NOT NULL DEFAULT 0;""", ct)
                    .ConfigureAwait(false);
            }
            catch { /* already exists */ }
        }
        catch
        {
            /* table missing */
        }
    }

    private async Task EnsurePlayerImagePathColumnAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF COL_LENGTH(N'dbo.Players', N'ImagePath') IS NULL
                    ALTER TABLE dbo.Players ADD ImagePath nvarchar(80) NULL;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Players" ADD COLUMN "ImagePath" TEXT NULL;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* already exists */
        }
    }

    private async Task EnsurePlayerIdentityColumnsAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF COL_LENGTH(N'dbo.Players', N'NickName') IS NULL
                    ALTER TABLE dbo.Players ADD NickName nvarchar(40) NULL;
                IF COL_LENGTH(N'dbo.Players', N'PlayingRole') IS NULL
                    ALTER TABLE dbo.Players ADD PlayingRole nvarchar(40) NULL;
                IF COL_LENGTH(N'dbo.Players', N'BattingStyle') IS NULL
                    ALTER TABLE dbo.Players ADD BattingStyle nvarchar(40) NULL;
                IF COL_LENGTH(N'dbo.Players', N'BowlingStyle') IS NULL
                    ALTER TABLE dbo.Players ADD BowlingStyle nvarchar(60) NULL;
                IF COL_LENGTH(N'dbo.Players', N'Bio') IS NULL
                    ALTER TABLE dbo.Players ADD Bio nvarchar(500) NULL;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        foreach (var (_, sql) in new[]
        {
            ("NickName", """ALTER TABLE "Players" ADD COLUMN "NickName" TEXT NULL;"""),
            ("PlayingRole", """ALTER TABLE "Players" ADD COLUMN "PlayingRole" TEXT NULL;"""),
            ("BattingStyle", """ALTER TABLE "Players" ADD COLUMN "BattingStyle" TEXT NULL;"""),
            ("BowlingStyle", """ALTER TABLE "Players" ADD COLUMN "BowlingStyle" TEXT NULL;"""),
            ("Bio", """ALTER TABLE "Players" ADD COLUMN "Bio" TEXT NULL;""")
        })
        {
            try
            {
                await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
                await db.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false);
            }
            catch { /* already exists */ }
        }
    }

    private async Task EnsureTeamImagePathColumnAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF COL_LENGTH(N'dbo.Teams', N'ImagePath') IS NULL
                    ALTER TABLE dbo.Teams ADD ImagePath nvarchar(80) NULL;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Teams" ADD COLUMN "ImagePath" TEXT NULL;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* already exists */
        }
    }

    private async Task EnsureVenueBookingParticipantAmountColumnAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF OBJECT_ID(N'dbo.VenueBookingParticipants', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.VenueBookingParticipants', N'Amount') IS NULL
                    ALTER TABLE dbo.VenueBookingParticipants ADD Amount decimal(18,2) NOT NULL
                        CONSTRAINT DF_VenueBookingParticipants_Amount DEFAULT 0;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "VenueBookingParticipants" ADD COLUMN "Amount" TEXT NOT NULL DEFAULT '0';""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* already exists */
        }
    }

    private async Task EnsureTeamPlayerPlayingColumnAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF COL_LENGTH(N'dbo.TeamPlayers', N'IsPlaying') IS NULL
                    ALTER TABLE dbo.TeamPlayers ADD IsPlaying bit NOT NULL CONSTRAINT DF_TeamPlayers_IsPlaying DEFAULT 1;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "TeamPlayers" ADD COLUMN "IsPlaying" INTEGER NOT NULL DEFAULT 1;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* already exists */
        }
    }

    private async Task EnsureMatchSquadColumnAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF COL_LENGTH(N'dbo.Matches', N'SquadRosterJson') IS NULL
                    ALTER TABLE dbo.Matches ADD SquadRosterJson nvarchar(max) NULL;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Matches" ADD COLUMN "SquadRosterJson" TEXT NULL;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* already exists */
        }
    }

    private async Task EnsureIsLiveTelecastColumnAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF COL_LENGTH(N'dbo.Matches', N'IsLiveTelecast') IS NULL
                    ALTER TABLE dbo.Matches ADD IsLiveTelecast bit NOT NULL
                        CONSTRAINT DF_Matches_IsLiveTelecast DEFAULT (1);
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Matches" ADD COLUMN "IsLiveTelecast" INTEGER NOT NULL DEFAULT 1;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* already exists */
        }
    }

    private async Task EnsureLiveStreamUrlColumnAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF COL_LENGTH(N'dbo.Matches', N'LiveStreamUrl') IS NULL
                    ALTER TABLE dbo.Matches ADD LiveStreamUrl nvarchar(1000) NULL;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Matches" ADD COLUMN "LiveStreamUrl" TEXT NULL;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* already exists */
        }
    }

    private async Task EnsureScoringOwnerColumnsAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF COL_LENGTH(N'dbo.Matches', N'StartedByUserId') IS NULL
                    ALTER TABLE dbo.Matches ADD StartedByUserId uniqueidentifier NULL;
                IF COL_LENGTH(N'dbo.Matches', N'StartedOnDeviceId') IS NULL
                    ALTER TABLE dbo.Matches ADD StartedOnDeviceId uniqueidentifier NULL;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Matches" ADD COLUMN "StartedByUserId" TEXT NULL;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* already exists */
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Matches" ADD COLUMN "StartedOnDeviceId" TEXT NULL;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* already exists */
        }
    }

    /// <summary>EnsureCreated does not add columns to existing league catalogs.</summary>
    private async Task EnsureOversModeColumnsAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF COL_LENGTH(N'dbo.Matches', N'OversMode') IS NULL
                    ALTER TABLE dbo.Matches ADD OversMode tinyint NOT NULL CONSTRAINT DF_Matches_OversMode DEFAULT 0;
                IF OBJECT_ID(N'dbo.Tournaments', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Tournaments', N'OversMode') IS NULL
                    ALTER TABLE dbo.Tournaments ADD OversMode tinyint NOT NULL CONSTRAINT DF_Tournaments_OversMode DEFAULT 0;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Matches" ADD COLUMN "OversMode" INTEGER NOT NULL DEFAULT 0;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* already exists */
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Tournaments" ADD COLUMN "OversMode" INTEGER NOT NULL DEFAULT 0;""", ct)
                .ConfigureAwait(false);
        }
        catch
        {
            /* already exists */
        }
    }

    private async Task EnsureTournamentRoundRobinColumnsAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            // ALTER + UPDATE on a new column cannot share one SQL Server batch —
            // the parser rejects KnockoutFormat until the ADD batch finishes.
            cmd.CommandText = """
                IF OBJECT_ID(N'dbo.Tournaments', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Tournaments', N'RoundRobinType') IS NULL
                    ALTER TABLE dbo.Tournaments ADD RoundRobinType tinyint NOT NULL CONSTRAINT DF_Tournaments_RoundRobinType DEFAULT 0;
                IF OBJECT_ID(N'dbo.Tournaments', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Tournaments', N'MatchesPerTeam') IS NULL
                    ALTER TABLE dbo.Tournaments ADD MatchesPerTeam int NOT NULL CONSTRAINT DF_Tournaments_MatchesPerTeam DEFAULT 0;
                IF OBJECT_ID(N'dbo.Tournaments', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Tournaments', N'CustomPairsJson') IS NULL
                    ALTER TABLE dbo.Tournaments ADD CustomPairsJson nvarchar(max) NULL;
                IF OBJECT_ID(N'dbo.Tournaments', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Tournaments', N'KnockoutFormat') IS NULL
                    ALTER TABLE dbo.Tournaments ADD KnockoutFormat tinyint NOT NULL CONSTRAINT DF_Tournaments_KnockoutFormat DEFAULT 2;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            cmd.CommandText = """
                IF OBJECT_ID(N'dbo.Tournaments', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Tournaments', N'KnockoutFormat') IS NOT NULL
                BEGIN
                    UPDATE dbo.Tournaments SET KnockoutFormat = 4 WHERE GroupCount = 1 AND QualifyPerGroup = 1 AND KnockoutFormat = 2;
                    UPDATE dbo.Tournaments SET KnockoutFormat = 3 WHERE GroupCount = 1 AND QualifyPerGroup = 2 AND KnockoutFormat = 2;
                    UPDATE dbo.Tournaments SET KnockoutFormat = 3 WHERE GroupCount = 2 AND QualifyPerGroup = 1 AND KnockoutFormat = 2;
                END
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        foreach (var sql in new[]
                 {
                     """ALTER TABLE "Tournaments" ADD COLUMN "RoundRobinType" INTEGER NOT NULL DEFAULT 0;""",
                     """ALTER TABLE "Tournaments" ADD COLUMN "MatchesPerTeam" INTEGER NOT NULL DEFAULT 0;""",
                     """ALTER TABLE "Tournaments" ADD COLUMN "CustomPairsJson" TEXT NULL;""",
                     """ALTER TABLE "Tournaments" ADD COLUMN "KnockoutFormat" INTEGER NOT NULL DEFAULT 2;"""
                 })
        {
            try
            {
                await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
                await db.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false);
            }
            catch
            {
                /* already exists */
            }
        }
    }

    private async Task EnsureEventAwardColumnsAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF OBJECT_ID(N'dbo.Tournaments', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Tournaments', N'PlayerOfTheTournamentPlayerId') IS NULL
                    ALTER TABLE dbo.Tournaments ADD PlayerOfTheTournamentPlayerId uniqueidentifier NULL;
                IF OBJECT_ID(N'dbo.Tournaments', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Tournaments', N'BestBatsmanPlayerId') IS NULL
                    ALTER TABLE dbo.Tournaments ADD BestBatsmanPlayerId uniqueidentifier NULL;
                IF OBJECT_ID(N'dbo.Tournaments', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Tournaments', N'BestBowlerPlayerId') IS NULL
                    ALTER TABLE dbo.Tournaments ADD BestBowlerPlayerId uniqueidentifier NULL;
                IF OBJECT_ID(N'dbo.Tournaments', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Tournaments', N'BestFielderPlayerId') IS NULL
                    ALTER TABLE dbo.Tournaments ADD BestFielderPlayerId uniqueidentifier NULL;
                IF OBJECT_ID(N'dbo.Series', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Series', N'PlayerOfTheSeriesPlayerId') IS NULL
                    ALTER TABLE dbo.Series ADD PlayerOfTheSeriesPlayerId uniqueidentifier NULL;
                IF OBJECT_ID(N'dbo.Series', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Series', N'BestBatsmanPlayerId') IS NULL
                    ALTER TABLE dbo.Series ADD BestBatsmanPlayerId uniqueidentifier NULL;
                IF OBJECT_ID(N'dbo.Series', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Series', N'BestBowlerPlayerId') IS NULL
                    ALTER TABLE dbo.Series ADD BestBowlerPlayerId uniqueidentifier NULL;
                IF OBJECT_ID(N'dbo.Series', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Series', N'BestFielderPlayerId') IS NULL
                    ALTER TABLE dbo.Series ADD BestFielderPlayerId uniqueidentifier NULL;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        foreach (var sql in new[]
                 {
                     """ALTER TABLE "Tournaments" ADD COLUMN "PlayerOfTheTournamentPlayerId" TEXT NULL;""",
                     """ALTER TABLE "Tournaments" ADD COLUMN "BestBatsmanPlayerId" TEXT NULL;""",
                     """ALTER TABLE "Tournaments" ADD COLUMN "BestBowlerPlayerId" TEXT NULL;""",
                     """ALTER TABLE "Tournaments" ADD COLUMN "BestFielderPlayerId" TEXT NULL;""",
                     """ALTER TABLE "Series" ADD COLUMN "PlayerOfTheSeriesPlayerId" TEXT NULL;""",
                     """ALTER TABLE "Series" ADD COLUMN "BestBatsmanPlayerId" TEXT NULL;""",
                     """ALTER TABLE "Series" ADD COLUMN "BestBowlerPlayerId" TEXT NULL;""",
                     """ALTER TABLE "Series" ADD COLUMN "BestFielderPlayerId" TEXT NULL;"""
                 })
        {
            try
            {
                await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
                await db.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false);
            }
            catch
            {
                /* already exists */
            }
        }
    }

    /// <summary>
    /// Fill awards on already-completed tournaments/series that pre-date the award columns.
    /// Safe to re-run: skips rows that already have Player of the event set.
    /// </summary>
    private async Task BackfillEventAwardsAsync(Guid leagueId, string dbName, CancellationToken ct)
    {
        try
        {
            await using var db = CreateContext(ToConnection(dbName), leagueId);
            var tours = await db.Tournaments
                .Include(t => t.Fixtures)
                .Where(t => !t.IsDeleted
                            && t.Status == TournamentStatus.Completed
                            && t.PlayerOfTheTournamentPlayerId == null)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            foreach (var t in tours)
            {
                var (_, changed) = await TournamentEngine.ResolveProgressAsync(db, t, ct).ConfigureAwait(false);
                if (changed)
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            var seriesList = await db.Series
                .Include(s => s.Fixtures)
                .Where(s => !s.IsDeleted
                            && s.Status == SeriesStatus.Completed
                            && s.PlayerOfTheSeriesPlayerId == null)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            foreach (var s in seriesList)
            {
                var (_, changed) = await SeriesEngine.ResolveProgressAsync(db, s, ct).ConfigureAwait(false);
                if (changed)
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
        catch
        {
            /* best-effort — awards also fill on next Get via ResolveProgress */
        }
    }

    /// <summary>EnsureCreated does not add tables to existing league catalogs.</summary>
    private async Task EnsureNetSessionTablesAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF OBJECT_ID(N'dbo.NetSessions', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.NetSessions (
                        Id uniqueidentifier NOT NULL CONSTRAINT PK_NetSessions PRIMARY KEY,
                        Name nvarchar(160) NOT NULL,
                        SessionDateUtc datetimeoffset NOT NULL,
                        Status tinyint NOT NULL,
                        PlayerRosterJson nvarchar(max) NULL,
                        Version bigint NOT NULL,
                        CreatedAtUtc datetimeoffset NOT NULL,
                        UpdatedAtUtc datetimeoffset NOT NULL,
                        IsDeleted bit NOT NULL
                    );
                    CREATE INDEX IX_NetSessions_SessionDateUtc ON dbo.NetSessions (SessionDateUtc);
                END

                IF OBJECT_ID(N'dbo.NetDeliveries', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.NetDeliveries (
                        Id uniqueidentifier NOT NULL CONSTRAINT PK_NetDeliveries PRIMARY KEY,
                        SessionId uniqueidentifier NOT NULL,
                        Sequence int NOT NULL,
                        OverNumber int NOT NULL,
                        BallInOver int NOT NULL,
                        BatterId uniqueidentifier NOT NULL,
                        NonStrikerId uniqueidentifier NOT NULL,
                        BowlerId uniqueidentifier NOT NULL,
                        RunsOffBat int NOT NULL,
                        ExtraRuns int NOT NULL,
                        ExtraType tinyint NOT NULL,
                        IsWicket bit NOT NULL,
                        WicketType tinyint NOT NULL,
                        DismissedPlayerId uniqueidentifier NULL,
                        FielderId uniqueidentifier NULL,
                        IsDroppedCatch bit NOT NULL CONSTRAINT DF_NetDeliveries_IsDroppedCatch DEFAULT 0,
                        IsLegalDelivery bit NOT NULL,
                        CreatedAtUtc datetimeoffset NOT NULL,
                        Version bigint NOT NULL,
                        CONSTRAINT FK_NetDeliveries_NetSessions_SessionId
                            FOREIGN KEY (SessionId) REFERENCES dbo.NetSessions (Id) ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX IX_NetDeliveries_SessionId_Sequence ON dbo.NetDeliveries (SessionId, Sequence);
                    CREATE INDEX IX_NetDeliveries_BatterId_BowlerId ON dbo.NetDeliveries (BatterId, BowlerId);
                END
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "NetSessions" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_NetSessions" PRIMARY KEY,
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
                    "IsDroppedCatch" INTEGER NOT NULL DEFAULT 0,
                    "IsLegalDelivery" INTEGER NOT NULL,
                    "CreatedAtUtc" TEXT NOT NULL,
                    "Version" INTEGER NOT NULL,
                    CONSTRAINT "FK_NetDeliveries_NetSessions_SessionId" FOREIGN KEY ("SessionId") REFERENCES "NetSessions" ("Id") ON DELETE CASCADE
                );
                """, ct).ConfigureAwait(false);
        }
        catch
        {
            /* already exists */
        }
    }

    /// <summary>EnsureCreated does not add tables/columns to existing league catalogs.</summary>
    private async Task EnsureSeriesTablesAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF COL_LENGTH(N'dbo.Matches', N'SeriesId') IS NULL
                    ALTER TABLE dbo.Matches ADD SeriesId uniqueidentifier NULL;
                IF COL_LENGTH(N'dbo.Matches', N'SeriesFixtureId') IS NULL
                    ALTER TABLE dbo.Matches ADD SeriesFixtureId uniqueidentifier NULL;

                IF OBJECT_ID(N'dbo.Series', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Series (
                        Id uniqueidentifier NOT NULL CONSTRAINT PK_Series PRIMARY KEY,
                        Name nvarchar(160) NOT NULL,
                        TeamAId uniqueidentifier NOT NULL,
                        TeamBId uniqueidentifier NOT NULL,
                        MatchCount int NOT NULL,
                        VenueId uniqueidentifier NOT NULL,
                        Format tinyint NOT NULL,
                        OversLimit int NOT NULL,
                        OversMode tinyint NOT NULL CONSTRAINT DF_Series_OversMode DEFAULT 0,
                        LastBatsmanCanBatAsSingleWicket bit NOT NULL CONSTRAINT DF_Series_LastBat DEFAULT 0,
                        BowlerMaxOvers int NOT NULL CONSTRAINT DF_Series_BowlerMax DEFAULT 0,
                        Status tinyint NOT NULL,
                        WinnerTeamId uniqueidentifier NULL,
                        IsTied bit NOT NULL CONSTRAINT DF_Series_IsTied DEFAULT 0,
                        Version bigint NOT NULL,
                        CreatedAtUtc datetimeoffset NOT NULL,
                        UpdatedAtUtc datetimeoffset NOT NULL,
                        IsDeleted bit NOT NULL
                    );
                    CREATE INDEX IX_Series_CreatedAtUtc ON dbo.Series (CreatedAtUtc);
                END

                IF EXISTS (
                    SELECT 1 FROM sys.columns c
                    INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                    WHERE c.object_id = OBJECT_ID(N'dbo.Series')
                      AND c.name IN (N'CreatedAtUtc', N'UpdatedAtUtc')
                      AND t.name IN (N'datetime', N'datetime2'))
                BEGIN
                    DROP INDEX IF EXISTS IX_Series_CreatedAtUtc ON dbo.Series;
                    ALTER TABLE dbo.Series ALTER COLUMN CreatedAtUtc datetimeoffset NOT NULL;
                    ALTER TABLE dbo.Series ALTER COLUMN UpdatedAtUtc datetimeoffset NOT NULL;
                    CREATE INDEX IX_Series_CreatedAtUtc ON dbo.Series (CreatedAtUtc);
                END

                IF OBJECT_ID(N'dbo.SeriesFixtures', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.SeriesFixtures (
                        Id uniqueidentifier NOT NULL CONSTRAINT PK_SeriesFixtures PRIMARY KEY,
                        SeriesId uniqueidentifier NOT NULL,
                        Sequence int NOT NULL,
                        Label nvarchar(80) NOT NULL,
                        TeamAId uniqueidentifier NOT NULL,
                        TeamBId uniqueidentifier NOT NULL,
                        MatchId uniqueidentifier NULL,
                        CONSTRAINT FK_SeriesFixtures_Series_SeriesId
                            FOREIGN KEY (SeriesId) REFERENCES dbo.Series (Id) ON DELETE CASCADE
                    );
                    CREATE INDEX IX_SeriesFixtures_SeriesId_Sequence ON dbo.SeriesFixtures (SeriesId, Sequence);
                    CREATE INDEX IX_SeriesFixtures_MatchId ON dbo.SeriesFixtures (MatchId);
                END
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                        """ALTER TABLE "Matches" ADD COLUMN "SeriesId" TEXT NULL;""", ct)
                    .ConfigureAwait(false);
            }
            catch { /* exists */ }
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                        """ALTER TABLE "Matches" ADD COLUMN "SeriesFixtureId" TEXT NULL;""", ct)
                    .ConfigureAwait(false);
            }
            catch { /* exists */ }

            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "Series" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_Series" PRIMARY KEY,
                    "Name" TEXT NOT NULL,
                    "TeamAId" TEXT NOT NULL,
                    "TeamBId" TEXT NOT NULL,
                    "MatchCount" INTEGER NOT NULL,
                    "VenueId" TEXT NOT NULL,
                    "Format" INTEGER NOT NULL,
                    "OversLimit" INTEGER NOT NULL,
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
        }
        catch
        {
            /* already exists */
        }
    }

    private async Task EnsureFormatsAsync(string dbName, CancellationToken ct)
    {
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName))
                return;
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF OBJECT_ID(N'dbo.Formats', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Formats (
                        Id uniqueidentifier NOT NULL CONSTRAINT PK_Formats PRIMARY KEY,
                        Name nvarchar(160) NOT NULL,
                        Format tinyint NOT NULL,
                        OversLimit int NOT NULL CONSTRAINT DF_Formats_OversLimit DEFAULT 8,
                        OversMode tinyint NOT NULL CONSTRAINT DF_Formats_OversMode DEFAULT 1,
                        Version bigint NOT NULL,
                        CreatedAtUtc datetime2 NOT NULL,
                        UpdatedAtUtc datetime2 NOT NULL,
                        IsDeleted bit NOT NULL CONSTRAINT DF_Formats_IsDeleted DEFAULT 0
                    );
                    CREATE INDEX IX_Formats_Name ON dbo.Formats (Name);
                END
                IF COL_LENGTH(N'dbo.Matches', N'FormatId') IS NULL
                    ALTER TABLE dbo.Matches ADD FormatId uniqueidentifier NULL;
                IF OBJECT_ID(N'dbo.Tournaments', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Tournaments', N'FormatId') IS NULL
                    ALTER TABLE dbo.Tournaments ADD FormatId uniqueidentifier NULL;
                IF OBJECT_ID(N'dbo.Series', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Series', N'FormatId') IS NULL
                    ALTER TABLE dbo.Series ADD FormatId uniqueidentifier NULL;
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        else
        {
            try
            {
                await using var db = CreateContext(ToConnection(dbName), Guid.Empty);
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS "Formats" (
                        "Id" TEXT NOT NULL CONSTRAINT "PK_Formats" PRIMARY KEY,
                        "Name" TEXT NOT NULL,
                        "Format" INTEGER NOT NULL,
                        "OversLimit" INTEGER NOT NULL DEFAULT 8,
                        "OversMode" INTEGER NOT NULL DEFAULT 1,
                        "Version" INTEGER NOT NULL,
                        "CreatedAtUtc" TEXT NOT NULL,
                        "UpdatedAtUtc" TEXT NOT NULL,
                        "IsDeleted" INTEGER NOT NULL
                    );
                    """, ct).ConfigureAwait(false);
                try
                {
                    await db.Database.ExecuteSqlRawAsync(
                            """ALTER TABLE "Matches" ADD COLUMN "FormatId" TEXT NULL;""", ct)
                        .ConfigureAwait(false);
                }
                catch { /* exists */ }
                try
                {
                    await db.Database.ExecuteSqlRawAsync(
                            """ALTER TABLE "Tournaments" ADD COLUMN "FormatId" TEXT NULL;""", ct)
                        .ConfigureAwait(false);
                }
                catch { /* exists */ }
                try
                {
                    await db.Database.ExecuteSqlRawAsync(
                            """ALTER TABLE "Series" ADD COLUMN "FormatId" TEXT NULL;""", ct)
                        .ConfigureAwait(false);
                }
                catch { /* exists */ }
            }
            catch
            {
                /* already exists */
            }
        }

        try
        {
            await using var ctx = CreateContext(ToConnection(dbName), Guid.Empty);
            await FormatCatalog.BackfillAsync(ctx, ct).ConfigureAwait(false);
        }
        catch
        {
            /* backfill is best-effort on older catalogs */
        }

        if (!_sqlServer || !SafeName.IsMatch(dbName))
            return;

        try
        {
            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var fk = conn.CreateCommand();
            fk.CommandText = """
                IF OBJECT_ID(N'dbo.Formats', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Matches', N'FormatId') IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Matches_Formats_FormatId')
                    ALTER TABLE dbo.Matches WITH NOCHECK
                    ADD CONSTRAINT FK_Matches_Formats_FormatId FOREIGN KEY (FormatId) REFERENCES dbo.Formats (Id);
                IF OBJECT_ID(N'dbo.Tournaments', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Tournaments', N'FormatId') IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Tournaments_Formats_FormatId')
                    ALTER TABLE dbo.Tournaments WITH NOCHECK
                    ADD CONSTRAINT FK_Tournaments_Formats_FormatId FOREIGN KEY (FormatId) REFERENCES dbo.Formats (Id);
                IF OBJECT_ID(N'dbo.Series', 'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Series', N'FormatId') IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Series_Formats_FormatId')
                    ALTER TABLE dbo.Series WITH NOCHECK
                    ADD CONSTRAINT FK_Series_Formats_FormatId FOREIGN KEY (FormatId) REFERENCES dbo.Formats (Id);
                """;
            await fk.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            /* FK is optional if orphans remain */
        }
    }

    private async Task DropSportsLeagueIdColumnsAsync(string dbName, CancellationToken ct)
    {
        if (!_sqlServer || !SafeName.IsMatch(dbName))
            return;

        var tables = new[]
        {
            "Players", "Teams", "Matches", "Auctions", "Venues", "VenueBookings", "Tournaments", "Series"
        };

        await using var conn = new SqlConnection(ToConnection(dbName));
        await conn.OpenAsync(ct).ConfigureAwait(false);
        foreach (var table in tables)
            await DropColumnIfExistsAsync(conn, table, "LeagueId", ct).ConfigureAwait(false);
    }

    private static async Task DropColumnIfExistsAsync(
        SqlConnection conn, string table, string column, CancellationToken ct)
    {
        if (!SafeName.IsMatch(table) || !SafeName.IsMatch(column))
            return;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql = @sql + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON dbo.[{table}];'
            FROM sys.indexes i
            WHERE i.object_id = OBJECT_ID(N'dbo.[{table}]')
              AND i.name IS NOT NULL
              AND EXISTS (
                  SELECT 1
                  FROM sys.index_columns ic
                  INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND c.name = N'{column}');
            SELECT @sql = @sql + N'ALTER TABLE dbo.[{table}] DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';'
            FROM sys.default_constraints dc
            INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
            WHERE dc.parent_object_id = OBJECT_ID(N'dbo.[{table}]') AND c.name = N'{column}';
            IF COL_LENGTH(N'dbo.[{table}]', N'{column}') IS NOT NULL
                SET @sql = @sql + N'ALTER TABLE dbo.[{table}] DROP COLUMN [{column}];';
            IF @sql <> N'' EXEC sys.sp_executesql @sql;
            """;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Users + Leagues live only in Cric360 master — leftover copies in league catalogs are dropped.</summary>
    private async Task DropLeagueDirectoryTablesAsync(string dbName, CancellationToken ct)
    {
        var tables = new[] { "Users", "Leagues" };
        if (_sqlServer)
        {
            if (!SafeName.IsMatch(dbName)
                || dbName.Equals(MasterCatalogName, StringComparison.OrdinalIgnoreCase))
                return;

            await using var conn = new SqlConnection(ToConnection(dbName));
            await conn.OpenAsync(ct).ConfigureAwait(false);
            foreach (var table in tables)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    IF OBJECT_ID(N'dbo.[{table}]', 'U') IS NOT NULL
                        DROP TABLE dbo.[{table}];
                    """;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            return;
        }

        var connection = ToConnection(dbName);
        await using var db = CreateContext(connection, Guid.Empty);
        foreach (var table in tables)
        {
            await db.Database.ExecuteSqlRawAsync($"""DROP TABLE IF EXISTS "{table}";""", ct)
                .ConfigureAwait(false);
        }
    }

    public async Task DropMasterSportsTablesAsync(CancellationToken ct = default)
    {
        if (!_sqlServer)
            return;

        var tables = new[]
        {
            "Deliveries", "Innings", "SeriesFixtures", "Series", "TournamentFixtures", "TournamentTeams", "Tournaments",
            "VenueBookingParticipants", "VenueBookings", "AuctionLots", "AuctionCaptains", "Auctions",
            "TeamPlayers", "Matches", "Teams", "Players", "Venues", "SyncReceipts"
        };

        await using var conn = new SqlConnection(_defaultConnection);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        foreach (var table in tables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                IF OBJECT_ID(N'dbo.[{table}]', 'U') IS NOT NULL
                    DROP TABLE dbo.[{table}];
                """;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task RenameSqlCatalogAsync(string fromName, string toName, CancellationToken ct)
    {
        if (!SafeName.IsMatch(fromName) || !SafeName.IsMatch(toName))
            throw new InvalidOperationException("Database name is not valid.");
        if (fromName.Equals(MasterCatalogName, StringComparison.OrdinalIgnoreCase)
            || toName.Equals(MasterCatalogName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot rename the Cric360 master database.");

        await using var conn = new SqlConnection(MasterSystemConnection());
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 0;
        cmd.CommandText = $"""
            IF DB_ID(N'{fromName}') IS NOT NULL AND DB_ID(N'{toName}') IS NULL
            BEGIN
                ALTER DATABASE [{fromName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                ALTER DATABASE [{fromName}] MODIFY NAME = [{toName}];
                ALTER DATABASE [{toName}] SET MULTI_USER;
            END
            """;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> CatalogExistsAsync(string catalog, CancellationToken ct)
    {
        if (!_sqlServer || !SafeName.IsMatch(catalog))
            return false;
        await using var conn = new SqlConnection(MasterSystemConnection());
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN DB_ID(@name) IS NULL THEN 0 ELSE 1 END";
        cmd.Parameters.AddWithValue("@name", catalog);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null and not DBNull && Convert.ToInt32(result) == 1;
    }

    private async Task CreateSqlServerDatabaseAsync(string dbName, CancellationToken ct)
    {
        if (!SafeName.IsMatch(dbName))
            throw new InvalidOperationException("Database name is not valid.");

        await using var conn = new SqlConnection(MasterSystemConnection());
        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Cannot reach SQL Server to auto-create databases. Check the API connection string.",
                ex);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            IF DB_ID(N'{dbName}') IS NULL
                CREATE DATABASE [{dbName}];
            """;
        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqlException ex) when (ex.Number is 262 or 297 or 229)
        {
            throw new InvalidOperationException(
                "SQL login cannot CREATE DATABASE. Grant dbcreator (or equivalent) to the API account — no manual SSMS create is needed after that.",
                ex);
        }
    }

    private string MasterSystemConnection()
    {
        var b = new SqlConnectionStringBuilder(_defaultConnection)
        {
            InitialCatalog = "master"
        };
        return b.ConnectionString;
    }

    private ApiDbContext CreateContext(string connection, Guid leagueId)
        => CreateContext(connection, new FixedApiLeagueScope(leagueId));

    private ApiDbContext CreateContext(string connection, IApiLeagueScope scope)
    {
        var builder = new DbContextOptionsBuilder<ApiDbContext>();
        if (_sqlServer)
            builder.UseSqlServer(connection);
        else
            builder.UseSqlite(connection);
        return new ApiDbContext(builder.Options, scope);
    }

    private string ToConnection(string databaseName)
    {
        if (_sqlServer)
        {
            var b = new SqlConnectionStringBuilder(_defaultConnection)
            {
                InitialCatalog = databaseName
            };
            return b.ConnectionString;
        }

        return BuildSqliteConnection(SqlitePath(databaseName));
    }

    private string SqlitePath(string databaseName)
    {
        if (databaseName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
            || databaseName.Contains(Path.DirectorySeparatorChar)
            || databaseName.Contains(Path.AltDirectorySeparatorChar))
        {
            return Path.IsPathRooted(databaseName)
                ? databaseName
                : Path.Combine(_env.ContentRootPath, databaseName);
        }

        return Path.Combine(_env.ContentRootPath, "leagues", databaseName + ".db");
    }

    private string BuildSqliteConnection(string path)
    {
        if (!Path.IsPathRooted(path))
            path = Path.Combine(_env.ContentRootPath, path);
        return "Data Source=" + path;
    }
}

public sealed class ApiDbFactory : IApiDbFactory
{
    private readonly LeagueDatabaseManager _manager;
    private readonly IApiLeagueScope _scope;

    public ApiDbFactory(LeagueDatabaseManager manager, IApiLeagueScope scope)
    {
        _manager = manager;
        _scope = scope;
    }

    public MasterDbContext CreateMaster() => _manager.CreateMaster();

    public ApiDbContext CreateForLeague(Guid leagueId) => _manager.CreateForLeague(leagueId);

    public ApiDbContext CreateForRequest() => _manager.CreateForRequest(_scope);
}

public sealed class FixedApiLeagueScope : IApiLeagueScope
{
    public FixedApiLeagueScope(Guid leagueId) => CurrentLeagueId = leagueId;

    public Guid CurrentLeagueId { get; }
}
