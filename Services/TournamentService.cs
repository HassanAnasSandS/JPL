using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public sealed class TournamentListItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string MetaLine { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string WinnerLine { get; init; } = string.Empty;
    public byte Status { get; init; }
    /// <summary>All local tournament matches finished, outbox still pending — show Sync.</summary>
    public bool NeedsSync { get; init; }
}

public sealed class TournamentDetail
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int GroupCount { get; init; }
    public int QualifyPerGroup { get; init; }
    public TournamentKnockoutFormat KnockoutFormat { get; init; }
    public TournamentRoundRobinType RoundRobinType { get; init; }
    public int MatchesPerTeam { get; init; }
    public string? CustomPairsJson { get; init; }
    public bool CustomFixturesReady { get; init; } = true;
    public Guid VenueId { get; init; }
    public string VenueName { get; init; } = string.Empty;
    public Guid? FormatId { get; init; }
    public byte Format { get; init; }
    public string FormatText { get; init; } = string.Empty;
    public int OversLimit { get; init; }
    public OversMode OversMode { get; init; }
    public bool LastBatsmanCanBatAsSingleWicket { get; init; }
    public int BowlerMaxOvers { get; init; }
    public byte Status { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public Guid? WinnerTeamId { get; set; }
    public string? WinnerTeamName { get; set; }
    public Guid? PlayerOfTheTournamentPlayerId { get; set; }
    public string? PlayerOfTheTournamentName { get; set; }
    public Guid? BestBatsmanPlayerId { get; set; }
    public string? BestBatsmanName { get; set; }
    public Guid? BestBowlerPlayerId { get; set; }
    public string? BestBowlerName { get; set; }
    public Guid? BestFielderPlayerId { get; set; }
    public string? BestFielderName { get; set; }
    public IReadOnlyList<TournamentTeamItem> Teams { get; init; } = Array.Empty<TournamentTeamItem>();
    public IReadOnlyList<TournamentFixtureItem> Fixtures { get; init; } = Array.Empty<TournamentFixtureItem>();
    public IReadOnlyList<TournamentStandingItem> Standings { get; set; } = Array.Empty<TournamentStandingItem>();
}

public sealed class TournamentTeamItem
{
    public Guid TeamId { get; init; }
    public string TeamName { get; init; } = string.Empty;
    public int GroupNumber { get; init; }
}

public sealed class TournamentFixtureItem
{
    public Guid Id { get; init; }
    public string Label { get; init; } = string.Empty;
    public string StageText { get; init; } = string.Empty;
    public int? GroupNumber { get; init; }
    public int Sequence { get; init; }
    public Guid? TeamAId { get; set; }
    public Guid? TeamBId { get; set; }
    public string TeamAName { get; set; } = string.Empty;
    public string TeamBName { get; set; } = string.Empty;
    public Guid? MatchId { get; set; }
    public string MatchStatusText { get; set; } = string.Empty;
    public string ResultText { get; set; } = string.Empty;
    public bool CanStart { get; set; }
    public bool CanResume { get; set; }
    public bool CanOpenScorecard { get; set; }
    public string VsLine => $"{TeamAName}  vs  {TeamBName}";
    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);
    public string ScorecardLabel =>
        string.Equals(MatchStatusText, "Live", StringComparison.OrdinalIgnoreCase)
            ? "Main Score"
            : "Scorecard";
}

public sealed class TournamentStandingItem
{
    public int GroupNumber { get; init; }
    public Guid TeamId { get; init; }
    public string TeamName { get; init; } = string.Empty;
    public int Rank { get; init; }
    public int Played { get; init; }
    public int Won { get; init; }
    public int Lost { get; init; }
    public int Tied { get; init; }
    public int Points { get; init; }
    public double NetRunRate { get; init; }
    public string NrrText => NetRunRate.ToString("+0.000;-0.000;0.000");
    public string RecordLine => $"P{Played} W{Won} L{Lost} T{Tied}";
    /// <summary>"Q" qualified, "E" eliminated, empty = still undecided.</summary>
    public string QualMark { get; set; } = string.Empty;
    public bool IsQualified => QualMark == "Q";
    public bool IsEliminated => QualMark == "E";
}

public sealed record TournamentDraft(
    string Name,
    int GroupCount,
    int QualifyPerGroup,
    Guid VenueId,
    byte Format,
    int OversLimit,
    bool LastBatsmanCanBatAsSingleWicket,
    int BowlerMaxOvers,
    OversMode OversMode,
    IReadOnlyList<(Guid TeamId, int GroupNumber)> Teams,
    Guid? FormatId = null,
    TournamentRoundRobinType RoundRobinType = TournamentRoundRobinType.Single,
    int MatchesPerTeam = 0,
    string? CustomPairsJson = null,
    TournamentKnockoutFormat KnockoutFormat = TournamentKnockoutFormat.SemiFinals);

public interface ITournamentService
{
    Task<IReadOnlyList<TournamentListItem>> GetAllAsync(CancellationToken ct = default);
    Task<ApiPagedResult<TournamentListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default);
    Task<TournamentDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<TournamentDetail> CreateAsync(TournamentDraft draft, CancellationToken ct = default);
    Task<TournamentDetail> UpdateAsync(Guid id, TournamentDraft draft, CancellationToken ct = default);
    Task<TournamentDetail> GenerateAsync(Guid id, CancellationToken ct = default);
    Task LinkMatchAsync(Guid tournamentId, Guid fixtureId, Guid matchId, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>True when tournament is fully finished locally (Final done / RR champion) and may be synced.</summary>
    Task<bool> IsReadyToSyncAsync(Guid tournamentId, CancellationToken ct = default);
    Task EnsureReadyToSyncAsync(Guid tournamentId, CancellationToken ct = default);
}

public sealed class TournamentService : ITournamentService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly ISyncService _sync;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IOutboxWriter _outbox;
    private readonly ISyncTrigger _syncTrigger;

    public TournamentService(
        IApiClient api,
        IAuthService auth,
        ILeagueContext league,
        ISyncService sync,
        IDbContextFactory<JplDbContext> dbFactory,
        IOutboxWriter outbox,
        ISyncTrigger syncTrigger)
    {
        _api = api;
        _auth = auth;
        _league = league;
        _sync = sync;
        _dbFactory = dbFactory;
        _outbox = outbox;
        _syncTrigger = syncTrigger;
    }

    public async Task<IReadOnlyList<TournamentListItem>> GetAllAsync(CancellationToken ct = default)
    {
        var canUseLocal = LeagueSyncPolicy.UsesLocalStorage(_auth.Current);
        if (canUseLocal)
            return await LoadLocalListAsync(ct).ConfigureAwait(false);

        try
        {
            var remote = await _api.GetTournamentsAsync(ct).ConfigureAwait(false);
            var pendingTours = await _sync.GetPendingTournamentIdsAsync(ct).ConfigureAwait(false);
            var list = new List<TournamentListItem>();
            foreach (var t in remote)
            {
                var pending = pendingTours.Contains(t.Id);
                var ready = pending && await IsReadyToSyncAsync(t.Id, ct).ConfigureAwait(false);
                list.Add(new TournamentListItem
                {
                    Id = t.Id,
                    Name = t.Name,
                    Status = t.Status,
                    StatusText = t.StatusText,
                    MetaLine = $"{t.GroupCount} group{(t.GroupCount == 1 ? "" : "s")} · Qualify {t.QualifyPerGroup} · {t.TeamCount} teams · {t.FixtureCount} fixtures",
                    WinnerLine = string.IsNullOrWhiteSpace(t.WinnerTeamName) ? "" : $"Champion: {t.WinnerTeamName}",
                    NeedsSync = ready
                });
            }
            return list;
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex))
        {
            if (canUseLocal)
                return await LoadLocalListAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<IReadOnlyList<TournamentListItem>> LoadLocalListAsync(CancellationToken ct)
    {
        await using var localDb = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var ids = await localDb.Tournaments.AsNoTracking().OrderByDescending(row => row.UpdatedAtUtc)
            .Select(row => row.Id).ToListAsync(ct).ConfigureAwait(false);
        var pending = await _sync.GetPendingTournamentIdsAsync(ct).ConfigureAwait(false);
        var result = new List<TournamentListItem>(ids.Count);
        foreach (var id in ids)
        {
            var detail = await GetLocalDetailAsync(id, ct).ConfigureAwait(false);
            if (detail is null) continue;
            result.Add(ToListItem(detail, pending.Contains(id) && IsTournamentFullyComplete(detail)));
        }
        return result;
    }

    public async Task<ApiPagedResult<TournamentListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default)
    {
        var local = await GetAllAsync(ct).ConfigureAwait(false);
        var filteredLocal = string.IsNullOrWhiteSpace(q) ? local
            : local.Where(row => row.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var localRows = filteredLocal.ToList();
        return new ApiPagedResult<TournamentListItem>(localRows.Skip(Math.Max(0, skip))
            .Take(ListPaging.PageSize).ToList(), localRows.Count, Math.Max(0, skip), ListPaging.PageSize);
    }

    public async Task<TournamentDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var canUseLocal = LeagueSyncPolicy.UsesLocalStorage(_auth.Current);
        if (canUseLocal)
            return await GetLocalDetailAsync(id, ct).ConfigureAwait(false);

        try
        {
            var dto = await _api.GetTournamentAsync(id, ct).ConfigureAwait(false);
            if (dto is null) return canUseLocal ? await GetLocalDetailAsync(id, ct).ConfigureAwait(false) : null;
            var detail = ToDetail(dto);
            if (canUseLocal)
                await OverlayLocalMatchStateAsync(detail, ct).ConfigureAwait(false);
            return detail;
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex))
        {
            if (canUseLocal)
                return await GetLocalDetailAsync(id, ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<TournamentDetail> CreateAsync(TournamentDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        Validate(draft);
        var now = DateTime.UtcNow;
        var entity = new Tournament
        {
            Id = Guid.NewGuid(), Name = draft.Name.Trim(),
            GroupCount = draft.RoundRobinType == TournamentRoundRobinType.Custom ? 1 : draft.GroupCount,
            QualifyPerGroup = draft.QualifyPerGroup, KnockoutFormat = draft.KnockoutFormat,
            RoundRobinType = draft.RoundRobinType,
            MatchesPerTeam = draft.RoundRobinType == TournamentRoundRobinType.Custom ? draft.MatchesPerTeam : 0,
            CustomPairsJson = draft.RoundRobinType == TournamentRoundRobinType.Custom ? draft.CustomPairsJson : null,
            VenueId = draft.VenueId, FormatId = draft.FormatId, Format = (MatchFormat)draft.Format,
            OversLimit = OversRules.StoredOversLimit(draft.OversMode, draft.OversLimit),
            OversMode = draft.OversMode, LastBatsmanCanBatAsSingleWicket = draft.LastBatsmanCanBatAsSingleWicket,
            BowlerMaxOvers = draft.BowlerMaxOvers, Status = TournamentStatus.Draft,
            Version = 1, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await ValidateLocalReferencesAsync(db, draft, ct).ConfigureAwait(false);
        db.Tournaments.Add(entity);
        _outbox.EnqueueEntity(db, entity, "Upsert");
        foreach (var row in draft.Teams)
        {
            var team = new TournamentTeam { TournamentId = entity.Id, TeamId = row.TeamId,
                GroupNumber = entity.RoundRobinType == TournamentRoundRobinType.Custom ? 1 : row.GroupNumber };
            db.TournamentTeams.Add(team);
            _outbox.EnqueueEntity(db, team, "Upsert");
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _syncTrigger.RequestSyncNow();
        return await GetLocalDetailAsync(entity.Id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The tournament was saved locally but could not be loaded.");
    }

    public async Task<TournamentDetail> UpdateAsync(Guid id, TournamentDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        Validate(draft);
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.Tournaments.Include(row => row.Teams)
            .FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Tournament not found.");
        await ValidateLocalReferencesAsync(db, draft, ct).ConfigureAwait(false);
        if (entity.Status != TournamentStatus.Draft)
            throw new InvalidOperationException("Only draft tournaments can be edited. Saved tournaments are locked.");
        entity.Name = draft.Name.Trim();
        entity.GroupCount = draft.RoundRobinType == TournamentRoundRobinType.Custom ? 1 : draft.GroupCount;
        entity.QualifyPerGroup = draft.QualifyPerGroup; entity.KnockoutFormat = draft.KnockoutFormat;
        entity.RoundRobinType = draft.RoundRobinType;
        entity.MatchesPerTeam = draft.RoundRobinType == TournamentRoundRobinType.Custom ? draft.MatchesPerTeam : 0;
        entity.CustomPairsJson = draft.RoundRobinType == TournamentRoundRobinType.Custom ? draft.CustomPairsJson : null;
        entity.VenueId = draft.VenueId; entity.FormatId = draft.FormatId; entity.Format = (MatchFormat)draft.Format;
        entity.OversLimit = OversRules.StoredOversLimit(draft.OversMode, draft.OversLimit);
        entity.OversMode = draft.OversMode; entity.LastBatsmanCanBatAsSingleWicket = draft.LastBatsmanCanBatAsSingleWicket;
        entity.BowlerMaxOvers = draft.BowlerMaxOvers; entity.Version++; entity.UpdatedAtUtc = DateTime.UtcNow;
        var wanted = draft.Teams.ToDictionary(row => row.TeamId, row =>
            entity.RoundRobinType == TournamentRoundRobinType.Custom ? 1 : row.GroupNumber);
        foreach (var old in entity.Teams.ToList())
        {
            if (!wanted.Remove(old.TeamId, out var groupNumber))
            {
                db.TournamentTeams.Remove(old);
                _outbox.EnqueueEntity(db, old, "Delete");
                continue;
            }
            if (old.GroupNumber == groupNumber) continue;
            old.GroupNumber = groupNumber;
            _outbox.EnqueueEntity(db, old, "Upsert");
        }
        foreach (var (teamId, groupNumber) in wanted)
        {
            var team = new TournamentTeam { TournamentId = entity.Id, TeamId = teamId, GroupNumber = groupNumber };
            db.TournamentTeams.Add(team);
            _outbox.EnqueueEntity(db, team, "Upsert");
        }
        _outbox.EnqueueEntity(db, entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _syncTrigger.RequestSyncNow();
        return await GetLocalDetailAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The tournament was saved locally but could not be loaded.");
    }

    public async Task<TournamentDetail> GenerateAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.Tournaments.Include(row => row.Teams).Include(row => row.Fixtures)
            .FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Tournament not found.");
        if (entity.Status != TournamentStatus.Draft)
            throw new InvalidOperationException("Tournament already saved. Edit / delete is only allowed while Draft.");
        if (entity.RoundRobinType == TournamentRoundRobinType.Custom)
        {
            var pairs = ReadCustomPairs(entity.CustomPairsJson);
            SchedulerEngine.ValidateCustomPairings(entity.Teams.Select(row => row.TeamId).ToList(),
                entity.MatchesPerTeam, pairs);
        }
        var teamIds = entity.Teams.Select(row => row.TeamId).Distinct().ToArray();
        var teamNames = teamIds.Length == 0 ? new Dictionary<Guid, string>()
            : await db.Teams.AsNoTracking().Where(row => teamIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.Name, ct).ConfigureAwait(false);
        var schedulerTeams = entity.Teams.OrderBy(row => row.GroupNumber)
            .ThenBy(row => teamNames.GetValueOrDefault(row.TeamId, string.Empty), StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.TeamId)
            .Select((row, index) => new SchedulerTeam { Id = row.TeamId, TournamentId = entity.Id,
                Name = teamNames.GetValueOrDefault(row.TeamId, string.Empty), GroupNumber = row.GroupNumber,
                SortOrder = index }).ToList();
        var schedule = new SchedulerTournament
        {
            Id = entity.Id, GroupCount = entity.GroupCount, QualifyPerGroup = entity.QualifyPerGroup,
            RoundRobinType = (SchedulerRoundRobinType)entity.RoundRobinType,
            KnockoutFormat = (SchedulerKnockoutFormat)entity.KnockoutFormat,
            Teams = schedulerTeams
        };
        foreach (var old in entity.Fixtures.ToList())
        {
            db.TournamentFixtures.Remove(old);
            _outbox.EnqueueEntity(db, old, "Delete");
        }
        entity.Fixtures.Clear();
        var built = SchedulerEngine.BuildMatches(schedule,
            entity.RoundRobinType == TournamentRoundRobinType.Custom ? ReadCustomPairs(entity.CustomPairsJson) : null);
        foreach (var row in built)
        {
            var fixture = new TournamentFixture
            {
                Id = row.Id, TournamentId = entity.Id, Stage = row.Stage switch
                {
                    SchedulerMatchStage.Group => TournamentFixtureStage.Group,
                    SchedulerMatchStage.QuarterFinal => TournamentFixtureStage.QuarterFinal,
                    SchedulerMatchStage.Playoff => TournamentFixtureStage.Playoff,
                    SchedulerMatchStage.SemiFinal => TournamentFixtureStage.SemiFinal,
                    SchedulerMatchStage.Final => TournamentFixtureStage.Final,
                    _ => TournamentFixtureStage.Group
                },
                GroupNumber = row.Stage == SchedulerMatchStage.Group ? row.GroupNumber : null,
                Sequence = row.Sequence, Label = row.Label, TeamAId = row.TeamAId, TeamBId = row.TeamBId,
                TeamASlot = row.TeamASlot, TeamBSlot = row.TeamBSlot
            };
            db.TournamentFixtures.Add(fixture);
            _outbox.EnqueueEntity(db, fixture, "Upsert");
        }
        entity.Status = TournamentStatus.Scheduled; entity.WinnerTeamId = null;
        entity.PlayerOfTheTournamentPlayerId = null; entity.BestBatsmanPlayerId = null;
        entity.BestBowlerPlayerId = null; entity.BestFielderPlayerId = null;
        entity.Version++; entity.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.EnqueueEntity(db, entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _syncTrigger.RequestSyncNow();
        return await GetLocalDetailAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The tournament fixtures were saved locally but could not be loaded.");
    }

    public async Task LinkMatchAsync(Guid tournamentId, Guid fixtureId, Guid matchId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.Tournaments.Include(row => row.Fixtures)
            .FirstOrDefaultAsync(row => row.Id == tournamentId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Tournament not found.");
        if (entity.Status == TournamentStatus.Draft)
            throw new InvalidOperationException("Save the tournament first before starting matches.");
        var fixture = entity.Fixtures.FirstOrDefault(row => row.Id == fixtureId)
            ?? throw new InvalidOperationException("Tournament fixture not found.");
        if (fixture.TeamAId is null || fixture.TeamBId is null)
            throw new InvalidOperationException("Fixture teams are not decided yet.");
        fixture.MatchId = matchId;
        if (entity.Status == TournamentStatus.Scheduled) entity.Status = TournamentStatus.InProgress;
        entity.Version++; entity.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.EnqueueEntity(db, fixture, "Upsert");
        _outbox.EnqueueEntity(db, entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _syncTrigger.RequestSyncNow();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.Tournaments.FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
        if (entity is null) return;
        if (entity.Status != TournamentStatus.Draft)
            throw new InvalidOperationException("Only draft tournaments can be deleted. Saved tournaments are locked.");
        entity.IsDeleted = true; entity.Version++; entity.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.EnqueueEntity(db, entity, "Delete");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _syncTrigger.RequestSyncNow();
    }

    public async Task<bool> IsReadyToSyncAsync(Guid tournamentId, CancellationToken ct = default)
    {
        var pending = await _sync.GetPendingTournamentIdsAsync(ct).ConfigureAwait(false);
        if (!pending.Contains(tournamentId))
            return false;

        try
        {
            var detail = await GetAsync(tournamentId, ct).ConfigureAwait(false);
            return detail is not null && IsTournamentFullyComplete(detail);
        }
        catch
        {
            // Need fixture schedule (Final vs RR-only) from API — don't offer Sync offline mid-tournament.
            return false;
        }
    }

    public async Task EnsureReadyToSyncAsync(Guid tournamentId, CancellationToken ct = default)
    {
        if (await IsReadyToSyncAsync(tournamentId, ct).ConfigureAwait(false))
            return;
        throw new InvalidOperationException(
            "Sync only after the tournament is fully completed (Final / champion decided). Semi-finals and earlier stay on this device.");
    }

    /// <summary>
    /// Finished = champion decided locally: Final match completed, or RR-only tournament with all group matches done.
    /// Sync is not allowed after group/semi only.
    /// </summary>
    private static bool IsTournamentFullyComplete(TournamentDetail detail)
    {
        if (detail.Status == 3 || detail.WinnerTeamId is Guid)
            return true;

        var fixtures = detail.Fixtures;
        if (fixtures.Count == 0)
            return detail.Status == 3;

        static bool IsFinal(TournamentFixtureItem f)
            => string.Equals(f.StageText, "Final", StringComparison.OrdinalIgnoreCase)
               || string.Equals(f.Label, "Final", StringComparison.OrdinalIgnoreCase);

        static bool IsSemi(TournamentFixtureItem f)
            => f.StageText.Contains("Semi", StringComparison.OrdinalIgnoreCase)
               || f.Label.Contains("Semi", StringComparison.OrdinalIgnoreCase);

        static bool IsCompleted(TournamentFixtureItem f)
            => string.Equals(f.MatchStatusText, "Completed", StringComparison.OrdinalIgnoreCase);

        var final = fixtures.FirstOrDefault(IsFinal);
        if (final is not null)
            return IsCompleted(final);

        // RR-only (1 group / 1 qualify): every group fixture must be completed.
        if (fixtures.Any(IsSemi))
            return false;

        var playable = fixtures.Where(f => f.TeamAId is Guid && f.TeamBId is Guid).ToList();
        return playable.Count > 0 && playable.All(IsCompleted);
    }

    private static void Validate(TournamentDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
            throw new ArgumentException("Tournament name is required.");
        var groupCount = draft.RoundRobinType == TournamentRoundRobinType.Custom ? 1 : draft.GroupCount;
        if (draft.RoundRobinType == TournamentRoundRobinType.Custom)
        {
            if (groupCount != 1)
                throw new ArgumentException("Custom type allows only 1 group.");
        }
        else if (groupCount is not (1 or 2 or 4))
            throw new ArgumentException("Groups must be 1, 2, or 4.");

        var ko = (SchedulerKnockoutFormat)(byte)draft.KnockoutFormat;
        var qualifyOpts = SchedulerEngine.QualifyOptions(groupCount, ko);
        if (qualifyOpts.Count == 0 || !qualifyOpts.Contains(draft.QualifyPerGroup))
            throw new ArgumentException(
                $"{SchedulerEngine.KnockoutLabel(ko)} does not fit {groupCount} group{(groupCount == 1 ? "" : "s")}.");

        if (draft.VenueId == Guid.Empty)
            throw new ArgumentException("Venue is required.");
        if (draft.Format is not (0 or 1))
            throw new ArgumentException("Select Single inning or Double innings.");
        OversRules.Validate(draft.OversMode, draft.OversLimit, draft.BowlerMaxOvers);
        if (draft.Teams.Count == 0)
            throw new ArgumentException("Add at least one team.");
        if (draft.Teams.Select(row => row.TeamId).Distinct().Count() != draft.Teams.Count)
            throw new ArgumentException("Duplicate teams are not allowed.");
        if (draft.Teams.Any(row => row.GroupNumber < 1 || row.GroupNumber > groupCount))
            throw new ArgumentException("Every team must be assigned to a valid group.");
        for (var g = 1; g <= groupCount; g++)
        {
            var n = draft.Teams.Count(t => t.GroupNumber == g);
            var min = draft.KnockoutFormat == TournamentKnockoutFormat.None ? 2 : Math.Max(2, draft.QualifyPerGroup);
            if (n < min)
                throw new ArgumentException($"Group {g} needs at least {min} team(s).");
        }

        if (draft.KnockoutFormat != TournamentKnockoutFormat.None)
        {
            var need = SchedulerEngine.KnockoutTeamCount(ko);
            if (draft.Teams.Count < need)
                throw new ArgumentException($"{SchedulerEngine.KnockoutLabel(ko)} needs {need} teams.");
        }
    }

    private static async Task ValidateLocalReferencesAsync(
        JplDbContext db, TournamentDraft draft, CancellationToken ct)
    {
        if (!await db.Venues.AnyAsync(row => row.Id == draft.VenueId, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Venue not found in local league data. Sync before creating this tournament.");
        if (draft.FormatId is Guid formatId
            && !await db.Formats.AnyAsync(row => row.Id == formatId, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Format not found in local league data. Sync before creating this tournament.");
        var ids = draft.Teams.Select(row => row.TeamId).Distinct().ToArray();
        var count = await db.Teams.CountAsync(row => ids.Contains(row.Id), ct).ConfigureAwait(false);
        if (count != ids.Length)
            throw new InvalidOperationException("A selected team is missing from local league data. Sync before creating this tournament.");
    }

    private static object ToBody(TournamentDraft draft)
    {
        var isCustom = draft.RoundRobinType == TournamentRoundRobinType.Custom;
        var groupCount = isCustom ? 1 : draft.GroupCount;
        return new
        {
            name = draft.Name.Trim(),
            groupCount,
            qualifyPerGroup = draft.QualifyPerGroup,
            knockoutFormat = (byte)draft.KnockoutFormat,
            roundRobinType = (byte)draft.RoundRobinType,
            matchesPerTeam = isCustom ? draft.MatchesPerTeam : 0,
            customPairsJson = isCustom ? draft.CustomPairsJson : null,
            venueId = draft.VenueId,
            formatId = draft.FormatId,
            format = draft.Format,
            oversLimit = OversRules.StoredOversLimit(draft.OversMode, draft.OversLimit),
            oversMode = (byte)draft.OversMode,
            lastBatsmanCanBatAsSingleWicket = draft.LastBatsmanCanBatAsSingleWicket,
            bowlerMaxOvers = draft.BowlerMaxOvers,
            teams = draft.Teams.Select(t => new
            {
                teamId = t.TeamId,
                groupNumber = isCustom ? 1 : t.GroupNumber
            }).ToList()
        };
    }

    private static TournamentDetail ToDetail(ApiTournamentDetailDto dto)
    {
        var detail = new TournamentDetail
        {
            Id = dto.Id,
            Name = dto.Name,
            GroupCount = dto.GroupCount,
            QualifyPerGroup = dto.QualifyPerGroup,
            KnockoutFormat = (TournamentKnockoutFormat)dto.KnockoutFormat,
            RoundRobinType = (TournamentRoundRobinType)dto.RoundRobinType,
            MatchesPerTeam = dto.MatchesPerTeam,
            CustomPairsJson = dto.CustomPairsJson,
            CustomFixturesReady = dto.RoundRobinType != (byte)TournamentRoundRobinType.Custom || dto.CustomFixturesReady,
            VenueId = dto.VenueId,
            VenueName = dto.VenueName,
            FormatId = dto.FormatId,
            Format = dto.Format,
            FormatText = string.IsNullOrWhiteSpace(dto.FormatText)
                ? (dto.Format == 1 ? "Double innings" : "Single inning")
                : dto.FormatText,
            OversLimit = dto.OversLimit,
            OversMode = (OversMode)dto.OversMode,
            LastBatsmanCanBatAsSingleWicket = dto.LastBatsmanCanBatAsSingleWicket,
            BowlerMaxOvers = dto.BowlerMaxOvers,
            Status = dto.Status,
            StatusText = dto.StatusText,
            WinnerTeamId = dto.WinnerTeamId,
            WinnerTeamName = dto.WinnerTeamName,
            PlayerOfTheTournamentPlayerId = dto.PlayerOfTheTournamentPlayerId,
            PlayerOfTheTournamentName = dto.PlayerOfTheTournamentName,
            BestBatsmanPlayerId = dto.BestBatsmanPlayerId,
            BestBatsmanName = dto.BestBatsmanName,
            BestBowlerPlayerId = dto.BestBowlerPlayerId,
            BestBowlerName = dto.BestBowlerName,
            BestFielderPlayerId = dto.BestFielderPlayerId,
            BestFielderName = dto.BestFielderName,
            Teams = dto.Teams.Select(t => new TournamentTeamItem
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName,
                GroupNumber = t.GroupNumber
            }).ToList(),
            Fixtures = dto.Fixtures.Select(f => new TournamentFixtureItem
            {
                Id = f.Id,
                Label = f.Label,
                StageText = f.StageText,
                GroupNumber = f.GroupNumber,
                Sequence = f.Sequence,
                TeamAId = f.TeamAId,
                TeamBId = f.TeamBId,
                TeamAName = f.TeamAName,
                TeamBName = f.TeamBName,
                MatchId = f.MatchId,
                MatchStatusText = f.MatchStatusText,
                ResultText = f.ResultText ?? string.Empty,
                CanStart = f.CanStart,
                CanResume = false,
                CanOpenScorecard = f.MatchId is Guid
            }).ToList(),
            Standings = dto.Standings.Select(s => new TournamentStandingItem
            {
                GroupNumber = s.GroupNumber,
                TeamId = s.TeamId,
                TeamName = s.TeamName,
                Rank = s.Rank,
                Played = s.Played,
                Won = s.Won,
                Lost = s.Lost,
                Tied = s.Tied,
                Points = s.Points,
                NetRunRate = s.NetRunRate
            }).ToList()
        };
        ApplyQualificationMarks(detail);
        return detail;
    }

    private async Task<TournamentDetail?> GetLocalDetailAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.Tournaments.AsNoTracking().Include(row => row.Teams)
            .Include(row => row.Fixtures).FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
        if (entity is null) return null;

        var teamIds = entity.Teams.Select(row => row.TeamId).Distinct().ToArray();
        var teams = teamIds.Length == 0 ? new Dictionary<Guid, string>()
            : await db.Teams.AsNoTracking().Where(row => teamIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.Name, ct).ConfigureAwait(false);
        var venueName = await db.Venues.AsNoTracking().Where(row => row.Id == entity.VenueId)
            .Select(row => row.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        var awardIds = new[] { entity.PlayerOfTheTournamentPlayerId, entity.BestBatsmanPlayerId,
            entity.BestBowlerPlayerId, entity.BestFielderPlayerId }.OfType<Guid>().Distinct().ToArray();
        var awards = awardIds.Length == 0 ? new Dictionary<Guid, string>()
            : await db.Players.AsNoTracking().Where(row => awardIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.FullName, ct).ConfigureAwait(false);
        var fixtureIds = entity.Fixtures.Select(fixture => fixture.Id).ToArray();
        var matches = await db.Matches.AsNoTracking().Where(row => row.TournamentId == id
                || (row.TournamentFixtureId != null && fixtureIds.Contains(row.TournamentFixtureId.Value)))
            .ToListAsync(ct).ConfigureAwait(false);
        var matchesById = matches.ToDictionary(row => row.Id);
        var matchesByFixture = matches.Where(row => row.TournamentFixtureId is not null)
            .GroupBy(row => row.TournamentFixtureId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.UpdatedAtUtc).First());

        var dto = new ApiTournamentDetailDto
        {
            Id = entity.Id, Name = entity.Name, GroupCount = entity.GroupCount,
            QualifyPerGroup = entity.QualifyPerGroup, KnockoutFormat = (byte)entity.KnockoutFormat,
            RoundRobinType = (byte)entity.RoundRobinType, MatchesPerTeam = entity.MatchesPerTeam,
            CustomPairsJson = entity.CustomPairsJson,
            CustomFixturesReady = entity.RoundRobinType != TournamentRoundRobinType.Custom
                || CustomPairsAreComplete(entity),
            VenueId = entity.VenueId, VenueName = venueName, FormatId = entity.FormatId,
            Format = (byte)entity.Format,
            FormatText = entity.FormatId is Guid formatId
                ? await db.Formats.AsNoTracking().Where(row => row.Id == formatId).Select(row => row.Name)
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? FormatRules.DetailsLine(entity.Format, entity.OversMode, entity.OversLimit)
                : FormatRules.DetailsLine(entity.Format, entity.OversMode, entity.OversLimit),
            OversLimit = entity.OversLimit, OversMode = (byte)entity.OversMode,
            LastBatsmanCanBatAsSingleWicket = entity.LastBatsmanCanBatAsSingleWicket,
            BowlerMaxOvers = entity.BowlerMaxOvers, Status = (byte)entity.Status,
            StatusText = entity.Status.ToString(), WinnerTeamId = entity.WinnerTeamId,
            WinnerTeamName = entity.WinnerTeamId is Guid winnerId ? teams.GetValueOrDefault(winnerId) : null,
            PlayerOfTheTournamentPlayerId = entity.PlayerOfTheTournamentPlayerId,
            PlayerOfTheTournamentName = entity.PlayerOfTheTournamentPlayerId is Guid potId
                ? awards.GetValueOrDefault(potId) : null,
            BestBatsmanPlayerId = entity.BestBatsmanPlayerId,
            BestBatsmanName = entity.BestBatsmanPlayerId is Guid batsmanId ? awards.GetValueOrDefault(batsmanId) : null,
            BestBowlerPlayerId = entity.BestBowlerPlayerId,
            BestBowlerName = entity.BestBowlerPlayerId is Guid bowlerId ? awards.GetValueOrDefault(bowlerId) : null,
            BestFielderPlayerId = entity.BestFielderPlayerId,
            BestFielderName = entity.BestFielderPlayerId is Guid fielderId ? awards.GetValueOrDefault(fielderId) : null,
            Teams = entity.Teams.OrderBy(row => row.GroupNumber).ThenBy(row => teams.GetValueOrDefault(row.TeamId, string.Empty))
                .Select(row => new ApiTournamentTeamDto
                {
                    TeamId = row.TeamId, TeamName = teams.GetValueOrDefault(row.TeamId, string.Empty),
                    GroupNumber = row.GroupNumber
                }).ToList(),
            Fixtures = entity.Fixtures.OrderBy(row => row.Sequence).Select(row =>
            {
                var match = row.MatchId is Guid linkedId && matchesById.TryGetValue(linkedId, out var direct)
                    ? direct : matchesByFixture.GetValueOrDefault(row.Id);
                return new ApiTournamentFixtureDto
                {
                    Id = row.Id, Stage = (byte)row.Stage, StageText = StageText(row.Stage),
                    GroupNumber = row.GroupNumber, Sequence = row.Sequence, Label = row.Label,
                    TeamAId = row.TeamAId, TeamBId = row.TeamBId,
                    TeamAName = row.TeamAId is Guid teamA ? teams.GetValueOrDefault(teamA, row.TeamASlot ?? string.Empty) : row.TeamASlot ?? string.Empty,
                    TeamBName = row.TeamBId is Guid teamB ? teams.GetValueOrDefault(teamB, row.TeamBSlot ?? string.Empty) : row.TeamBSlot ?? string.Empty,
                    TeamASlot = row.TeamASlot, TeamBSlot = row.TeamBSlot, MatchId = match?.Id,
                    MatchStatus = match is null ? null : (byte)match.Status,
                    MatchStatusText = match?.Status.ToString() ?? string.Empty,
                    CanStart = match is null && entity.Status is not TournamentStatus.Draft and not TournamentStatus.Completed,
                    CanOpenScorecard = match is not null
                };
            }).ToList(),
            Standings = entity.Teams.GroupBy(row => row.GroupNumber).OrderBy(group => group.Key)
                .SelectMany(group => group.OrderBy(row => teams.GetValueOrDefault(row.TeamId, string.Empty))
                    .Select((row, index) => new ApiTournamentStandingDto
                    {
                        GroupNumber = row.GroupNumber, TeamId = row.TeamId,
                        TeamName = teams.GetValueOrDefault(row.TeamId, string.Empty), Rank = index + 1
                    })).ToList()
        };

        var detail = ToDetail(dto);
        await OverlayLocalMatchStateAsync(detail, ct).ConfigureAwait(false);
        return detail;
    }

    private static TournamentListItem ToListItem(TournamentDetail detail, bool needsSync = false)
        => new()
        {
            Id = detail.Id, Name = detail.Name, Status = detail.Status, StatusText = detail.StatusText,
            MetaLine = $"{detail.GroupCount} group{(detail.GroupCount == 1 ? "" : "s")} · Qualify {detail.QualifyPerGroup} · {detail.Teams.Count} teams · {detail.Fixtures.Count} fixtures",
            WinnerLine = string.IsNullOrWhiteSpace(detail.WinnerTeamName) ? string.Empty : $"Champion: {detail.WinnerTeamName}",
            NeedsSync = needsSync
        };

    private static bool CustomPairsAreComplete(Tournament tournament)
    {
        if (string.IsNullOrWhiteSpace(tournament.CustomPairsJson)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(tournament.CustomPairsJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
            var pairs = doc.RootElement.EnumerateArray().Select(pair =>
            {
                var a = pair.TryGetProperty("a", out var left) ? left.GetGuid() : Guid.Empty;
                var b = pair.TryGetProperty("b", out var right) ? right.GetGuid() : Guid.Empty;
                return (A: a, B: b);
            }).ToList();
            var teamIds = tournament.Teams.Select(row => row.TeamId).Distinct().ToList();
            SchedulerEngine.ValidateCustomPairings(teamIds, tournament.MatchesPerTeam, pairs);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<(Guid A, Guid B)> ReadCustomPairs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return [];
            var result = new List<(Guid A, Guid B)>();
            foreach (var pair in doc.RootElement.EnumerateArray())
            {
                if (pair.ValueKind != System.Text.Json.JsonValueKind.Object
                    || !pair.TryGetProperty("a", out var left)
                    || !pair.TryGetProperty("b", out var right)
                    || left.ValueKind != System.Text.Json.JsonValueKind.String
                    || right.ValueKind != System.Text.Json.JsonValueKind.String
                    || !Guid.TryParse(left.GetString(), out var a)
                    || !Guid.TryParse(right.GetString(), out var b))
                    continue;
                result.Add((a, b));
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    private static string StageText(TournamentFixtureStage stage) => stage switch
    {
        TournamentFixtureStage.Group => "Group",
        TournamentFixtureStage.QuarterFinal => "Quarter-final",
        TournamentFixtureStage.Playoff => "Playoff",
        TournamentFixtureStage.SemiFinal => "Semi-final",
        TournamentFixtureStage.Final => "Final",
        _ => stage.ToString()
    };

    private async Task<bool> IsBootstrappedAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Tournament matches stay on-device until tournament Sync — overlay local status so
    /// Resume / Scorecard / results still work before the API has the balls.
    /// Also fills SF/Final teams from local standings so knockouts can continue without Sync.
    /// </summary>
    private async Task OverlayLocalMatchStateAsync(TournamentDetail detail, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var localMatches = await db.Matches.AsNoTracking()
            .Where(m => m.TournamentId == detail.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byId = localMatches.ToDictionary(m => m.Id);
        var byFixture = localMatches
            .Where(m => m.TournamentFixtureId is Guid)
            .GroupBy(m => m.TournamentFixtureId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.UpdatedAtUtc).First());

        if (localMatches.Count > 0)
        {

        var matchIds = localMatches.Select(m => m.Id).ToList();
        var innings = await db.Innings.AsNoTracking()
            .Where(i => matchIds.Contains(i.MatchId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var deliveries = await db.Deliveries.AsNoTracking()
            .Where(d => matchIds.Contains(d.MatchId))
            .Select(d => new { d.InningsId, d.RunsOffBat, d.ExtraRuns, d.IsWicket, d.IsLegalDelivery })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var delsByInn = deliveries
            .GroupBy(d => d.InningsId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => (x.RunsOffBat, x.ExtraRuns, x.IsWicket, x.IsLegalDelivery)).ToList());
        var innsByMatch = innings.GroupBy(i => i.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        // First pass: attach local match state
        foreach (var f in detail.Fixtures)
        {
            Match? local = null;
            if (f.MatchId is Guid mid && byId.TryGetValue(mid, out var byMatch))
                local = byMatch;
            else if (byFixture.TryGetValue(f.Id, out var byFix))
                local = byFix;

            if (local is null)
                continue;

            f.MatchId = local.Id;
            f.MatchStatusText = local.Status switch
            {
                MatchStatus.Setup => "Setup",
                MatchStatus.InProgress => "Live",
                MatchStatus.Completed => "Completed",
                MatchStatus.Abandoned => "Abandoned",
                _ => f.MatchStatusText
            };

            if (local.Status == MatchStatus.Completed
                && innsByMatch.TryGetValue(local.Id, out var matchInns))
            {
                var (aRuns, aWkts) = LocalTeamTotals(matchInns, local.TeamAId, delsByInn);
                var (bRuns, bWkts) = LocalTeamTotals(matchInns, local.TeamBId, delsByInn);
                var firstBat = matchInns.OrderBy(i => i.InningsNumber).FirstOrDefault()?.BattingTeamId;
                f.ResultText = LocalResultText(
                    local, f.TeamAName, f.TeamBName, aRuns, aWkts, bRuns, bWkts, firstBat);
            }
            else if (local.Status == MatchStatus.InProgress
                     && innsByMatch.TryGetValue(local.Id, out var liveInns))
            {
                var (aRuns, aWkts) = LocalTeamTotals(liveInns, local.TeamAId, delsByInn);
                var (bRuns, bWkts) = LocalTeamTotals(liveInns, local.TeamBId, delsByInn);
                f.ResultText = $"LIVE · {aRuns}/{aWkts} vs {bRuns}/{bWkts}";
            }
        }

        ApplyLocalKnockoutTeams(detail, byId, innsByMatch, delsByInn);
        ApplyLocalStandings(detail, byId, innsByMatch, delsByInn);
        ApplyKnockoutTiebreakResultText(detail);

        // Local champion (before API sync)
        if (detail.WinnerTeamId is null)
        {
            var winnerId = TryLocalChampion(detail, byId, innsByMatch, delsByInn);
            if (winnerId is Guid wid)
            {
                detail.WinnerTeamId = wid;
                detail.WinnerTeamName = detail.Teams.FirstOrDefault(t => t.TeamId == wid)?.TeamName
                                        ?? detail.Fixtures.SelectMany(f => new[]
                                        {
                                            (f.TeamAId, f.TeamAName),
                                            (f.TeamBId, f.TeamBName)
                                        }).FirstOrDefault(x => x.Item1 == wid).Item2;
                if (detail.Status != 3)
                {
                    detail.Status = 3;
                    detail.StatusText = "Completed";
                }
            }
        }
        }

        // Resume only for the match this phone is scoring. Other live matches stay Main Score.
        var draftOrLocked = detail.Status == 0;
        var leagueActiveId = (await db.Matches.AsNoTracking()
                .Where(m => m.Status == MatchStatus.Setup || m.Status == MatchStatus.InProgress)
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .OrderByDescending(m => m.Status == MatchStatus.InProgress)
            .ThenByDescending(m => m.UpdatedAtUtc)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefault();

        foreach (var f in detail.Fixtures)
        {
            Match? local = null;
            if (f.MatchId is Guid mid && byId.TryGetValue(mid, out var byMatch))
                local = byMatch;
            else if (byFixture.TryGetValue(f.Id, out var byFix))
                local = byFix;

            var teamsReady = f.TeamAId is Guid && f.TeamBId is Guid;
            // Start stays visible even if another match is active; StartFixtureAsync gates that.
            f.CanStart = teamsReady && f.MatchId is null && !draftOrLocked
                         && detail.Status is not 0 and not 3;
            f.CanResume = local is not null
                          && (local.Status is MatchStatus.Setup or MatchStatus.InProgress)
                          && !draftOrLocked
                          && leagueActiveId == local.Id
                          && ScoringOwnership.IsOwner(local, _auth);
            f.CanOpenScorecard = f.MatchId is Guid && !f.CanResume;
            if (f.CanResume || f.CanOpenScorecard)
                f.CanStart = false;
        }

        ApplyQualificationMarks(detail);
    }

    /// <summary>
    /// Knockout fixtures (QF / Playoff / SF / Final) must never feed the points table.
    /// Detect by StageText and Label — Custom RR uses Label "Custom" with StageText "Group".
    /// </summary>
    private static bool IsKnockoutFixture(TournamentFixtureItem f)
    {
        var stage = f.StageText ?? string.Empty;
        var label = f.Label ?? string.Empty;
        return stage.Contains("Semi", StringComparison.OrdinalIgnoreCase)
               || stage.Contains("Quarter", StringComparison.OrdinalIgnoreCase)
               || stage.Contains("Playoff", StringComparison.OrdinalIgnoreCase)
               || string.Equals(stage, "Final", StringComparison.OrdinalIgnoreCase)
               || label.Contains("Semi", StringComparison.OrdinalIgnoreCase)
               || label.Contains("Quarter", StringComparison.OrdinalIgnoreCase)
               || label.Contains("Playoff", StringComparison.OrdinalIgnoreCase)
               || string.Equals(label, "Final", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Group / Custom round-robin fixtures only (points table + qualification).</summary>
    private static bool IsGroupStageFixture(TournamentFixtureItem f)
    {
        if (IsKnockoutFixture(f))
            return false;

        var stage = f.StageText ?? string.Empty;
        var label = f.Label ?? string.Empty;
        if (string.Equals(stage, "Group", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(label, "Custom", StringComparison.OrdinalIgnoreCase))
            return true;
        if (label.Contains("Round robin", StringComparison.OrdinalIgnoreCase))
            return true;
        if (label.StartsWith("Group ", StringComparison.OrdinalIgnoreCase))
            return true;

        // Last resort: numbered group slot that is not a knockout.
        return f.GroupNumber is > 0;
    }

    private static List<TournamentFixtureItem> GroupStageFixtures(TournamentDetail detail)
        => detail.Fixtures.Where(IsGroupStageFixture).ToList();

    private static void ApplyLocalStandings(
        TournamentDetail detail,
        Dictionary<Guid, Match> matchesById,
        Dictionary<Guid, List<Innings>> innsByMatch,
        Dictionary<Guid, List<(int RunsOffBat, int ExtraRuns, bool IsWicket, bool IsLegalDelivery)>> delsByInn)
    {
        var groupFixtures = GroupStageFixtures(detail);

        // Always rebuild from local group results (API standings stay 0 until tournament Sync).
        if (groupFixtures.Count == 0 && detail.Teams.Count == 0)
            return;

        var local = BuildLocalStandings(detail, groupFixtures, matchesById, innsByMatch, delsByInn);
        var localPlayed = local.Sum(s => s.Played);

        // If this device has no group ball data yet, keep API standings (do not wipe Pts to 0).
        // Previously any local match rows (even knockout-only / empty innings) zeroed the table.
        if (localPlayed == 0
            && detail.Standings.Any(s => s.Played > 0 || s.Points > 0 || Math.Abs(s.NetRunRate) > 0.0005))
        {
            ApplyQualificationMarks(detail);
            return;
        }

        var names = detail.Teams.ToDictionary(t => t.TeamId, t => t.TeamName);
        var rows = new List<TournamentStandingItem>();
        foreach (var g in local.GroupBy(s => s.GroupNumber).OrderBy(x => x.Key))
        {
            var rank = 1;
            foreach (var s in g)
            {
                rows.Add(new TournamentStandingItem
                {
                    GroupNumber = s.GroupNumber,
                    TeamId = s.TeamId,
                    TeamName = names.GetValueOrDefault(s.TeamId, "?"),
                    Rank = rank++,
                    Played = s.Played,
                    Won = s.Won,
                    Lost = s.Lost,
                    Tied = s.Tied,
                    Points = s.Points,
                    NetRunRate = Math.Round(s.Nrr, 3)
                });
            }
        }

        detail.Standings = rows;
        ApplyQualificationMarks(detail);
    }

    /// <summary>
    /// Q/E on the points table. After all group matches: Rank ≤ qualify → Q else E.
    /// During group stage: conservative points envelope only (never guesses on NRR).
    /// </summary>
    private static void ApplyQualificationMarks(TournamentDetail detail)
    {
        if (detail.Standings.Count == 0 || detail.QualifyPerGroup <= 0)
            return;

        var groupFixtures = GroupStageFixtures(detail);

        static bool IsFinished(TournamentFixtureItem f)
            => string.Equals(f.MatchStatusText, "Completed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(f.MatchStatusText, "Abandoned", StringComparison.OrdinalIgnoreCase);

        foreach (var group in detail.Standings.GroupBy(s => s.GroupNumber))
        {
            var rows = group.ToList();
            var k = Math.Min(detail.QualifyPerGroup, rows.Count);
            if (k <= 0)
                continue;

            if (k >= rows.Count)
            {
                foreach (var r in rows)
                    r.QualMark = "Q";
                continue;
            }

            var fixturesInGroup = detail.GroupCount <= 1
                ? groupFixtures
                : groupFixtures.Where(f => (f.GroupNumber ?? 0) == group.Key).ToList();

            // Don't mark Q/E from a zero table while group matches aren't actually scored into standings.
            var groupDone = fixturesInGroup.Count > 0
                            && fixturesInGroup.All(IsFinished)
                            && rows.Any(r => r.Played > 0);
            if (groupDone)
            {
                // Rank already includes Pts → NRR → RunsFor.
                foreach (var r in rows)
                    r.QualMark = r.Rank <= k ? "Q" : "E";
                continue;
            }

            if (fixturesInGroup.Count == 0
                || (!fixturesInGroup.Any(IsFinished) && rows.All(r => r.Played == 0)))
            {
                foreach (var r in rows)
                    r.QualMark = string.Empty;
                continue;
            }

            // Mid-group: points envelope only — NRR mid-season is not safe without simulating balls.
            var temp = rows.Select(r => new SchedulerStandingRow
            {
                TeamId = r.TeamId,
                TeamName = r.TeamName,
                GroupNumber = r.GroupNumber,
                Rank = r.Rank,
                Played = r.Played,
                Won = r.Won,
                Lost = r.Lost,
                Tied = r.Tied,
                Points = r.Points
            }).ToList();

            SchedulerEngine.ApplyEnvelopeQualificationMarks(
                temp,
                id => fixturesInGroup.Count(f =>
                    (f.TeamAId == id || f.TeamBId == id) && !IsFinished(f)),
                k);

            foreach (var r in rows)
            {
                var mark = temp.FirstOrDefault(t => t.TeamId == r.TeamId)?.QualMark;
                r.QualMark = mark ?? string.Empty;
            }
        }
    }

    private static void ApplyKnockoutTiebreakResultText(TournamentDetail detail)
    {
        foreach (var f in detail.Fixtures)
        {
            var isKnockout = f.StageText.Contains("Semi", StringComparison.OrdinalIgnoreCase)
                             || f.Label.Contains("Semi", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(f.StageText, "Final", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(f.Label, "Final", StringComparison.OrdinalIgnoreCase);
            if (!isKnockout)
                continue;
            if (!f.ResultText.StartsWith("Match tied", StringComparison.OrdinalIgnoreCase))
                continue;
            if (f.TeamAId is not Guid a || f.TeamBId is not Guid b)
                continue;

            var winnerId = BetterOnStandingItems(a, b, detail.Standings);
            if (winnerId is not Guid wid)
                continue;
            var name = wid == a ? f.TeamAName : f.TeamBName;
            if (string.IsNullOrWhiteSpace(name))
                name = detail.Teams.FirstOrDefault(t => t.TeamId == wid)?.TeamName ?? "?";
            f.ResultText = $"Match tied — {name} advances (group Pts/NRR)";
        }
    }

    private static Guid? BetterOnStandingItems(
        Guid teamA, Guid teamB, IReadOnlyList<TournamentStandingItem> standings)
    {
        var a = standings.FirstOrDefault(s => s.TeamId == teamA);
        var b = standings.FirstOrDefault(s => s.TeamId == teamB);
        if (a is null && b is null) return null;
        if (a is null) return teamB;
        if (b is null) return teamA;
        var cmp = a.Points.CompareTo(b.Points);
        if (cmp != 0) return cmp > 0 ? teamA : teamB;
        cmp = a.NetRunRate.CompareTo(b.NetRunRate);
        if (cmp != 0) return cmp > 0 ? teamA : teamB;
        return teamA;
    }

    private static Guid? BetterOnLocalTable(Guid teamA, Guid teamB, List<LocalStanding> standings)
    {
        var a = standings.FirstOrDefault(s => s.TeamId == teamA);
        var b = standings.FirstOrDefault(s => s.TeamId == teamB);
        if (a is null && b is null) return null;
        if (a is null) return teamB;
        if (b is null) return teamA;
        var cmp = a.Points.CompareTo(b.Points);
        if (cmp != 0) return cmp > 0 ? teamA : teamB;
        cmp = a.Nrr.CompareTo(b.Nrr);
        if (cmp != 0) return cmp > 0 ? teamA : teamB;
        cmp = a.RunsFor.CompareTo(b.RunsFor);
        if (cmp != 0) return cmp > 0 ? teamA : teamB;
        return teamA;
    }

    private static void ApplyLocalKnockoutTeams(
        TournamentDetail detail,
        Dictionary<Guid, Match> matchesById,
        Dictionary<Guid, List<Innings>> innsByMatch,
        Dictionary<Guid, List<(int RunsOffBat, int ExtraRuns, bool IsWicket, bool IsLegalDelivery)>> delsByInn)
    {
        var groupFixtures = GroupStageFixtures(detail);

        var groupComplete = groupFixtures.Count > 0 && groupFixtures.All(f =>
            f.MatchId is Guid mid
            && matchesById.TryGetValue(mid, out var m)
            && m.Status == MatchStatus.Completed);

        if (!groupComplete)
            return;

        var standings = BuildLocalStandings(detail, groupFixtures, matchesById, innsByMatch, delsByInn);
        if (standings.Sum(s => s.Played) == 0)
            return; // no local group balls — leave API-assigned KO teams alone

        var names = detail.Teams.ToDictionary(t => t.TeamId, t => t.TeamName);
        // Overall seeds for 1-group Custom / single-pool brackets (SF/QF).
        var seeds = standings
            .OrderBy(s => s.GroupNumber)
            .ThenByDescending(s => s.Points)
            .ThenByDescending(s => s.Nrr)
            .ThenByDescending(s => s.RunsFor)
            .Select(s => s.TeamId)
            .ToList();

        Guid? Seed(int oneBased)
            => oneBased >= 1 && oneBased <= seeds.Count ? seeds[oneBased - 1] : null;

        Guid? Pick(int group, int rank)
            => standings.Where(s => s.GroupNumber == group).Skip(rank - 1).FirstOrDefault()?.TeamId;

        void SetTeams(TournamentFixtureItem f, Guid? a, Guid? b)
        {
            if (a is Guid aa)
            {
                f.TeamAId = aa;
                f.TeamAName = names.GetValueOrDefault(aa, f.TeamAName);
            }

            if (b is Guid bb)
            {
                f.TeamBId = bb;
                f.TeamBName = names.GetValueOrDefault(bb, f.TeamBName);
            }
        }

        Guid? WinnerOf(TournamentFixtureItem f)
        {
            if (f.MatchId is not Guid mid
                || !matchesById.TryGetValue(mid, out var match)
                || match.Status != MatchStatus.Completed
                || !innsByMatch.TryGetValue(mid, out var inns))
                return null;
            var (aRuns, _) = LocalTeamTotals(inns, match.TeamAId, delsByInn);
            var (bRuns, _) = LocalTeamTotals(inns, match.TeamBId, delsByInn);
            if (aRuns != bRuns)
                return aRuns > bRuns ? match.TeamAId : match.TeamBId;
            return BetterOnLocalTable(match.TeamAId, match.TeamBId, standings);
        }

        foreach (var f in detail.Fixtures.Where(IsKnockoutFixture))
        {
            var isFinal = string.Equals(f.StageText, "Final", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(f.Label, "Final", StringComparison.OrdinalIgnoreCase);
            var isSemi = f.StageText.Contains("Semi", StringComparison.OrdinalIgnoreCase)
                         || f.Label.Contains("Semi", StringComparison.OrdinalIgnoreCase);
            var isQuarter = f.StageText.Contains("Quarter", StringComparison.OrdinalIgnoreCase)
                            || f.Label.Contains("Quarter", StringComparison.OrdinalIgnoreCase);
            var isPlayoff = f.StageText.Contains("Playoff", StringComparison.OrdinalIgnoreCase)
                            || f.Label.Contains("Playoff", StringComparison.OrdinalIgnoreCase);

            // 1-group Custom / pool: seed bracket (same as API Seed 1..N).
            if (detail.GroupCount == 1)
            {
                if (isSemi && f.Label.Contains('1'))
                    SetTeams(f, Seed(1), Seed(4));
                else if (isSemi)
                    SetTeams(f, Seed(2), Seed(3));
                else if (isFinal && detail.QualifyPerGroup == 2)
                    SetTeams(f, Seed(1), Seed(2));
                else if (isFinal)
                {
                    var semis = detail.Fixtures
                        .Where(x => x.StageText.Contains("Semi", StringComparison.OrdinalIgnoreCase)
                                    || x.Label.Contains("Semi", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (semis.Count >= 2)
                        SetTeams(f, WinnerOf(semis[0]), WinnerOf(semis[1]));
                }
                else if (isQuarter && f.Label.Contains('1'))
                    SetTeams(f, Seed(1), Seed(8));
                else if (isQuarter && f.Label.Contains('2'))
                    SetTeams(f, Seed(4), Seed(5));
                else if (isQuarter && f.Label.Contains('3'))
                    SetTeams(f, Seed(2), Seed(7));
                else if (isQuarter)
                    SetTeams(f, Seed(3), Seed(6));
                else if (isPlayoff && f.Label.Contains('1'))
                    SetTeams(f, Seed(1), Seed(2));
                else if (isPlayoff)
                    SetTeams(f, Seed(3), Seed(4));
                continue;
            }

            if (isFinal && detail.GroupCount == 2 && detail.QualifyPerGroup == 1)
                SetTeams(f, Pick(1, 1), Pick(2, 1));
            else if (isSemi && f.Label.Contains('1'))
                SetTeams(f, Pick(1, 1), Pick(2, 2));
            else if (isSemi)
                SetTeams(f, Pick(2, 1), Pick(1, 2));
            else if (isFinal && detail.GroupCount == 2 && detail.QualifyPerGroup == 2)
            {
                var semis = detail.Fixtures
                    .Where(x => x.StageText.Contains("Semi", StringComparison.OrdinalIgnoreCase)
                                || x.Label.Contains("Semi", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (semis.Count >= 2)
                    SetTeams(f, WinnerOf(semis[0]), WinnerOf(semis[1]));
            }
        }
    }

    private static Guid? TryLocalChampion(
        TournamentDetail detail,
        Dictionary<Guid, Match> matchesById,
        Dictionary<Guid, List<Innings>> innsByMatch,
        Dictionary<Guid, List<(int RunsOffBat, int ExtraRuns, bool IsWicket, bool IsLegalDelivery)>> delsByInn)
    {
        var final = detail.Fixtures.FirstOrDefault(f =>
            string.Equals(f.StageText, "Final", StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.Label, "Final", StringComparison.OrdinalIgnoreCase));

        if (final is not null)
        {
            if (final.MatchId is not Guid mid
                || !matchesById.TryGetValue(mid, out var match)
                || match.Status != MatchStatus.Completed
                || !innsByMatch.TryGetValue(mid, out var inns))
                return null;
            var (aRuns, _) = LocalTeamTotals(inns, match.TeamAId, delsByInn);
            var (bRuns, _) = LocalTeamTotals(inns, match.TeamBId, delsByInn);
            if (aRuns != bRuns)
                return aRuns > bRuns ? match.TeamAId : match.TeamBId;

            var groupFixtures = GroupStageFixtures(detail);
            var standings = BuildLocalStandings(detail, groupFixtures, matchesById, innsByMatch, delsByInn);
            return BetterOnLocalTable(match.TeamAId, match.TeamBId, standings);
        }

        // RR-only: Knockout = No
        if (detail.KnockoutFormat == TournamentKnockoutFormat.None)
        {
            var groupFixtures = GroupStageFixtures(detail);
            if (groupFixtures.Count == 0 || !groupFixtures.All(f =>
                    f.MatchId is Guid id
                    && matchesById.TryGetValue(id, out var m)
                    && m.Status == MatchStatus.Completed))
                return null;

            var standings = BuildLocalStandings(detail, groupFixtures, matchesById, innsByMatch, delsByInn);
            return standings.FirstOrDefault(s => s.GroupNumber == 1)?.TeamId;
        }

        return null;
    }

    private sealed class LocalStanding
    {
        public int GroupNumber { get; init; }
        public Guid TeamId { get; init; }
        public int Played { get; set; }
        public int Won { get; set; }
        public int Lost { get; set; }
        public int Tied { get; set; }
        public int Points { get; set; }
        public int RunsFor { get; set; }
        public int RunsAgainst { get; set; }
        public int BallsFor { get; set; }
        public int BallsAgainst { get; set; }
        public double Nrr
        {
            get
            {
                var of = BallsFor <= 0 ? 0 : RunsFor / (BallsFor / 6.0);
                var oa = BallsAgainst <= 0 ? 0 : RunsAgainst / (BallsAgainst / 6.0);
                return of - oa;
            }
        }
    }

    private static List<LocalStanding> BuildLocalStandings(
        TournamentDetail detail,
        List<TournamentFixtureItem> groupFixtures,
        Dictionary<Guid, Match> matchesById,
        Dictionary<Guid, List<Innings>> innsByMatch,
        Dictionary<Guid, List<(int RunsOffBat, int ExtraRuns, bool IsWicket, bool IsLegalDelivery)>> delsByInn)
    {
        var rows = detail.Teams.ToDictionary(
            t => (t.GroupNumber, t.TeamId),
            t => new LocalStanding { GroupNumber = t.GroupNumber, TeamId = t.TeamId });

        foreach (var f in groupFixtures)
        {
            if (f.TeamAId is not Guid a || f.TeamBId is not Guid b)
                continue;
            if (f.MatchId is not Guid mid
                || !matchesById.TryGetValue(mid, out var match)
                || match.Status != MatchStatus.Completed
                || !innsByMatch.TryGetValue(mid, out var inns))
                continue;

            var g = f.GroupNumber ?? 1;
            if (!rows.TryGetValue((g, a), out var ra) || !rows.TryGetValue((g, b), out var rb))
                continue;

            var (aRuns, aBalls) = LocalTeamRunsBalls(inns, a, delsByInn);
            var (bRuns, bBalls) = LocalTeamRunsBalls(inns, b, delsByInn);
            ra.Played++;
            rb.Played++;
            ra.RunsFor += aRuns;
            ra.RunsAgainst += bRuns;
            ra.BallsFor += aBalls;
            ra.BallsAgainst += bBalls;
            rb.RunsFor += bRuns;
            rb.RunsAgainst += aRuns;
            rb.BallsFor += bBalls;
            rb.BallsAgainst += aBalls;

            if (aRuns == bRuns)
            {
                ra.Tied++;
                rb.Tied++;
                ra.Points += 1;
                rb.Points += 1;
            }
            else if (aRuns > bRuns)
            {
                ra.Won++;
                rb.Lost++;
                ra.Points += 2;
            }
            else
            {
                rb.Won++;
                ra.Lost++;
                rb.Points += 2;
            }
        }

        return rows.Values
            .OrderBy(r => r.GroupNumber)
            .ThenByDescending(r => r.Points)
            .ThenByDescending(r => r.Nrr)
            .ThenByDescending(r => r.RunsFor)
            .ToList();
    }

    private static (int Runs, int Balls) LocalTeamRunsBalls(
        List<Innings> inns,
        Guid teamId,
        Dictionary<Guid, List<(int RunsOffBat, int ExtraRuns, bool IsWicket, bool IsLegalDelivery)>> delsByInn)
    {
        var runs = 0;
        var balls = 0;
        foreach (var inn in inns.Where(i => i.BattingTeamId == teamId))
        {
            if (!delsByInn.TryGetValue(inn.Id, out var dels))
                continue;
            foreach (var d in dels)
            {
                runs += d.RunsOffBat + d.ExtraRuns;
                if (d.IsLegalDelivery) balls++;
            }
        }

        return (runs, balls);
    }

    private static (int Runs, int Wickets) LocalTeamTotals(
        List<Innings> inns,
        Guid teamId,
        Dictionary<Guid, List<(int RunsOffBat, int ExtraRuns, bool IsWicket, bool IsLegalDelivery)>> delsByInn)
    {
        var runs = 0;
        var wickets = 0;
        foreach (var inn in inns.Where(i => i.BattingTeamId == teamId))
        {
            if (!delsByInn.TryGetValue(inn.Id, out var dels))
                continue;
            foreach (var d in dels)
            {
                runs += d.RunsOffBat + d.ExtraRuns;
                if (d.IsWicket) wickets++;
            }
        }

        return (runs, wickets);
    }

    private static string LocalResultText(
        Match match,
        string teamA,
        string teamB,
        int aRuns,
        int aWkts,
        int bRuns,
        int bWkts,
        Guid? firstBattingTeamId)
    {
        if (aRuns == bRuns)
            return "Match tied";

        var aWon = aRuns > bRuns;
        var winner = aWon ? teamA : teamB;
        var winnerRuns = aWon ? aRuns : bRuns;
        var loserRuns = aWon ? bRuns : aRuns;
        var winnerWkts = aWon ? aWkts : bWkts;

        if (firstBattingTeamId is Guid first)
        {
            var chasingIsA = first == match.TeamBId;
            var chasingWon = chasingIsA ? aWon : !aWon;
            if (chasingWon)
            {
                var roster = MatchRosterPayload.TryParse(match.SquadRosterJson);
                var squadSize = chasingIsA
                    ? roster?.TeamAPlayers.Count ?? 0
                    : roster?.TeamBPlayers.Count ?? 0;
                var chasingWkts = chasingIsA ? aWkts : bWkts;
                var wktsLeft = MatchResultRules.WicketsRemaining(
                    squadSize, chasingWkts, match.LastBatsmanCanBatAsSingleWicket);
                return $"{winner} won by {wktsLeft} wkt{(wktsLeft == 1 ? "" : "s")}";
            }

            var margin = winnerRuns - loserRuns;
            return $"{winner} won by {margin} run{(margin == 1 ? "" : "s")}";
        }

        return $"{winner} won ({winnerRuns}/{winnerWkts} vs {loserRuns})";
    }
}
