using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Contracts;
using JPL.Api.Data;
using JPL.Api.Models;
using JPL.Api.Services;

namespace JPL.Api.Controllers;

[ApiController]
[Route("api/series")]
public sealed class SeriesController : ControllerBase
{
    private readonly ApiDbContext _db;

    public SeriesController(ApiDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SeriesListDto>>> List(CancellationToken ct)
    {
        var list = await _db.Series.AsNoTracking()
            .Where(s => !s.IsDeleted)
            .Include(s => s.Fixtures)
            .OrderByDescending(s => s.UpdatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var teamIds = list.SelectMany(s => new[] { s.TeamAId, s.TeamBId })
            .Concat(list.Where(s => s.WinnerTeamId is Guid).Select(s => s.WinnerTeamId!.Value))
            .Distinct()
            .ToList();
        var names = teamIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Teams.AsNoTracking()
                .Where(t => teamIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct)
                .ConfigureAwait(false);

        var dtos = new List<SeriesListDto>();
        foreach (var s in list)
        {
            var (aWins, bWins) = await CountWinsAsync(s, ct).ConfigureAwait(false);
            dtos.Add(new SeriesListDto
            {
                Id = s.Id,
                Name = s.Name,
                TeamAId = s.TeamAId,
                TeamBId = s.TeamBId,
                TeamAName = names.GetValueOrDefault(s.TeamAId, "?"),
                TeamBName = names.GetValueOrDefault(s.TeamBId, "?"),
                MatchCount = s.MatchCount,
                Status = (byte)s.Status,
                StatusText = StatusText(s.Status),
                FixtureCount = s.Fixtures.Count,
                TeamAWins = aWins,
                TeamBWins = bWins,
                WinnerTeamId = s.WinnerTeamId,
                WinnerTeamName = s.IsTied
                    ? "Tied"
                    : s.WinnerTeamId is Guid w ? names.GetValueOrDefault(w) : null,
                IsTied = s.IsTied,
                UpdatedAtUtc = s.UpdatedAtUtc
            });
        }

        return Ok(dtos);
    }

    [HttpGet("page")]
    public async Task<ActionResult<PagedResult<SeriesListDto>>> GetPage(
        int skip = 0, int take = 15, string? q = null, CancellationToken ct = default)
    {
        (skip, take) = Paging.Clamp(skip, take);
        var query = _db.Series.AsNoTracking().Where(s => !s.IsDeleted);
        var like = Paging.LikePattern(q);
        if (like is not null)
            query = query.Where(s => EF.Functions.Like(s.Name, like));

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var list = await query
            .Include(s => s.Fixtures)
            .OrderByDescending(s => s.UpdatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var teamIds = list.SelectMany(s => new[] { s.TeamAId, s.TeamBId })
            .Concat(list.Where(s => s.WinnerTeamId is Guid).Select(s => s.WinnerTeamId!.Value))
            .Distinct()
            .ToList();
        var names = teamIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Teams.AsNoTracking()
                .Where(t => teamIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct)
                .ConfigureAwait(false);

        var dtos = new List<SeriesListDto>();
        foreach (var s in list)
        {
            var (aWins, bWins) = await CountWinsAsync(s, ct).ConfigureAwait(false);
            dtos.Add(new SeriesListDto
            {
                Id = s.Id,
                Name = s.Name,
                TeamAId = s.TeamAId,
                TeamBId = s.TeamBId,
                TeamAName = names.GetValueOrDefault(s.TeamAId, "?"),
                TeamBName = names.GetValueOrDefault(s.TeamBId, "?"),
                MatchCount = s.MatchCount,
                Status = (byte)s.Status,
                StatusText = StatusText(s.Status),
                FixtureCount = s.Fixtures.Count,
                TeamAWins = aWins,
                TeamBWins = bWins,
                WinnerTeamId = s.WinnerTeamId,
                WinnerTeamName = s.IsTied
                    ? "Tied"
                    : s.WinnerTeamId is Guid w ? names.GetValueOrDefault(w) : null,
                IsTied = s.IsTied,
                UpdatedAtUtc = s.UpdatedAtUtc
            });
        }

        return Ok(new PagedResult<SeriesListDto>(dtos, total, skip, take));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SeriesDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var s = await LoadTrackedAsync(id, ct).ConfigureAwait(false);
        if (s is null) return NotFound();

        var (standings, changed) = await SeriesEngine.ResolveProgressAsync(_db, s, ct).ConfigureAwait(false);
        if (changed)
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Ok(await ToDetailAsync(s, standings, ct).ConfigureAwait(false));
    }

    [HttpPost]
    public async Task<ActionResult<SeriesDetailDto>> Create([FromBody] SeriesUpsertRequest body, CancellationToken ct)
    {
        var name = RequireName(body.Name);
        SeriesEngine.ValidateSetup(body.TeamAId, body.TeamBId, body.MatchCount);
        await ValidateMatchDefaultsAsync(body, ct).ConfigureAwait(false);

        var entity = new Series
        {
            Id = Guid.NewGuid(),
            Name = name,
            TeamAId = body.TeamAId,
            TeamBId = body.TeamBId,
            MatchCount = body.MatchCount,
            VenueId = body.VenueId,
            FormatId = body.FormatId,
            Format = body.Format,
            OversLimit = OversRules.StoredOversLimit((OversMode)body.OversMode, body.OversLimit),
            OversMode = (OversMode)body.OversMode,
            LastBatsmanCanBatAsSingleWicket = body.LastBatsmanCanBatAsSingleWicket,
            BowlerMaxOvers = body.BowlerMaxOvers,
            Status = SeriesStatus.Draft
        };
        _db.Series.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(await ToDetailAsync(entity, new List<SeriesStandingRow>(), ct).ConfigureAwait(false));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SeriesDetailDto>> Update(Guid id, [FromBody] SeriesUpsertRequest body, CancellationToken ct)
    {
        var s = await LoadTrackedAsync(id, ct).ConfigureAwait(false);
        if (s is null) return NotFound();
        if (s.Status != SeriesStatus.Draft)
            return BadRequest(new { message = "Only draft series can be edited. Saved series are locked." });

        var name = RequireName(body.Name);
        SeriesEngine.ValidateSetup(body.TeamAId, body.TeamBId, body.MatchCount);
        await ValidateMatchDefaultsAsync(body, ct).ConfigureAwait(false);

        s.Name = name;
        s.TeamAId = body.TeamAId;
        s.TeamBId = body.TeamBId;
        s.MatchCount = body.MatchCount;
        s.VenueId = body.VenueId;
        s.FormatId = body.FormatId;
        s.Format = body.Format;
        s.OversLimit = OversRules.StoredOversLimit((OversMode)body.OversMode, body.OversLimit);
        s.OversMode = (OversMode)body.OversMode;
        s.LastBatsmanCanBatAsSingleWicket = body.LastBatsmanCanBatAsSingleWicket;
        s.BowlerMaxOvers = body.BowlerMaxOvers;
        s.UpdatedAtUtc = DateTime.Now;
        s.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(await ToDetailAsync(s, new List<SeriesStandingRow>(), ct).ConfigureAwait(false));
    }

    [HttpPost("{id:guid}/generate")]
    public async Task<ActionResult<SeriesDetailDto>> Generate(Guid id, CancellationToken ct)
    {
        var s = await LoadTrackedAsync(id, ct).ConfigureAwait(false);
        if (s is null) return NotFound();
        if (s.Status != SeriesStatus.Draft)
            return BadRequest(new { message = "Series already saved. Edit / delete is only allowed while Draft." });

        SeriesEngine.ValidateSetup(s.TeamAId, s.TeamBId, s.MatchCount);
        if (s.Fixtures.Count > 0)
            _db.SeriesFixtures.RemoveRange(s.Fixtures);

        foreach (var f in SeriesEngine.BuildFixtures(s))
            _db.SeriesFixtures.Add(f);

        s.Status = SeriesStatus.Scheduled;
        s.WinnerTeamId = null;
        s.PlayerOfTheSeriesPlayerId = null;
        s.BestBatsmanPlayerId = null;
        s.BestBowlerPlayerId = null;
        s.BestFielderPlayerId = null;
        s.IsTied = false;
        s.UpdatedAtUtc = DateTime.Now;
        s.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var (standings, _) = await SeriesEngine.ResolveProgressAsync(_db, s, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(await ToDetailAsync(s, standings, ct).ConfigureAwait(false));
    }

    [HttpPost("{id:guid}/fixtures/{fixtureId:guid}/link-match")]
    public async Task<ActionResult<SeriesDetailDto>> LinkMatch(
        Guid id, Guid fixtureId, [FromBody] SeriesLinkMatchRequest body, CancellationToken ct)
    {
        var s = await LoadTrackedAsync(id, ct).ConfigureAwait(false);
        if (s is null) return NotFound();
        if (s.Status == SeriesStatus.Draft)
            return BadRequest(new { message = "Save the series first before starting matches." });
        var fixture = s.Fixtures.FirstOrDefault(f => f.Id == fixtureId);
        if (fixture is null) return NotFound();

        fixture.MatchId = body.MatchId;
        if (s.Status == SeriesStatus.Scheduled)
            s.Status = SeriesStatus.InProgress;
        s.UpdatedAtUtc = DateTime.Now;
        s.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var (standings, changed) = await SeriesEngine.ResolveProgressAsync(_db, s, ct).ConfigureAwait(false);
        if (changed) await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(await ToDetailAsync(s, standings, ct).ConfigureAwait(false));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var s = await _db.Series.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct).ConfigureAwait(false);
        if (s is null) return NotFound();
        if (s.Status != SeriesStatus.Draft)
            return BadRequest(new { message = "Only draft series can be deleted. Saved series are locked." });
        s.IsDeleted = true;
        s.UpdatedAtUtc = DateTime.Now;
        s.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private async Task<(int AWins, int BWins)> CountWinsAsync(Series s, CancellationToken ct)
    {
        var matchIds = s.Fixtures.Where(f => f.MatchId is Guid).Select(f => f.MatchId!.Value).ToList();
        if (matchIds.Count == 0) return (0, 0);
        var matches = await _db.Matches.AsNoTracking()
            .Where(m => matchIds.Contains(m.Id) && !m.IsDeleted && m.Status == MatchStatus.Completed)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (matches.Count == 0) return (0, 0);
        var ids = matches.Select(m => m.Id).ToList();
        var inns = await _db.Innings.AsNoTracking()
            .Where(i => ids.Contains(i.MatchId))
            .Include(i => i.Deliveries)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var byMatch = inns.GroupBy(i => i.MatchId).ToDictionary(g => g.Key, g => g.ToList());
        var a = 0;
        var b = 0;
        foreach (var m in matches)
        {
            if (!byMatch.TryGetValue(m.Id, out var list)) continue;
            var (winner, tied) = TournamentEngine.MatchResult(m, list);
            if (tied || winner is null) continue;
            if (winner == s.TeamAId) a++;
            else if (winner == s.TeamBId) b++;
        }

        return (a, b);
    }

    private Task<Series?> LoadTrackedAsync(Guid id, CancellationToken ct)
        => _db.Series.Include(s => s.Fixtures).FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, ct);

    private async Task ValidateMatchDefaultsAsync(SeriesUpsertRequest body, CancellationToken ct)
    {
        if (body.VenueId == Guid.Empty)
            throw new InvalidOperationException("Venue is required.");
        var venueOk = await _db.Venues.AsNoTracking()
            .AnyAsync(v => v.Id == body.VenueId && !v.IsDeleted, ct)
            .ConfigureAwait(false);
        if (!venueOk)
            throw new InvalidOperationException("Selected venue was not found.");
        var resolved = await FormatCatalog.ResolveAsync(
                _db,
                body.FormatId ?? Guid.Empty,
                (MatchFormat)body.Format,
                (OversMode)body.OversMode,
                body.OversLimit,
                ct)
            .ConfigureAwait(false);
        body.FormatId = resolved.Id;
        body.Format = (byte)resolved.Format;
        body.OversMode = (byte)resolved.Mode;
        body.OversLimit = resolved.OversLimit;
        OversRules.Validate(resolved.Mode, resolved.OversLimit, body.BowlerMaxOvers);

        var teamIds = new[] { body.TeamAId, body.TeamBId };
        var teamCount = await _db.Teams.AsNoTracking()
            .CountAsync(t => teamIds.Contains(t.Id) && !t.IsDeleted, ct)
            .ConfigureAwait(false);
        if (teamCount != 2)
            throw new InvalidOperationException("Both series teams must exist.");
    }

    private static string RequireName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) throw new InvalidOperationException("Series name is required.");
        if (n.Length > 160) throw new InvalidOperationException("Series name is too long.");
        return n;
    }

    private static string StatusText(SeriesStatus s) => s switch
    {
        SeriesStatus.Draft => "Draft",
        SeriesStatus.Scheduled => "Saved",
        SeriesStatus.InProgress => "In progress",
        SeriesStatus.Completed => "Completed",
        _ => s.ToString()
    };

    private async Task<SeriesDetailDto> ToDetailAsync(
        Series s, List<SeriesStandingRow> standings, CancellationToken ct)
    {
        var teamIds = new[] { s.TeamAId, s.TeamBId }
            .Concat(s.WinnerTeamId is Guid w ? new[] { w } : Array.Empty<Guid>())
            .Distinct()
            .ToList();
        var names = await _db.Teams.AsNoTracking()
            .Where(x => teamIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct)
            .ConfigureAwait(false);

        var awardPlayerIds = new[]
            {
                s.PlayerOfTheSeriesPlayerId,
                s.BestBatsmanPlayerId,
                s.BestBowlerPlayerId,
                s.BestFielderPlayerId
            }
            .Where(id => id is Guid)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var playerNames = awardPlayerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Players.AsNoTracking()
                .Where(p => awardPlayerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
                .ConfigureAwait(false);

        var venueName = await _db.Venues.AsNoTracking()
            .Where(v => v.Id == s.VenueId)
            .Select(v => v.Name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? "?";

        string? formatName = null;
        if (s.FormatId is Guid formatId)
        {
            formatName = await _db.Formats.AsNoTracking()
                .Where(f => f.Id == formatId)
                .Select(f => f.Name)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        var matchIds = s.Fixtures.Where(f => f.MatchId is Guid).Select(f => f.MatchId!.Value).ToList();
        var matches = matchIds.Count == 0
            ? new List<Match>()
            : await _db.Matches.AsNoTracking()
                .Where(m => matchIds.Contains(m.Id) && !m.IsDeleted)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        var matchById = matches.ToDictionary(m => m.Id);

        var scoredIds = matches.Where(m => m.Status is MatchStatus.Completed or MatchStatus.InProgress).Select(m => m.Id).ToList();
        var innsByMatch = scoredIds.Count == 0
            ? new Dictionary<Guid, List<Innings>>()
            : (await _db.Innings.AsNoTracking()
                    .Where(i => scoredIds.Contains(i.MatchId))
                    .Include(i => i.Deliveries)
                    .ToListAsync(ct)
                    .ConfigureAwait(false))
                .GroupBy(i => i.MatchId)
                .ToDictionary(g => g.Key, g => g.ToList());

        var squadCounts = teamIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await _db.TeamPlayers.AsNoTracking()
                .Where(tp => teamIds.Contains(tp.TeamId))
                .GroupBy(tp => tp.TeamId)
                .ToDictionaryAsync(g => g.Key, g => g.Count(), ct)
                .ConfigureAwait(false);

        foreach (var row in standings)
            row.TeamName = names.GetValueOrDefault(row.TeamId, "?");

        var standingDtos = standings.Select((row, i) => new SeriesStandingDto
        {
            TeamId = row.TeamId,
            TeamName = row.TeamName,
            Played = row.Played,
            Won = row.Won,
            Lost = row.Lost,
            Tied = row.Tied,
            Points = row.Points,
            NetRunRate = row.NetRunRate,
            Rank = i + 1
        }).ToList();

        var teamAName = names.GetValueOrDefault(s.TeamAId, "?");
        var teamBName = names.GetValueOrDefault(s.TeamBId, "?");
        var winnerName = s.IsTied
            ? "Series tied"
            : s.WinnerTeamId is Guid wid ? names.GetValueOrDefault(wid) : null;

        return new SeriesDetailDto
        {
            Id = s.Id,
            Name = s.Name,
            TeamAId = s.TeamAId,
            TeamBId = s.TeamBId,
            TeamAName = teamAName,
            TeamBName = teamBName,
            MatchCount = s.MatchCount,
            VenueId = s.VenueId,
            VenueName = venueName,
            FormatId = s.FormatId,
            Format = s.Format,
            FormatText = formatName ?? (s.Format == (byte)MatchFormat.DoubleInnings ? "Double innings" : "Single inning"),
            OversLimit = s.OversLimit,
            OversMode = (byte)s.OversMode,
            LastBatsmanCanBatAsSingleWicket = s.LastBatsmanCanBatAsSingleWicket,
            BowlerMaxOvers = s.BowlerMaxOvers,
            Status = (byte)s.Status,
            StatusText = StatusText(s.Status),
            WinnerTeamId = s.WinnerTeamId,
            WinnerTeamName = winnerName,
            PlayerOfTheSeriesPlayerId = s.PlayerOfTheSeriesPlayerId,
            PlayerOfTheSeriesName = s.PlayerOfTheSeriesPlayerId is Guid pot
                ? playerNames.GetValueOrDefault(pot)
                : null,
            BestBatsmanPlayerId = s.BestBatsmanPlayerId,
            BestBatsmanName = s.BestBatsmanPlayerId is Guid bat
                ? playerNames.GetValueOrDefault(bat)
                : null,
            BestBowlerPlayerId = s.BestBowlerPlayerId,
            BestBowlerName = s.BestBowlerPlayerId is Guid bowl
                ? playerNames.GetValueOrDefault(bowl)
                : null,
            BestFielderPlayerId = s.BestFielderPlayerId,
            BestFielderName = s.BestFielderPlayerId is Guid field
                ? playerNames.GetValueOrDefault(field)
                : null,
            IsTied = s.IsTied,
            Version = s.Version,
            CreatedAtUtc = s.CreatedAtUtc,
            UpdatedAtUtc = s.UpdatedAtUtc,
            Standings = standingDtos,
            Fixtures = s.Fixtures.OrderBy(f => f.Sequence).Select(f =>
            {
                Match? match = f.MatchId is Guid mid && matchById.TryGetValue(mid, out var m) ? m : null;
                MatchStatus? ms = match?.Status;
                var resultText = string.Empty;
                if (match is { Status: MatchStatus.Completed }
                    && innsByMatch.TryGetValue(match.Id, out var matchInns))
                {
                    var (aRuns, aWkts) = TeamTotals(matchInns, match.TeamAId);
                    var (bRuns, bWkts) = TeamTotals(matchInns, match.TeamBId);
                    var firstBat = matchInns.OrderBy(i => i.InningsNumber).FirstOrDefault()?.BattingTeamId;
                    (resultText, _) = BuildResult(
                        match,
                        names.GetValueOrDefault(match.TeamAId, teamAName),
                        names.GetValueOrDefault(match.TeamBId, teamBName),
                        aRuns, aWkts, bRuns, bWkts, firstBat,
                        MatchSquadSnapshot.SizeFor(match.SquadRosterJson, match.TeamAId, true, squadCounts.GetValueOrDefault(match.TeamAId)),
                        MatchSquadSnapshot.SizeFor(match.SquadRosterJson, match.TeamBId, false, squadCounts.GetValueOrDefault(match.TeamBId)));
                }
                else if (match is { Status: MatchStatus.InProgress }
                         && innsByMatch.TryGetValue(match.Id, out var liveInns))
                {
                    var (aRuns, aWkts) = TeamTotals(liveInns, match.TeamAId);
                    var (bRuns, bWkts) = TeamTotals(liveInns, match.TeamBId);
                    resultText = $"LIVE · {aRuns}/{aWkts} vs {bRuns}/{bWkts}";
                }
                else if (match is { Status: MatchStatus.Abandoned })
                {
                    resultText = "No result";
                }

                return new SeriesFixtureDto
                {
                    Id = f.Id,
                    Sequence = f.Sequence,
                    Label = f.Label,
                    TeamAId = f.TeamAId,
                    TeamBId = f.TeamBId,
                    TeamAName = names.GetValueOrDefault(f.TeamAId, teamAName),
                    TeamBName = names.GetValueOrDefault(f.TeamBId, teamBName),
                    MatchId = f.MatchId,
                    MatchStatus = ms is null ? null : (byte)ms.Value,
                    MatchStatusText = ms switch
                    {
                        MatchStatus.Setup => "Setup",
                        MatchStatus.InProgress => "Live",
                        MatchStatus.Completed => "Completed",
                        MatchStatus.Abandoned => "Abandoned",
                        _ => ""
                    },
                    ResultText = resultText,
                    CanStart = f.MatchId is null
                               && (s.Status is SeriesStatus.Scheduled or SeriesStatus.InProgress),
                    CanResume = false,
                    CanOpenScorecard = ms is MatchStatus.Completed or MatchStatus.InProgress or MatchStatus.Setup
                };
            }).ToList()
        };
    }

    private static (int Runs, int Wickets) TeamTotals(IReadOnlyList<Innings> inns, Guid teamId)
    {
        var runs = 0;
        var wickets = 0;
        foreach (var inn in inns.Where(i => i.BattingTeamId == teamId))
        {
            foreach (var d in inn.Deliveries)
            {
                runs += d.RunsOffBat + d.ExtraRuns;
                if (d.IsWicket) wickets++;
            }
        }

        return (runs, wickets);
    }

    private static (string Text, Guid? WinnerId) BuildResult(
        Match match, string teamA, string teamB,
        int aRuns, int aWkts, int bRuns, int bWkts, Guid? firstBattingTeamId,
        int teamASquadSize, int teamBSquadSize)
    {
        if (aRuns == bRuns)
            return ("Match tied", null);

        Guid winnerId;
        string winName;
        int winWkts;
        if (aRuns > bRuns)
        {
            winnerId = match.TeamAId;
            winName = teamA;
            winWkts = aWkts;
        }
        else
        {
            winnerId = match.TeamBId;
            winName = teamB;
            winWkts = bWkts;
        }

        var chased = firstBattingTeamId is Guid first && first != winnerId;
        if (chased)
        {
            var squadSize = winnerId == match.TeamAId ? teamASquadSize : teamBSquadSize;
            var remaining = MatchResultRules.WicketsRemaining(
                squadSize, winWkts, match.LastBatsmanCanBatAsSingleWicket);
            var unit = remaining == 1 ? "wicket" : "wickets";
            return ($"{winName} won by {remaining} {unit}", winnerId);
        }

        var margin = Math.Abs(aRuns - bRuns);
        var runUnit = margin == 1 ? "run" : "runs";
        return ($"{winName} won by {margin} {runUnit}", winnerId);
    }
}
