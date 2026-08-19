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
        await EnsureNetSessionTablesAsync(dbName, ct).ConfigureAwait(false);
        await DropSportsLeagueIdColumnsAsync(dbName, ct).ConfigureAwait(false);
        await DropLeagueDirectoryTablesAsync(dbName, ct).ConfigureAwait(false);
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

    private async Task DropSportsLeagueIdColumnsAsync(string dbName, CancellationToken ct)
    {
        if (!_sqlServer || !SafeName.IsMatch(dbName))
            return;

        var tables = new[]
        {
            "Players", "Teams", "Matches", "Auctions", "Venues", "VenueBookings", "Tournaments"
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
            "Deliveries", "Innings", "TournamentFixtures", "TournamentTeams", "Tournaments",
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
