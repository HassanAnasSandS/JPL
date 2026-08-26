using JPL.Domain;

namespace JPL.Services;

public interface IPlayerService
{
    Task<IReadOnlyList<Player>> GetAllAsync(CancellationToken ct = default);
    /// <summary>Active players only — for team create/update squad pickers.</summary>
    Task<IReadOnlyList<Player>> GetActiveAsync(CancellationToken ct = default);
    Task<Player?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Player> CreateAsync(PlayerDraft draft, CancellationToken ct = default);
    Task<Player> UpdateAsync(Guid id, PlayerDraft draft, CancellationToken ct = default);
    Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed record PlayerDraft(
    string FullName,
    bool IsActive = true,
    string? NickName = null,
    string? PlayingRole = null,
    string? BattingStyle = null,
    string? BowlingStyle = null,
    string? Bio = null);

/// <summary>Players always come from the live API. SQLite is not used for master data.</summary>
public sealed class PlayerService : IPlayerService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly IMatchSquadCache _squadCache;

    public PlayerService(IApiClient api, IAuthService auth, IMatchSquadCache squadCache)
    {
        _api = api;
        _auth = auth;
        _squadCache = squadCache;
    }

    public async Task<IReadOnlyList<Player>> GetAllAsync(CancellationToken ct = default)
    {
        var remote = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
        return remote
            .Where(p => !p.IsDeleted)
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.FullName)
            .Select(ToEntity)
            .ToList();
    }

    public async Task<IReadOnlyList<Player>> GetActiveAsync(CancellationToken ct = default)
    {
        var remote = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
        return remote
            .Where(p => !p.IsDeleted && p.IsActive)
            .OrderBy(p => p.FullName)
            .Select(ToEntity)
            .ToList();
    }

    public async Task<Player?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var remote = await _api.GetPlayerAsync(id, ct).ConfigureAwait(false);
        return remote is null || remote.IsDeleted ? null : ToEntity(remote);
    }

    public async Task<Player> CreateAsync(PlayerDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var name = RequireName(draft.FullName);
        await EnsureUniqueNameAsync(name, excludeId: null, ct).ConfigureAwait(false);

        var remote = await _api.CreatePlayerAsync(
                new
                {
                    id = Guid.NewGuid(),
                    fullName = name,
                    isActive = draft.IsActive,
                    nickName = TrimTo(draft.NickName, 40),
                    playingRole = TrimTo(draft.PlayingRole, 40),
                    battingStyle = TrimTo(draft.BattingStyle, 40),
                    bowlingStyle = TrimTo(draft.BowlingStyle, 60),
                    bio = TrimTo(draft.Bio, 500)
                }, ct)
            .ConfigureAwait(false);
        return ToEntity(remote);
    }

    public async Task<Player> UpdateAsync(Guid id, PlayerDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var name = RequireName(draft.FullName);
        await EnsureUniqueNameAsync(name, excludeId: id, ct).ConfigureAwait(false);

        var remote = await _api.UpdatePlayerAsync(id, new
        {
            id,
            fullName = name,
            isActive = draft.IsActive,
            nickName = TrimTo(draft.NickName, 40),
            playingRole = TrimTo(draft.PlayingRole, 40),
            battingStyle = TrimTo(draft.BattingStyle, 40),
            bowlingStyle = TrimTo(draft.BowlingStyle, 60),
            bio = TrimTo(draft.Bio, 500)
        }, ct)
            .ConfigureAwait(false);
        _squadCache.ClearAll();
        return ToEntity(remote);
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var player = await GetAsync(id, ct).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Player not found.");
        await UpdateAsync(id, new PlayerDraft(
            player.FullName,
            isActive,
            player.NickName,
            player.PlayingRole,
            player.BattingStyle,
            player.BowlingStyle,
            player.Bio), ct).ConfigureAwait(false);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await _api.DeletePlayerAsync(id, ct).ConfigureAwait(false);
    }

    private static Player ToEntity(ApiPlayerDto remote) => new()
    {
        Id = remote.Id,
        FullName = remote.FullName,
        NickName = remote.NickName,
        PlayingRole = remote.PlayingRole,
        BattingStyle = remote.BattingStyle,
        BowlingStyle = remote.BowlingStyle,
        Bio = remote.Bio,
        IsActive = remote.IsActive,
        Version = remote.Version,
        UpdatedAtUtc = remote.UpdatedAtUtc,
        IsDeleted = remote.IsDeleted
    };

    private static string? TrimTo(string? raw, int max)
    {
        var s = raw?.Trim();
        if (string.IsNullOrWhiteSpace(s))
            return null;
        return s.Length <= max ? s : s[..max];
    }

    private async Task EnsureUniqueNameAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var key = name.ToLowerInvariant();
        var remote = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
        var taken = remote.Any(p =>
            !p.IsDeleted
            && (excludeId is null || p.Id != excludeId)
            && string.Equals(p.FullName, key, StringComparison.OrdinalIgnoreCase));
        if (taken)
            throw new InvalidOperationException($"Player name \"{name}\" already exists.");
    }

    private static string RequireName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("Player name is required.");
        if (trimmed.Length > 120)
            throw new ArgumentException("Player name must be 120 characters or fewer.");
        return trimmed;
    }
}
