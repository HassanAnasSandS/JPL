using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public sealed record AuthSession(Guid UserId, string Username, AppRole Role, Guid? LeagueId)
{
    public bool CanWrite => Role is AppRole.Admin or AppRole.SuperAdmin;
    public bool NeedsLeaguePicker => LeagueId is null;
    public bool CanManagePlatform => Role == AppRole.SuperAdmin && PlatformAdmin.Is(Username);
    public bool CanManageLeagues => CanManagePlatform;
    public bool CanManageUsers => CanManagePlatform;
    public bool CanSwitchLeague => LeagueId is null;
}

public interface IAuthService
{
    AuthSession? Current { get; }
    bool IsAuthenticated { get; }
    bool CanWrite { get; }

    /// <summary>Preferences-only restore — no network / no SQLite. Safe on UI thread at startup.</summary>
    void RestoreLocalSession();

    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>Optional GetMe check after UI is up. Deletes/deactivates force logout.</summary>
    Task ValidateRemoteSessionAsync(CancellationToken ct = default);

    Task<bool> LoginAsync(string username, string password, CancellationToken ct = default);
    Task LogoutAsync();
    void EnsureCanWrite();
    void EnsureCanManageUsers();
}

public sealed class AuthService : IAuthService
{
    private const string SessionUserIdKey = "jpl.session.userId";
    private const string SessionUsernameKey = "jpl.session.username";
    private const string SessionRoleKey = "jpl.session.role";
    private const string SessionTokenKey = "jpl.session.token";
    private const string SessionLeagueIdKey = "jpl.session.leagueId";

    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IApiClient _api;
    private readonly IUsersModuleGate _usersGate;
    private readonly IAuctionsModuleGate _auctionsGate;
    private readonly AuthTokenStore _tokens;
    private readonly MasterDataCache _masterCache;
    private readonly ILeagueContext _league;
    private readonly PasswordHasher<AppUser> _hasher = new();
    private AuthSession? _current;

    public AuthService(
        IDbContextFactory<JplDbContext> dbFactory,
        IApiClient api,
        IUsersModuleGate usersGate,
        IAuctionsModuleGate auctionsGate,
        AuthTokenStore tokens,
        MasterDataCache masterCache,
        ILeagueContext league)
    {
        _dbFactory = dbFactory;
        _api = api;
        _usersGate = usersGate;
        _auctionsGate = auctionsGate;
        _tokens = tokens;
        _masterCache = masterCache;
        _league = league;
    }

    public AuthSession? Current => _current;
    public bool IsAuthenticated => _current is not null;
    public bool CanWrite => _current?.CanWrite == true;

    public void RestoreLocalSession()
    {
        try
        {
            var idText = Preferences.Default.Get(SessionUserIdKey, string.Empty);
            var username = Preferences.Default.Get(SessionUsernameKey, string.Empty);
            var roleText = Preferences.Default.Get(SessionRoleKey, string.Empty);
            var token = Preferences.Default.Get(SessionTokenKey, string.Empty);
            var leagueText = Preferences.Default.Get(SessionLeagueIdKey, string.Empty);

            if (!Guid.TryParse(idText, out var userId) ||
                string.IsNullOrWhiteSpace(username) ||
                !Enum.TryParse<AppRole>(roleText, out var role))
            {
                _current = null;
                _tokens.Clear();
                return;
            }

            Guid? leagueId = Guid.TryParse(leagueText, out var lid) && lid != Guid.Empty ? lid : null;

            if (!string.IsNullOrWhiteSpace(token))
                _tokens.Set(token, userId);
            else
                _tokens.Clear();

            _current = new AuthSession(userId, username, role, leagueId);
            if (leagueId is Guid bound)
                _league.Set(bound);
            else
                _league.Restore();
            ApplyModuleGates();
        }
        catch
        {
            _current = null;
            _tokens.Clear();
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Local seed only as offline fallback when API is unreachable.
        await SeedDefaultUsersAsync(ct).ConfigureAwait(false);
        // Keep / refresh local session from prefs (no HTTP on this path).
        if (_current is null)
            RestoreLocalSession();
    }

    public async Task ValidateRemoteSessionAsync(CancellationToken ct = default)
    {
        if (_current is null)
            return;

        var token = Preferences.Default.Get(SessionTokenKey, string.Empty);
        if (string.IsNullOrWhiteSpace(token))
            return;

        _tokens.Set(token, _current.UserId);
        _tokens.SuppressForcedLogout = true;
        try
        {
            var me = await _api.GetMeAsync(ct).ConfigureAwait(false);
            if (me is null)
            {
                await LogoutAsync().ConfigureAwait(false);
                return;
            }

            if (!Enum.TryParse<AppRole>(me.Role, true, out var role))
                role = _current.Role;

            _current = new AuthSession(me.UserId, me.Username, role, me.LeagueId);
            PersistSession(_current, token);
            if (me.LeagueId is Guid bound && bound != Guid.Empty)
                _league.Set(bound);
        }
        catch (SessionExpiredException)
        {
            await LogoutAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Timeout / cancel — keep local session; next API call will re-check.
        }
        catch
        {
            // Offline / transient — keep local session.
        }
        finally
        {
            _tokens.SuppressForcedLogout = false;
        }
    }

    public async Task<bool> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        username = username?.Trim() ?? string.Empty;
        if (username.Length == 0 || string.IsNullOrEmpty(password))
            return false;

        // Prefer API (SQL Server Users) — source of truth.
        try
        {
            var remote = await _api.LoginAsync(username, password, ct).ConfigureAwait(false);
            if (remote is not null)
            {
                if (!Enum.TryParse<AppRole>(remote.Role, ignoreCase: true, out var role))
                    role = AppRole.User;

                _current = new AuthSession(remote.UserId, remote.Username, role, remote.LeagueId);
                PersistSession(_current, remote.Token);
                ApplyLeagueAfterLogin(_current);
                ApplyModuleGates();
                await CacheLocalLoginAsync(remote.UserId, remote.Username, role, password, remote.LeagueId, ct)
                    .ConfigureAwait(false);
                return true;
            }
        }
        catch
        {
            // Fall through to local cache when offline / API down.
        }

        return await LoginLocalAsync(username, password, ct).ConfigureAwait(false);
    }

    public Task LogoutAsync()
    {
        _current = null;
        _tokens.Clear();
        _league.Clear();
        _masterCache.Invalidate();
        _usersGate.Lock();
        _auctionsGate.Lock();
        Preferences.Default.Remove(SessionUserIdKey);
        Preferences.Default.Remove(SessionUsernameKey);
        Preferences.Default.Remove(SessionRoleKey);
        Preferences.Default.Remove(SessionTokenKey);
        Preferences.Default.Remove(SessionLeagueIdKey);
        return Task.CompletedTask;
    }

    public void EnsureCanWrite()
    {
        if (!CanWrite)
            throw new UnauthorizedAccessException("Read-only user cannot create, edit, or delete.");
    }

    public void EnsureCanManageUsers()
    {
        if (_current?.CanManageUsers != true)
            throw new UnauthorizedAccessException("Only HassanAnas can manage users.");
    }

    private async Task<bool> LoginLocalAsync(string username, string password, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive, ct)
            .ConfigureAwait(false);

        if (user is null)
            return false;

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return false;

        _current = new AuthSession(user.Id, user.Username, user.Role, user.LeagueId);
        PersistSession(_current, token: null);
        ApplyLeagueAfterLogin(_current);
        ApplyModuleGates();
        return true;
    }

    /// <summary>Keep a local password cache so login still works briefly offline.</summary>
    private async Task CacheLocalLoginAsync(
        Guid userId,
        string username,
        AppRole role,
        string password,
        Guid? leagueId,
        CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var local = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
            if (local is null)
            {
                var byName = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct)
                    .ConfigureAwait(false);
                if (byName is not null && byName.Id != userId)
                    db.Users.Remove(byName);

                local = new AppUser
                {
                    Id = userId,
                    Username = username,
                    Role = role,
                    LeagueId = leagueId,
                    IsActive = true,
                    CreatedAtUtc = DateTime.Now
                };
                local.PasswordHash = _hasher.HashPassword(local, password);
                db.Users.Add(local);
            }
            else
            {
                local.Username = username;
                local.Role = role;
                local.LeagueId = leagueId;
                local.IsActive = true;
                local.PasswordHash = _hasher.HashPassword(local, password);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            /* login succeeded via API even if cache fails */
        }
    }

    private void PersistSession(AuthSession session, string? token)
    {
        Preferences.Default.Set(SessionUserIdKey, session.UserId.ToString());
        Preferences.Default.Set(SessionUsernameKey, session.Username);
        Preferences.Default.Set(SessionRoleKey, session.Role.ToString());
        if (session.LeagueId is Guid leagueId)
            Preferences.Default.Set(SessionLeagueIdKey, leagueId.ToString("D"));
        else
            Preferences.Default.Remove(SessionLeagueIdKey);

        if (!string.IsNullOrWhiteSpace(token))
        {
            Preferences.Default.Set(SessionTokenKey, token);
            _tokens.Set(token, session.UserId);
        }
        else
        {
            Preferences.Default.Remove(SessionTokenKey);
            _tokens.Clear();
        }
    }

    private void ApplyLeagueAfterLogin(AuthSession session)
    {
        if (session.LeagueId is Guid bound && bound != Guid.Empty)
            _league.Set(bound);
        else
            _league.Clear();
    }

    private void ApplyModuleGates()
    {
        if (_current?.Role == AppRole.SuperAdmin)
        {
            _usersGate.Unlock();
            _auctionsGate.Unlock();
        }
    }

    private async Task SeedDefaultUsersAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        async Task EnsureUser(string username, string password, AppRole role, Guid? leagueId)
        {
            if (await db.Users.AnyAsync(u => u.Username == username, ct).ConfigureAwait(false))
                return;
            var user = new AppUser
            {
                Username = username,
                Role = role,
                LeagueId = leagueId,
                IsActive = true,
                CreatedAtUtc = DateTime.Now
            };
            user.PasswordHash = _hasher.HashPassword(user, password);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var stale = await db.Users
            .Where(u => u.Username == "AmaanRehan")
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (stale.Count > 0)
        {
            db.Users.RemoveRange(stale);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await EnsureUser("user", "user123", AppRole.User, null).ConfigureAwait(false);
        await EnsureSoleSuperAdminAsync(db, ct).ConfigureAwait(false);
    }

    private async Task EnsureSoleSuperAdminAsync(JplDbContext db, CancellationToken ct)
    {
        const string username = "HassanAnas";
        const string password = "Qwerty123@";

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct).ConfigureAwait(false);
        if (existing is null)
        {
            var user = new AppUser
            {
                Username = username,
                Role = AppRole.SuperAdmin,
                LeagueId = null,
                IsActive = true,
                CreatedAtUtc = DateTime.Now
            };
            user.PasswordHash = _hasher.HashPassword(user, password);
            db.Users.Add(user);
        }
        else
        {
            existing.Role = AppRole.SuperAdmin;
            existing.LeagueId = null;
            existing.IsActive = true;
            existing.PasswordHash = _hasher.HashPassword(existing, password);
        }

        var others = await db.Users
            .Where(u => u.Role == AppRole.SuperAdmin)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var other in others)
        {
            if (string.Equals(other.Username, username, StringComparison.OrdinalIgnoreCase))
                continue;
            other.Role = AppRole.User;
            other.IsActive = false;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
