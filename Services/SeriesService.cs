using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public sealed class SeriesListItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string MetaLine { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string WinnerLine { get; init; } = string.Empty;
    public byte Status { get; init; }
    public bool NeedsSync { get; init; }
}

public sealed class SeriesDetail
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid TeamAId { get; init; }
    public Guid TeamBId { get; init; }
    public string TeamAName { get; init; } = string.Empty;
    public string TeamBName { get; init; } = string.Empty;
    public int MatchCount { get; init; }
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
    public bool IsTied { get; set; }
    public string ScoreLine { get; set; } = string.Empty;
    public IReadOnlyList<SeriesFixtureItem> Fixtures { get; init; } = Array.Empty<SeriesFixtureItem>();
    public IReadOnlyList<SeriesStandingItem> Standings { get; set; } = Array.Empty<SeriesStandingItem>();
}

public sealed class SeriesFixtureItem
{
    public Guid Id { get; init; }
    public string Label { get; init; } = string.Empty;
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
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

public sealed class SeriesStandingItem
{
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

public sealed record SeriesDraft(
    string Name,
    Guid TeamAId,
    Guid TeamBId,
    int MatchCount,
    Guid VenueId,
    byte Format,
    int OversLimit,
    bool LastBatsmanCanBatAsSingleWicket,
    int BowlerMaxOvers,
    OversMode OversMode);

public interface ISeriesService
{
    Task<IReadOnlyList<SeriesListItem>> GetAllAsync(CancellationToken ct = default);
    Task<SeriesDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<SeriesDetail> CreateAsync(SeriesDraft draft, CancellationToken ct = default);
    Task<SeriesDetail> UpdateAsync(Guid id, SeriesDraft draft, CancellationToken ct = default);
    Task<SeriesDetail> GenerateAsync(Guid id, CancellationToken ct = default);
    Task LinkMatchAsync(Guid seriesId, Guid fixtureId, Guid matchId, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> IsReadyToSyncAsync(Guid seriesId, CancellationToken ct = default);
    Task EnsureReadyToSyncAsync(Guid seriesId, CancellationToken ct = default);
}

public sealed class SeriesService : ISeriesService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly ISyncService _sync;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;

    public SeriesService(
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

    public async Task<IReadOnlyList<SeriesListItem>> GetAllAsync(CancellationToken ct = default)
    {
        var remote = await _api.GetSeriesListAsync(ct).ConfigureAwait(false);
        var pending = await _sync.GetPendingSeriesIdsAsync(ct).ConfigureAwait(false);
        var list = new List<SeriesListItem>();
        foreach (var s in remote)
        {
            var waiting = pending.Contains(s.Id);
            var ready = waiting && await IsReadyToSyncAsync(s.Id, ct).ConfigureAwait(false);
            var score = $"{s.TeamAWins}–{s.TeamBWins}";
            var winner = s.IsTied
                ? "Series tied"
                : string.IsNullOrWhiteSpace(s.WinnerTeamName) ? "" : $"Winner: {s.WinnerTeamName}";
            list.Add(new SeriesListItem
            {
                Id = s.Id,
                Name = s.Name,
                Status = s.Status,
                StatusText = s.StatusText,
                MetaLine = $"{s.TeamAName} vs {s.TeamBName} · {s.MatchCount} matches · {score}",
                WinnerLine = winner,
                NeedsSync = ready
            });
        }

        return list;
    }

    public async Task<SeriesDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _api.GetSeriesAsync(id, ct).ConfigureAwait(false);
        if (dto is null) return null;
        var detail = ToDetail(dto);
        await OverlayLocalMatchStateAsync(detail, ct).ConfigureAwait(false);
        return detail;
    }

    public async Task<SeriesDetail> CreateAsync(SeriesDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        Validate(draft);
        var remote = await _api.CreateSeriesAsync(ToBody(draft), ct).ConfigureAwait(false);
        return ToDetail(remote);
    }

    public async Task<SeriesDetail> UpdateAsync(Guid id, SeriesDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        Validate(draft);
        var remote = await _api.UpdateSeriesAsync(id, ToBody(draft), ct).ConfigureAwait(false);
        return ToDetail(remote);
    }

    public async Task<SeriesDetail> GenerateAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var remote = await _api.GenerateSeriesAsync(id, ct).ConfigureAwait(false);
        return ToDetail(remote);
    }

    public Task LinkMatchAsync(Guid seriesId, Guid fixtureId, Guid matchId, CancellationToken ct = default)
        => _api.LinkSeriesMatchAsync(seriesId, fixtureId, matchId, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await _api.DeleteSeriesAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<bool> IsReadyToSyncAsync(Guid seriesId, CancellationToken ct = default)
    {
        var pending = await _sync.GetPendingSeriesIdsAsync(ct).ConfigureAwait(false);
        if (!pending.Contains(seriesId))
            return false;

        try
        {
            var detail = await GetAsync(seriesId, ct).ConfigureAwait(false);
            return detail is not null && IsSeriesFullyComplete(detail);
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureReadyToSyncAsync(Guid seriesId, CancellationToken ct = default)
    {
        if (await IsReadyToSyncAsync(seriesId, ct).ConfigureAwait(false))
            return;
        throw new InvalidOperationException(
            "Sync only after every series match is completed or abandoned.");
    }

    private static bool IsSeriesFullyComplete(SeriesDetail detail)
    {
        if (detail.Status == 3 || detail.WinnerTeamId is Guid || detail.IsTied)
            return true;
        if (detail.Fixtures.Count == 0)
            return false;
        return detail.Fixtures.All(f =>
            string.Equals(f.MatchStatusText, "Completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.MatchStatusText, "Abandoned", StringComparison.OrdinalIgnoreCase));
    }

    private static void Validate(SeriesDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
            throw new ArgumentException("Series name is required.");
        if (draft.TeamAId == Guid.Empty || draft.TeamBId == Guid.Empty)
            throw new ArgumentException("Select both teams.");
        if (draft.TeamAId == draft.TeamBId)
            throw new ArgumentException("A series must be between two different teams.");
        if (draft.MatchCount is < 1 or > 7)
            throw new ArgumentException("A series must have between 1 and 7 matches.");
        if (draft.VenueId == Guid.Empty)
            throw new ArgumentException("Venue is required.");
        if (draft.Format is not (0 or 1))
            throw new ArgumentException("Select Single inning or Double innings.");
        OversRules.Validate(draft.OversMode, draft.OversLimit, draft.BowlerMaxOvers);
    }

    private static object ToBody(SeriesDraft draft) => new
    {
        name = draft.Name.Trim(),
        teamAId = draft.TeamAId,
        teamBId = draft.TeamBId,
        matchCount = draft.MatchCount,
        venueId = draft.VenueId,
        format = draft.Format,
        oversLimit = OversRules.StoredOversLimit(draft.OversMode, draft.OversLimit),
        oversMode = (byte)draft.OversMode,
        lastBatsmanCanBatAsSingleWicket = draft.LastBatsmanCanBatAsSingleWicket,
        bowlerMaxOvers = draft.BowlerMaxOvers
    };

    private static SeriesDetail ToDetail(ApiSeriesDetailDto dto)
    {
        var aWins = dto.Standings.FirstOrDefault(s => s.TeamId == dto.TeamAId)?.Won ?? 0;
        var bWins = dto.Standings.FirstOrDefault(s => s.TeamId == dto.TeamBId)?.Won ?? 0;
        var score = $"{aWins}–{bWins}";
        return new SeriesDetail
        {
            Id = dto.Id,
            Name = dto.Name,
            TeamAId = dto.TeamAId,
            TeamBId = dto.TeamBId,
            TeamAName = dto.TeamAName,
            TeamBName = dto.TeamBName,
            MatchCount = dto.MatchCount,
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
            IsTied = dto.IsTied,
            ScoreLine = dto.IsTied ? $"Series tied {score}" : score,
            Fixtures = dto.Fixtures.Select(f => new SeriesFixtureItem
            {
                Id = f.Id,
                Label = f.Label,
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
            Standings = dto.Standings.Select(s => new SeriesStandingItem
            {
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
    }

    private async Task OverlayLocalMatchStateAsync(SeriesDetail detail, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var localMatches = await db.Matches.AsNoTracking()
            .Where(m => m.SeriesId == detail.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (localMatches.Count == 0)
            return;

        var byId = localMatches.ToDictionary(m => m.Id);
        var byFixture = localMatches
            .Where(m => m.SeriesFixtureId is Guid)
            .GroupBy(m => m.SeriesFixtureId!.Value)
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

        var draft = detail.Status == 0;
        foreach (var f in detail.Fixtures)
        {
            Match? local = null;
            if (f.MatchId is Guid mid && byId.TryGetValue(mid, out var byMatch))
                local = byMatch;
            else if (byFixture.TryGetValue(f.Id, out var byFix))
                local = byFix;
            if (local is null) continue;

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
            else if (local.Status == MatchStatus.Abandoned)
            {
                f.ResultText = "No result";
            }
        }

        ApplyLocalStandings(detail, byId, innsByMatch, delsByInn);

        var liveId = (await db.Matches.AsNoTracking()
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

            f.CanStart = f.MatchId is null && !draft
                         && detail.Status is not 0 and not 3
                         && liveId is null;
            f.CanResume = local is not null
                          && (local.Status is MatchStatus.Setup or MatchStatus.InProgress)
                          && !draft
                          && liveId == local.Id;
            f.CanOpenScorecard = local is { Status: MatchStatus.Completed };
            if (f.CanResume || f.CanOpenScorecard)
                f.CanStart = false;
        }

        var allDecided = detail.Fixtures.Count > 0 && detail.Fixtures.All(f =>
            string.Equals(f.MatchStatusText, "Completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.MatchStatusText, "Abandoned", StringComparison.OrdinalIgnoreCase));
        if (!allDecided)
            return;

        var aWins = detail.Standings.FirstOrDefault(s => s.TeamId == detail.TeamAId)?.Won ?? 0;
        var bWins = detail.Standings.FirstOrDefault(s => s.TeamId == detail.TeamBId)?.Won ?? 0;
        detail.ScoreLine = $"{aWins}–{bWins}";
        if (aWins == bWins)
        {
            detail.IsTied = true;
            detail.WinnerTeamId = null;
            detail.WinnerTeamName = "Series tied";
            detail.ScoreLine = $"Series tied {aWins}–{bWins}";
        }
        else
        {
            detail.IsTied = false;
            detail.WinnerTeamId = aWins > bWins ? detail.TeamAId : detail.TeamBId;
            detail.WinnerTeamName = aWins > bWins ? detail.TeamAName : detail.TeamBName;
        }

        if (detail.Status != 3)
        {
            detail.Status = 3;
            detail.StatusText = "Completed";
        }
    }

    private static void ApplyLocalStandings(
        SeriesDetail detail,
        Dictionary<Guid, Match> matchesById,
        Dictionary<Guid, List<Innings>> innsByMatch,
        Dictionary<Guid, List<(int RunsOffBat, int ExtraRuns, bool IsWicket, bool IsLegalDelivery)>> delsByInn)
    {
        var rows = new Dictionary<Guid, SeriesStandingItem>
        {
            [detail.TeamAId] = new() { TeamId = detail.TeamAId, TeamName = detail.TeamAName },
            [detail.TeamBId] = new() { TeamId = detail.TeamBId, TeamName = detail.TeamBName }
        };
        var acc = new Dictionary<Guid, (int P, int W, int L, int T, int Pts, int Rf, int Ra, int Bf, int Ba)>
        {
            [detail.TeamAId] = default,
            [detail.TeamBId] = default
        };

        foreach (var f in detail.Fixtures)
        {
            if (f.MatchId is not Guid mid
                || !matchesById.TryGetValue(mid, out var match)
                || match.Status != MatchStatus.Completed
                || !innsByMatch.TryGetValue(mid, out var inns))
                continue;

            var (aRuns, aBalls) = LocalTeamRunsBalls(inns, detail.TeamAId, delsByInn);
            var (bRuns, bBalls) = LocalTeamRunsBalls(inns, detail.TeamBId, delsByInn);
            var a = acc[detail.TeamAId];
            var b = acc[detail.TeamBId];
            a.P++; b.P++;
            a.Rf += aRuns; a.Ra += bRuns; a.Bf += aBalls; a.Ba += bBalls;
            b.Rf += bRuns; b.Ra += aRuns; b.Bf += bBalls; b.Ba += aBalls;
            if (aRuns == bRuns) { a.T++; b.T++; a.Pts += 1; b.Pts += 1; }
            else if (aRuns > bRuns) { a.W++; b.L++; a.Pts += 2; }
            else { b.W++; a.L++; b.Pts += 2; }
            acc[detail.TeamAId] = a;
            acc[detail.TeamBId] = b;
        }

        double Nrr(int rf, int ra, int bf, int ba)
        {
            var of = bf <= 0 ? 0 : rf / (bf / 6.0);
            var oa = ba <= 0 ? 0 : ra / (ba / 6.0);
            return of - oa;
        }

        var ordered = acc
            .OrderByDescending(kv => kv.Value.Pts)
            .ThenByDescending(kv => kv.Value.W)
            .ThenByDescending(kv => Nrr(kv.Value.Rf, kv.Value.Ra, kv.Value.Bf, kv.Value.Ba))
            .ToList();
        var rank = 1;
        var list = new List<SeriesStandingItem>();
        foreach (var kv in ordered)
        {
            var v = kv.Value;
            list.Add(new SeriesStandingItem
            {
                TeamId = kv.Key,
                TeamName = kv.Key == detail.TeamAId ? detail.TeamAName : detail.TeamBName,
                Rank = rank++,
                Played = v.P,
                Won = v.W,
                Lost = v.L,
                Tied = v.T,
                Points = v.Pts,
                NetRunRate = Math.Round(Nrr(v.Rf, v.Ra, v.Bf, v.Ba), 3)
            });
        }

        detail.Standings = list;
        _ = rows;
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
            if (!delsByInn.TryGetValue(inn.Id, out var dels)) continue;
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
            if (!delsByInn.TryGetValue(inn.Id, out var dels)) continue;
            foreach (var d in dels)
            {
                runs += d.RunsOffBat + d.ExtraRuns;
                if (d.IsWicket) wickets++;
            }
        }

        return (runs, wickets);
    }

    private static string LocalResultText(
        Match match, string teamA, string teamB,
        int aRuns, int aWkts, int bRuns, int bWkts, Guid? firstBattingTeamId)
    {
        if (aRuns == bRuns)
            return "Match tied";

        var aWon = aRuns > bRuns;
        var winner = aWon ? teamA : teamB;
        var winnerRuns = aWon ? aRuns : bRuns;
        var loserRuns = aWon ? bRuns : aRuns;

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

        return $"{winner} won";
    }
}
