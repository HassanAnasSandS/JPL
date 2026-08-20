using JPL.Api.Data;
using JPL.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JPL.Api.Services;

/// <summary>Two-team series: fixed N matches, same pattern, winner by match wins (or tied).</summary>
public static class SeriesEngine
{
    public const int MinMatches = 1;
    public const int MaxMatches = 7;
    public const int PointsWin = 2;
    public const int PointsTie = 1;

    public static void ValidateSetup(Guid teamAId, Guid teamBId, int matchCount)
    {
        if (teamAId == Guid.Empty || teamBId == Guid.Empty)
            throw new InvalidOperationException("Select both teams.");
        if (teamAId == teamBId)
            throw new InvalidOperationException("A series must be between two different teams.");
        if (matchCount is < MinMatches or > MaxMatches)
            throw new InvalidOperationException($"A series must have between {MinMatches} and {MaxMatches} matches.");
    }

    public static List<SeriesFixture> BuildFixtures(Series series)
    {
        var fixtures = new List<SeriesFixture>(series.MatchCount);
        for (var i = 1; i <= series.MatchCount; i++)
        {
            fixtures.Add(new SeriesFixture
            {
                Id = Guid.NewGuid(),
                SeriesId = series.Id,
                Sequence = i,
                Label = $"Match {i}",
                TeamAId = series.TeamAId,
                TeamBId = series.TeamBId
            });
        }

        return fixtures;
    }

    public static async Task<(List<SeriesStandingRow> Standings, bool Changed)> ResolveProgressAsync(
        ApiDbContext db,
        Series series,
        CancellationToken ct)
    {
        var changed = false;
        var fixtures = series.Fixtures.OrderBy(f => f.Sequence).ToList();
        var matchIds = fixtures.Where(f => f.MatchId is Guid).Select(f => f.MatchId!.Value).Distinct().ToList();
        var matches = matchIds.Count == 0
            ? new List<Match>()
            : await db.Matches.AsNoTracking()
                .Where(m => matchIds.Contains(m.Id) && !m.IsDeleted)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        var matchById = matches.ToDictionary(m => m.Id);

        var completedIds = matches
            .Where(m => m.Status == MatchStatus.Completed)
            .Select(m => m.Id)
            .ToList();
        var innsByMatch = completedIds.Count == 0
            ? new Dictionary<Guid, List<Innings>>()
            : (await db.Innings.AsNoTracking()
                    .Where(i => completedIds.Contains(i.MatchId))
                    .Include(i => i.Deliveries)
                    .ToListAsync(ct)
                    .ConfigureAwait(false))
                .GroupBy(i => i.MatchId)
                .ToDictionary(g => g.Key, g => g.ToList());

        var standings = BuildStandings(series, fixtures, matchById, innsByMatch);

        var anyMatch = fixtures.Any(f => f.MatchId is Guid);
        var allDecided = fixtures.Count > 0 && fixtures.All(f =>
            f.MatchId is Guid mid
            && matchById.TryGetValue(mid, out var m)
            && m.Status is MatchStatus.Completed or MatchStatus.Abandoned);

        if (allDecided)
        {
            var aWins = standings.FirstOrDefault(s => s.TeamId == series.TeamAId)?.Won ?? 0;
            var bWins = standings.FirstOrDefault(s => s.TeamId == series.TeamBId)?.Won ?? 0;
            Guid? winner = aWins == bWins ? null : aWins > bWins ? series.TeamAId : series.TeamBId;
            var tied = winner is null;

            if (series.Status != SeriesStatus.Completed
                || series.WinnerTeamId != winner
                || series.IsTied != tied)
            {
                series.Status = SeriesStatus.Completed;
                series.WinnerTeamId = winner;
                series.IsTied = tied;
                series.UpdatedAtUtc = DateTime.Now;
                series.Version++;
                changed = true;
            }
        }
        else if (anyMatch && series.Status == SeriesStatus.Scheduled)
        {
            series.Status = SeriesStatus.InProgress;
            series.UpdatedAtUtc = DateTime.Now;
            series.Version++;
            changed = true;
        }

        return (standings, changed);
    }

    public static List<SeriesStandingRow> BuildStandings(
        Series series,
        List<SeriesFixture> fixtures,
        Dictionary<Guid, Match> matchById,
        Dictionary<Guid, List<Innings>> innsByMatch)
    {
        var rows = new Dictionary<Guid, SeriesStandingRow>
        {
            [series.TeamAId] = new() { TeamId = series.TeamAId },
            [series.TeamBId] = new() { TeamId = series.TeamBId }
        };

        foreach (var f in fixtures)
        {
            if (f.MatchId is not Guid mid
                || !matchById.TryGetValue(mid, out var match)
                || match.Status != MatchStatus.Completed
                || !innsByMatch.TryGetValue(mid, out var inns))
                continue;

            var (winner, tied) = TournamentEngine.MatchResult(match, inns);
            var a = rows[series.TeamAId];
            var b = rows[series.TeamBId];
            a.Played++;
            b.Played++;

            var (aRuns, aBalls) = TeamBalls(inns, series.TeamAId);
            var (bRuns, bBalls) = TeamBalls(inns, series.TeamBId);
            a.RunsFor += aRuns;
            a.RunsAgainst += bRuns;
            a.BallsFor += aBalls;
            a.BallsAgainst += bBalls;
            b.RunsFor += bRuns;
            b.RunsAgainst += aRuns;
            b.BallsFor += bBalls;
            b.BallsAgainst += aBalls;

            if (tied)
            {
                a.Tied++;
                b.Tied++;
                a.Points += PointsTie;
                b.Points += PointsTie;
            }
            else if (winner == series.TeamAId)
            {
                a.Won++;
                b.Lost++;
                a.Points += PointsWin;
            }
            else if (winner == series.TeamBId)
            {
                b.Won++;
                a.Lost++;
                b.Points += PointsWin;
            }
        }

        return rows.Values
            .OrderByDescending(r => r.Points)
            .ThenByDescending(r => r.Won)
            .ThenByDescending(r => r.NetRunRate)
            .ThenByDescending(r => r.RunsFor)
            .ToList();
    }

    private static (int Runs, int Balls) TeamBalls(List<Innings> inns, Guid teamId)
    {
        var teamInns = inns.Where(i => i.BattingTeamId == teamId).ToList();
        var runs = teamInns.Sum(i => i.Deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns));
        var balls = teamInns.Sum(i => i.Deliveries.Count(d => d.IsLegalDelivery));
        return (runs, balls);
    }
}

public sealed class SeriesStandingRow
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Tied { get; set; }
    public int Points { get; set; }
    public int RunsFor { get; set; }
    public int RunsAgainst { get; set; }
    public int BallsFor { get; set; }
    public int BallsAgainst { get; set; }
    public double NetRunRate
    {
        get
        {
            var of = BallsFor <= 0 ? 0 : RunsFor / (BallsFor / 6.0);
            var oa = BallsAgainst <= 0 ? 0 : RunsAgainst / (BallsAgainst / 6.0);
            return of - oa;
        }
    }
}
