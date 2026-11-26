namespace JPL.Services;

/// <summary>One row in SuperAdmin League management (league record only — no sports data).</summary>
public sealed record ManagedLeague(
    Guid Id,
    string Name,
    byte[]? ImageBytes,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    int AdminCount,
    int UserCount,
    int LiveMatchCount,
    int LiveNetsCount);

/// <summary>Create / edit form values. ImageBytes null + RemoveImage false = keep current image.</summary>
public sealed record ManagedLeagueDraft(
    string Name,
    byte[]? ImageBytes,
    bool IsActive,
    bool RemoveImage = false);

/// <summary>
/// SuperAdmin (HassanAnas) League management. Live API only (SuperAdmin never syncs).
/// All failures surface as <see cref="InvalidOperationException"/> / <see cref="ArgumentException"/>
/// with a clean, user-facing message.
/// </summary>
public interface ILeagueAdminService
{
    /// <param name="status">0 = all, 1 = active, 2 = inactive.</param>
    Task<ApiPagedResult<ManagedLeague>> GetPageAsync(int skip, string? q, int status, CancellationToken ct = default);
    Task<ManagedLeague?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ManagedLeague> CreateAsync(ManagedLeagueDraft draft, CancellationToken ct = default);
    Task<ManagedLeague> UpdateAsync(Guid id, ManagedLeagueDraft draft, CancellationToken ct = default);
    Task<ManagedLeague> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    /// <summary>Soft delete (league database is kept). League must be deactivated first.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class LeagueAdminService : ILeagueAdminService
{
    public const int NameMaxLength = 160;
    public const int ImageMaxBytes = 2_000_000;

    private readonly IApiClient _api;
    private readonly IAuthService _auth;

    public LeagueAdminService(IApiClient api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    public async Task<ApiPagedResult<ManagedLeague>> GetPageAsync(int skip, string? q, int status, CancellationToken ct = default)
    {
        EnsurePlatformAdmin();
        var statusKey = status switch
        {
            1 => "active",
            2 => "inactive",
            _ => null
        };
        var page = await Guard(() => _api.GetManagedLeaguesPageAsync(skip, q?.Trim(), statusKey, ct)).ConfigureAwait(false);
        return page.WithItems((page.Items ?? []).Select(ToModel).ToList());
    }

    public async Task<ManagedLeague?> GetAsync(Guid id, CancellationToken ct = default)
    {
        EnsurePlatformAdmin();
        var dto = await Guard(() => _api.GetManagedLeagueAsync(id, ct)).ConfigureAwait(false);
        return dto is null ? null : ToModel(dto);
    }

    public async Task<ManagedLeague> CreateAsync(ManagedLeagueDraft draft, CancellationToken ct = default)
    {
        EnsurePlatformAdmin();
        var name = ValidateName(draft.Name);
        ValidateImage(draft.ImageBytes);
        var dto = await Guard(() => _api.CreateManagedLeagueAsync(new
        {
            name,
            imageBase64 = draft.ImageBytes is { Length: > 0 } ? Convert.ToBase64String(draft.ImageBytes) : null,
            isActive = draft.IsActive
        }, ct)).ConfigureAwait(false);
        return ToModel(dto);
    }

    public async Task<ManagedLeague> UpdateAsync(Guid id, ManagedLeagueDraft draft, CancellationToken ct = default)
    {
        EnsurePlatformAdmin();
        var name = ValidateName(draft.Name);
        ValidateImage(draft.ImageBytes);
        var dto = await Guard(() => _api.UpdateManagedLeagueAsync(id, new
        {
            name,
            imageBase64 = !draft.RemoveImage && draft.ImageBytes is { Length: > 0 }
                ? Convert.ToBase64String(draft.ImageBytes)
                : null,
            isActive = draft.IsActive,
            removeImage = draft.RemoveImage
        }, ct)).ConfigureAwait(false);
        return ToModel(dto);
    }

    public async Task<ManagedLeague> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        EnsurePlatformAdmin();
        var dto = await Guard(() => _api.SetLeagueActiveAsync(id, isActive, ct)).ConfigureAwait(false);
        return ToModel(dto);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        EnsurePlatformAdmin();
        await Guard(async () =>
        {
            await _api.DeleteLeagueAsync(id, ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    /// <summary>Shared by the edit form so the user sees the same rule before the API is called.</summary>
    public static string ValidateName(string? raw)
    {
        var name = string.Join(' ', (raw ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0)
            throw new ArgumentException("League name is required.");
        if (name.Length < 2)
            throw new ArgumentException("League name must be at least 2 characters.");
        if (name.Length > NameMaxLength)
            throw new ArgumentException($"League name must be {NameMaxLength} characters or fewer.");
        if (!name.Any(char.IsLetterOrDigit))
            throw new ArgumentException("League name must contain letters or numbers.");
        return name;
    }

    private static void ValidateImage(byte[]? bytes)
    {
        if (bytes is { Length: > ImageMaxBytes })
            throw new ArgumentException("League image must be under 2 MB.");
    }

    private void EnsurePlatformAdmin()
    {
        if (_auth.Current?.CanManageLeagues != true)
            throw new UnauthorizedAccessException("Only HassanAnas can manage leagues.");
    }

    /// <summary>Turns "API 409: message" into "message"; keeps offline / session exceptions as-is.</summary>
    private static async Task<T> Guard<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is not null)
        {
            throw new InvalidOperationException(CleanApiMessage(ex), ex);
        }
    }

    private static string CleanApiMessage(HttpRequestException ex)
    {
        var raw = ex.Message ?? string.Empty;
        var colon = raw.IndexOf(':');
        if (raw.StartsWith("API ", StringComparison.Ordinal) && colon > 0)
            raw = raw[(colon + 1)..].Trim().Trim('"');

        if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return string.IsNullOrWhiteSpace(raw) ? "Only HassanAnas can manage leagues." : raw;
        if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "League not found. It may have been deleted.";
        if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith('{') || raw.StartsWith('<'))
            return "Could not save the league. Please try again.";
        return raw.Length <= 300 ? raw : raw[..300] + "…";
    }

    private static byte[]? TryDecode(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return null;
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch
        {
            return null;
        }
    }

    private static ManagedLeague ToModel(ApiLeagueAdminDto d)
        => new(
            d.Id,
            d.Name,
            TryDecode(d.ImageBase64),
            d.IsActive,
            d.CreatedAtUtc,
            d.UpdatedAtUtc,
            d.AdminCount,
            d.UserCount,
            d.LiveMatchCount,
            d.LiveNetsCount);
}
