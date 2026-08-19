using JPL.Domain;

namespace JPL.Services;

public interface ITeamService
{
    Task<IReadOnlyList<TeamListItem>> GetAllAsync(CancellationToken ct = default);
    /// <summary>Active teams only — for match create dropdowns.</summary>
    Task<IReadOnlyList<TeamListItem>> GetActiveAsync(CancellationToken ct = default);
    Task<TeamDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Team> CreateAsync(TeamDraft draft, CancellationToken ct = default);
    Task<Team> UpdateAsync(Guid id, TeamDraft draft, CancellationToken ct = default);
    Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed record TeamDraft(
    string Name,
    Guid? CaptainPlayerId,
    IReadOnlyList<Guid> PlayerIds,
    bool IsActive = true);

public sealed record TeamListItem(
    Guid Id,
    string Name,
    string? CaptainName,
    int PlayerCount,
    bool IsActive)
{
    public string StatusText => IsActive ? "Active" : "Inactive";
    public string ToggleActiveLabel => IsActive ? "Deactivate" : "Activate";
    public string CaptainLine => string.IsNullOrWhiteSpace(CaptainName)
        ? "No captain"
        : $"Captain: {CaptainName} (C)";
}

public sealed record TeamDetail(
    Guid Id,
    string Name,
    Guid? CaptainPlayerId,
    IReadOnlyList<Player> Players,
    bool IsActive);

/// <summary>Teams always come from the live API. SQLite is not used for master data.</summary>
public sealed class TeamService : ITeamService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;

    public TeamService(IApiClient api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    public async Task<IReadOnlyList<TeamListItem>> GetAllAsync(CancellationToken ct = default)
        => await QueryListAsync(activeOnly: false, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<TeamListItem>> GetActiveAsync(CancellationToken ct = default)
        => await QueryListAsync(activeOnly: true, ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<TeamListItem>> QueryListAsync(bool activeOnly, CancellationToken ct)
    {
        var teams = await _api.GetTeamsAsync(ct).ConfigureAwait(false);
        var players = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
        var names = players.Where(p => !p.IsDeleted).ToDictionary(p => p.Id, p => p.FullName);

        IEnumerable<ApiTeamDto> q = teams.Where(t => !t.IsDeleted);
        if (activeOnly)
            q = q.Where(t => t.IsActive);

        return q
            .OrderByDescending(t => t.IsActive)
            .ThenBy(t => t.Name)
            .Select(t => new TeamListItem(
                t.Id,
                t.Name,
                t.CaptainPlayerId is Guid cid ? names.GetValueOrDefault(cid) : null,
                t.PlayerIds?.Count ?? 0,
                t.IsActive))
            .ToList();
    }

    public async Task<TeamDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var team = await _api.GetTeamAsync(id, ct).ConfigureAwait(false);
        if (team is null || team.IsDeleted)
            return null;

        var players = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
        var byId = players.Where(p => !p.IsDeleted).ToDictionary(p => p.Id);
        var squad = (team.PlayerIds ?? Array.Empty<Guid>())
            .Where(pid => byId.ContainsKey(pid))
            .Select(pid => new Player
            {
                Id = pid,
                FullName = byId[pid].FullName,
                IsActive = byId[pid].IsActive,
                Version = byId[pid].Version,
                UpdatedAtUtc = byId[pid].UpdatedAtUtc,
                IsDeleted = byId[pid].IsDeleted
            })
            .OrderBy(p => p.FullName)
            .ToList();

        return new TeamDetail(team.Id, team.Name, team.CaptainPlayerId, squad, team.IsActive);
    }

    public async Task<Team> CreateAsync(TeamDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var name = RequireName(draft.Name);
        var playerIds = DistinctIds(draft.PlayerIds);
        ValidateCaptain(draft.CaptainPlayerId, playerIds);

        var remote = await _api.CreateTeamAsync(new
        {
            id = Guid.NewGuid(),
            name,
            captainPlayerId = draft.CaptainPlayerId,
            playerIds,
            isActive = draft.IsActive
        }, ct).ConfigureAwait(false);

        return ToEntity(remote);
    }

    public async Task<Team> UpdateAsync(Guid id, TeamDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var name = RequireName(draft.Name);
        var playerIds = DistinctIds(draft.PlayerIds);
        ValidateCaptain(draft.CaptainPlayerId, playerIds);

        var remote = await _api.UpdateTeamAsync(id, new
        {
            id,
            name,
            captainPlayerId = draft.CaptainPlayerId,
            playerIds,
            isActive = draft.IsActive
        }, ct).ConfigureAwait(false);

        return ToEntity(remote);
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var detail = await GetAsync(id, ct).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Team not found.");
        var playerIds = detail.Players.Select(p => p.Id).ToList();
        await UpdateAsync(id, new TeamDraft(detail.Name, detail.CaptainPlayerId, playerIds, isActive), ct)
            .ConfigureAwait(false);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await _api.DeleteTeamAsync(id, ct).ConfigureAwait(false);
    }

    private static Team ToEntity(ApiTeamDto remote) => new()
    {
        Id = remote.Id,
        Name = remote.Name,
        CaptainPlayerId = remote.CaptainPlayerId,
        IsActive = remote.IsActive,
        Version = remote.Version,
        UpdatedAtUtc = remote.UpdatedAtUtc,
        IsDeleted = remote.IsDeleted
    };

    private static void ValidateCaptain(Guid? captainId, IReadOnlyList<Guid> playerIds)
    {
        if (captainId is null)
            return;
        if (!playerIds.Contains(captainId.Value))
            throw new InvalidOperationException("Captain must be one of the team players. Add the player to the team first.");
    }

    private static List<Guid> DistinctIds(IReadOnlyList<Guid> ids)
        => ids.Where(id => id != Guid.Empty).Distinct().ToList();

    private static string RequireName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("Team name is required.");
        return trimmed;
    }
}
