using JPL.Domain;

namespace JPL.Services;

public sealed record UserDraft(
    string Username,
    string? Password,
    AppRole Role,
    Guid LeagueId,
    bool IsActive = true);

public interface IUserService
{
    Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default);
    Task<AppUser?> GetAsync(Guid id, CancellationToken ct = default);
    Task<AppUser> CreateAsync(UserDraft draft, CancellationToken ct = default);
    Task<AppUser> UpdateAsync(Guid id, UserDraft draft, CancellationToken ct = default);
    Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Users module is API-only (SQL Server). No local SQLite CRUD.
/// </summary>
public sealed class UserService : IUserService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;

    public UserService(IApiClient api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    public async Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default)
    {
        var remote = await _api.GetUsersAsync(ct).ConfigureAwait(false);
        return remote
            .Select(ToEntity)
            .OrderByDescending(u => u.Role)
            .ThenBy(u => u.Username)
            .ToList();
    }

    public async Task<AppUser?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _api.GetUserAsync(id, ct).ConfigureAwait(false);
        return dto is null ? null : ToEntity(dto);
    }

    public async Task<AppUser> CreateAsync(UserDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanManageUsers();
        var username = RequireUsername(draft.Username);
        var password = RequirePassword(draft.Password);
        if (draft.LeagueId == Guid.Empty)
            throw new ArgumentException("Select a league for this user.");

        var remote = await _api.CreateUserAsync(new
        {
            id = (Guid?)null,
            username,
            password,
            role = draft.Role.ToString(),
            isActive = draft.IsActive,
            leagueId = draft.LeagueId
        }, ct).ConfigureAwait(false);

        return ToEntity(remote);
    }

    public async Task<AppUser> UpdateAsync(Guid id, UserDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanManageUsers();
        var username = RequireUsername(draft.Username);
        if (draft.LeagueId == Guid.Empty)
            throw new ArgumentException("Select a league for this user.");

        var remote = await _api.UpdateUserAsync(id, new
        {
            id,
            username,
            password = string.IsNullOrWhiteSpace(draft.Password) ? null : draft.Password.Trim(),
            role = draft.Role.ToString(),
            isActive = draft.IsActive,
            leagueId = draft.LeagueId
        }, ct).ConfigureAwait(false);

        return ToEntity(remote);
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        _auth.EnsureCanManageUsers();
        var user = await GetAsync(id, ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("User not found.");

        await UpdateAsync(
                id,
                new UserDraft(user.Username, Password: null, user.Role, user.LeagueId ?? Guid.Empty, isActive),
                ct)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanManageUsers();
        if (_auth.Current?.UserId == id)
            throw new InvalidOperationException("You cannot delete the account you are signed in with.");

        await _api.DeleteUserAsync(id, ct).ConfigureAwait(false);
    }

    private static AppUser ToEntity(ApiUserDto dto)
    {
        if (!Enum.TryParse<AppRole>(dto.Role, ignoreCase: true, out var role))
            role = AppRole.User;

        return new AppUser
        {
            Id = dto.Id,
            Username = dto.Username,
            Role = role,
            LeagueId = dto.LeagueId,
            LeagueName = dto.LeagueName ?? string.Empty,
            IsActive = dto.IsActive,
            CreatedAtUtc = dto.CreatedAtUtc,
            PasswordHash = string.Empty
        };
    }

    private static string RequireUsername(string? username)
    {
        var name = username?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw new ArgumentException("Username is required.");
        if (name.Length > 80)
            throw new ArgumentException("Username must be 80 characters or fewer.");
        return name;
    }

    private static string RequirePassword(string? password)
    {
        var pwd = password?.Trim() ?? string.Empty;
        if (pwd.Length == 0)
            throw new ArgumentException("Password is required.");
        if (pwd.Length < 4)
            throw new ArgumentException("Password must be at least 4 characters.");
        return pwd;
    }
}
