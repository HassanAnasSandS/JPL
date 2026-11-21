using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Contracts;
using JPL.Api.Data;
using JPL.Api.Models;

namespace JPL.Api.Controllers;

[ApiController]
[Route("api/live")]
public sealed class LiveController : ControllerBase
{
    private readonly ApiDbContext _db;

    public LiveController(ApiDbContext db) => _db = db;

    /// <summary>In-progress / setup matches in this league (standalone + tournament + series).</summary>
    [HttpGet("matches")]
    public async Task<ActionResult<IReadOnlyList<MatchSummaryDto>>> GetLiveMatches(CancellationToken ct)
    {
        try
        {
            var matches = await _db.Matches.AsNoTracking()
                .Where(m => !m.IsDeleted && (m.Status == MatchStatus.Setup || m.Status == MatchStatus.InProgress))
                .OrderByDescending(m => m.UpdatedAtUtc)
                .Select(m => new
                {
                    m.Id,
                    m.Title,
                    m.TeamAId,
                    m.TeamBId,
                    m.Status,
                    m.Format,
                    m.MatchDateUtc,
                    m.OversLimit,
                    m.Venue,
                    m.OversMode,
                    m.TournamentId,
                    m.SeriesId,
                    m.LiveStreamUrl
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (matches.Count == 0)
                return Ok(Array.Empty<MatchSummaryDto>());

            var matchIds = matches.Select(m => m.Id).ToList();
            var teamIds = matches.SelectMany(m => new[] { m.TeamAId, m.TeamBId }).Distinct().ToList();

            var teams = await _db.Teams.AsNoTracking()
                .Where(t => teamIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct)
                .ConfigureAwait(false);

            // Aggregate in SQL. Avoid loading every innings and delivery just to show
            // the current score in the match list.
            var scoreTotals = await (
                from innings in _db.Innings.AsNoTracking()
                join delivery in _db.Deliveries.AsNoTracking() on innings.Id equals delivery.InningsId
                where matchIds.Contains(innings.MatchId)
                group delivery by new { innings.MatchId, innings.BattingTeamId } into g
                select new
                {
                    g.Key.MatchId,
                    g.Key.BattingTeamId,
                    Runs = g.Sum(d => d.RunsOffBat + d.ExtraRuns),
                    Wickets = g.Sum(d => d.IsWicket ? 1 : 0)
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var scoresByMatchAndTeam = scoreTotals.ToDictionary(
                x => (x.MatchId, x.BattingTeamId),
                x => (x.Runs, x.Wickets));

            var list = matches.Select(m =>
            {
                var teamA = teams.GetValueOrDefault(m.TeamAId, "?");
                var teamB = teams.GetValueOrDefault(m.TeamBId, "?");
                var (aRuns, aWkts) = scoresByMatchAndTeam.GetValueOrDefault((m.Id, m.TeamAId));
                var (bRuns, bWkts) = scoresByMatchAndTeam.GetValueOrDefault((m.Id, m.TeamBId));
                var live = m.Status == MatchStatus.InProgress;
                return new MatchSummaryDto(
                    m.Id,
                    m.Title,
                    m.TeamAId,
                    m.TeamBId,
                    teamA,
                    teamB,
                    (byte)m.Status,
                    (byte)m.Format,
                    m.MatchDateUtc,
                    m.OversLimit,
                    m.Venue,
                    aRuns,
                    aWkts,
                    bRuns,
                    bWkts,
                    live ? "LIVE" : "Upcoming",
                    null,
                    null,
                    null,
                    (byte)m.OversMode,
                    m.TournamentId,
                    m.SeriesId,
                    m.LiveStreamUrl);
            }).ToList();

            return Ok(list);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The client can leave the match list or time out while the DB is querying.
            // This is a normal aborted request, not an API failure.
            return new EmptyResult();
        }
    }

    /// <summary>Setup / in-progress nets sessions in this league.</summary>
    [HttpGet("nets")]
    public async Task<ActionResult<IReadOnlyList<NetSessionSummaryDto>>> GetLiveNets(CancellationToken ct)
    {
        var sessions = await _db.NetSessions.AsNoTracking()
            .Where(s => !s.IsDeleted && (s.Status == NetSessionStatus.Setup || s.Status == NetSessionStatus.InProgress))
            .OrderByDescending(s => s.UpdatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (sessions.Count == 0)
            return Ok(Array.Empty<NetSessionSummaryDto>());

        var ids = sessions.Select(s => s.Id).ToList();
        var balls = await _db.NetDeliveries.AsNoTracking()
            .Where(d => ids.Contains(d.SessionId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var bySession = balls.GroupBy(d => d.SessionId).ToDictionary(g => g.Key, g => g.ToList());

        var list = sessions.Select(s =>
        {
            bySession.TryGetValue(s.Id, out var d);
            d ??= new List<NetDelivery>();
            var legal = d.Count(x => x.IsLegalDelivery);
            return new NetSessionSummaryDto(
                s.Id,
                s.Name,
                s.SessionDateUtc,
                (byte)s.Status,
                CountRoster(s.PlayerRosterJson),
                d.Sum(x => x.RunsOffBat + x.ExtraRuns),
                d.Count(x => x.IsWicket),
                legal / 6,
                legal % 6);
        }).ToList();

        return Ok(list);
    }

    private static int CountRoster(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return 0;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("players", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                return arr.GetArrayLength();
        }
        catch
        {
            /* ignore */
        }
        return 0;
    }
}
