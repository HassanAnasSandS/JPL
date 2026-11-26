using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

/// <summary>Selects an isolated SQLite file for each signed-in account.</summary>
public interface IAccountDatabaseScope
{
    Guid? UserId { get; }
    AppRole? Role { get; }
    string GetConnectionString();
    void Set(Guid userId, AppRole role);
    void Clear();
}

public sealed class AccountDatabaseScope : IAccountDatabaseScope, IDisposable
{
    private readonly object _gate = new();
    private Guid? _userId;
    private AppRole? _role;
    private SqliteConnection? _sessionAnchor;
    private string? _sessionConnectionString;
    public Guid? UserId { get { lock (_gate) return _userId; } }
    public AppRole? Role { get { lock (_gate) return _role; } }

    public string GetConnectionString()
    {
        lock (_gate)
        {
            if (_role is AppRole role && role != AppRole.Admin)
                return _sessionConnectionString!;
            var path = DbBootstrap.GetDatabasePath(_role == AppRole.Admin ? _userId : null);
            return new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();
        }
    }

    public void Set(Guid userId, AppRole role)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("A signed-in account must have a valid user id.", nameof(userId));
        lock (_gate)
        {
            if (_userId == userId && _role == role)
                return;
            ClearSessionStore();
            _userId = userId;
            _role = role;
            if (role != AppRole.Admin)
            {
                var name = $"jpl-session-{Guid.NewGuid():N}";
                _sessionConnectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = name,
                    Mode = SqliteOpenMode.Memory,
                    Cache = SqliteCacheMode.Shared
                }.ToString();
                _sessionAnchor = new SqliteConnection(_sessionConnectionString);
                _sessionAnchor.Open();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            ClearSessionStore();
            _userId = null;
            _role = null;
        }
    }

    public void Dispose() => Clear();

    private void ClearSessionStore()
    {
        try { _sessionAnchor?.Dispose(); }
        catch { /* session teardown must always complete */ }
        _sessionAnchor = null;
        _sessionConnectionString = null;
    }
}

public sealed class AccountScopedDbContextFactory(
    IAccountDatabaseScope scope,
    ILeagueContext league) : IDbContextFactory<JplDbContext>
{
    public JplDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JplDbContext>()
            .UseSqlite(scope.GetConnectionString())
            .Options;
        return new JplDbContext(options, league);
    }

    public Task<JplDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
}

/// <summary>The shared pre-login database that stores the offline credential cache.</summary>
public interface IMasterJplDbContextFactory : IDbContextFactory<JplDbContext> { }

public sealed class MasterJplDbContextFactory(ILeagueContext league) : IMasterJplDbContextFactory
{
    public JplDbContext CreateDbContext()
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = DbBootstrap.GetDatabasePath(),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        var options = new DbContextOptionsBuilder<JplDbContext>()
            .UseSqlite(csb.ToString())
            .Options;
        return new JplDbContext(options, league);
    }

    public Task<JplDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
}
