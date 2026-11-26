using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public sealed class SchedulerListItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string MetaLine { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string WinnerLine { get; init; } = string.Empty;
    public SchedulerStatus Status { get; init; }
    public bool CanEdit { get; init; }
    public Guid CreatedByUserId { get; init; }
    public string CreatedByUsername { get; init; } = string.Empty;
}

public sealed class SchedulerTeamInput
{
    public Guid? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int GroupNumber { get; init; } = 1;
}

public sealed class SchedulerDetail
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int GroupCount { get; init; }
    public SchedulerRoundRobinType RoundRobinType { get; init; }
    public int QualifyPerGroup { get; init; }
    public SchedulerKnockoutFormat KnockoutFormat { get; init; }
    public SchedulerStatus Status { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public string MetaLine { get; init; } = string.Empty;
    public string WinnerLine { get; init; } = string.Empty;
    public bool HasWinner => !string.IsNullOrWhiteSpace(WinnerLine);
    public bool CanEdit { get; init; }
    public bool GroupStageComplete { get; init; }
    /// <summary>Custom type only — matches each team should play (inferred or stored).</summary>
    public int? MatchesPerTeam { get; init; }
    public QualificationDecision Qualification { get; init; } = new();
    public IReadOnlyList<SchedulerTeam> Teams { get; init; } = Array.Empty<SchedulerTeam>();
    public IReadOnlyList<SchedulerStandingRow> Standings { get; init; } = Array.Empty<SchedulerStandingRow>();
    public IReadOnlyList<SchedulerMatchView> Matches { get; init; } = Array.Empty<SchedulerMatchView>();
}

public sealed class SchedulerCustomPairInput
{
    public Guid TeamAId { get; init; }
    public Guid TeamBId { get; init; }
}

public sealed class SchedulerMatchView
{
    public Guid Id { get; init; }
    public SchedulerMatchStage Stage { get; init; }
    public int GroupNumber { get; init; }
    public int Sequence { get; init; }
    public string Label { get; init; } = string.Empty;
    public string StageTitle { get; init; } = string.Empty;
    public Guid? TeamAId { get; init; }
    public Guid? TeamBId { get; init; }
    public string TeamAName { get; init; } = string.Empty;
    public string TeamBName { get; init; } = string.Empty;
    public SchedulerMatchResult Result { get; init; }
    public string ResultText { get; init; } = string.Empty;
    public bool BothTeamsKnown { get; init; }
    public bool CanRecord { get; set; }
    public bool IsKnockout { get; init; }
    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);
    public string VsLine => $"{TeamAName}  vs  {TeamBName}";
    public string ActionLabel => HasResult ? "Change" : "Result";
    /// <summary>Shown in View / completed mode when Result/Change is hidden.</summary>
    public bool ShowStatusBadge => !CanRecord;
    public string StatusBadge => HasResult
        ? (string.IsNullOrWhiteSpace(ResultText) ? "Played" : ResultText)
        : BothTeamsKnown ? "Upcoming" : "TBD";
}

public interface ITournamentSchedulerService
{
    Task<IReadOnlyList<SchedulerListItem>> GetAllAsync(CancellationToken ct = default);
    Task<ApiPagedResult<SchedulerListItem>> GetPageAsync(int skip, string? q, Guid? createdBy = null, CancellationToken ct = default);
    Task<SchedulerDetail> GetAsync(Guid id, CancellationToken ct = default);
    Task<Guid> SaveAsync(
        Guid? id,
        string name,
        int groupCount,
        SchedulerRoundRobinType roundRobin,
        int qualifyPerGroup,
        SchedulerKnockoutFormat knockout,
        IReadOnlyList<SchedulerTeamInput> teams,
        int? matchesPerTeam = null,
        IReadOnlyList<SchedulerCustomPairInput>? customPairs = null,
        CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task SetMatchResultAsync(Guid tournamentId, Guid matchId, SchedulerMatchResult result, CancellationToken ct = default);
    Task SaveQualificationPicksAsync(Guid tournamentId, IReadOnlyList<Guid> teamIds, CancellationToken ct = default);
}

public sealed class TournamentSchedulerService : ITournamentSchedulerService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly ISchedulerOutboxSyncService _schedulerSync;
    private static readonly JsonSerializerOptions SyncJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public TournamentSchedulerService(
        IApiClient api,
        IAuthService auth,
        IDbContextFactory<JplDbContext> dbFactory,
        ISchedulerOutboxSyncService schedulerSync)
    {
        _api = api;
        _auth = auth;
        _dbFactory = dbFactory;
        _schedulerSync = schedulerSync;
    }

    public async Task<IReadOnlyList<SchedulerListItem>> GetAllAsync(CancellationToken ct = default)
    {
        EnsureCanUse();
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
            return (await _api.GetSchedulerListAsync(ct).ConfigureAwait(false)).Select(ToListItem).ToList();

        var local = await GetLocalListAsync(ct).ConfigureAwait(false);
        return local;
    }

    public async Task<ApiPagedResult<SchedulerListItem>> GetPageAsync(int skip, string? q, Guid? createdBy = null, CancellationToken ct = default)
    {
        EnsureCanUse();
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetSchedulerPageAsync(skip, q, createdBy, ct).ConfigureAwait(false);
            return live.WithItems((live.Items ?? []).Select(ToListItem).ToList());
        }

        var localRows = await GetLocalListAsync(ct).ConfigureAwait(false);
        if (createdBy is Guid ownerId && ownerId != Guid.Empty)
            localRows = localRows.Where(row => row.CreatedByUserId == ownerId).ToList();
        if (!string.IsNullOrWhiteSpace(q))
            localRows = localRows.Where(row => row.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return new ApiPagedResult<SchedulerListItem>(localRows.Skip(Math.Max(0, skip))
            .Take(ListPaging.PageSize).ToList(), localRows.Count, Math.Max(0, skip), ListPaging.PageSize);

    }

    private static SchedulerListItem ToListItem(ApiSchedulerListDto t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        MetaLine = string.IsNullOrWhiteSpace(t.MetaLine)
            ? SchedulerEngine.SetupHint(
                t.GroupCount,
                t.QualifyPerGroup,
                (SchedulerKnockoutFormat)t.KnockoutFormat,
                (SchedulerRoundRobinType)t.RoundRobinType,
                t.TeamCount)
            : t.MetaLine,
        StatusText = string.IsNullOrWhiteSpace(t.StatusText)
            ? SchedulerEngine.StatusText((SchedulerStatus)t.Status)
            : t.StatusText,
        WinnerLine = t.WinnerLine,
        Status = (SchedulerStatus)t.Status,
        CanEdit = t.CanEdit,
        CreatedByUserId = t.CreatedByUserId,
        CreatedByUsername = t.CreatedByUsername ?? string.Empty
    };

    public async Task<SchedulerDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        EnsureCanUse();
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetSchedulerAsync(id, ct).ConfigureAwait(false)
                       ?? throw new InvalidOperationException("Scheduled tournament not found.");
            return ToDetail(FromDto(live));
        }

        var entity = await LoadLocalEntityAsync(id, ct).ConfigureAwait(false);
        if (entity is null || entity.IsDeleted)
            throw new InvalidOperationException("Scheduled tournament not found on this device.");
        if (!entity.HasDetailsCached)
            throw new InvalidOperationException("This scheduler's details are not available locally yet. Run Sync to refresh them.");
        var dirty = TryReseatUnplayedKnockout(entity)
                    || TryEnsureRoundRobinChampion(entity)
                    || TryEnsureKnockoutChampion(entity);
        if (dirty && IsCreator(entity.CreatedByUserId))
        {
            entity.UpdatedAtUtc = DateTime.UtcNow;
            await PersistAndQueueAsync(entity, "Upsert", ct).ConfigureAwait(false);
        }

        return ToDetail(entity);
    }

    public async Task<Guid> SaveAsync(
        Guid? id,
        string name,
        int groupCount,
        SchedulerRoundRobinType roundRobin,
        int qualifyPerGroup,
        SchedulerKnockoutFormat knockout,
        IReadOnlyList<SchedulerTeamInput> teams,
        int? matchesPerTeam = null,
        IReadOnlyList<SchedulerCustomPairInput>? customPairs = null,
        CancellationToken ct = default)
    {
        EnsureCanWrite();
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Enter a tournament name.");

        if (roundRobin == SchedulerRoundRobinType.Custom)
        {
            groupCount = 1;
            // Custom is a single pool — fold every team into group 1 before validate/save.
            teams = teams
                .Select(t => new SchedulerTeamInput
                {
                    Id = t.Id,
                    Name = t.Name,
                    GroupNumber = 1
                })
                .ToList();
        }

        SchedulerEngine.ValidateSetup(
            groupCount,
            roundRobin,
            qualifyPerGroup,
            knockout,
            teams.Select(t => (t.Name.Trim(), t.GroupNumber)).ToList());

        var isCustom = roundRobin == SchedulerRoundRobinType.Custom;
        var pairList = (customPairs ?? Array.Empty<SchedulerCustomPairInput>())
            .Select(p => (p.TeamAId, p.TeamBId))
            .ToList();
        var teamIdsForCustom = new List<Guid>();
        var customComplete = false;
        var customMpt = matchesPerTeam ?? 0;

        if (isCustom)
        {
            // Resolve stable ids up front so pair validation matches persisted teams.
            foreach (var input in teams)
            {
                teamIdsForCustom.Add(input.Id is Guid teamId && teamId != Guid.Empty
                    ? teamId
                    : Guid.NewGuid());
            }

            if (pairList.Count == 0)
            {
                customComplete = false;
            }
            else if (customMpt < 1)
            {
                throw new InvalidOperationException("Select matches per team for Custom.");
            }
            else if (SchedulerEngine.IsCustomPairingComplete(teamIdsForCustom, customMpt, pairList))
            {
                customComplete = true;
            }
            else
            {
                throw new InvalidOperationException(
                    SchedulerEngine.CustomPairingHint(teamIdsForCustom, customMpt, pairList));
            }
        }

        SchedulerTournament entity;
        var previousMatches = new List<SchedulerMatch>();
        var previousRoster = new HashSet<(Guid Id, int GroupNumber)>();
        var prevKnockout = default(SchedulerKnockoutFormat);
        var prevQualify = 0;
        var prevRoundRobin = default(SchedulerRoundRobinType);
        var prevGroupCount = 0;
        string? prevPicks = null;
        var isExisting = false;

        if (id is Guid existing)
        {
            entity = await LoadOwnedAsync(existing, ct).ConfigureAwait(false);
            if (entity.Status == SchedulerStatus.Completed || entity.WinnerTeamId is not null)
                throw new InvalidOperationException("Cannot edit a completed scheduler.");
            isExisting = true;
            previousMatches = entity.Matches.ToList();
            previousRoster = entity.Teams.Select(t => (t.Id, t.GroupNumber)).ToHashSet();
            prevKnockout = entity.KnockoutFormat;
            prevQualify = entity.QualifyPerGroup;
            prevRoundRobin = entity.RoundRobinType;
            prevGroupCount = entity.GroupCount;
            prevPicks = entity.QualificationPicksJson;
            entity.Teams.Clear();
            entity.Matches.Clear();
        }
        else
        {
            entity = new SchedulerTournament
            {
                CreatedByUserId = _auth.Current!.UserId,
                CreatedByUsername = _auth.Current.Username,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        entity.Name = name;
        entity.GroupCount = groupCount;
        entity.RoundRobinType = roundRobin;
        entity.QualifyPerGroup = qualifyPerGroup;
        entity.KnockoutFormat = knockout;
        entity.WinnerTeamId = null;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.IsDeleted = false;

        var order = 0;
        var customIdIndex = 0;
        foreach (var input in teams)
        {
            Guid teamId;
            if (isCustom)
                teamId = teamIdsForCustom[customIdIndex++];
            else
                teamId = input.Id is Guid existingId && existingId != Guid.Empty ? existingId : Guid.NewGuid();

            entity.Teams.Add(new SchedulerTeam
            {
                Id = teamId,
                TournamentId = entity.Id,
                Name = input.Name.Trim(),
                GroupNumber = input.GroupNumber,
                SortOrder = order++
            });
        }

        var preserveKnockout = isExisting
            && previousRoster.SetEquals(entity.Teams.Select(t => (t.Id, t.GroupNumber)))
            && prevKnockout == knockout
            && prevQualify == qualifyPerGroup
            && prevRoundRobin == roundRobin
            && prevGroupCount == groupCount
            && (!isCustom || customComplete);

        IReadOnlyList<(Guid A, Guid B)>? buildPairs = null;
        if (isCustom)
        {
            if (customComplete)
            {
                buildPairs = pairList;
                entity.QualificationPicksJson = preserveKnockout ? prevPicks : null;
            }
            else
            {
                // Draft — teams only; schedule starts after custom fixtures are complete.
                buildPairs = Array.Empty<(Guid, Guid)>();
                entity.QualificationPicksJson = customMpt > 0
                    ? SchedulerEngine.WriteCustomMatchesPerTeam(customMpt)
                    : null;
                preserveKnockout = false;
            }
        }
        else
        {
            entity.QualificationPicksJson = preserveKnockout ? prevPicks : null;
        }

        foreach (var match in SchedulerEngine.RebuildMatches(entity, previousMatches, preserveKnockout, buildPairs))
            entity.Matches.Add(match);

        if (isCustom && !customComplete)
        {
            entity.Status = SchedulerStatus.Draft;
        }
        else if (isExisting)
        {
            if (isCustom && entity.Status == SchedulerStatus.Draft)
                entity.Status = SchedulerStatus.GroupStage;
            RefreshProgress(entity, reseatKnockout: !preserveKnockout);
        }
        else
        {
            entity.Status = SchedulerStatus.GroupStage;
        }

        await PersistAndQueueAsync(entity, "Upsert", ct).ConfigureAwait(false);
        _schedulerSync.RequestSyncNow();
        return entity.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        EnsureCanWrite();
        var entity = await LoadOwnedAsync(id, ct).ConfigureAwait(false);
        if (entity.Status == SchedulerStatus.Completed || entity.WinnerTeamId is not null)
            throw new InvalidOperationException("Cannot delete a completed scheduler.");
        entity.IsDeleted = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await PersistAndQueueAsync(entity, "Delete", ct).ConfigureAwait(false);
        _schedulerSync.RequestSyncNow();
    }

    public async Task SetMatchResultAsync(
        Guid tournamentId,
        Guid matchId,
        SchedulerMatchResult result,
        CancellationToken ct = default)
    {
        EnsureCanWrite();
        var t = await LoadOwnedAsync(tournamentId, ct).ConfigureAwait(false);
        if (t.Status == SchedulerStatus.Draft)
            throw new InvalidOperationException("Finish Custom fixtures in Edit before recording results.");
        if (t.Status == SchedulerStatus.Completed || t.WinnerTeamId is not null)
            throw new InvalidOperationException("Cannot change results on a completed scheduler.");
        var match = t.Matches.FirstOrDefault(m => m.Id == matchId)
            ?? throw new InvalidOperationException("Match not found.");

        var wasGroup = match.Stage == SchedulerMatchStage.Group;
        SchedulerEngine.ApplyMatchResult(match, t.Matches.ToList(), result);

        if (wasGroup)
        {
            t.QualificationPicksJson = null;
            t.WinnerTeamId = null;
        }

        RefreshProgress(t, reseatKnockout: wasGroup);
        t.UpdatedAtUtc = DateTime.UtcNow;
        await PersistAndQueueAsync(t, "Upsert", ct).ConfigureAwait(false);
        _schedulerSync.RequestSyncNow();
    }

    public async Task SaveQualificationPicksAsync(
        Guid tournamentId,
        IReadOnlyList<Guid> teamIds,
        CancellationToken ct = default)
    {
        EnsureCanWrite();
        var t = await LoadOwnedAsync(tournamentId, ct).ConfigureAwait(false);
        if (t.Status == SchedulerStatus.Completed || t.WinnerTeamId is not null)
            throw new InvalidOperationException("Cannot change qualification on a completed scheduler.");
        if (!SchedulerEngine.GroupStageComplete(t.Matches.ToList()))
            throw new InvalidOperationException("Finish every round-robin match before picking knockout teams.");

        var standings = SchedulerEngine.BuildStandings(t.Teams.ToList(), t.Matches.ToList());
        var decision = SchedulerEngine.DecideQualification(t, t.Teams.ToList(), standings);
        var need = t.KnockoutFormat == SchedulerKnockoutFormat.None
            ? 1
            : SchedulerEngine.KnockoutTeamCount(t.KnockoutFormat);
        if (need <= 0)
            throw new InvalidOperationException("This scheduler has no teams to qualify.");
        var qualify = t.KnockoutFormat == SchedulerKnockoutFormat.None ? 0 : t.QualifyPerGroup;
        SchedulerEngine.EnsurePicksValid(
            teamIds,
            decision.AutoQualifiedIds,
            decision.PickFrom,
            t.Teams.ToList(),
            qualify,
            t.GroupCount,
            need);

        t.QualificationPicksJson = SchedulerEngine.WritePicks(teamIds);
        RefreshProgress(t, reseatKnockout: true);
        t.UpdatedAtUtc = DateTime.UtcNow;
        await PersistAndQueueAsync(t, "Upsert", ct).ConfigureAwait(false);
        _schedulerSync.RequestSyncNow();
    }

    private async Task<SchedulerTournament> LoadOwnedAsync(Guid id, CancellationToken ct)
    {
        var local = await LoadLocalEntityAsync(id, ct).ConfigureAwait(false);
        if (local is not null && await HasPendingAsync(id, ct).ConfigureAwait(false))
        {
            if (!local.HasDetailsCached)
                throw new InvalidOperationException("This scheduler's details are not available on this device yet.");
            EnsureIsCreator(local.CreatedByUserId);
            return local;
        }
        try
        {
            var dto = await _api.GetSchedulerAsync(id, ct).ConfigureAwait(false);
            if (dto is not null)
            {
                var remote = FromDto(dto);
                EnsureIsCreator(remote.CreatedByUserId);
                await CacheDetailAsync(remote, ct).ConfigureAwait(false);
                return remote;
            }
            if (local is not null)
            {
                await MarkLocalDeletedAsync(id, ct).ConfigureAwait(false);
                throw new InvalidOperationException("Scheduled tournament was deleted on the server.");
            }
        }
        catch (Exception ex) when (NetworkUi.IsNetworkFailure(ex))
        {
            if (local is not null && !local.IsDeleted)
            {
                EnsureIsCreator(local.CreatedByUserId);
                return local;
            }
        }
        if (local is not null && !local.IsDeleted)
        {
            if (!local.HasDetailsCached)
                throw new InvalidOperationException("This scheduler's details have not been synced to this device yet. Connect once to open it offline later.");
            EnsureIsCreator(local.CreatedByUserId);
            return local;
        }
        throw new InvalidOperationException("Scheduled tournament is not available on this device.");
    }

    private SchedulerTournament FromDto(ApiSchedulerDetailDto dto)
    {
        var entity = new SchedulerTournament
        {
            Id = dto.Id,
            LeagueId = Guid.Empty,
            Name = dto.Name,
            GroupCount = dto.GroupCount,
            RoundRobinType = (SchedulerRoundRobinType)dto.RoundRobinType,
            QualifyPerGroup = dto.QualifyPerGroup,
            KnockoutFormat = (SchedulerKnockoutFormat)dto.KnockoutFormat,
            Status = (SchedulerStatus)dto.Status,
            WinnerTeamId = dto.WinnerTeamId,
            QualificationPicksJson = dto.QualificationPicksJson,
            CreatedByUserId = dto.CreatedByUserId == Guid.Empty ? _auth.Current?.UserId ?? Guid.Empty : dto.CreatedByUserId,
            CreatedByUsername = dto.CreatedByUsername,
            Version = dto.Version,
            CreatedAtUtc = dto.CreatedAtUtc,
            UpdatedAtUtc = dto.UpdatedAtUtc,
            HasDetailsCached = true
        };
        foreach (var team in dto.Teams)
        {
            entity.Teams.Add(new SchedulerTeam
            {
                Id = team.Id,
                TournamentId = dto.Id,
                Name = team.Name,
                GroupNumber = team.GroupNumber,
                SortOrder = team.SortOrder
            });
        }

        foreach (var match in dto.Matches)
        {
            entity.Matches.Add(new SchedulerMatch
            {
                Id = match.Id,
                TournamentId = dto.Id,
                Stage = (SchedulerMatchStage)match.Stage,
                GroupNumber = match.GroupNumber,
                Sequence = match.Sequence,
                Label = match.Label,
                TeamAId = match.TeamAId,
                TeamBId = match.TeamBId,
                TeamASlot = match.TeamASlot,
                TeamBSlot = match.TeamBSlot,
                Result = (SchedulerMatchResult)match.Result,
                WinnerFeedsMatchId = match.WinnerFeedsMatchId,
                WinnerFeedsIsTeamA = match.WinnerFeedsIsTeamA,
                LoserFeedsMatchId = match.LoserFeedsMatchId,
                LoserFeedsIsTeamA = match.LoserFeedsIsTeamA
            });
        }

        return entity;
    }

    private static object ToUpsert(SchedulerTournament t) => new
    {
        t.Id,
        t.Name,
        t.GroupCount,
        RoundRobinType = (byte)t.RoundRobinType,
        t.QualifyPerGroup,
        KnockoutFormat = (byte)t.KnockoutFormat,
        Status = (byte)t.Status,
        t.WinnerTeamId,
        t.QualificationPicksJson,
        Teams = t.Teams.Select(x => new
        {
            x.Id,
            x.Name,
            x.GroupNumber,
            x.SortOrder
        }).ToList(),
        Matches = t.Matches.Select(x => new
        {
            x.Id,
            Stage = (byte)x.Stage,
            x.GroupNumber,
            x.Sequence,
            x.Label,
            x.TeamAId,
            x.TeamBId,
            x.TeamASlot,
            x.TeamBSlot,
            Result = (byte)x.Result,
            x.WinnerFeedsMatchId,
            x.WinnerFeedsIsTeamA,
            x.LoserFeedsMatchId,
            x.LoserFeedsIsTeamA
        }).ToList()
    };

    private async Task CacheListAsync(IReadOnlyList<ApiSchedulerListDto> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return;
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        foreach (var row in rows)
        {
            if (await db.SchedulerSyncOutbox.AsNoTracking().AnyAsync(item => item.TournamentId == row.Id
                    && item.Status != SyncOutboxStatus.Completed, ct).ConfigureAwait(false))
                continue;

            var local = await db.SchedulerTournaments.IgnoreQueryFilters().Include(item => item.Teams)
                .FirstOrDefaultAsync(item => item.Id == row.Id && item.LeagueId == db.CurrentLeagueId, ct)
                .ConfigureAwait(false);
            if (local is null)
            {
                local = new SchedulerTournament { Id = row.Id };
                db.SchedulerTournaments.Add(local);
            }
            local.Name = row.Name;
            local.GroupCount = row.GroupCount;
            local.RoundRobinType = (SchedulerRoundRobinType)row.RoundRobinType;
            local.QualifyPerGroup = row.QualifyPerGroup;
            local.KnockoutFormat = (SchedulerKnockoutFormat)row.KnockoutFormat;
            local.Status = (SchedulerStatus)row.Status;
            local.CreatedByUserId = row.CreatedByUserId;
            local.CreatedByUsername = row.CreatedByUsername ?? string.Empty;
            local.Version = row.Version;
            local.UpdatedAtUtc = row.UpdatedAtUtc;
            local.IsDeleted = false;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task CacheDetailAsync(SchedulerTournament source, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (await db.SchedulerSyncOutbox.AsNoTracking().AnyAsync(item => item.TournamentId == source.Id
                && item.Status != SyncOutboxStatus.Completed, ct).ConfigureAwait(false))
            return;

        var local = await db.SchedulerTournaments.IgnoreQueryFilters()
            .Include(item => item.Teams)
            .Include(item => item.Matches)
            .FirstOrDefaultAsync(item => item.Id == source.Id && item.LeagueId == db.CurrentLeagueId, ct)
            .ConfigureAwait(false);
        if (local is null)
        {
            local = new SchedulerTournament { Id = source.Id };
            db.SchedulerTournaments.Add(local);
        }
        var cachedUsername = local.CreatedByUsername;
        CopyScalars(local, source);
        local.CreatedByUsername = string.IsNullOrWhiteSpace(source.CreatedByUsername)
            ? cachedUsername
            : source.CreatedByUsername;
        ReconcileTeams(db, local, source.Teams);
        ReconcileMatches(db, local, source.Matches);
        local.HasDetailsCached = true;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<SchedulerTournament?> LoadLocalEntityAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.SchedulerTournaments.AsNoTracking()
            .Include(item => item.Teams)
            .Include(item => item.Matches)
            .FirstOrDefaultAsync(item => item.Id == id, ct).ConfigureAwait(false);
    }

    private async Task<bool> HasPendingAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.SchedulerSyncOutbox.AsNoTracking().AnyAsync(item => item.TournamentId == id
            && item.Status != SyncOutboxStatus.Completed, ct).ConfigureAwait(false);
    }

    private async Task MarkLocalDeletedAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await db.SchedulerTournaments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == id && item.LeagueId == db.CurrentLeagueId, ct)
            .ConfigureAwait(false);
        if (row is null)
            return;
        row.IsDeleted = true;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<List<SchedulerListItem>> GetLocalListAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var rows = await db.SchedulerTournaments.AsNoTracking()
            .Include(item => item.Teams)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        var session = _auth.Current;
        // Local DB is already home-league scoped; League Admin/User can see league schedules.
        // SuperAdmin platform: own rows only when not platform (local rarely used).
        rows = rows.Where(row => session is not null && (
            session.CanManagePlatform
            || session.Role is AppRole.Admin or AppRole.User
            || row.CreatedByUserId == session.UserId)).ToList();
        return rows.Select(row =>
        {
            var winner = row.WinnerTeamId is Guid winnerId
                ? row.Teams.FirstOrDefault(team => team.Id == winnerId)?.Name
                : null;
            return new SchedulerListItem
            {
                Id = row.Id,
                Name = row.Name,
                MetaLine = SchedulerEngine.SetupHint(row.GroupCount, row.QualifyPerGroup,
                    row.KnockoutFormat, row.RoundRobinType, row.Teams.Count,
                    row.RoundRobinType == SchedulerRoundRobinType.Custom
                        ? SchedulerEngine.TryReadCustomMatchesPerTeam(row.QualificationPicksJson)
                        : null),
                StatusText = SchedulerEngine.StatusText(row.Status),
                WinnerLine = winner is null ? string.Empty : $"Champion: {winner}",
                Status = row.Status,
                CanEdit = session is not null
                    && session.CanWrite
                    && session.UserId == row.CreatedByUserId
                    && row.Status != SchedulerStatus.Completed,
                CreatedByUserId = row.CreatedByUserId,
                CreatedByUsername = row.CreatedByUsername
            };
        }).ToList();
    }

    private async Task PersistAndQueueAsync(SchedulerTournament source, string operation, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var local = await db.SchedulerTournaments.IgnoreQueryFilters()
            .Include(item => item.Teams)
            .Include(item => item.Matches)
            .FirstOrDefaultAsync(item => item.Id == source.Id && item.LeagueId == db.CurrentLeagueId, ct)
            .ConfigureAwait(false);

        var pending = await db.SchedulerSyncOutbox
            .Where(item => item.TournamentId == source.Id && item.Status != SyncOutboxStatus.Completed)
            .OrderBy(item => item.CreatedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        if (pending.Any(item => item.Status == SyncOutboxStatus.Failed))
            throw new InvalidOperationException("This scheduler has a sync conflict. Run Sync now to see the conflict details before editing it again.");

        var expectedVersion = (local?.Version ?? source.Version) + pending.Count;
        var knownServerVersion = local?.Version ?? source.Version;
        if (local is null)
        {
            local = new SchedulerTournament { Id = source.Id, Version = source.Version };
            db.SchedulerTournaments.Add(local);
        }
        CopyScalars(local, source);
        local.Version = Math.Max(local.Version, knownServerVersion);
        local.HasDetailsCached = true;
        ReconcileTeams(db, local, source.Teams);
        ReconcileMatches(db, local, source.Matches);

        var operationId = Guid.NewGuid();
        var request = new ApiSchedulerSyncRequest
        {
            OperationId = operationId,
            TournamentId = source.Id,
            ExpectedVersion = expectedVersion,
            Operation = operation,
            Payload = ToUpsert(source)
        };
        db.SchedulerSyncOutbox.Add(new SchedulerSyncOutboxItem
        {
            Id = Guid.NewGuid(),
            TournamentId = source.Id,
            OperationId = operationId,
            ExpectedVersion = expectedVersion,
            Operation = operation,
            PayloadJson = JsonSerializer.Serialize(request, SyncJson),
            Status = SyncOutboxStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static void CopyScalars(SchedulerTournament target, SchedulerTournament source)
    {
        target.Name = source.Name;
        target.GroupCount = source.GroupCount;
        target.RoundRobinType = source.RoundRobinType;
        target.QualifyPerGroup = source.QualifyPerGroup;
        target.KnockoutFormat = source.KnockoutFormat;
        target.Status = source.Status;
        target.WinnerTeamId = source.WinnerTeamId;
        target.CreatedByUserId = source.CreatedByUserId;
        target.QualificationPicksJson = source.QualificationPicksJson;
        target.CreatedAtUtc = source.CreatedAtUtc;
        target.UpdatedAtUtc = source.UpdatedAtUtc;
        target.IsDeleted = source.IsDeleted;
        target.Version = source.Version;
        target.CreatedByUsername = source.CreatedByUsername;
        target.HasDetailsCached = source.HasDetailsCached || source.Teams.Count > 0 || source.Matches.Count > 0;
    }

    private static void ReconcileTeams(JplDbContext db, SchedulerTournament target, IEnumerable<SchedulerTeam> source)
    {
        var desired = source.ToDictionary(item => item.Id);
        foreach (var old in target.Teams.ToList())
        {
            if (!desired.Remove(old.Id, out var value))
            {
                db.SchedulerTeams.Remove(old);
                target.Teams.Remove(old);
                continue;
            }
            old.Name = value.Name;
            old.GroupNumber = value.GroupNumber;
            old.SortOrder = value.SortOrder;
        }
        foreach (var value in desired.Values)
            target.Teams.Add(new SchedulerTeam
            {
                Id = value.Id,
                TournamentId = target.Id,
                Name = value.Name,
                GroupNumber = value.GroupNumber,
                SortOrder = value.SortOrder
            });
    }

    private static void ReconcileMatches(JplDbContext db, SchedulerTournament target, IEnumerable<SchedulerMatch> source)
    {
        var desired = source.ToDictionary(item => item.Id);
        foreach (var old in target.Matches.ToList())
        {
            if (!desired.Remove(old.Id, out var value))
            {
                db.SchedulerMatches.Remove(old);
                target.Matches.Remove(old);
                continue;
            }
            CopyMatch(old, value);
        }
        foreach (var value in desired.Values)
        {
            var match = new SchedulerMatch { Id = value.Id, TournamentId = target.Id };
            CopyMatch(match, value);
            target.Matches.Add(match);
        }
    }

    private static void CopyMatch(SchedulerMatch target, SchedulerMatch source)
    {
        target.Stage = source.Stage;
        target.GroupNumber = source.GroupNumber;
        target.Sequence = source.Sequence;
        target.Label = source.Label;
        target.TeamAId = source.TeamAId;
        target.TeamBId = source.TeamBId;
        target.TeamASlot = source.TeamASlot;
        target.TeamBSlot = source.TeamBSlot;
        target.Result = source.Result;
        target.WinnerFeedsMatchId = source.WinnerFeedsMatchId;
        target.WinnerFeedsIsTeamA = source.WinnerFeedsIsTeamA;
        target.LoserFeedsMatchId = source.LoserFeedsMatchId;
        target.LoserFeedsIsTeamA = source.LoserFeedsIsTeamA;
    }

    private void RefreshProgress(SchedulerTournament t, bool reseatKnockout)
    {
        var matches = t.Matches.ToList();
        var teams = t.Teams.ToList();
        var standings = SchedulerEngine.BuildStandings(teams, matches);
        var groupDone = SchedulerEngine.GroupStageComplete(matches);
        t.WinnerTeamId = null;
        t.Status = SchedulerStatus.GroupStage;

        if (!groupDone)
        {
            SchedulerEngine.ClearKnockoutProgress(matches);
            return;
        }

        // Round-robin only — table leader is champion (tie → manual pick).
        if (t.KnockoutFormat == SchedulerKnockoutFormat.None)
        {
            SchedulerEngine.ClearKnockoutProgress(matches);
            var championDecision = SchedulerEngine.DecideQualification(t, teams, standings);
            if (championDecision.NeedsManualPick)
                return;

            t.WinnerTeamId = championDecision.QualifiedInSeedOrder.Count > 0
                ? championDecision.QualifiedInSeedOrder[0]
                : null;
            t.Status = t.WinnerTeamId is not null
                ? SchedulerStatus.Completed
                : SchedulerStatus.GroupStage;
            return;
        }

        var decision = SchedulerEngine.DecideQualification(t, teams, standings);
        if (decision.NeedsManualPick)
        {
            SchedulerEngine.ClearKnockoutProgress(matches);
            return;
        }

        if (reseatKnockout)
        {
            SchedulerEngine.ClearKnockoutProgress(matches);
            SchedulerEngine.AssignOpeningKnockoutSeeds(matches, decision.QualifiedInSeedOrder, standings);
        }

        var final = matches.FirstOrDefault(m => m.Stage == SchedulerMatchStage.Final);
        if (final?.Result is SchedulerMatchResult.TeamAWon or SchedulerMatchResult.TeamBWon)
            t.WinnerTeamId = SchedulerEngine.WinnerOf(final);

        t.Status = t.WinnerTeamId is not null
            ? SchedulerStatus.Completed
            : SchedulerStatus.Knockout;
    }

    /// <summary>
    /// Re-applies group-vs-group knockout pairing when the group stage is done
    /// and no knockout result has been recorded yet.
    /// </summary>
    private bool TryReseatUnplayedKnockout(SchedulerTournament t)
    {
        if (t.KnockoutFormat == SchedulerKnockoutFormat.None)
            return false;

        var matches = t.Matches.ToList();
        if (!SchedulerEngine.GroupStageComplete(matches))
            return false;
        if (matches.Any(m => m.Stage != SchedulerMatchStage.Group && m.Result != SchedulerMatchResult.None))
            return false;

        var before = matches
            .Where(m => m.Stage != SchedulerMatchStage.Group)
            .Select(m => (m.Id, m.TeamAId, m.TeamBId))
            .ToList();
        RefreshProgress(t, reseatKnockout: true);
        return !before.SequenceEqual(
            t.Matches
                .Where(m => m.Stage != SchedulerMatchStage.Group)
                .Select(m => (m.Id, m.TeamAId, m.TeamBId)));
    }

    /// <summary>
    /// Knockout = No tournaments completed before champion logic had no WinnerTeamId.
    /// Recompute table leader (or leave pending for tie modal) and persist when changed.
    /// </summary>
    private bool TryEnsureRoundRobinChampion(SchedulerTournament t)
    {
        if (t.KnockoutFormat != SchedulerKnockoutFormat.None)
            return false;
        if (t.WinnerTeamId is not null)
            return false;
        if (!SchedulerEngine.GroupStageComplete(t.Matches.ToList()))
            return false;

        var beforeWinner = t.WinnerTeamId;
        var beforeStatus = t.Status;
        var beforePicks = t.QualificationPicksJson;
        RefreshProgress(t, reseatKnockout: true);
        return t.WinnerTeamId != beforeWinner
            || t.Status != beforeStatus
            || !string.Equals(t.QualificationPicksJson, beforePicks, StringComparison.Ordinal);
    }

    /// <summary>
    /// Knockout completes that never stored WinnerTeamId — read Final result only (no reseat).
    /// </summary>
    private bool TryEnsureKnockoutChampion(SchedulerTournament t)
    {
        if (t.KnockoutFormat == SchedulerKnockoutFormat.None)
            return false;
        if (t.WinnerTeamId is not null)
            return false;

        var final = t.Matches.FirstOrDefault(m => m.Stage == SchedulerMatchStage.Final);
        if (final?.Result is not (SchedulerMatchResult.TeamAWon or SchedulerMatchResult.TeamBWon))
            return false;

        t.WinnerTeamId = SchedulerEngine.WinnerOf(final);
        t.Status = SchedulerStatus.Completed;
        return t.WinnerTeamId is not null;
    }

    private SchedulerDetail ToDetail(SchedulerTournament t)
    {
        var teams = t.Teams.ToList();
        var matches = t.Matches.OrderBy(m => m.Sequence).ToList();
        var standings = SchedulerEngine.BuildStandings(teams, matches);
        var groupDone = SchedulerEngine.GroupStageComplete(matches);
        var qualification = groupDone
            ? SchedulerEngine.DecideQualification(t, teams, standings)
            : new QualificationDecision();
        SchedulerEngine.ApplyQualificationMarks(
            standings, matches, t.QualifyPerGroup, groupDone, qualification);
        var names = teams.ToDictionary(x => x.Id, x => x.Name);
        string NameOf(Guid? id, string? slot)
        {
            if (id is Guid teamId && names.TryGetValue(teamId, out var n))
                return n;
            return string.IsNullOrWhiteSpace(slot) ? "TBD" : slot!;
        }

        var winnerName = t.WinnerTeamId is Guid wid && names.TryGetValue(wid, out var w) ? w : null;
        var matchesPerTeam = t.RoundRobinType == SchedulerRoundRobinType.Custom
            ? SchedulerEngine.InferMatchesPerTeam(matches, teams.Count)
              ?? SchedulerEngine.TryReadCustomMatchesPerTeam(t.QualificationPicksJson)
            : null;

        return new SchedulerDetail
        {
            Id = t.Id,
            Name = t.Name,
            GroupCount = t.GroupCount,
            RoundRobinType = t.RoundRobinType,
            QualifyPerGroup = t.QualifyPerGroup,
            KnockoutFormat = t.KnockoutFormat,
            Status = t.Status,
            StatusText = SchedulerEngine.StatusText(t.Status),
            MetaLine = SchedulerEngine.SetupHint(
                t.GroupCount, t.QualifyPerGroup, t.KnockoutFormat, t.RoundRobinType, teams.Count, matchesPerTeam),
            WinnerLine = winnerName is null ? string.Empty : $"Champion: {winnerName}",
            CanEdit = IsCreator(t.CreatedByUserId)
                && t.Status != SchedulerStatus.Completed,
            GroupStageComplete = groupDone,
            MatchesPerTeam = matchesPerTeam,
            Qualification = qualification,
            Teams = teams,
            Standings = standings,
            Matches = matches.Select(m =>
            {
                var both = m.TeamAId is not null && m.TeamBId is not null && m.TeamAId != m.TeamBId;
                var knockout = m.Stage != SchedulerMatchStage.Group;
                var canRecord = both;
                if (knockout && canRecord)
                {
                    try { SchedulerEngine.ValidateKnockoutReady(m, matches); }
                    catch { canRecord = m.Result != SchedulerMatchResult.None; }
                }

                return new SchedulerMatchView
                {
                    Id = m.Id,
                    Stage = m.Stage,
                    GroupNumber = m.GroupNumber,
                    Sequence = m.Sequence,
                    Label = m.Label,
                    StageTitle = StageTitle(m),
                    TeamAId = m.TeamAId,
                    TeamBId = m.TeamBId,
                    TeamAName = NameOf(m.TeamAId, m.TeamASlot),
                    TeamBName = NameOf(m.TeamBId, m.TeamBSlot),
                    Result = m.Result,
                    ResultText = ResultText(m, names),
                    BothTeamsKnown = both,
                    CanRecord = canRecord,
                    IsKnockout = knockout
                };
            }).ToList()
        };
    }

    private static string StageTitle(SchedulerMatch m) => m.Stage switch
    {
        SchedulerMatchStage.Group => m.GroupNumber > 0 && m.GroupNumber != 0
            ? $"Group {m.GroupNumber}"
            : "Round robin",
        SchedulerMatchStage.QuarterFinal => "Quarter-finals",
        SchedulerMatchStage.Playoff => "Playoffs",
        SchedulerMatchStage.SemiFinal => "Semi-finals",
        SchedulerMatchStage.Final => "Final",
        _ => m.Label
    };

    private static string ResultText(SchedulerMatch m, IReadOnlyDictionary<Guid, string> names)
    {
        return m.Result switch
        {
            SchedulerMatchResult.TeamAWon when m.TeamAId is Guid id =>
                names.TryGetValue(id, out var n) ? $"{n} won" : "Team A won",
            SchedulerMatchResult.TeamBWon when m.TeamBId is Guid id =>
                names.TryGetValue(id, out var n) ? $"{n} won" : "Team B won",
            SchedulerMatchResult.Tied => "Tied",
            _ => string.Empty
        };
    }

    private void EnsureCanUse()
    {
        if (_auth.Current is null || _auth.Current.IsGuest)
            throw new InvalidOperationException("Login required to use Tournament Scheduler.");
    }

    private void EnsureCanWrite()
    {
        EnsureCanUse();
        if (_auth.Current?.CanWrite != true)
            throw new InvalidOperationException("League Admin required to manage Tournament Scheduler.");
    }

    private bool IsCreator(Guid createdByUserId)
        => _auth.Current is { CanWrite: true } session && session.UserId == createdByUserId;

    private void EnsureIsCreator(Guid createdByUserId)
    {
        EnsureCanWrite();
        if (!IsCreator(createdByUserId))
            throw new InvalidOperationException("Only the League Admin who created this tournament can edit it.");
    }
}
