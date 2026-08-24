namespace JPL.Services;

/// <summary>Builds category rankings from a career leaderboard (client fallback when API rankings is unavailable).</summary>
public static class RankingsBuilder
{
    public static RankingsBundle FromLeaderboard(IReadOnlyList<PlayerCareerSummary> board)
    {
        static double BatSr(PlayerCareerSummary p) => SkillRatings.BatSr(p);
        static double BowlEcon(PlayerCareerSummary p) => SkillRatings.BowlEcon(p);
        static double BowlAvg(PlayerCareerSummary p) => SkillRatings.BowlAvg(p);
        static double BatAvg(PlayerCareerSummary p) => SkillRatings.BatAvg(p);
        static string BatAvgText(PlayerCareerSummary p)
            => p.InningsBatted <= 0 && p.BallsFaced == 0 && p.Runs == 0
                ? "-"
                : BatAvg(p).ToString("0.##");
        static string BowlAvgText(PlayerCareerSummary p) => p.Wickets == 0 ? "-" : BowlAvg(p).ToString("0.##");
        static string BowlSrText(PlayerCareerSummary p)
            => p.Wickets == 0 ? "-" : (p.OversBowledBalls / (double)p.Wickets).ToString("0.##");
        static string OversText(PlayerCareerSummary p)
            => $"{p.OversBowledBalls / 6}.{p.OversBowledBalls % 6}";
        static string EconText(PlayerCareerSummary p)
            => p.OversBowledBalls == 0 ? "-" : BowlEcon(p).ToString("0.##");
        static int FieldActions(PlayerCareerSummary p) => SkillRatings.FieldActions(p);
        static double BatPts(PlayerCareerSummary p) => SkillRatings.BattingPts(p);
        static double BowlPts(PlayerCareerSummary p) => SkillRatings.BowlingPts(p);
        static bool HasBatted(PlayerCareerSummary p) => SkillRatings.HasBatted(p);
        static bool HasBowled(PlayerCareerSummary p) => SkillRatings.HasBowled(p);
        static double ArPts(PlayerCareerSummary p) => SkillRatings.AllRounderPts(p);
        static double FieldPts(PlayerCareerSummary p) => SkillRatings.FieldingPts(p);
        static double CapPts(PlayerCareerSummary p) => SkillRatings.CaptaincyPts(p);
        static double CapWinPct(PlayerCareerSummary p)
            => p.CaptainMatches == 0 ? 0 : (p.CaptainWon + p.CaptainTied * 0.5) / p.CaptainMatches;
        static string CapWinPctText(PlayerCareerSummary p)
            => p.CaptainMatches == 0 ? "-" : (CapWinPct(p) * 100).ToString("0.#") + "%";

        var batting = board
            .Where(p => p.BallsFaced > 0 || p.Runs > 0)
            .OrderByDescending(BatPts)
            .ThenByDescending(BatAvg)
            .ThenByDescending(p => p.Runs)
            .Select((p, i) => new RankingRow
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = BatPts(p).ToString("0"),
                SecondaryLabel = "Avg",
                SecondaryValue = BatAvgText(p),
                Detail = $"Runs {p.Runs} · Balls {p.BallsFaced} · Avg {BatAvgText(p)} · SR {BatSr(p):0.#}",
                Breakdown = RankingExplain.Batting(p)
            })
            .ToList();

        var bowling = board
            .Where(p => p.Wickets > 0 || p.OversBowledBalls > 0)
            .OrderByDescending(BowlPts)
            .ThenBy(BowlEcon)
            .ThenByDescending(p => p.Wickets)
            .Select((p, i) => new RankingRow
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = BowlPts(p).ToString("0"),
                SecondaryLabel = "Econ",
                SecondaryValue = EconText(p),
                Detail = $"Wkts {p.Wickets} · Overs {OversText(p)} · Balls {p.OversBowledBalls} · Runs {p.RunsConceded} · Econ {EconText(p)} · SR {BowlSrText(p)} · Avg {BowlAvgText(p)}",
                Breakdown = RankingExplain.Bowling(p)
            })
            .ToList();

        var allRounder = board
            .Where(p => HasBatted(p) && HasBowled(p))
            .OrderByDescending(ArPts)
            .ThenByDescending(BatPts)
            .ThenByDescending(BowlPts)
            .Select((p, i) => new RankingRow
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = ArPts(p).ToString("0"),
                SecondaryLabel = "B/Bowl",
                SecondaryValue = $"{BatPts(p):0}/{BowlPts(p):0}",
                Detail = $"Bat {BatPts(p):0} + Bowl {BowlPts(p):0} · Runs {p.Runs} · Wkts {p.Wickets} · Overs {OversText(p)}",
                Breakdown = RankingExplain.AllRounder(p)
            })
            .ToList();

        var fielder = board
            .Where(p => FieldActions(p) > 0)
            .OrderByDescending(FieldPts)
            .ThenByDescending(p => p.Catches)
            .ThenByDescending(p => p.RunOutsEffected)
            .Select((p, i) => new RankingRow
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = FieldPts(p).ToString("0"),
                SecondaryLabel = "Ct",
                SecondaryValue = p.Catches.ToString(),
                Detail = $"Ct {p.Catches} · RO {p.RunOutsEffected} · St {p.Stumpings} · Total {FieldActions(p)}",
                Breakdown = RankingExplain.Fielding(p)
            })
            .ToList();

        var captain = board
            .Where(p => p.CaptainMatches > 0)
            .OrderByDescending(CapPts)
            .ThenByDescending(CapWinPct)
            .ThenByDescending(p => p.CaptainWon)
            .ThenByDescending(p => p.CaptainMatches)
            .Select((p, i) => new RankingRow
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = CapPts(p).ToString("0"),
                SecondaryLabel = "Win%",
                SecondaryValue = CapWinPctText(p),
                Detail = $"Cap {p.CaptainMatches} · W {p.CaptainWon} · L {p.CaptainLoss} · T {p.CaptainTied} · Win% {CapWinPctText(p)}",
                Breakdown = RankingExplain.Captaincy(p)
            })
            .ToList();

        return new RankingsBundle
        {
            Batting = RankingBars.Apply(batting),
            Bowling = RankingBars.Apply(bowling),
            AllRounder = RankingBars.Apply(allRounder),
            Fielder = RankingBars.Apply(fielder),
            Captain = RankingBars.Apply(captain),
            Team = Array.Empty<RankingRow>()
        };
    }
}
