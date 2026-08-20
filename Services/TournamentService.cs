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
    public Guid VenueId { get; init; }
    public string VenueName { get; init; } = string.Empty;
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
    IReadOnlyList<(Guid TeamId, int GroupNumber)> Teams);

public interface ITournamentService
{
    Task<IReadOnlyList<TournamentListItem>> GetAllAsync(CancellationToken ct = default);
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
    private readonly ISyncService _sync;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;

    public TournamentService(
        IApiClient api,
        IAuthService auth,
        ISyncService sync,
        IDbContextFactory<JplDbContext> dbFactory)
    {
        _api = api;
        _auth = auth;
        _sync = sync;
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<TournamentListItem>> GetAllAsync(CancellationToken ct = default)
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

    public async Task<TournamentDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _api.GetTournamentAsync(id, ct).ConfigureAwait(false);
        if (dto is null) return null;
        var detail = ToDetail(dto);
        await OverlayLocalMatchStateAsync(detail, ct).ConfigureAwait(false);
        return detail;
    }

    public async Task<TournamentDetail> CreateAsync(TournamentDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        Validate(draft);
        var remote = await _api.CreateTournamentAsync(ToBody(draft), ct).ConfigureAwait(false);
        return ToDetail(remote);
    }

    public async Task<TournamentDetail> UpdateAsync(Guid id, TournamentDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        Validate(draft);
        var remote = await _api.UpdateTournamentAsync(id, ToBody(draft), ct).ConfigureAwait(false);
        return ToDetail(remote);
    }

    public async Task<TournamentDetail> GenerateAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var remote = await _api.GenerateTournamentAsync(id, ct).ConfigureAwait(false);
        return ToDetail(remote);
    }

    public Task LinkMatchAsync(Guid tournamentId, Guid fixtureId, Guid matchId, CancellationToken ct = default)
        => _api.LinkTournamentMatchAsync(tournamentId, fixtureId, matchId, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await _api.DeleteTournamentAsync(id, ct).ConfigureAwait(false);
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
        if (draft.GroupCount is < 1 or > 2)
            throw new ArgumentException("Groups must be 1 or 2.");
        if (draft.QualifyPerGroup is < 1 or > 2)
            throw new ArgumentException("Qualify per group must be 1 or 2.");
        if (draft.VenueId == Guid.Empty)
            throw new ArgumentException("Venue is required.");
        if (draft.Format is not (0 or 1))
            throw new ArgumentException("Select Single inning or Double innings.");
        OversRules.Validate(draft.OversMode, draft.OversLimit, draft.BowlerMaxOvers);
        if (draft.Teams.Count == 0)
            throw new ArgumentException("Add at least one team.");
        for (var g = 1; g <= draft.GroupCount; g++)
        {
            var n = draft.Teams.Count(t => t.GroupNumber == g);
            if (n < draft.QualifyPerGroup)
                throw new ArgumentException($"Group {g} needs at least {draft.QualifyPerGroup} team(s).");
        }
    }

    private static object ToBody(TournamentDraft draft) => new
    {
        name = draft.Name.Trim(),
        groupCount = draft.GroupCount,
        qualifyPerGroup = draft.QualifyPerGroup,
        venueId = draft.VenueId,
        format = draft.Format,
        oversLimit = OversRules.StoredOversLimit(draft.OversMode, draft.OversLimit),
        oversMode = (byte)draft.OversMode,
        lastBatsmanCanBatAsSingleWicket = draft.LastBatsmanCanBatAsSingleWicket,
        bowlerMaxOvers = draft.BowlerMaxOvers,
        teams = draft.Teams.Select(t => new { teamId = t.TeamId, groupNumber = t.GroupNumber }).ToList()
    };

    private static TournamentDetail ToDetail(ApiTournamentDetailDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        GroupCount = dto.GroupCount,
        QualifyPerGroup = dto.QualifyPerGroup,
        VenueId = dto.VenueId,
        VenueName = dto.VenueName,
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
            TeamAId = f.TeamAId,
            TeamBId = f.TeamBId,
            TeamAName = f.TeamAName,
            TeamBName = f.TeamBName,
            MatchId = f.MatchId,
            MatchStatusText = f.MatchStatusText,
            ResultText = f.ResultText ?? string.Empty,
            CanStart = f.CanStart,
            CanResume = f.CanResume,
            CanOpenScorecard = f.CanOpenScorecard
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
        if (localMatches.Count == 0)
            return;

        var byId = localMatches.ToDictionary(m => m.Id);
        var byFixture = localMatches
            .Where(m => m.TournamentFixtureId is Guid)
            .GroupBy(m => m.TournamentFixtureId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.UpdatedAtUtc).First());

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
        var draftOrLocked = detail.Status == 0;
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
        }

        ApplyLocalKnockoutTeams(detail, byId, innsByMatch, delsByInn);
        ApplyLocalStandings(detail, byId, innsByMatch, delsByInn);
        ApplyKnockoutTiebreakResultText(detail);

        // One live match per league on this device — other leagues' local matches do not block.
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
            f.CanStart = teamsReady && f.MatchId is null && !draftOrLocked
                         && detail.Status is not 0 and not 3
                         && leagueActiveId is null;
            f.CanResume = local is not null
                          && (local.Status is MatchStatus.Setup or MatchStatus.InProgress)
                          && !draftOrLocked
                          && leagueActiveId == local.Id;
            f.CanOpenScorecard = local is { Status: MatchStatus.Completed };
            if (f.CanResume || f.CanOpenScorecard)
                f.CanStart = false;
        }

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

    private static void ApplyLocalStandings(
        TournamentDetail detail,
        Dictionary<Guid, Match> matchesById,
        Dictionary<Guid, List<Innings>> innsByMatch,
        Dictionary<Guid, List<(int RunsOffBat, int ExtraRuns, bool IsWicket, bool IsLegalDelivery)>> delsByInn)
    {
        var groupFixtures = detail.Fixtures
            .Where(f => string.Equals(f.StageText, "Group", StringComparison.OrdinalIgnoreCase)
                        || f.GroupNumber is int)
            .Where(f => !f.StageText.Contains("Semi", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(f.StageText, "Final", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(f.Label, "Final", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Always rebuild from local group results (API standings stay 0 until tournament Sync).
        if (groupFixtures.Count == 0 && detail.Teams.Count == 0)
            return;

        var local = BuildLocalStandings(detail, groupFixtures, matchesById, innsByMatch, delsByInn);
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
        var groupFixtures = detail.Fixtures
            .Where(f => string.Equals(f.StageText, "Group", StringComparison.OrdinalIgnoreCase)
                        || f.GroupNumber is int)
            .Where(f => !f.StageText.Contains("Semi", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(f.StageText, "Final", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(f.Label, "Final", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var groupComplete = groupFixtures.Count > 0 && groupFixtures.All(f =>
            f.MatchId is Guid mid
            && matchesById.TryGetValue(mid, out var m)
            && m.Status == MatchStatus.Completed);

        if (!groupComplete)
            return;

        var standings = BuildLocalStandings(detail, groupFixtures, matchesById, innsByMatch, delsByInn);
        var names = detail.Teams.ToDictionary(t => t.TeamId, t => t.TeamName);

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

        foreach (var f in detail.Fixtures)
        {
            var isFinal = string.Equals(f.StageText, "Final", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(f.Label, "Final", StringComparison.OrdinalIgnoreCase);
            var isSemi = f.StageText.Contains("Semi", StringComparison.OrdinalIgnoreCase)
                         || f.Label.Contains("Semi", StringComparison.OrdinalIgnoreCase);

            if (isFinal && detail.GroupCount == 1 && detail.QualifyPerGroup == 2)
                SetTeams(f, Pick(1, 1), Pick(1, 2));
            else if (isFinal && detail.GroupCount == 2 && detail.QualifyPerGroup == 1)
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
                    .OrderBy(x => x.Label)
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

            var groupFixtures = detail.Fixtures
                .Where(f => string.Equals(f.StageText, "Group", StringComparison.OrdinalIgnoreCase)
                            || f.GroupNumber is int)
                .Where(f => !f.StageText.Contains("Semi", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(f.StageText, "Final", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(f.Label, "Final", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var standings = BuildLocalStandings(detail, groupFixtures, matchesById, innsByMatch, delsByInn);
            return BetterOnLocalTable(match.TeamAId, match.TeamBId, standings);
        }

        // RR-only: 1 group / 1 qualify
        if (detail.GroupCount == 1 && detail.QualifyPerGroup == 1)
        {
            var groupFixtures = detail.Fixtures
                .Where(f => string.Equals(f.StageText, "Group", StringComparison.OrdinalIgnoreCase)
                            || f.GroupNumber is not null)
                .ToList();
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
