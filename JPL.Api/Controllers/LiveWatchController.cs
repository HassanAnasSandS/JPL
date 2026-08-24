using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Contracts;
using JPL.Api.Data;
using JPL.Api.Models;
using JPL.Api.Services;

namespace JPL.Api.Controllers;

[ApiController]
public sealed class LiveWatchController : ControllerBase
{
    private readonly IApiDbFactory _dbs;
    private readonly IWebHostEnvironment _env;

    public LiveWatchController(IApiDbFactory dbs, IWebHostEnvironment env)
    {
        _dbs = dbs;
        _env = env;
    }

    /// <summary>Public iframe page: live stream + live score together.</summary>
    [HttpGet("/watch/{leagueId:guid}/{matchId:guid}")]
    public async Task<IActionResult> Watch(Guid leagueId, Guid matchId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Content-Security-Policy"] = "frame-ancestors *";
        Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        var path = Path.Combine(_env.WebRootPath, "embed.html");
        if (!System.IO.File.Exists(path))
            return NotFound("Live watch page is missing.");

        var html = await System.IO.File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        html = html
            .Replace("{{LEAGUE_ID}}", leagueId.ToString("D"), StringComparison.Ordinal)
            .Replace("{{MATCH_ID}}", matchId.ToString("D"), StringComparison.Ordinal);
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpGet("/api/public/leagues/{leagueId:guid}/matches/{matchId:guid}")]
    public async Task<ActionResult<PublicLiveMatchDto>> GetPublicMatch(
        Guid leagueId, Guid matchId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        await using var master = _dbs.CreateMaster();
        var leagueOk = await master.Leagues.AsNoTracking()
            .AnyAsync(l => l.Id == leagueId && l.IsActive, ct)
            .ConfigureAwait(false);
        if (!leagueOk)
            return NotFound();

        await using var db = _dbs.CreateForLeague(leagueId);
        var match = await db.Matches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId && !m.IsDeleted, ct)
            .ConfigureAwait(false);
        if (match is null)
            return NotFound();

        var innings = await db.Innings.AsNoTracking()
            .Where(i => i.MatchId == matchId)
            .Include(i => i.Deliveries)
            .OrderBy(i => i.InningsNumber)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var teamIds = new[] { match.TeamAId, match.TeamBId };
        var teams = await db.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct)
            .ConfigureAwait(false);

        var (aRuns, aWkts) = TeamTotals(innings, match.TeamAId);
        var (bRuns, bWkts) = TeamTotals(innings, match.TeamBId);
        var teamA = teams.GetValueOrDefault(match.TeamAId, "?");
        var teamB = teams.GetValueOrDefault(match.TeamBId, "?");

        string result;
        if (match.Status == MatchStatus.Completed)
            result = "Completed";
        else if (match.Status == MatchStatus.InProgress)
            result = "LIVE";
        else if (match.Status == MatchStatus.Abandoned)
            result = "Abandoned";
        else
            result = "Upcoming";

        var current = innings.LastOrDefault();
        string? batting = null;
        string? innScore = null;
        string? overs = null;
        string? striker = null;
        string? nonStriker = null;
        string? bowler = null;

        if (current is not null)
        {
            batting = teams.GetValueOrDefault(current.BattingTeamId, "?");
            var balls = current.Deliveries.OrderBy(d => d.Sequence).ToList();
            var runs = balls.Sum(d => d.RunsOffBat + d.ExtraRuns);
            var wkts = balls.Count(d => d.IsWicket);
            var legal = balls.Count(d => d.IsLegalDelivery);
            innScore = $"{runs}/{wkts}";
            overs = $"{legal / 6}.{legal % 6}";

            var last = balls.LastOrDefault();
            if (last is not null)
            {
                var playerIds = new[] { last.BatterId, last.NonStrikerId, last.BowlerId }
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();
                var names = await db.Players.AsNoTracking()
                    .Where(p => playerIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
                    .ConfigureAwait(false);

                string Name(Guid id) => names.GetValueOrDefault(id, "?");

                var strikerBalls = balls.Where(d => d.BatterId == last.BatterId).ToList();
                var nonBalls = balls.Where(d => d.BatterId == last.NonStrikerId).ToList();
                var bowled = balls.Where(d => d.BowlerId == last.BowlerId).ToList();
                var bowledLegal = bowled.Count(d => d.IsLegalDelivery);

                striker = $"{Name(last.BatterId)} {strikerBalls.Sum(d => d.RunsOffBat)}* ({strikerBalls.Count})";
                if (last.NonStrikerId != Guid.Empty && last.NonStrikerId != last.BatterId)
                    nonStriker = $"{Name(last.NonStrikerId)} {nonBalls.Sum(d => d.RunsOffBat)}* ({nonBalls.Count})";
                bowler = $"{Name(last.BowlerId)} {bowled.Count(d => d.IsWicket)}-{bowled.Sum(d => d.RunsOffBat + d.ExtraRuns)} ({bowledLegal / 6}.{bowledLegal % 6})";
            }
        }

        var embed = LiveStreamUrls.ToEmbedSrc(match.LiveStreamUrl);
        return Ok(new PublicLiveMatchDto
        {
            MatchId = match.Id,
            LeagueId = leagueId,
            Title = match.Title,
            Status = (byte)match.Status,
            TeamAName = teamA,
            TeamBName = teamB,
            TeamAScore = $"{aRuns}/{aWkts}",
            TeamBScore = $"{bRuns}/{bWkts}",
            ResultText = result,
            LiveStreamUrl = match.LiveStreamUrl,
            EmbedSrc = embed,
            HasStream = !string.IsNullOrWhiteSpace(embed) && match.Status == MatchStatus.InProgress,
            BattingTeamName = batting,
            InningsScore = innScore,
            OversText = overs,
            StrikerText = striker,
            NonStrikerText = nonStriker,
            BowlerText = bowler
        });
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
}
