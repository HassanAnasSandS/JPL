using JPL.Domain;

namespace JPL.Services;

public sealed record UserDraft(
    string Username,
    string? Password,
    AppRole Role,
    Guid? LeagueId,
    bool IsActive = true,
    string? WhatsAppNumber = null);

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
/// Admin/User accounts: JPL.Api (jpl.shoptick.shop :5025).
/// GroundOwner accounts: JPL.Grounds.Api (jplground.shoptick.shop :5026) writing the same Master Users table.
/// </summary>
public sealed class UserService : IUserService
{
    private readonly IApiClient _api;
    private readonly IGroundsApiClient _grounds;
    private readonly IAuthService _auth;

    public UserService(IApiClient api, IGroundsApiClient grounds, IAuthService auth)
    {
        _api = api;
        _grounds = grounds;
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
        if (draft.Role == AppRole.PublicUser)
            throw new ArgumentException("Public users sign up from the login screen.");
        if (draft.Role == AppRole.Guest)
            throw new ArgumentException("Guest is a built-in account.");
        if (draft.Role is not AppRole.GroundOwner
            && (draft.LeagueId is null || draft.LeagueId == Guid.Empty))
            throw new ArgumentException("Select a league for this user.");

        if (draft.Role == AppRole.GroundOwner)
        {
            var owner = await _grounds.CreateGroundOwnerAsync(new
            {
                username,
                password,
                isActive = draft.IsActive,
                whatsAppNumber = RequireWhatsApp(draft.WhatsAppNumber)
            }, ct).ConfigureAwait(false);
            return ToEntity(owner);
        }

        var remote = await _api.CreateUserAsync(new
        {
            id = (Guid?)null,
            username,
            password,
            role = draft.Role.ToString(),
            isActive = draft.IsActive,
            leagueId = draft.Role is AppRole.GroundOwner or AppRole.PublicUser or AppRole.Guest
                ? (Guid?)null
                : draft.LeagueId,
            whatsAppNumber = RequireWhatsApp(draft.WhatsAppNumber)
        }, ct).ConfigureAwait(false);

        return ToEntity(remote);
    }

    public async Task<AppUser> UpdateAsync(Guid id, UserDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanManageUsers();
        var username = RequireUsername(draft.Username);
        if (draft.Role is not AppRole.GroundOwner and not AppRole.PublicUser and not AppRole.Guest
            && (draft.LeagueId is null || draft.LeagueId == Guid.Empty))
            throw new ArgumentException("Select a league for this user.");

        if (draft.Role == AppRole.GroundOwner)
        {
            var owner = await _grounds.UpdateGroundOwnerAsync(id, new
            {
                username,
                password = string.IsNullOrWhiteSpace(draft.Password) ? null : draft.Password.Trim(),
                isActive = draft.IsActive,
                whatsAppNumber = NormalizeWhatsAppOrNull(draft.WhatsAppNumber)
            }, ct).ConfigureAwait(false);
            return ToEntity(owner);
        }

        var remote = await _api.UpdateUserAsync(id, new
        {
            id,
            username,
            password = string.IsNullOrWhiteSpace(draft.Password) ? null : draft.Password.Trim(),
            role = draft.Role.ToString(),
            isActive = draft.IsActive,
            leagueId = draft.Role is AppRole.GroundOwner or AppRole.PublicUser or AppRole.Guest
                ? (Guid?)null
                : draft.LeagueId,
            whatsAppNumber = NormalizeWhatsAppOrNull(draft.WhatsAppNumber)
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
                new UserDraft(user.Username, Password: null, user.Role, user.LeagueId, isActive, user.WhatsAppNumber),
                ct)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanManageUsers();
        if (_auth.Current?.UserId == id)
            throw new InvalidOperationException("You cannot delete the account you are signed in with.");

        var user = await GetAsync(id, ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("User not found.");
        if (user.Role == AppRole.Guest || AppRoleLabels.IsGuestUsername(user.Username))
            throw new InvalidOperationException("Cannot delete the Guest account.");
        if (user.Role == AppRole.GroundOwner)
        {
            await _grounds.DeleteGroundOwnerAsync(id, ct).ConfigureAwait(false);
            return;
        }

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
            WhatsAppNumber = dto.WhatsAppNumber,
            IsActive = dto.IsActive,
            CreatedAtUtc = dto.CreatedAtUtc,
            PasswordHash = string.Empty
        };
    }

    private static string? NormalizeWhatsAppOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return RequireWhatsApp(raw);
    }

    private static string RequireWhatsApp(string? raw)
    {
        if (!PakistanWhatsApp.TryNormalize(raw, out var whatsApp))
            throw new ArgumentException(PakistanWhatsApp.RequiredError);
        return whatsApp;
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
