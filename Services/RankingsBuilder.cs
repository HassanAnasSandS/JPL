namespace JPL.Services;

/// <summary>Builds category rankings from a career leaderboard (client fallback when API rankings is unavailable).</summary>
public static class RankingsBuilder
{
    public static RankingsBundle FromLeaderboard(IReadOnlyList<PlayerCareerSummary> board)
    {
        static double BatSr(PlayerCareerSummary p) => p.BallsFaced == 0 ? 0 : p.Runs * 100.0 / p.BallsFaced;
        static double BowlEcon(PlayerCareerSummary p) => p.OversBowledBalls == 0 ? double.MaxValue : p.RunsConceded * 6.0 / p.OversBowledBalls;
        static double BowlAvg(PlayerCareerSummary p) => p.Wickets == 0 ? double.MaxValue : (double)p.RunsConceded / p.Wickets;
        static double BatAvg(PlayerCareerSummary p) => p.Dismissals == 0 ? p.Runs : (double)p.Runs / p.Dismissals;
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
        static int FieldActions(PlayerCareerSummary p) => p.Catches + p.RunOutsEffected + p.Stumpings;

        // ICC Player Rankings: 0–1000. Full ICC per-match opposition algorithm is proprietary;
        // this uses the published ICC structure (T20 weights, innings qualification, AR = √(bat×bowl)).
        static double BatPts(PlayerCareerSummary p)
        {
            if (p.BallsFaced <= 0 && p.Runs <= 0) return 0;
            var innings = Math.Max(p.InningsBatted, p.BallsFaced > 0 || p.Runs > 0 ? 1 : 0);
            var avg = BatAvg(p);
            var sr = BatSr(p);
            var avgF = Math.Min(avg / 40.0, 1.25);
            var srF = Math.Min(sr / 140.0, 1.25);
            var volF = Math.Min(p.Runs / 300.0, 1.0);
            var index = avgF * 0.45 + srF * 0.40 + volF * 0.15;
            var qualify = Math.Min(1.0, innings / 20.0);
            return Math.Round(Math.Clamp(1000.0 * index * qualify, 0, 1000));
        }

        static double BowlPts(PlayerCareerSummary p)
        {
            if (p.Wickets <= 0 && p.OversBowledBalls <= 0) return 0;
            var avg = BowlAvg(p);
            var econ = BowlEcon(p);
            var overs = p.OversBowledBalls / 6.0;
            var bowlSr = p.Wickets == 0 ? double.MaxValue : p.OversBowledBalls / (double)p.Wickets;
            var avgF = avg >= double.MaxValue / 2 ? 0 : Math.Min(20.0 / Math.Max(avg, 8.0), 1.25);
            var econF = econ >= double.MaxValue / 2 ? 0 : Math.Min(7.5 / Math.Max(econ, 4.5), 1.25);
            var srF = p.Wickets <= 0 ? 0 : Math.Min(16.0 / Math.Max(bowlSr, 8.0), 1.25);
            var wktsF = Math.Min(p.Wickets / 20.0, 1.0);
            var index = avgF * 0.35 + econF * 0.35 + srF * 0.20 + wktsF * 0.10;
            var qualify = Math.Min(1.0, Math.Max(overs / 15.0, p.Wickets / 15.0));
            return Math.Round(Math.Clamp(1000.0 * index * qualify, 0, 1000));
        }

        static bool HasBatted(PlayerCareerSummary p) => p.BallsFaced > 0 || p.Runs > 0;
        static bool HasBowled(PlayerCareerSummary p) => p.OversBowledBalls > 0 || p.Wickets > 0;

        static double ArPts(PlayerCareerSummary p)
        {
            if (!HasBatted(p) || !HasBowled(p)) return 0;
            return Math.Round(Math.Clamp(Math.Sqrt(BatPts(p) * BowlPts(p)), 0, 1000));
        }

        static double FieldPts(PlayerCareerSummary p)
        {
            if (FieldActions(p) <= 0) return 0;
            var matches = Math.Max(p.Matches, 1);
            var dpm = FieldActions(p) / (double)matches;
            var rateF = Math.Min(dpm / 0.7, 1.25);
            var volF = Math.Min(FieldActions(p) / 20.0, 1.0);
            var stF = Math.Min(p.Stumpings / 8.0, 1.0);
            var index = rateF * 0.60 + volF * 0.25 + stF * 0.15;
            var qualify = Math.Min(1.0, matches / 15.0);
            return Math.Round(Math.Clamp(1000.0 * index * qualify, 0, 1000));
        }

        static double CapWinPct(PlayerCareerSummary p)
            => p.CaptainMatches == 0 ? 0 : (p.CaptainWon + p.CaptainTied * 0.5) / p.CaptainMatches;
        static string CapWinPctText(PlayerCareerSummary p)
            => p.CaptainMatches == 0 ? "-" : (CapWinPct(p) * 100).ToString("0.#") + "%";
        static double CapPts(PlayerCareerSummary p)
        {
            if (p.CaptainMatches <= 0) return 0;
            var winF = CapWinPct(p);
            var winsF = Math.Min(p.CaptainWon / 15.0, 1.0);
            var gamesF = Math.Min(p.CaptainMatches / 15.0, 1.0);
            var index = winF * 0.70 + winsF * 0.20 + gamesF * 0.10;
            var qualify = Math.Min(1.0, p.CaptainMatches / 10.0);
            return Math.Round(Math.Clamp(1000.0 * index * qualify, 0, 1000));
        }

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
                Detail = $"Runs {p.Runs} · HS {p.HighestScoreText} · Balls {p.BallsFaced} · Avg {BatAvgText(p)} · SR {BatSr(p):0.#} · 50s {p.Fifties} · 100s {p.Hundreds} · 4s {p.Fours} · 6s {p.Sixes}"
            })
            .ToList();

        var bowling = board
            .Where(p => p.Wickets > 0 || p.OversBowledBalls > 0)
            .OrderByDescending(BowlPts)
            .ThenBy(BowlAvg)
            .ThenByDescending(p => p.Wickets)
            .Select((p, i) => new RankingRow
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = BowlPts(p).ToString("0"),
                SecondaryLabel = "Avg",
                SecondaryValue = BowlAvgText(p),
                Detail = $"Wkts {p.Wickets} · Best {p.BestBowlingText} · Overs {OversText(p)} · Runs {p.RunsConceded} · Avg {BowlAvgText(p)} · SR {BowlSrText(p)} · Econ {EconText(p)} · Maidens {p.Maidens} · Dots {p.DotBallsBowled}"
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
                Detail = $"ICC √(Bat×Bowl) · Bat {BatPts(p):0} · Bowl {BowlPts(p):0} · Runs {p.Runs} · Wkts {p.Wickets} · Overs {OversText(p)}"
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
                Detail = $"Ct {p.Catches} · RO {p.RunOutsEffected} · St {p.Stumpings} · Total {FieldActions(p)}"
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
                Detail = $"Cap {p.CaptainMatches} · W {p.CaptainWon} · L {p.CaptainLoss} · T {p.CaptainTied} · Win% {CapWinPctText(p)}"
            })
            .ToList();

        return new RankingsBundle
        {
            Batting = batting,
            Bowling = bowling,
            AllRounder = allRounder,
            Fielder = fielder,
            Captain = captain
        };
    }
}
