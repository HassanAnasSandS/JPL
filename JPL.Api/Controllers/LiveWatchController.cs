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
    private readonly NeuralCommentaryTts _tts;

    public LiveWatchController(IApiDbFactory dbs, IWebHostEnvironment env, NeuralCommentaryTts tts)
    {
        _dbs = dbs;
        _env = env;
        _tts = tts;
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
        Guid? strikerId = null;
        Guid? nonStrikerId = null;
        Guid? bowlerId = null;
        var lastSeq = 0;
        string? commentaryEn = null;
        string? commentaryUr = null;
        string? commentaryCue = null;

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
                    .Append(last.DismissedPlayerId ?? Guid.Empty)
                    .Append(last.FielderId ?? Guid.Empty)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();
                var names = await db.Players.AsNoTracking()
                    .Where(p => playerIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
                    .ConfigureAwait(false);

                string Name(Guid id)
                {
                    var n = names.GetValueOrDefault(id, "");
                    return string.IsNullOrWhiteSpace(n) || n == "?" ? "" : n;
                }

                var strikerBalls = balls.Where(d => d.BatterId == last.BatterId).ToList();
                var nonBalls = balls.Where(d => d.BatterId == last.NonStrikerId).ToList();
                var bowled = balls.Where(d => d.BowlerId == last.BowlerId).ToList();
                var bowledLegal = bowled.Count(d => d.IsLegalDelivery);

                striker = $"{Name(last.BatterId)} {strikerBalls.Sum(d => d.RunsOffBat)}* ({strikerBalls.Count})";
                strikerId = last.BatterId == Guid.Empty ? null : last.BatterId;
                if (last.NonStrikerId != Guid.Empty && last.NonStrikerId != last.BatterId)
                {
                    nonStriker = $"{Name(last.NonStrikerId)} {nonBalls.Sum(d => d.RunsOffBat)}* ({nonBalls.Count})";
                    nonStrikerId = last.NonStrikerId;
                }
                bowler = $"{Name(last.BowlerId)} {bowled.Count(d => d.IsWicket)}-{bowled.Sum(d => d.RunsOffBat + d.ExtraRuns)} ({bowledLegal / 6}.{bowledLegal % 6})";
                bowlerId = last.BowlerId == Guid.Empty ? null : last.BowlerId;

                var line = LiveBallCommentary.For(
                    last,
                    balls,
                    Name(last.BatterId),
                    Name(last.BowlerId),
                    last.FielderId is Guid fid && fid != Guid.Empty ? Name(fid) : null,
                    batting ?? "?",
                    runs,
                    wkts);
                lastSeq = last.Sequence;
                commentaryEn = line?.English;
                commentaryUr = line?.Urdu;
                commentaryCue = line?.Cue;
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
            BowlerText = bowler,
            StrikerPlayerId = strikerId,
            NonStrikerPlayerId = nonStrikerId,
            BowlerPlayerId = bowlerId,
            LastBallSequence = lastSeq,
            CommentaryEnglish = commentaryEn,
            CommentaryUrdu = commentaryUr,
            CommentaryCue = commentaryCue
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

    [HttpGet("/api/public/leagues/{leagueId:guid}/matches/{matchId:guid}/analytics")]
    public async Task<ActionResult<MatchAnalyticsReport>> GetPublicAnalytics(
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
        if (innings.Count == 0)
            return NotFound();

        var teamIds = new[] { match.TeamAId, match.TeamBId };
        var teams = await db.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct)
            .ConfigureAwait(false);
        var playerIds = innings.SelectMany(i => i.Deliveries)
            .SelectMany(d => new[] { d.BatterId, d.NonStrikerId })
            .Distinct()
            .ToList();
        var names = await db.Players.AsNoTracking()
            .Where(p => playerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
            .ConfigureAwait(false);
        var teamA = teams.GetValueOrDefault(match.TeamAId, "Team A");
        var teamB = teams.GetValueOrDefault(match.TeamBId, "Team B");
        var rows = innings.Select(inn => (
            inn.InningsNumber,
            inn.BattingTeamId == match.TeamAId ? teamA : teamB,
            (IReadOnlyList<JPL.Api.Models.Delivery>)inn.Deliveries.OrderBy(d => d.Sequence).ToList()
        )).ToList();
        var result = match.Status == MatchStatus.InProgress ? "LIVE"
            : match.Status == MatchStatus.Completed ? "Completed" : "";
        return Ok(MatchAnalyticsBuilder.Build(match.Title, teamA, teamB, result, Math.Max(match.OversLimit, 1), rows, names));
    }

    /// <summary>Neural MP3 for live commentary. Public watch page plays this so the voice is human, not robotic SAPI.</summary>
    [HttpGet("/api/public/tts")]
    public async Task<IActionResult> CommentaryAudio([FromQuery] string? lang, [FromQuery] string? q, CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=120";
        var mp3 = await _tts.SpeakMp3Async(q, lang, ct).ConfigureAwait(false);
        if (mp3 is null || mp3.Length == 0)
            return StatusCode(503);
        return File(mp3, "audio/mpeg");
    }
}
