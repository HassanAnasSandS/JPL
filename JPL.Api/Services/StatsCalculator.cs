using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Data;
using JPL.Api.Models;

namespace JPL.Api.Services;

public sealed class PlayerCareerDto
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    /// <summary>Completed nets sessions this player attended (roster or on a ball).</summary>
    public int Sessions { get; set; }
    public int Matches { get; set; }
    public int Won { get; set; }
    public int Loss { get; set; }
    public int Tied { get; set; }
    public int CaptainMatches { get; set; }
    public int CaptainWon { get; set; }
    public int CaptainLoss { get; set; }
    public int CaptainTied { get; set; }

    // Tournament (completed only) — same shape as Match
    public int Tournaments { get; set; }
    public int TournamentWon { get; set; }
    public int TournamentLoss { get; set; }
    public int TournamentTied { get; set; }
    public int CaptainTournaments { get; set; }
    public int CaptainTournamentWon { get; set; }
    public int CaptainTournamentLoss { get; set; }
    public int CaptainTournamentTied { get; set; }

    // Series (completed only) — same shape as Tournament; ties count as tied, not loss
    public int Series { get; set; }
    public int SeriesWon { get; set; }
    public int SeriesLoss { get; set; }
    public int SeriesTied { get; set; }
    public int CaptainSeries { get; set; }
    public int CaptainSeriesWon { get; set; }
    public int CaptainSeriesLoss { get; set; }
    public int CaptainSeriesTied { get; set; }

    public int InningsBatted { get; set; }
    public int BallsFaced { get; set; }
    public int Runs { get; set; }
    public int Dots { get; set; }
    public int Ones { get; set; }
    public int Twos { get; set; }
    public int Threes { get; set; }
    public int Fours { get; set; }
    public int Fives { get; set; }
    public int Sixes { get; set; }
    public int WidesFaced { get; set; }
    public int NoBallsFaced { get; set; }
    public int Dismissals { get; set; }
    public int BowledOut { get; set; }
    public int CaughtOut { get; set; }
    public int LbwOut { get; set; }
    public int RunOut { get; set; }
    public int StumpedOut { get; set; }
    public int HitWicketOut { get; set; }
    public int RetiredOut { get; set; }
    public int OtherOut { get; set; }
    public int OversBowledBalls { get; set; }
    public int RunsConceded { get; set; }
    public int Wickets { get; set; }
    public int DotBallsBowled { get; set; }
    public int WidesBowled { get; set; }
    public int NoBallsBowled { get; set; }
    public int ByesConceded { get; set; }
    public int LegByesConceded { get; set; }
    public int BowledWkts { get; set; }
    public int CaughtWkts { get; set; }
    public int LbwWkts { get; set; }
    public int StumpedWkts { get; set; }
    public int HitWicketWkts { get; set; }
    public int RunOutWkts { get; set; }
    public int Catches { get; set; }
    public int RunOutsEffected { get; set; }
    public int Stumpings { get; set; }

    // Attendance (venue bookings): Total = all venue rows; Present = listed; Absent = not listed
    public int AttendanceTotal { get; set; }
    public int AttendancePresent { get; set; }
    public int AttendanceAbsent { get; set; }
    public double AttendancePct => AttendanceTotal == 0
        ? 0
        : Math.Round(AttendancePresent * 100.0 / AttendanceTotal, 1);
    public string AttendancePctText => AttendanceTotal == 0 ? "-" : AttendancePct.ToString("0.#") + "%";

    // Batting milestones (per innings)
    public int HighestScore { get; set; }
    public bool HighestScoreNotOut { get; set; }
    /// <summary>Legal balls faced in the highest-score innings.</summary>
    public int HighestScoreBalls { get; set; }
    public int NotOuts { get; set; }
    public int Ducks { get; set; }
    public int GoldenDucks { get; set; }
    public int Fifties { get; set; }
    public int Hundreds { get; set; }

    // Bowling milestones
    public int Maidens { get; set; }
    public int BestBowlingWickets { get; set; }
    public int BestBowlingRuns { get; set; }
    public string BestBowlingText { get; set; } = "-";
    /// <summary>Bowler-credited wickets in three consecutive deliveries (same innings).</summary>
    public int HatTricks { get; set; }
    public string HighestScoreText
    {
        get
        {
            if (InningsBatted <= 0) return "-";
            var star = HighestScoreNotOut ? "*" : "";
            return $"{HighestScore}({HighestScoreBalls}){star}";
        }
    }

    public int Rank { get; set; }
}

public sealed class ConfrontationDto
{
    public Guid BatterId { get; set; }
    public string BatterName { get; set; } = string.Empty;
    public Guid BowlerId { get; set; }
    public string BowlerName { get; set; } = string.Empty;
    public int Balls { get; set; }
    public int Runs { get; set; }
    public int Dots { get; set; }
    public int Ones { get; set; }
    public int Twos { get; set; }
    public int Threes { get; set; }
    public int Fours { get; set; }
    public int Fives { get; set; }
    public int Sixes { get; set; }
    public int Outs { get; set; }
    public int ExtrasConceded { get; set; }
}

/// <summary>Captain vs opposing captain across completed matches (and series).</summary>
public sealed class CaptainVsDto
{
    public Guid CaptainId { get; set; }
    public string CaptainName { get; set; } = string.Empty;
    public Guid OpponentCaptainId { get; set; }
    public string OpponentCaptainName { get; set; } = string.Empty;
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Tied { get; set; }
    public int Series { get; set; }
    public int SeriesWon { get; set; }
    public int SeriesLoss { get; set; }
    public int SeriesTied { get; set; }
    public double WinPct => Played == 0
        ? 0
        : Math.Round(((Won + Tied * 0.5) / Played) * 100, 1);
    public string WinPctText => Played == 0 ? "-" : WinPct.ToString("0.#") + "%";
    public string RecordText => $"{Won}–{Lost}–{Tied}";
}

public sealed class RankingRowDto
{
    public int Rank { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string PrimaryLabel { get; set; } = string.Empty;
    public string PrimaryValue { get; set; } = string.Empty;
    public string SecondaryLabel { get; set; } = string.Empty;
    public string SecondaryValue { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public double SortScore { get; set; }
}

/// <summary>Team table for Statistics · Teams: matches, NRR, tournaments, series.</summary>
public sealed class TeamCareerDto
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Tied { get; set; }
    public int Points => Won * 2 + Tied;
    public int RunsFor { get; set; }
    public int RunsAgainst { get; set; }
    public int BallsFor { get; set; }
    public int BallsAgainst { get; set; }
    public int Tournaments { get; set; }
    public int TournamentWon { get; set; }
    public int TournamentLoss { get; set; }
    public int Series { get; set; }
    public int SeriesWon { get; set; }
    public int SeriesLoss { get; set; }
    public int SeriesTied { get; set; }
    public double WinPct => Played == 0
        ? 0
        : Math.Round(((Won + Tied * 0.5) / Played) * 100, 1);
    public string WinPctText => Played == 0 ? "-" : WinPct.ToString("0.#") + "%";
    public double Nrr
    {
        get
        {
            var of = BallsFor <= 0 ? 0 : RunsFor / (BallsFor / 6.0);
            var oa = BallsAgainst <= 0 ? 0 : RunsAgainst / (BallsAgainst / 6.0);
            return Math.Round(of - oa, 3);
        }
    }
    public string NrrText => Played == 0 ? "-" : Nrr.ToString("+0.000;-0.000;0.000");
    public int Rank { get; set; }
}

/// <summary>One opponent’s record against a selected team (Statistics · Teams 1 vs 1).</summary>
public sealed class TeamVsDto
{
    public Guid OpponentTeamId { get; set; }
    public string OpponentTeamName { get; set; } = string.Empty;
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Tied { get; set; }
    public int RunsFor { get; set; }
    public int RunsAgainst { get; set; }
    public int BallsFor { get; set; }
    public int BallsAgainst { get; set; }
    public int Series { get; set; }
    public int SeriesWon { get; set; }
    public int SeriesLoss { get; set; }
    public int SeriesTied { get; set; }
    public double WinPct => Played == 0
        ? 0
        : Math.Round(((Won + Tied * 0.5) / Played) * 100, 1);
    public string WinPctText => Played == 0 ? "-" : WinPct.ToString("0.#") + "%";
    public double Nrr
    {
        get
        {
            var of = BallsFor <= 0 ? 0 : RunsFor / (BallsFor / 6.0);
            var oa = BallsAgainst <= 0 ? 0 : RunsAgainst / (BallsAgainst / 6.0);
            return Math.Round(of - oa, 3);
        }
    }
    public string NrrText => Played == 0 ? "-" : Nrr.ToString("+0.000;-0.000;0.000");
    public string RecordText => $"{Won}–{Lost}–{Tied}";
}

public sealed class RankingsDto
{
    public IReadOnlyList<RankingRowDto> Batting { get; set; } = Array.Empty<RankingRowDto>();
    public IReadOnlyList<RankingRowDto> Bowling { get; set; } = Array.Empty<RankingRowDto>();
    public IReadOnlyList<RankingRowDto> AllRounder { get; set; } = Array.Empty<RankingRowDto>();
    public IReadOnlyList<RankingRowDto> Fielder { get; set; } = Array.Empty<RankingRowDto>();
    public IReadOnlyList<RankingRowDto> Captain { get; set; } = Array.Empty<RankingRowDto>();
    public IReadOnlyList<RankingRowDto> Team { get; set; } = Array.Empty<RankingRowDto>();
}

public static class StatsCalculator
{
    public static List<Guid>? NormalizeFormatIds(IReadOnlyList<Guid>? formatIds)
    {
        if (formatIds is null || formatIds.Count == 0)
            return null;
        var ids = formatIds.Where(id => id != Guid.Empty).Distinct().ToList();
        return ids.Count == 0 ? null : ids;
    }

    public static async Task<RankingsDto> BuildRankingsAsync(
        ApiDbContext db,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default)
    {
        var board = await BuildLeaderboardAsync(db, fromUtc, toExclusiveUtc, formatIds: formatIds, ct: ct)
            .ConfigureAwait(false);
        var dto = BuildRankingsFromBoard(board);
        dto.Team = await BuildTeamRankingsAsync(db, fromUtc, toExclusiveUtc, formatIds, ct).ConfigureAwait(false);
        return dto;
    }

    public static RankingsDto BuildRankingsFromBoard(IReadOnlyList<PlayerCareerDto> board)
    {
        static double BatSr(PlayerCareerDto p) => p.BallsFaced == 0 ? 0 : p.Runs * 100.0 / p.BallsFaced;
        static double BowlEcon(PlayerCareerDto p) => p.OversBowledBalls == 0 ? double.MaxValue : p.RunsConceded * 6.0 / p.OversBowledBalls;
        static double BowlAvg(PlayerCareerDto p) => p.Wickets == 0 ? double.MaxValue : (double)p.RunsConceded / p.Wickets;
        static double BatAvg(PlayerCareerDto p) => p.Dismissals == 0 ? p.Runs : (double)p.Runs / p.Dismissals;
        static string BatAvgText(PlayerCareerDto p)
            => p.InningsBatted <= 0 && p.BallsFaced == 0 && p.Runs == 0
                ? "-"
                : BatAvg(p).ToString("0.##");
        static string BowlAvgText(PlayerCareerDto p) => p.Wickets == 0 ? "-" : BowlAvg(p).ToString("0.##");
        static string BowlSrText(PlayerCareerDto p)
            => p.Wickets == 0 ? "-" : (p.OversBowledBalls / (double)p.Wickets).ToString("0.##");
        static string OversText(PlayerCareerDto p)
            => $"{p.OversBowledBalls / 6}.{p.OversBowledBalls % 6}";
        static string EconText(PlayerCareerDto p)
            => p.OversBowledBalls == 0 ? "-" : BowlEcon(p).ToString("0.##");
        static int FieldActions(PlayerCareerDto p) => p.Catches + p.RunOutsEffected + p.Stumpings;

        static double BatPts(PlayerCareerDto p)
        {
            if (p.BallsFaced <= 0 && p.Runs <= 0) return 0;
            double pts = p.Runs + p.Twos * 0.5 + p.Threes + p.Fours + p.Fives + p.Sixes * 2
                         + p.Fifties * 12 + p.Hundreds * 25 + p.NotOuts * 3 + p.HighestScore * 0.2;
            if (p.BallsFaced >= 6)
                pts += (BatSr(p) - 100) * p.BallsFaced / 200.0;
            if (p.InningsBatted >= 2)
                pts += (BatAvg(p) - 20) * Math.Min(p.InningsBatted, 15) * 0.35;
            pts += p.Dots * -0.4 + p.Ducks * -6 + p.GoldenDucks * -4
                   + p.RunOut * -3 + p.StumpedOut * -3 + p.HitWicketOut * -2
                   + p.BowledOut * -1 + p.LbwOut * -1 + p.CaughtOut * -0.5;
            return Math.Round(pts);
        }

        static double BowlPts(PlayerCareerDto p)
        {
            if (p.Wickets <= 0 && p.OversBowledBalls <= 0) return 0;
            var overs = p.OversBowledBalls / 6.0;
            double pts = p.Wickets * 25 + p.BowledWkts * 3 + p.LbwWkts * 2 + p.StumpedWkts * 2
                         + p.HitWicketWkts * 2 + p.Maidens * 8 + p.DotBallsBowled * 0.35
                         + p.HatTricks * 20 + p.BestBowlingWickets * 2;
            if (p.OversBowledBalls >= 6)
                pts += (8.0 - BowlEcon(p)) * overs;
            if (p.Wickets > 0 && p.OversBowledBalls >= 6)
                pts += (18.0 - p.OversBowledBalls / (double)p.Wickets) * p.Wickets * 0.3;
            else if (p.Wickets == 0 && p.OversBowledBalls >= 6)
                pts -= overs;
            pts += p.WidesBowled * -1.5 + p.NoBallsBowled * -2 + p.ByesConceded * -0.2 + p.LegByesConceded * -0.2;
            return Math.Round(pts);
        }

        static bool HasBatted(PlayerCareerDto p) => p.BallsFaced > 0 || p.Runs > 0;
        static bool HasBowled(PlayerCareerDto p) => p.OversBowledBalls > 0 || p.Wickets > 0;

        static double ArPts(PlayerCareerDto p)
            => !HasBatted(p) || !HasBowled(p) ? 0 : BatPts(p) + BowlPts(p);

        static double FieldPts(PlayerCareerDto p)
        {
            var actions = FieldActions(p);
            if (actions <= 0) return 0;
            double pts = p.Catches * 10 + p.RunOutsEffected * 12 + p.Stumpings * 15;
            if (p.Matches >= 4)
            {
                var expected = p.Matches * 0.25;
                if (actions < expected)
                    pts += (actions - expected) * 4;
            }
            return Math.Round(pts);
        }

        static double CapWinPct(PlayerCareerDto p)
            => p.CaptainMatches == 0 ? 0 : (p.CaptainWon + p.CaptainTied * 0.5) / p.CaptainMatches;
        static string CapWinPctText(PlayerCareerDto p)
            => p.CaptainMatches == 0 ? "-" : (CapWinPct(p) * 100).ToString("0.#") + "%";
        static double CapPts(PlayerCareerDto p)
        {
            if (p.CaptainMatches <= 0) return 0;
            return Math.Round(p.CaptainWon * 4.0 + p.CaptainTied - p.CaptainLoss * 2.0);
        }

        var batting = board
            .Where(p => p.BallsFaced > 0 || p.Runs > 0)
            .OrderByDescending(BatPts)
            .ThenByDescending(BatAvg)
            .ThenByDescending(p => p.Runs)
            .Select((p, i) => new RankingRowDto
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = BatPts(p).ToString("0"),
                SecondaryLabel = "Avg",
                SecondaryValue = BatAvgText(p),
                Detail = $"Runs {p.Runs} · Balls {p.BallsFaced} · Avg {BatAvgText(p)} · SR {BatSr(p):0.#}",
                SortScore = BatPts(p)
            })
            .ToList();

        var bowling = board
            .Where(p => p.Wickets > 0 || p.OversBowledBalls > 0)
            .OrderByDescending(BowlPts)
            .ThenBy(BowlEcon)
            .ThenByDescending(p => p.Wickets)
            .Select((p, i) => new RankingRowDto
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = BowlPts(p).ToString("0"),
                SecondaryLabel = "Econ",
                SecondaryValue = EconText(p),
                Detail = $"Wkts {p.Wickets} · Overs {OversText(p)} · Balls {p.OversBowledBalls} · Runs {p.RunsConceded} · Econ {EconText(p)} · SR {BowlSrText(p)} · Avg {BowlAvgText(p)}",
                SortScore = BowlPts(p)
            })
            .ToList();

        var allRounder = board
            .Where(p => HasBatted(p) && HasBowled(p))
            .OrderByDescending(ArPts)
            .ThenByDescending(BatPts)
            .ThenByDescending(BowlPts)
            .Select((p, i) => new RankingRowDto
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = ArPts(p).ToString("0"),
                SecondaryLabel = "B/Bowl",
                SecondaryValue = $"{BatPts(p):0}/{BowlPts(p):0}",
                Detail = $"Bat {BatPts(p):0} + Bowl {BowlPts(p):0} · Runs {p.Runs} · Wkts {p.Wickets} · Overs {OversText(p)}",
                SortScore = ArPts(p)
            })
            .ToList();

        var fielder = board
            .Where(p => FieldActions(p) > 0)
            .OrderByDescending(FieldPts)
            .ThenByDescending(p => p.Catches)
            .ThenByDescending(p => p.RunOutsEffected)
            .Select((p, i) => new RankingRowDto
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = FieldPts(p).ToString("0"),
                SecondaryLabel = "Ct",
                SecondaryValue = p.Catches.ToString(),
                Detail = $"Ct {p.Catches} · RO {p.RunOutsEffected} · St {p.Stumpings} · Total {FieldActions(p)}",
                SortScore = FieldPts(p)
            })
            .ToList();

        var captain = board
            .Where(p => p.CaptainMatches > 0)
            .OrderByDescending(CapPts)
            .ThenByDescending(CapWinPct)
            .ThenByDescending(p => p.CaptainWon)
            .ThenByDescending(p => p.CaptainMatches)
            .Select((p, i) => new RankingRowDto
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = CapPts(p).ToString("0"),
                SecondaryLabel = "Win%",
                SecondaryValue = CapWinPctText(p),
                Detail = $"Cap {p.CaptainMatches} · W {p.CaptainWon} · L {p.CaptainLoss} · T {p.CaptainTied} · Win% {CapWinPctText(p)}",
                SortScore = CapPts(p)
            })
            .ToList();

        return new RankingsDto
        {
            Batting = batting,
            Bowling = bowling,
            AllRounder = allRounder,
            Fielder = fielder,
            Captain = captain
        };
    }

    /// <summary>League table across all completed matches (standalone, tournament, series). Win 2 · Tie 1.</summary>
    public static async Task<IReadOnlyList<RankingRowDto>> BuildTeamRankingsAsync(
        ApiDbContext db,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default)
    {
        var matchQuery = db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.Completed && !m.IsDeleted);
        if (fromUtc is DateTime from)
            matchQuery = matchQuery.Where(m => m.MatchDateUtc >= from);
        if (toExclusiveUtc is DateTime to)
            matchQuery = matchQuery.Where(m => m.MatchDateUtc < to);
        var formatIdList = NormalizeFormatIds(formatIds);
        if (formatIdList is not null)
            matchQuery = matchQuery.Where(m => formatIdList.Contains(m.FormatId ?? Guid.Empty));

        var matches = await matchQuery.ToListAsync(ct).ConfigureAwait(false);
        if (matches.Count == 0)
            return Array.Empty<RankingRowDto>();

        var matchIds = matches.Select(m => m.Id).ToList();
        var innings = await db.Innings.AsNoTracking()
            .Where(i => matchIds.Contains(i.MatchId))
            .Include(i => i.Deliveries)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var innsByMatch = innings.GroupBy(i => i.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        var teamIds = matches.SelectMany(m => new[] { m.TeamAId, m.TeamBId }).Distinct().ToList();
        var names = await db.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct)
            .ConfigureAwait(false);

        var rows = new Dictionary<Guid, TeamAccum>();
        foreach (var match in matches)
        {
            innsByMatch.TryGetValue(match.Id, out var inns);
            inns ??= new List<Innings>();

            var a = GetOrAdd(rows, match.TeamAId, names);
            var b = GetOrAdd(rows, match.TeamBId, names);
            var (aRuns, aBalls) = TeamMatchTotals(inns, match.TeamAId);
            var (bRuns, bBalls) = TeamMatchTotals(inns, match.TeamBId);

            a.Played++;
            b.Played++;
            a.RunsFor += aRuns;
            a.RunsAgainst += bRuns;
            a.BallsFor += aBalls;
            a.BallsAgainst += bBalls;
            b.RunsFor += bRuns;
            b.RunsAgainst += aRuns;
            b.BallsFor += bBalls;
            b.BallsAgainst += aBalls;

            var (winner, tied) = ResolveResult(match, inns);
            if (tied)
            {
                a.Tied++;
                b.Tied++;
                a.Points += 1;
                b.Points += 1;
            }
            else if (winner == match.TeamAId)
            {
                a.Won++;
                b.Lost++;
                a.Points += 2;
            }
            else if (winner == match.TeamBId)
            {
                b.Won++;
                a.Lost++;
                b.Points += 2;
            }
        }

        static double Nrr(TeamAccum t)
        {
            var of = t.BallsFor <= 0 ? 0 : t.RunsFor / (t.BallsFor / 6.0);
            var oa = t.BallsAgainst <= 0 ? 0 : t.RunsAgainst / (t.BallsAgainst / 6.0);
            return Math.Round(of - oa, 3);
        }

        static string NrrText(TeamAccum t) => Nrr(t).ToString("+0.000;-0.000;0.000");
        static string WinPctText(TeamAccum t)
            => t.Played == 0 ? "-" : (((t.Won + t.Tied * 0.5) / t.Played) * 100).ToString("0.#") + "%";

        return rows.Values
            .Where(t => t.Played > 0)
            .Select(t =>
            {
                var nrr = Nrr(t);
                var pts = t.Won * 2 + t.Tied - t.Lost + (int)Math.Round(nrr * 10);
                return (t, nrr, pts);
            })
            .OrderByDescending(x => x.pts)
            .ThenByDescending(x => x.nrr)
            .ThenByDescending(x => x.t.Won)
            .ThenByDescending(x => x.t.RunsFor)
            .ThenBy(x => x.t.Name, StringComparer.OrdinalIgnoreCase)
            .Select((x, i) => new RankingRowDto
            {
                Rank = i + 1,
                PlayerId = x.t.TeamId,
                PlayerName = x.t.Name,
                PrimaryLabel = "Pts",
                PrimaryValue = x.pts.ToString(),
                SecondaryLabel = "NRR",
                SecondaryValue = NrrText(x.t),
                Detail = $"P {x.t.Played} · W {x.t.Won} · L {x.t.Lost} · T {x.t.Tied} · Win% {WinPctText(x.t)} · NRR {NrrText(x.t)}",
                SortScore = x.pts
            })
            .ToList();
    }

    /// <summary>Statistics · Teams leaderboard (completed matches, tournaments, series).</summary>
    public static async Task<IReadOnlyList<TeamCareerDto>> BuildTeamLeaderboardAsync(
        ApiDbContext db,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default)
    {
        string? venueName = null;
        if (venueId is Guid vid)
        {
            venueName = await db.Venues.AsNoTracking()
                .Where(v => v.Id == vid && !v.IsDeleted)
                .Select(v => v.Name)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        var matchQuery = db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.Completed && !m.IsDeleted);
        if (fromUtc is DateTime from)
            matchQuery = matchQuery.Where(m => m.MatchDateUtc >= from);
        if (toExclusiveUtc is DateTime to)
            matchQuery = matchQuery.Where(m => m.MatchDateUtc < to);
        if (!string.IsNullOrWhiteSpace(venueName))
        {
            var vn = venueName.Trim();
            matchQuery = matchQuery.Where(m =>
                m.Venue != null && m.Venue.ToLower() == vn.ToLower());
        }
        var formatIdList = NormalizeFormatIds(formatIds);
        if (formatIdList is not null)
            matchQuery = matchQuery.Where(m => formatIdList.Contains(m.FormatId ?? Guid.Empty));

        var matches = await matchQuery.ToListAsync(ct).ConfigureAwait(false);
        var matchIds = matches.Select(m => m.Id).ToList();
        var innings = matchIds.Count == 0
            ? new List<Innings>()
            : await db.Innings.AsNoTracking()
                .Where(i => matchIds.Contains(i.MatchId))
                .Include(i => i.Deliveries)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        var innsByMatch = innings.GroupBy(i => i.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        IQueryable<Tournament> tournamentQuery = db.Tournaments.AsNoTracking()
            .Where(t => !t.IsDeleted && t.Status == TournamentStatus.Completed)
            .Include(t => t.Teams);
        if (fromUtc is DateTime tourFrom)
            tournamentQuery = tournamentQuery.Where(t => t.UpdatedAtUtc >= tourFrom);
        if (toExclusiveUtc is DateTime tourTo)
            tournamentQuery = tournamentQuery.Where(t => t.UpdatedAtUtc < tourTo);
        if (venueId is Guid tourVenueId)
            tournamentQuery = tournamentQuery.Where(t => t.VenueId == tourVenueId);
        if (formatIdList is not null)
            tournamentQuery = tournamentQuery.Where(t => formatIdList.Contains(t.FormatId ?? Guid.Empty));
        var tournaments = await tournamentQuery.ToListAsync(ct).ConfigureAwait(false);

        IQueryable<Series> seriesQuery = db.Series.AsNoTracking()
            .Where(s => !s.IsDeleted && s.Status == SeriesStatus.Completed);
        if (fromUtc is DateTime seriesFrom)
            seriesQuery = seriesQuery.Where(s => s.UpdatedAtUtc >= seriesFrom);
        if (toExclusiveUtc is DateTime seriesTo)
            seriesQuery = seriesQuery.Where(s => s.UpdatedAtUtc < seriesTo);
        if (venueId is Guid seriesVenueId)
            seriesQuery = seriesQuery.Where(s => s.VenueId == seriesVenueId);
        if (formatIdList is not null)
            seriesQuery = seriesQuery.Where(s => formatIdList.Contains(s.FormatId ?? Guid.Empty));
        var seriesList = await seriesQuery.ToListAsync(ct).ConfigureAwait(false);

        var teamIds = matches.SelectMany(m => new[] { m.TeamAId, m.TeamBId })
            .Concat(tournaments.SelectMany(t => t.Teams.Select(tt => tt.TeamId)))
            .Concat(seriesList.SelectMany(s => new[] { s.TeamAId, s.TeamBId }))
            .Distinct()
            .ToList();
        if (teamIds.Count == 0)
            return Array.Empty<TeamCareerDto>();

        var names = await db.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct)
            .ConfigureAwait(false);

        var rows = new Dictionary<Guid, TeamCareerDto>();
        TeamCareerDto Row(Guid teamId)
        {
            if (rows.TryGetValue(teamId, out var existing))
                return existing;
            var created = new TeamCareerDto
            {
                TeamId = teamId,
                TeamName = names.GetValueOrDefault(teamId, "?")
            };
            rows[teamId] = created;
            return created;
        }

        foreach (var match in matches)
        {
            innsByMatch.TryGetValue(match.Id, out var inns);
            inns ??= new List<Innings>();

            var a = Row(match.TeamAId);
            var b = Row(match.TeamBId);
            var (aRuns, aBalls) = TeamMatchTotals(inns, match.TeamAId);
            var (bRuns, bBalls) = TeamMatchTotals(inns, match.TeamBId);

            a.Played++;
            b.Played++;
            a.RunsFor += aRuns;
            a.RunsAgainst += bRuns;
            a.BallsFor += aBalls;
            a.BallsAgainst += bBalls;
            b.RunsFor += bRuns;
            b.RunsAgainst += aRuns;
            b.BallsFor += bBalls;
            b.BallsAgainst += aBalls;

            var (winner, tied) = ResolveResult(match, inns);
            if (tied)
            {
                a.Tied++;
                b.Tied++;
            }
            else if (winner == match.TeamAId)
            {
                a.Won++;
                b.Lost++;
            }
            else if (winner == match.TeamBId)
            {
                b.Won++;
                a.Lost++;
            }
        }

        foreach (var tour in tournaments)
        {
            foreach (var tt in tour.Teams)
            {
                var row = Row(tt.TeamId);
                row.Tournaments++;
                if (tour.WinnerTeamId == tt.TeamId)
                    row.TournamentWon++;
                else
                    row.TournamentLoss++;
            }
        }

        foreach (var series in seriesList)
        {
            foreach (var teamId in new[] { series.TeamAId, series.TeamBId })
            {
                var row = Row(teamId);
                row.Series++;
                if (series.IsTied)
                    row.SeriesTied++;
                else if (series.WinnerTeamId == teamId)
                    row.SeriesWon++;
                else
                    row.SeriesLoss++;
            }
        }

        return rows.Values
            .Where(t => t.Played > 0 || t.Tournaments > 0 || t.Series > 0)
            .OrderByDescending(t => t.Points)
            .ThenByDescending(t => t.Nrr)
            .ThenByDescending(t => t.Won)
            .ThenBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Head-to-head records for one team vs every opponent it has faced.</summary>
    public static async Task<IReadOnlyList<TeamVsDto>> BuildTeamVsAsync(
        ApiDbContext db,
        Guid teamId,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default)
    {
        string? venueName = null;
        if (venueId is Guid vid)
        {
            venueName = await db.Venues.AsNoTracking()
                .Where(v => v.Id == vid && !v.IsDeleted)
                .Select(v => v.Name)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        var matchQuery = db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.Completed && !m.IsDeleted
                        && (m.TeamAId == teamId || m.TeamBId == teamId));
        if (fromUtc is DateTime from)
            matchQuery = matchQuery.Where(m => m.MatchDateUtc >= from);
        if (toExclusiveUtc is DateTime to)
            matchQuery = matchQuery.Where(m => m.MatchDateUtc < to);
        if (!string.IsNullOrWhiteSpace(venueName))
        {
            var vn = venueName.Trim();
            matchQuery = matchQuery.Where(m =>
                m.Venue != null && m.Venue.ToLower() == vn.ToLower());
        }
        var formatIdList = NormalizeFormatIds(formatIds);
        if (formatIdList is not null)
            matchQuery = matchQuery.Where(m => formatIdList.Contains(m.FormatId ?? Guid.Empty));

        var matches = await matchQuery.ToListAsync(ct).ConfigureAwait(false);
        var matchIds = matches.Select(m => m.Id).ToList();
        var innings = matchIds.Count == 0
            ? new List<Innings>()
            : await db.Innings.AsNoTracking()
                .Where(i => matchIds.Contains(i.MatchId))
                .Include(i => i.Deliveries)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        var innsByMatch = innings.GroupBy(i => i.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        IQueryable<Series> seriesQuery = db.Series.AsNoTracking()
            .Where(s => !s.IsDeleted && s.Status == SeriesStatus.Completed
                        && (s.TeamAId == teamId || s.TeamBId == teamId));
        if (fromUtc is DateTime seriesFrom)
            seriesQuery = seriesQuery.Where(s => s.UpdatedAtUtc >= seriesFrom);
        if (toExclusiveUtc is DateTime seriesTo)
            seriesQuery = seriesQuery.Where(s => s.UpdatedAtUtc < seriesTo);
        if (venueId is Guid seriesVenueId)
            seriesQuery = seriesQuery.Where(s => s.VenueId == seriesVenueId);
        if (formatIdList is not null)
            seriesQuery = seriesQuery.Where(s => formatIdList.Contains(s.FormatId ?? Guid.Empty));
        var seriesList = await seriesQuery.ToListAsync(ct).ConfigureAwait(false);

        var opponentIds = matches
            .Select(m => m.TeamAId == teamId ? m.TeamBId : m.TeamAId)
            .Concat(seriesList.Select(s => s.TeamAId == teamId ? s.TeamBId : s.TeamAId))
            .Distinct()
            .ToList();
        if (opponentIds.Count == 0)
            return Array.Empty<TeamVsDto>();

        var names = await db.Teams.AsNoTracking()
            .Where(t => opponentIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct)
            .ConfigureAwait(false);

        var rows = new Dictionary<Guid, TeamVsDto>();
        TeamVsDto Row(Guid opponentId)
        {
            if (rows.TryGetValue(opponentId, out var existing))
                return existing;
            var created = new TeamVsDto
            {
                OpponentTeamId = opponentId,
                OpponentTeamName = names.GetValueOrDefault(opponentId, "?")
            };
            rows[opponentId] = created;
            return created;
        }

        foreach (var match in matches)
        {
            var opponentId = match.TeamAId == teamId ? match.TeamBId : match.TeamAId;
            var row = Row(opponentId);
            innsByMatch.TryGetValue(match.Id, out var inns);
            inns ??= new List<Innings>();
            var (usRuns, usBalls) = TeamMatchTotals(inns, teamId);
            var (themRuns, themBalls) = TeamMatchTotals(inns, opponentId);
            row.Played++;
            row.RunsFor += usRuns;
            row.RunsAgainst += themRuns;
            row.BallsFor += usBalls;
            row.BallsAgainst += themBalls;
            var (winner, tied) = ResolveResult(match, inns);
            if (tied) row.Tied++;
            else if (winner == teamId) row.Won++;
            else row.Lost++;
        }

        foreach (var series in seriesList)
        {
            var opponentId = series.TeamAId == teamId ? series.TeamBId : series.TeamAId;
            var row = Row(opponentId);
            row.Series++;
            if (series.IsTied)
                row.SeriesTied++;
            else if (series.WinnerTeamId == teamId)
                row.SeriesWon++;
            else
                row.SeriesLoss++;
        }

        return rows.Values
            .Where(r => r.Played > 0 || r.Series > 0)
            .OrderByDescending(r => r.Played)
            .ThenByDescending(r => r.Won)
            .ThenBy(r => r.OpponentTeamName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class TeamAccum
    {
        public Guid TeamId { get; init; }
        public string Name { get; init; } = string.Empty;
        public int Played { get; set; }
        public int Won { get; set; }
        public int Lost { get; set; }
        public int Tied { get; set; }
        public int Points { get; set; }
        public int RunsFor { get; set; }
        public int RunsAgainst { get; set; }
        public int BallsFor { get; set; }
        public int BallsAgainst { get; set; }
    }

    private static TeamAccum GetOrAdd(Dictionary<Guid, TeamAccum> rows, Guid teamId, Dictionary<Guid, string> names)
    {
        if (rows.TryGetValue(teamId, out var existing))
            return existing;
        var created = new TeamAccum
        {
            TeamId = teamId,
            Name = names.GetValueOrDefault(teamId, "?")
        };
        rows[teamId] = created;
        return created;
    }

    private static (int Runs, int Balls) TeamMatchTotals(List<Innings> inns, Guid teamId)
    {
        var teamInns = inns.Where(i => i.BattingTeamId == teamId).ToList();
        var runs = teamInns.Sum(i => i.Deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns));
        var balls = teamInns.Sum(i => i.Deliveries.Count(d => d.IsLegalDelivery));
        return (runs, balls);
    }

    public static async Task<IReadOnlyList<PlayerCareerDto>> BuildLeaderboardAsync(
        ApiDbContext db,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default)
    {
        var players = await db.Players.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.FullName)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string? venueName = null;
        if (venueId is Guid vid)
        {
            venueName = await db.Venues.AsNoTracking()
                .Where(v => v.Id == vid && !v.IsDeleted)
                .Select(v => v.Name)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        var matchQuery = db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.Completed && !m.IsDeleted);
        if (fromUtc is DateTime from)
            matchQuery = matchQuery.Where(m => m.MatchDateUtc >= from);
        if (toExclusiveUtc is DateTime to)
            matchQuery = matchQuery.Where(m => m.MatchDateUtc < to);
        if (!string.IsNullOrWhiteSpace(venueName))
        {
            var vn = venueName.Trim();
            matchQuery = matchQuery.Where(m =>
                m.Venue != null && m.Venue.ToLower() == vn.ToLower());
        }
        var formatIdList = NormalizeFormatIds(formatIds);
        if (formatIdList is not null)
            matchQuery = matchQuery.Where(m => formatIdList.Contains(m.FormatId ?? Guid.Empty));

        var matches = await matchQuery
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var matchIds = matches.Select(m => m.Id).ToList();
        var allDeliveries = await db.Deliveries.AsNoTracking()
            .Where(d => matchIds.Contains(d.MatchId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var innings = await db.Innings.AsNoTracking()
            .Where(i => matchIds.Contains(i.MatchId))
            .Include(i => i.Deliveries)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var teams = await db.Teams.AsNoTracking()
            .Include(t => t.TeamPlayers)
            .ToDictionaryAsync(t => t.Id, t => t, ct)
            .ConfigureAwait(false);

        IQueryable<Tournament> tournamentQuery = db.Tournaments.AsNoTracking()
            .Where(t => !t.IsDeleted && t.Status == TournamentStatus.Completed)
            .Include(t => t.Teams);
        if (fromUtc is DateTime tourFrom)
            tournamentQuery = tournamentQuery.Where(t => t.UpdatedAtUtc >= tourFrom);
        if (toExclusiveUtc is DateTime tourTo)
            tournamentQuery = tournamentQuery.Where(t => t.UpdatedAtUtc < tourTo);
        if (venueId is Guid tourVenueId)
            tournamentQuery = tournamentQuery.Where(t => t.VenueId == tourVenueId);
        if (formatIdList is not null)
            tournamentQuery = tournamentQuery.Where(t => formatIdList.Contains(t.FormatId ?? Guid.Empty));
        var tournaments = await tournamentQuery.ToListAsync(ct).ConfigureAwait(false);

        IQueryable<Series> seriesQuery = db.Series.AsNoTracking()
            .Where(s => !s.IsDeleted && s.Status == SeriesStatus.Completed);
        if (fromUtc is DateTime seriesFrom)
            seriesQuery = seriesQuery.Where(s => s.UpdatedAtUtc >= seriesFrom);
        if (toExclusiveUtc is DateTime seriesTo)
            seriesQuery = seriesQuery.Where(s => s.UpdatedAtUtc < seriesTo);
        if (venueId is Guid seriesVenueId)
            seriesQuery = seriesQuery.Where(s => s.VenueId == seriesVenueId);
        if (formatIdList is not null)
            seriesQuery = seriesQuery.Where(s => formatIdList.Contains(s.FormatId ?? Guid.Empty));
        var seriesList = await seriesQuery.ToListAsync(ct).ConfigureAwait(false);

        var inningsByMatch = innings.GroupBy(i => i.MatchId).ToDictionary(g => g.Key, g => g.ToList());
        var batByPlayer = allDeliveries.GroupBy(d => d.BatterId).ToDictionary(g => g.Key, g => g.ToList());
        var bowlByPlayer = allDeliveries.GroupBy(d => d.BowlerId).ToDictionary(g => g.Key, g => g.ToList());
        // Includes non-striker run-outs / retired (DismissedPlayerId may differ from BatterId).
        var outsByPlayer = allDeliveries
            .Where(d => d.IsWicket && d.DismissedPlayerId is Guid)
            .GroupBy(d => d.DismissedPlayerId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var inningsById = innings.ToDictionary(i => i.Id);

        var venueQuery = db.VenueBookings.AsNoTracking()
            .Where(b => !b.IsDeleted);
        if (fromUtc is DateTime venueFrom)
            venueQuery = venueQuery.Where(b => b.BookingDateUtc >= venueFrom);
        if (toExclusiveUtc is DateTime venueTo)
            venueQuery = venueQuery.Where(b => b.BookingDateUtc < venueTo);
        if (venueId is Guid filterVenueId)
            venueQuery = venueQuery.Where(b => b.VenueId == filterVenueId);

        var venueBookings = await venueQuery
            .Include(b => b.Participants)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        // Total = sum of all venue booking rows; Present = listed as participant; Absent = Total − Present.
        var attendanceTotal = venueBookings.Count;
        var presentByPlayer = venueBookings
            .SelectMany(b => b.Participants.Select(p => new { p.PlayerId, BookingId = b.Id }))
            .GroupBy(x => x.PlayerId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.BookingId).Distinct().Count());

        var list = new List<PlayerCareerDto>();
        foreach (var player in players)
        {
            batByPlayer.TryGetValue(player.Id, out var asBatter);
            bowlByPlayer.TryGetValue(player.Id, out var asBowler);
            outsByPlayer.TryGetValue(player.Id, out var outs);
            asBatter ??= new List<Delivery>();
            asBowler ??= new List<Delivery>();
            outs ??= new List<Delivery>();

            var summary = new PlayerCareerDto { PlayerId = player.Id, PlayerName = player.FullName };
            ApplyBatting(summary, asBatter);
            ApplyDismissals(summary, outs);
            ApplyBowling(summary, asBowler);
            ApplyBattingMilestones(summary, asBatter, outs);
            ApplyBowlingMilestones(summary, asBowler);
            ApplyFielding(summary, allDeliveries, player.Id);
            ApplyMatchResults(summary, player.Id, matches, inningsByMatch, inningsById, asBatter, asBowler, teams);
            ApplyTournamentResults(summary, player.Id, tournaments, teams);
            ApplySeriesResults(summary, player.Id, seriesList, teams);
            ApplyAttendance(summary, attendanceTotal, presentByPlayer.GetValueOrDefault(player.Id));

            if (summary.Matches == 0 && summary.Runs == 0 && summary.Wickets == 0
                && asBatter.Count == 0 && asBowler.Count == 0
                && summary.AttendancePresent == 0
                && summary.Tournaments == 0
                && summary.Series == 0)
                continue;

            list.Add(summary);
        }

        return list.OrderByDescending(x => x.Runs).ThenByDescending(x => x.Wickets).ThenBy(x => x.PlayerName).ToList();
    }

    public static async Task<IReadOnlyList<PlayerCareerDto>> BuildNetsLeaderboardAsync(
        ApiDbContext db,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        CancellationToken ct = default)
    {
        var players = await db.Players.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.FullName)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var sessionQuery = db.NetSessions.AsNoTracking()
            .Where(s => s.Status == NetSessionStatus.Completed && !s.IsDeleted);
        if (fromUtc is DateTime from)
            sessionQuery = sessionQuery.Where(s => s.SessionDateUtc >= from);
        if (toExclusiveUtc is DateTime to)
            sessionQuery = sessionQuery.Where(s => s.SessionDateUtc < to);

        var sessions = await sessionQuery
            .Select(s => new { s.Id, s.PlayerRosterJson })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (sessions.Count == 0)
            return Array.Empty<PlayerCareerDto>();

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var netBalls = await db.NetDeliveries.AsNoTracking()
            .Where(d => sessionIds.Contains(d.SessionId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var sessionsByPlayer = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var session in sessions)
        {
            foreach (var playerId in RosterPlayerIds(session.PlayerRosterJson))
                MarkSession(sessionsByPlayer, playerId, session.Id);
        }
        foreach (var d in netBalls)
        {
            MarkSession(sessionsByPlayer, d.BatterId, d.SessionId);
            MarkSession(sessionsByPlayer, d.BowlerId, d.SessionId);
            MarkSession(sessionsByPlayer, d.NonStrikerId, d.SessionId);
            if (d.FielderId is Guid fielderId)
                MarkSession(sessionsByPlayer, fielderId, d.SessionId);
            if (d.DismissedPlayerId is Guid dismissedId)
                MarkSession(sessionsByPlayer, dismissedId, d.SessionId);
        }

        var battingBalls = new List<Delivery>();
        var bowlingBalls = new List<Delivery>();
        foreach (var group in netBalls.GroupBy(d => d.SessionId))
        {
            var ordered = group.OrderBy(d => d.Sequence).ToList();
            bowlingBalls.AddRange(ordered.Select(d => FromNet(d, group.Key)));
            battingBalls.AddRange(CloneWithBattingSpells(ordered));
        }

        var batByPlayer = battingBalls.GroupBy(d => d.BatterId).ToDictionary(g => g.Key, g => g.ToList());
        var bowlByPlayer = bowlingBalls.GroupBy(d => d.BowlerId).ToDictionary(g => g.Key, g => g.ToList());
        var outsByPlayer = battingBalls
            .Where(d => d.IsWicket && d.DismissedPlayerId is Guid)
            .GroupBy(d => d.DismissedPlayerId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var list = new List<PlayerCareerDto>();
        foreach (var player in players)
        {
            batByPlayer.TryGetValue(player.Id, out var asBatter);
            bowlByPlayer.TryGetValue(player.Id, out var asBowler);
            outsByPlayer.TryGetValue(player.Id, out var outs);
            asBatter ??= new List<Delivery>();
            asBowler ??= new List<Delivery>();
            outs ??= new List<Delivery>();

            var summary = new PlayerCareerDto { PlayerId = player.Id, PlayerName = player.FullName };
            summary.Sessions = sessionsByPlayer.GetValueOrDefault(player.Id)?.Count ?? 0;
            ApplyBatting(summary, asBatter);
            ApplyDismissals(summary, outs);
            ApplyBowling(summary, asBowler);
            ApplyBattingMilestones(summary, asBatter, outs);
            ApplyBowlingMilestones(summary, asBowler);
            ApplyFielding(summary, bowlingBalls, player.Id);

            if (summary.Sessions == 0 && summary.Runs == 0 && summary.Wickets == 0
                && asBatter.Count == 0 && asBowler.Count == 0
                && summary.Catches == 0 && summary.RunOutsEffected == 0 && summary.Stumpings == 0)
                continue;

            list.Add(summary);
        }

        return list.OrderByDescending(x => x.Runs).ThenByDescending(x => x.Wickets).ThenBy(x => x.PlayerName).ToList();
    }

    private static void MarkSession(Dictionary<Guid, HashSet<Guid>> map, Guid playerId, Guid sessionId)
    {
        if (playerId == Guid.Empty)
            return;
        if (!map.TryGetValue(playerId, out var set))
        {
            set = new HashSet<Guid>();
            map[playerId] = set;
        }
        set.Add(sessionId);
    }

    private static IEnumerable<Guid> RosterPlayerIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<Guid>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("players", out var players)
                && !doc.RootElement.TryGetProperty("Players", out players))
                return Array.Empty<Guid>();

            var ids = new List<Guid>();
            foreach (var p in players.EnumerateArray())
            {
                if (TryReadGuid(p, "id", out var id)
                    || TryReadGuid(p, "Id", out id)
                    || TryReadGuid(p, "playerId", out id))
                    ids.Add(id);
            }
            return ids;
        }
        catch
        {
            return Array.Empty<Guid>();
        }
    }

    private static bool TryReadGuid(JsonElement el, string name, out Guid id)
    {
        id = Guid.Empty;
        if (!el.TryGetProperty(name, out var prop))
            return false;
        if (prop.ValueKind == JsonValueKind.String && Guid.TryParse(prop.GetString(), out id) && id != Guid.Empty)
            return true;
        return false;
    }

    private static List<Delivery> CloneWithBattingSpells(IReadOnlyList<NetDelivery> ordered)
    {
        var result = new List<Delivery>(ordered.Count);
        Guid? currentBatter = null;
        var spellId = Guid.NewGuid();
        foreach (var d in ordered)
        {
            if (currentBatter is null || currentBatter != d.BatterId)
            {
                spellId = Guid.NewGuid();
                currentBatter = d.BatterId;
            }

            result.Add(FromNet(d, spellId));
            if (d.IsWicket && (d.DismissedPlayerId ?? d.BatterId) == d.BatterId)
                currentBatter = null;
        }

        return result;
    }

    private static Delivery FromNet(NetDelivery d, Guid inningsId) => new()
    {
        Id = d.Id,
        MatchId = d.SessionId,
        InningsId = inningsId,
        Sequence = d.Sequence,
        OverNumber = d.OverNumber,
        BallInOver = d.BallInOver,
        BatterId = d.BatterId,
        NonStrikerId = d.NonStrikerId,
        BowlerId = d.BowlerId,
        RunsOffBat = d.RunsOffBat,
        ExtraRuns = d.ExtraRuns,
        ExtraType = d.ExtraType,
        IsWicket = d.IsWicket,
        WicketType = d.WicketType,
        DismissedPlayerId = d.DismissedPlayerId,
        FielderId = d.FielderId,
        IsLegalDelivery = d.IsLegalDelivery,
        CreatedAtUtc = d.CreatedAtUtc,
        Version = d.Version
    };

    public static async Task<IReadOnlyList<ConfrontationDto>> BuildConfrontationsAsync(
        ApiDbContext db,
        Guid playerId,
        bool asBatterPerspective,
        CancellationToken ct)
    {
        var deliveries = asBatterPerspective
            ? await db.Deliveries.AsNoTracking()
                .Where(d => d.BatterId == playerId || d.DismissedPlayerId == playerId)
                .ToListAsync(ct).ConfigureAwait(false)
            : await db.Deliveries.AsNoTracking().Where(d => d.BowlerId == playerId).ToListAsync(ct).ConfigureAwait(false);

        if (deliveries.Count == 0)
            return Array.Empty<ConfrontationDto>();

        var names = await db.Players.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
            .ConfigureAwait(false);

        var groups = asBatterPerspective
            ? deliveries.GroupBy(d => d.BowlerId)
            : deliveries.GroupBy(d => d.BatterId);

        var stats = new List<ConfrontationDto>();
        foreach (var g in groups)
        {
            var first = g.First();
            var row = new ConfrontationDto
            {
                BatterId = asBatterPerspective ? playerId : first.BatterId,
                BatterName = names.GetValueOrDefault(asBatterPerspective ? playerId : first.BatterId, "?"),
                BowlerId = first.BowlerId,
                BowlerName = names.GetValueOrDefault(first.BowlerId, "?")
            };
            foreach (var d in g)
            {
                if (asBatterPerspective && d.BatterId != playerId)
                {
                    // Non-striker dismissal only — count the out, not balls/runs on that delivery.
                    if (d.IsWicket && d.DismissedPlayerId == playerId)
                        row.Outs++;
                    continue;
                }
                ApplyConfrontation(row, d);
            }
            stats.Add(row);
        }

        return asBatterPerspective
            ? stats.OrderByDescending(s => s.Runs).ThenByDescending(s => s.Balls).ToList()
            : stats.OrderByDescending(s => s.Outs).ThenBy(s => s.Runs).ToList();
    }

    /// <summary>1 vs 1 as captain: this player vs every opposing captain faced.</summary>
    public static async Task<IReadOnlyList<CaptainVsDto>> BuildCaptainVsAsync(
        ApiDbContext db,
        Guid playerId,
        CancellationToken ct = default)
    {
        var teams = await db.Teams.AsNoTracking()
            .Include(t => t.TeamPlayers)
            .ToDictionaryAsync(t => t.Id, t => t, ct)
            .ConfigureAwait(false);
        var captainTeamIds = teams.Values
            .Where(t => t.CaptainPlayerId == playerId)
            .Select(t => t.Id)
            .ToList();

        var matches = await db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.Completed && !m.IsDeleted
                        && (m.TeamACaptainId == playerId
                            || m.TeamBCaptainId == playerId
                            || captainTeamIds.Contains(m.TeamAId)
                            || captainTeamIds.Contains(m.TeamBId)))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var matchIds = matches.Select(m => m.Id).ToList();
        var innings = matchIds.Count == 0
            ? new List<Innings>()
            : await db.Innings.AsNoTracking()
                .Where(i => matchIds.Contains(i.MatchId))
                .Include(i => i.Deliveries)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        var innsByMatch = innings.GroupBy(i => i.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        var seriesList = await db.Series.AsNoTracking()
            .Where(s => !s.IsDeleted && s.Status == SeriesStatus.Completed
                        && (captainTeamIds.Contains(s.TeamAId) || captainTeamIds.Contains(s.TeamBId)))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var rows = new Dictionary<Guid, CaptainVsDto>();
        var nameIds = new HashSet<Guid> { playerId };

        Guid? CaptainOf(Guid teamId, Guid? matchCaptainId)
            => matchCaptainId ?? (teams.TryGetValue(teamId, out var team) ? team.CaptainPlayerId : null);

        foreach (var match in matches)
        {
            var capA = CaptainOf(match.TeamAId, match.TeamACaptainId);
            var capB = CaptainOf(match.TeamBId, match.TeamBCaptainId);
            Guid? opponentId = null;
            Guid? ourTeamId = null;
            if (capA == playerId)
            {
                opponentId = capB;
                ourTeamId = match.TeamAId;
            }
            else if (capB == playerId)
            {
                opponentId = capA;
                ourTeamId = match.TeamBId;
            }
            if (opponentId is not Guid opp || opp == Guid.Empty || opp == playerId || ourTeamId is null)
                continue;

            if (!rows.TryGetValue(opp, out var row))
            {
                row = new CaptainVsDto { CaptainId = playerId, OpponentCaptainId = opp };
                rows[opp] = row;
                nameIds.Add(opp);
            }

            innsByMatch.TryGetValue(match.Id, out var inns);
            inns ??= new List<Innings>();
            row.Played++;
            var (winner, tied) = ResolveResult(match, inns);
            if (tied) row.Tied++;
            else if (winner == ourTeamId) row.Won++;
            else row.Lost++;
        }

        foreach (var series in seriesList)
        {
            Guid? ourTeamId = null;
            Guid? oppTeamId = null;
            if (captainTeamIds.Contains(series.TeamAId))
            {
                ourTeamId = series.TeamAId;
                oppTeamId = series.TeamBId;
            }
            else if (captainTeamIds.Contains(series.TeamBId))
            {
                ourTeamId = series.TeamBId;
                oppTeamId = series.TeamAId;
            }
            if (ourTeamId is null || oppTeamId is null)
                continue;
            if (!teams.TryGetValue(oppTeamId.Value, out var oppTeam) || oppTeam.CaptainPlayerId is not Guid opp
                || opp == Guid.Empty || opp == playerId)
                continue;

            if (!rows.TryGetValue(opp, out var row))
            {
                row = new CaptainVsDto { CaptainId = playerId, OpponentCaptainId = opp };
                rows[opp] = row;
                nameIds.Add(opp);
            }
            row.Series++;
            if (series.IsTied) row.SeriesTied++;
            else if (series.WinnerTeamId == ourTeamId) row.SeriesWon++;
            else row.SeriesLoss++;
        }

        if (rows.Count == 0)
            return Array.Empty<CaptainVsDto>();

        var names = await db.Players.AsNoTracking()
            .Where(p => nameIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
            .ConfigureAwait(false);
        var selfName = names.GetValueOrDefault(playerId, "?");
        foreach (var row in rows.Values)
        {
            row.CaptainName = selfName;
            row.OpponentCaptainName = names.GetValueOrDefault(row.OpponentCaptainId, "?");
        }

        return rows.Values
            .Where(r => r.Played > 0 || r.Series > 0)
            .OrderByDescending(r => r.Played)
            .ThenByDescending(r => r.Won)
            .ThenBy(r => r.OpponentCaptainName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<IReadOnlyList<ConfrontationDto>> BuildNetsConfrontationsAsync(
        ApiDbContext db,
        Guid playerId,
        bool asBatterPerspective,
        CancellationToken ct)
    {
        var completedIds = await db.NetSessions.AsNoTracking()
            .Where(s => s.Status == NetSessionStatus.Completed && !s.IsDeleted)
            .Select(s => s.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (completedIds.Count == 0)
            return Array.Empty<ConfrontationDto>();

        var netBalls = asBatterPerspective
            ? await db.NetDeliveries.AsNoTracking()
                .Where(d => completedIds.Contains(d.SessionId)
                            && (d.BatterId == playerId || d.DismissedPlayerId == playerId))
                .ToListAsync(ct).ConfigureAwait(false)
            : await db.NetDeliveries.AsNoTracking()
                .Where(d => completedIds.Contains(d.SessionId) && d.BowlerId == playerId)
                .ToListAsync(ct).ConfigureAwait(false);

        var deliveries = netBalls.Select(d => FromNet(d, d.SessionId)).ToList();
        if (deliveries.Count == 0)
            return Array.Empty<ConfrontationDto>();

        var names = await db.Players.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
            .ConfigureAwait(false);

        var groups = asBatterPerspective
            ? deliveries.GroupBy(d => d.BowlerId)
            : deliveries.GroupBy(d => d.BatterId);

        var stats = new List<ConfrontationDto>();
        foreach (var g in groups)
        {
            var first = g.First();
            var row = new ConfrontationDto
            {
                BatterId = asBatterPerspective ? playerId : first.BatterId,
                BatterName = names.GetValueOrDefault(asBatterPerspective ? playerId : first.BatterId, "?"),
                BowlerId = first.BowlerId,
                BowlerName = names.GetValueOrDefault(first.BowlerId, "?")
            };
            foreach (var d in g)
            {
                if (asBatterPerspective && d.BatterId != playerId)
                {
                    if (d.IsWicket && d.DismissedPlayerId == playerId)
                        row.Outs++;
                    continue;
                }
                ApplyConfrontation(row, d);
            }
            stats.Add(row);
        }

        return asBatterPerspective
            ? stats.OrderByDescending(s => s.Runs).ThenByDescending(s => s.Balls).ToList()
            : stats.OrderByDescending(s => s.Outs).ThenBy(s => s.Runs).ToList();
    }

    private static void ApplyMatchResults(
        PlayerCareerDto summary,
        Guid playerId,
        List<Match> completedMatches,
        Dictionary<Guid, List<Innings>> inningsByMatch,
        Dictionary<Guid, Innings> inningsById,
        List<Delivery> asBatter,
        List<Delivery> asBowler,
        Dictionary<Guid, Team> teams)
    {
        var played = asBatter.Select(d => d.MatchId).Concat(asBowler.Select(d => d.MatchId)).ToHashSet();
        foreach (var match in completedMatches.Where(m => played.Contains(m.Id)))
        {
            if (!inningsByMatch.TryGetValue(match.Id, out var matchInns))
                continue;
            var playerTeamId = ResolvePlayerTeam(playerId, asBatter, asBowler, inningsById, match.Id);
            if (playerTeamId is null)
                continue;

            summary.Matches++;
            var (winnerId, tied) = ResolveResult(match, matchInns);
            if (tied) summary.Tied++;
            else if (winnerId == playerTeamId) summary.Won++;
            else summary.Loss++;

            var captainA = match.TeamACaptainId ?? (teams.TryGetValue(match.TeamAId, out var ta) ? ta.CaptainPlayerId : null);
            var captainB = match.TeamBCaptainId ?? (teams.TryGetValue(match.TeamBId, out var tb) ? tb.CaptainPlayerId : null);
            if (playerId != captainA && playerId != captainB)
                continue;

            summary.CaptainMatches++;
            if (tied) summary.CaptainTied++;
            else if (winnerId == playerTeamId) summary.CaptainWon++;
            else summary.CaptainLoss++;
        }
    }

    private static void ApplyTournamentResults(
        PlayerCareerDto summary,
        Guid playerId,
        List<Tournament> tournaments,
        Dictionary<Guid, Team> teams)
    {
        foreach (var tour in tournaments)
        {
            Guid? playerTeamId = null;
            foreach (var tt in tour.Teams)
            {
                if (!teams.TryGetValue(tt.TeamId, out var team))
                    continue;
                if (team.TeamPlayers.Any(tp => tp.PlayerId == playerId))
                {
                    playerTeamId = tt.TeamId;
                    break;
                }
            }

            if (playerTeamId is null)
                continue;

            summary.Tournaments++;
            var won = tour.WinnerTeamId == playerTeamId;
            if (won) summary.TournamentWon++;
            else summary.TournamentLoss++;

            if (!teams.TryGetValue(playerTeamId.Value, out var playerTeam)
                || playerTeam.CaptainPlayerId != playerId)
                continue;

            summary.CaptainTournaments++;
            if (won) summary.CaptainTournamentWon++;
            else summary.CaptainTournamentLoss++;
        }
    }

    private static void ApplySeriesResults(
        PlayerCareerDto summary,
        Guid playerId,
        List<Series> seriesList,
        Dictionary<Guid, Team> teams)
    {
        foreach (var series in seriesList)
        {
            Guid? playerTeamId = null;
            foreach (var teamId in new[] { series.TeamAId, series.TeamBId })
            {
                if (!teams.TryGetValue(teamId, out var team))
                    continue;
                if (team.TeamPlayers.Any(tp => tp.PlayerId == playerId))
                {
                    playerTeamId = teamId;
                    break;
                }
            }

            if (playerTeamId is null)
                continue;

            summary.Series++;
            if (series.IsTied)
                summary.SeriesTied++;
            else if (series.WinnerTeamId == playerTeamId)
                summary.SeriesWon++;
            else
                summary.SeriesLoss++;

            if (!teams.TryGetValue(playerTeamId.Value, out var playerTeam)
                || playerTeam.CaptainPlayerId != playerId)
                continue;

            summary.CaptainSeries++;
            if (series.IsTied)
                summary.CaptainSeriesTied++;
            else if (series.WinnerTeamId == playerTeamId)
                summary.CaptainSeriesWon++;
            else
                summary.CaptainSeriesLoss++;
        }
    }

    private static Guid? ResolvePlayerTeam(
        Guid playerId,
        List<Delivery> asBatter,
        List<Delivery> asBowler,
        Dictionary<Guid, Innings> inningsById,
        Guid matchId)
    {
        foreach (var d in asBatter.Where(x => x.MatchId == matchId))
            if (inningsById.TryGetValue(d.InningsId, out var inn))
                return inn.BattingTeamId;
        foreach (var d in asBowler.Where(x => x.MatchId == matchId))
            if (inningsById.TryGetValue(d.InningsId, out var inn))
                return inn.BowlingTeamId;
        return null;
    }

    private static (Guid? Winner, bool Tied) ResolveResult(Match match, List<Innings> inns)
    {
        var a = inns.Where(i => i.BattingTeamId == match.TeamAId).Sum(i => i.Deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns));
        var b = inns.Where(i => i.BattingTeamId == match.TeamBId).Sum(i => i.Deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns));
        if (a == b) return (null, true);
        return a > b ? (match.TeamAId, false) : (match.TeamBId, false);
    }

    private static void ApplyBatting(PlayerCareerDto s, List<Delivery> asBatter)
    {
        s.InningsBatted = asBatter.Select(d => d.InningsId).Distinct().Count();
        foreach (var d in asBatter)
        {
            if (d.ExtraType == ExtraType.Wide)
                s.WidesFaced++;
            else if (d.ExtraType == ExtraType.NoBall)
            {
                s.NoBallsFaced++;
                s.Runs += d.RunsOffBat;
                CountBat(s, d.RunsOffBat, false);
            }
            else             if (d.IsLegalDelivery)
            {
                s.BallsFaced++;
                s.Runs += d.RunsOffBat;
                CountBat(s, d.RunsOffBat, d.ExtraRuns == 0 && !d.IsWicket);
            }
        }
    }

    /// <summary>Count every dismissal for this player (striker or non-striker run-out).</summary>
    private static void ApplyDismissals(PlayerCareerDto s, List<Delivery> outs)
    {
        foreach (var d in outs)
        {
            s.Dismissals++;
            switch (d.WicketType)
            {
                case WicketType.Bowled: s.BowledOut++; break;
                case WicketType.Caught: s.CaughtOut++; break;
                case WicketType.Lbw: s.LbwOut++; break;
                case WicketType.RunOut: s.RunOut++; break;
                case WicketType.Stumped: s.StumpedOut++; break;
                case WicketType.HitWicket: s.HitWicketOut++; break;
                case WicketType.Retired: s.RetiredOut++; break;
                default: s.OtherOut++; break;
            }
        }
    }

    private static void CountBat(PlayerCareerDto s, int r, bool dot)
    {
        switch (r)
        {
            case 0 when dot: s.Dots++; break;
            case 1: s.Ones++; break;
            case 2: s.Twos++; break;
            case 3: s.Threes++; break;
            case 4: s.Fours++; break;
            case 5: s.Fives++; break;
            case >= 6: s.Sixes++; break;
        }
    }

    private static void ApplyBowling(PlayerCareerDto s, List<Delivery> asBowler)
    {
        foreach (var d in asBowler)
        {
            switch (d.ExtraType)
            {
                case ExtraType.Wide: s.WidesBowled++; break;
                case ExtraType.NoBall: s.NoBallsBowled++; break;
                case ExtraType.Bye: s.ByesConceded += d.ExtraRuns; break;
                case ExtraType.LegBye: s.LegByesConceded += d.ExtraRuns; break;
            }

            if (d.IsLegalDelivery)
            {
                s.OversBowledBalls++;
                if (d.RunsOffBat == 0 && d.ExtraRuns == 0 && !d.IsWicket)
                    s.DotBallsBowled++;
            }

            s.RunsConceded += d.RunsOffBat + d.ExtraRuns;
            if (!d.IsWicket) continue;
            switch (d.WicketType)
            {
                case WicketType.Bowled: s.BowledWkts++; s.Wickets++; break;
                case WicketType.Caught: s.CaughtWkts++; s.Wickets++; break;
                case WicketType.Lbw: s.LbwWkts++; s.Wickets++; break;
                case WicketType.Stumped: s.StumpedWkts++; s.Wickets++; break;
                case WicketType.HitWicket: s.HitWicketWkts++; s.Wickets++; break;
                case WicketType.RunOut: s.RunOutWkts++; break;
            }
        }
    }

    private static void ApplyBattingMilestones(PlayerCareerDto s, List<Delivery> asBatter, List<Delivery> outs)
    {
        var dismissedInnings = outs.Select(d => d.InningsId).ToHashSet();

        var hsRecorded = false;
        foreach (var innGroup in asBatter.GroupBy(d => d.InningsId))
        {
            var runs = 0;
            var balls = 0;
            var dismissed = dismissedInnings.Contains(innGroup.Key);
            foreach (var d in innGroup.OrderBy(x => x.Sequence))
            {
                if (d.ExtraType == ExtraType.Wide)
                {
                    // faced wide — no bat runs / balls
                }
                else if (d.ExtraType == ExtraType.NoBall)
                {
                    runs += d.RunsOffBat;
                }
                else if (d.IsLegalDelivery)
                {
                    balls++;
                    runs += d.RunsOffBat;
                }
            }

            if (!dismissed)
                s.NotOuts++;

            var betterHs = !hsRecorded
                || runs > s.HighestScore
                || (runs == s.HighestScore && !dismissed && !s.HighestScoreNotOut);
            if (betterHs)
            {
                s.HighestScore = runs;
                s.HighestScoreNotOut = !dismissed;
                s.HighestScoreBalls = balls;
                hsRecorded = true;
            }

            if (dismissed && runs == 0)
            {
                s.Ducks++;
                if (balls == 1)
                    s.GoldenDucks++;
            }

            if (runs >= 100) s.Hundreds++;
            else if (runs >= 50) s.Fifties++;
        }
    }

    private static void ApplyBowlingMilestones(PlayerCareerDto s, List<Delivery> asBowler)
    {
        var bestWkts = -1;
        var bestRuns = int.MaxValue;

        foreach (var innGroup in asBowler.GroupBy(d => d.InningsId))
        {
            var list = innGroup.OrderBy(d => d.Sequence).ToList();
            s.Maidens += CountMaidens(list);

            var wkts = 0;
            var runs = 0;
            foreach (var d in list)
            {
                runs += d.RunsOffBat + d.ExtraRuns;
                if (!d.IsWicket) continue;
                switch (d.WicketType)
                {
                    case WicketType.Bowled:
                    case WicketType.Caught:
                    case WicketType.Lbw:
                    case WicketType.Stumped:
                    case WicketType.HitWicket:
                        wkts++;
                        break;
                }
            }

            if (wkts > bestWkts || (wkts == bestWkts && runs < bestRuns))
            {
                bestWkts = wkts;
                bestRuns = runs;
            }

            s.HatTricks += CountHatTricks(list);
        }

        if (bestWkts >= 0)
        {
            s.BestBowlingWickets = bestWkts;
            s.BestBowlingRuns = bestRuns;
            s.BestBowlingText = $"{bestWkts}/{bestRuns}";
        }
    }

    private static bool IsBowlerWicket(Delivery d) =>
        d.IsWicket && d.WicketType is
            WicketType.Bowled or WicketType.Caught or WicketType.Lbw or
            WicketType.Stumped or WicketType.HitWicket;

    /// <summary>
    /// Three bowler-credited wickets in three consecutive deliveries (wides/no-balls included).
    /// Can span overs; run-outs break the streak.
    /// </summary>
    private static int CountHatTricks(IReadOnlyList<Delivery> bowledInInnings)
    {
        var hatTricks = 0;
        var streak = 0;
        foreach (var d in bowledInInnings)
        {
            if (IsBowlerWicket(d))
            {
                streak++;
                if (streak < 3)
                    continue;
                hatTricks++;
                streak = 0;
            }
            else
            {
                streak = 0;
            }
        }

        return hatTricks;
    }

    private static int CountMaidens(List<Delivery> bowled)
    {
        var maidens = 0;
        var legalInOver = 0;
        var runsInOver = 0;
        foreach (var d in bowled.OrderBy(x => x.Sequence))
        {
            runsInOver += d.RunsOffBat + d.ExtraRuns;
            if (!d.IsLegalDelivery)
                continue;
            legalInOver++;
            if (legalInOver < 6)
                continue;
            if (runsInOver == 0)
                maidens++;
            legalInOver = 0;
            runsInOver = 0;
        }
        return maidens;
    }

    private static void ApplyAttendance(PlayerCareerDto s, int venueTotal, int presentCount)
    {
        s.AttendanceTotal = venueTotal;
        s.AttendancePresent = presentCount;
        s.AttendanceAbsent = Math.Max(0, venueTotal - presentCount);
    }

    private static void ApplyFielding(PlayerCareerDto s, List<Delivery> all, Guid playerId)
    {
        foreach (var d in all.Where(x => x.FielderId == playerId && x.IsWicket))
        {
            switch (d.WicketType)
            {
                case WicketType.Caught: s.Catches++; break;
                case WicketType.RunOut: s.RunOutsEffected++; break;
                case WicketType.Stumped: s.Stumpings++; break;
            }
        }
    }

    private static void ApplyConfrontation(ConfrontationDto row, Delivery d)
    {
        if (d.IsLegalDelivery)
        {
            row.Balls++;
            row.Runs += d.RunsOffBat;
            switch (d.RunsOffBat)
            {
                case 0 when d.ExtraRuns == 0 && !d.IsWicket: row.Dots++; break;
                case 1: row.Ones++; break;
                case 2: row.Twos++; break;
                case 3: row.Threes++; break;
                case 4: row.Fours++; break;
                case 5: row.Fives++; break;
                case >= 6: row.Sixes++; break;
            }
        }
        row.ExtrasConceded += d.ExtraRuns;
        if (d.IsWicket && d.DismissedPlayerId == d.BatterId)
            row.Outs++;
    }
}
