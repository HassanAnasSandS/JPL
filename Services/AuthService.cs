using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public sealed class AuthFailedException : Exception
{
    public AuthFailedException(string message) : base(message) { }
}

public static class AuthMessages
{
    public const string MissingCredentials = "Enter your username and password.";
    public const string UserNotFound = "This user does not exist.";
    public const string WrongPassword = "Password is not correct.";
    public const string Generic = "Could not sign in. Please try again.";

    public static string ForLogin(Exception ex)
    {
        if (ex is AuthFailedException)
            return ex.Message;
        if (NetworkUi.IsNetworkFailure(ex))
            return NetworkUi.OfflineMessage;
        if (LooksTechnical(ex))
            return Generic;
        var text = NetworkUi.UserMessage(ex);
        return LooksTechnical(text) ? Generic : text;
    }

    public static bool TryFromApiBody(string? body, out AuthFailedException error)
    {
        error = new AuthFailedException(Generic);
        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            var code = ReadJsonString(root, "code");
            var message = ReadJsonString(root, "message");
            if (string.Equals(code, "user_not_found", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, UserNotFound, StringComparison.OrdinalIgnoreCase))
            {
                error = new AuthFailedException(UserNotFound);
                return true;
            }

            if (string.Equals(code, "wrong_password", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, WrongPassword, StringComparison.OrdinalIgnoreCase))
            {
                error = new AuthFailedException(WrongPassword);
                return true;
            }

            if (string.Equals(code, "missing_credentials", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, MissingCredentials, StringComparison.OrdinalIgnoreCase))
            {
                error = new AuthFailedException(MissingCredentials);
                return true;
            }

            if (!string.IsNullOrWhiteSpace(message) && !LooksTechnical(message))
            {
                error = new AuthFailedException(message);
                return true;
            }
        }
        catch
        {
            /* empty / non-JSON 401 from older API */
        }

        return false;
    }

    private static string? ReadJsonString(System.Text.Json.JsonElement root, string name)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            return prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                ? prop.Value.GetString()
                : prop.Value.ToString();
        }

        return null;
    }

    public static string ForSignup(Exception ex)
    {
        if (ex is AuthFailedException or ArgumentException or InvalidOperationException)
            return ex.Message;
        if (NetworkUi.IsNetworkFailure(ex))
            return NetworkUi.OfflineMessage;
        var text = NetworkUi.UserMessage(ex);
        return LooksTechnical(text) ? "Could not create account. Please try again." : text;
    }

    private static bool LooksTechnical(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (LooksTechnical(e.Message))
                return true;
        }

        return false;
    }

    private static bool LooksTechnical(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;
        return text.Contains("SQLite", StringComparison.OrdinalIgnoreCase)
               || text.Contains("SqlException", StringComparison.OrdinalIgnoreCase)
               || text.Contains("no such column", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Microsoft.Data", StringComparison.OrdinalIgnoreCase)
               || text.Contains("at Microsoft.", StringComparison.Ordinal);
    }
}

public sealed record AuthSession(Guid UserId, string Username, AppRole Role, Guid? LeagueId)
{
    public bool CanWrite => Role is AppRole.Admin or AppRole.SuperAdmin;
    public bool IsGroundOwner => Role == AppRole.GroundOwner;
    public bool IsGuest => Role == AppRole.Guest;
    public bool IsPublicUser => Role == AppRole.PublicUser;
    public bool NeedsLeaguePicker =>
        !IsGroundOwner && !IsPublicUser && !IsGuest && LeagueId is null;
    public bool NeedsModeChooser => true;
    public bool CanManagePlatform => Role == AppRole.SuperAdmin && PlatformAdmin.Is(Username);
    public bool CanManageLeagues => CanManagePlatform;
    public bool CanManageUsers => CanManagePlatform;
    public bool CanSwitchLeague => true;

    public string RoleLabel => AppRoleLabels.Of(Role);

    public bool HasHomeLeague => LeagueId is Guid id && id != Guid.Empty;

    public bool IsHomeLeague(Guid? selectedLeagueId) =>
        LeagueId is Guid home && home != Guid.Empty && selectedLeagueId == home;

    public bool CanWriteLeague(Guid? selectedLeagueId)
    {
        if (Role == AppRole.SuperAdmin)
            return true;
        return Role == AppRole.Admin && IsHomeLeague(selectedLeagueId);
    }

    public bool CanViewCatalogFor(Guid? selectedLeagueId)
    {
        if (IsPublicUser || IsGuest || IsGroundOwner)
            return false;
        if (Role == AppRole.SuperAdmin)
            return true;
        return Role is AppRole.Admin or AppRole.User && IsHomeLeague(selectedLeagueId);
    }

    public bool CanViewLeagueCatalog => !IsPublicUser && !IsGuest && !IsGroundOwner;

    public bool CanBookGrounds => !IsGuest && !IsGroundOwner;

    public string SignedInName => Username;
    public string SignedInLabel => $"{Username}  ·  {RoleLabel}";
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
    Task SignupAsync(string username, string password, string whatsApp, CancellationToken ct = default);
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
    public bool CanWrite => _current?.CanWriteLeague(_league.CurrentLeagueId) == true;

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
            role = NormalizeBuiltInRole(username, role);
            if (role == AppRole.Guest)
                leagueId = null;

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
        try
        {
            await SeedDefaultUsersAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            /* local seed must not block login */
        }

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
            role = NormalizeBuiltInRole(me.Username, role);

            _current = new AuthSession(me.UserId, me.Username, role, role == AppRole.Guest ? null : me.LeagueId);
            PersistSession(_current, token);
            if (!_league.HasSelection && me.LeagueId is Guid bound && bound != Guid.Empty)
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
            throw new AuthFailedException(AuthMessages.MissingCredentials);

        var apiServerError = false;
        try
        {
            var remote = await _api.LoginAsync(username, password, ct).ConfigureAwait(false);
            if (remote is not null)
            {
                if (!Enum.TryParse<AppRole>(remote.Role, ignoreCase: true, out var role))
                    role = AppRole.User;
                role = NormalizeBuiltInRole(remote.Username, role);

                _current = new AuthSession(remote.UserId, remote.Username, role, remote.LeagueId);
                PersistSession(_current, remote.Token);
                ApplyLeagueAfterLogin(_current);
                ApplyModuleGates();
                await CacheLocalLoginAsync(remote.UserId, remote.Username, role, password, remote.LeagueId, ct)
                    .ConfigureAwait(false);
                return true;
            }
        }
        catch (AuthFailedException)
        {
            throw;
        }
        catch (OfflineException)
        {
            // Use local cache; keep user-not-found / wrong-password wording.
        }
        catch
        {
            apiServerError = true;
        }

        try
        {
            return await LoginLocalAsync(username, password, ct).ConfigureAwait(false);
        }
        catch (AuthFailedException ex) when (apiServerError && ex.Message == AuthMessages.UserNotFound)
        {
            throw new AuthFailedException(AuthMessages.Generic);
        }
    }

    public async Task SignupAsync(string username, string password, string whatsApp, CancellationToken ct = default)
    {
        username = username?.Trim() ?? string.Empty;
        if (username.Length == 0)
            throw new ArgumentException("Name is required.");
        if (string.IsNullOrWhiteSpace(password) || password.Trim().Length < 4)
            throw new ArgumentException("Password must be at least 4 characters.");
        if (string.IsNullOrWhiteSpace(whatsApp))
            throw new ArgumentException("WhatsApp number is required.");
        if (!PakistanWhatsApp.TryNormalize(whatsApp, out var normalizedWhatsApp))
            throw new ArgumentException(PakistanWhatsApp.RequiredError);

        var remote = await _api.SignupAsync(username, password.Trim(), normalizedWhatsApp, ct).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Could not create account.");

        if (!Enum.TryParse<AppRole>(remote.Role, ignoreCase: true, out var role))
            role = AppRole.PublicUser;

        _current = new AuthSession(remote.UserId, remote.Username, role, remote.LeagueId);
        PersistSession(_current, remote.Token);
        ApplyLeagueAfterLogin(_current);
        ApplyModuleGates();
        await CacheLocalLoginAsync(remote.UserId, remote.Username, role, password.Trim(), remote.LeagueId, ct)
            .ConfigureAwait(false);
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
        AppUser? user;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            user = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == username, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new AuthFailedException(AuthMessages.ForLogin(ex));
        }

        if (user is null || !user.IsActive)
            throw new AuthFailedException(AuthMessages.UserNotFound);

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            throw new AuthFailedException(AuthMessages.WrongPassword);

        var role = NormalizeBuiltInRole(user.Username, user.Role);
        _current = new AuthSession(user.Id, user.Username, role, role == AppRole.Guest ? null : user.LeagueId);
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

    private static AppRole NormalizeBuiltInRole(string username, AppRole role)
        => AppRoleLabels.IsGuestUsername(username) ? AppRole.Guest : role;

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

        await EnsureUser("user", "user123", AppRole.Guest, null).ConfigureAwait(false);
        await EnsureUser("groundowner", "ground123", AppRole.GroundOwner, null).ConfigureAwait(false);
        await EnsureSoleSuperAdminAsync(db, ct).ConfigureAwait(false);

        var guest = await db.Users.FirstOrDefaultAsync(u => u.Username == "user", ct).ConfigureAwait(false);
        if (guest is not null)
        {
            guest.Role = AppRole.Guest;
            guest.LeagueId = null;
            guest.IsActive = true;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
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
