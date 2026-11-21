namespace JPL.Services;

public enum EventAwardPointsCategory
{
    Overall,
    Batting,
    Bowling,
    Fielding
}

/// <summary>Aggregates match stats, then applies ranking formulas once for the whole event.</summary>
public static class EventAwardPointsCalculator
{
    private sealed class Tally
    {
        public Guid PlayerId { get; init; }
        public string PlayerName { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;
        public PlayerCareerSummary Stats { get; } = new();
    }

    public static IReadOnlyList<MatchPointsRow> Aggregate(
        IEnumerable<(string MatchTitle, IReadOnlyList<MatchPointsRow> Players)> matches,
        string scopeLabel = "Event total")
    {
        var tallies = new Dictionary<Guid, Tally>();
        foreach (var (_, players) in matches)
        {
            foreach (var player in players)
            {
                if (player.PlayerId == Guid.Empty)
                    continue;
                if (!tallies.TryGetValue(player.PlayerId, out var tally))
                {
                    tally = new Tally { PlayerId = player.PlayerId };
                    tallies[player.PlayerId] = tally;
                }

                if (string.IsNullOrWhiteSpace(tally.PlayerName)) tally.PlayerName = player.PlayerName;
                if (string.IsNullOrWhiteSpace(tally.TeamName)) tally.TeamName = player.TeamName;
                MergeStats(tally.Stats, player.MatchStats);
            }
        }

        return tallies.Values.Select(t =>
            {
                var batting = RankingPoints.Batting(t.Stats)
                    .Select(f => f with { MatchTitle = scopeLabel }).ToList();
                var bowling = RankingPoints.Bowling(t.Stats)
                    .Select(f => f with { MatchTitle = scopeLabel }).ToList();
                var fielding = RankingPoints.Fielding(t.Stats)
                    .Select(f => f with { MatchTitle = scopeLabel }).ToList();
                return new MatchPointsRow
            {
                PlayerId = t.PlayerId,
                PlayerName = t.PlayerName,
                TeamName = t.TeamName,
                BattingPoints = (int)RankingPoints.Net(batting),
                BowlingPoints = (int)RankingPoints.Net(bowling),
                FieldingPoints = (int)RankingPoints.Net(fielding),
                Runs = t.Stats.Runs,
                Balls = t.Stats.BallsFaced,
                Fours = t.Stats.Fours,
                Sixes = t.Stats.Sixes,
                Wickets = t.Stats.Wickets,
                Maidens = t.Stats.Maidens,
                Catches = t.Stats.Catches,
                RunOuts = t.Stats.RunOutsEffected,
                Stumpings = t.Stats.Stumpings,
                MatchStats = t.Stats,
                BattingFactors = batting,
                BowlingFactors = bowling,
                FieldingFactors = fielding
            };
            })
            .OrderByDescending(r => r.TotalPoints)
            .ThenByDescending(r => r.Wickets)
            .ThenByDescending(r => r.Runs)
            .ThenBy(r => r.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void MergeStats(PlayerCareerSummary total, PlayerCareerSummary match)
    {
        total.Matches += match.Matches;
        total.InningsBatted += match.InningsBatted;
        total.BallsFaced += match.BallsFaced;
        total.Runs += match.Runs;
        total.Dots += match.Dots;
        total.Fours += match.Fours;
        total.Sixes += match.Sixes;
        total.Dismissals += match.Dismissals;
        total.BowledOut += match.BowledOut;
        total.CaughtOut += match.CaughtOut;
        total.LbwOut += match.LbwOut;
        total.RunOut += match.RunOut;
        total.StumpedOut += match.StumpedOut;
        total.HitWicketOut += match.HitWicketOut;
        total.RetiredOut += match.RetiredOut;
        total.OtherOut += match.OtherOut;
        total.Ducks += match.Ducks;
        total.GoldenDucks += match.GoldenDucks;
        total.Fifties += match.Fifties;
        total.Hundreds += match.Hundreds;
        total.NotOuts += match.NotOuts;
        if (match.HighestScore > total.HighestScore)
        {
            total.HighestScore = match.HighestScore;
            total.HighestScoreNotOut = match.HighestScoreNotOut;
            total.HighestScoreBalls = match.HighestScoreBalls;
        }

        total.InningsBowled += match.InningsBowled;
        total.OversBowledBalls += match.OversBowledBalls;
        total.RunsConceded += match.RunsConceded;
        total.Wickets += match.Wickets;
        total.DotBallsBowled += match.DotBallsBowled;
        total.WidesBowled += match.WidesBowled;
        total.NoBallsBowled += match.NoBallsBowled;
        total.ByesConceded += match.ByesConceded;
        total.LegByesConceded += match.LegByesConceded;
        total.BowledWkts += match.BowledWkts;
        total.CaughtWkts += match.CaughtWkts;
        total.LbwWkts += match.LbwWkts;
        total.StumpedWkts += match.StumpedWkts;
        total.HitWicketWkts += match.HitWicketWkts;
        total.Maidens += match.Maidens;
        total.HatTricks += match.HatTricks;
        if (match.BestBowlingWickets > total.BestBowlingWickets
            || (match.BestBowlingWickets == total.BestBowlingWickets
                && match.BestBowlingRuns < total.BestBowlingRuns))
        {
            total.BestBowlingWickets = match.BestBowlingWickets;
            total.BestBowlingRuns = match.BestBowlingRuns;
            total.BestBowlingText = match.BestBowlingText;
        }

        total.Catches += match.Catches;
        total.RunOutsEffected += match.RunOutsEffected;
        total.Stumpings += match.Stumpings;
        total.DroppedCatches += match.DroppedCatches;
    }

    public static IReadOnlyList<MatchPointsRow> ForCategory(
        IReadOnlyList<MatchPointsRow> rows,
        EventAwardPointsCategory category,
        Guid? awardWinnerId)
    {
        var sorted = rows
            .OrderByDescending(r => Score(r, category))
            .ThenByDescending(r => awardWinnerId == r.PlayerId)
            .ThenByDescending(r => r.TotalPoints)
            .ThenByDescending(r => r.Wickets)
            .ThenByDescending(r => r.Runs)
            .ThenBy(r => r.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < sorted.Count; i++)
        {
            var row = sorted[i];
            sorted[i] = Copy(row, i + 1, Score(row, category).ToString(), awardWinnerId == row.PlayerId, category);
        }
        return sorted;
    }

    private static int Score(MatchPointsRow row, EventAwardPointsCategory category) => category switch
    {
        EventAwardPointsCategory.Batting => row.BattingPoints,
        EventAwardPointsCategory.Bowling => row.BowlingPoints,
        EventAwardPointsCategory.Fielding => row.FieldingPoints,
        _ => row.TotalPoints
    };

    private static MatchPointsRow Copy(MatchPointsRow row, int rank, string score, bool isAwardWinner, EventAwardPointsCategory category)
    {
        var factors = CollapseFactors(FactorsFor(row, category)).ToList();
        var positive = factors.Where(x => x.Part.Pts > 0.0)
            .Select(x => ToAwardFactor(x.Part, x.Category, category == EventAwardPointsCategory.Overall, true))
            .ToList();
        var negative = factors.Where(x => x.Part.Pts < 0.0)
            .Select(x => ToAwardFactor(x.Part, x.Category, category == EventAwardPointsCategory.Overall, false))
            .ToList();
        var negativeSum = Math.Abs(factors.Where(x => x.Part.Pts < 0).Sum(x => x.Part.Pts));
        return new MatchPointsRow
        {
            PlayerId = row.PlayerId,
            PlayerName = row.PlayerName,
            TeamName = row.TeamName,
            Rank = rank,
            Order = rank - 1,
            BattingPoints = row.BattingPoints,
            BowlingPoints = row.BowlingPoints,
            FieldingPoints = row.FieldingPoints,
            Runs = row.Runs,
            Balls = row.Balls,
            Fours = row.Fours,
            Sixes = row.Sixes,
            Wickets = row.Wickets,
            Maidens = row.Maidens,
            Catches = row.Catches,
            RunOuts = row.RunOuts,
            Stumpings = row.Stumpings,
            MatchStats = row.MatchStats,
            IsPlayerOfTheMatch = isAwardWinner,
            PointsTextOverride = score,
            BreakdownTextOverride = category switch
            {
                EventAwardPointsCategory.Batting => $"Bat {row.BattingPoints:+#;-#;0}",
                EventAwardPointsCategory.Bowling => $"Bowl {row.BowlingPoints:+#;-#;0}",
                EventAwardPointsCategory.Fielding => $"Field {row.FieldingPoints:+#;-#;0}",
                _ => null
            },
            ImpactTextOverride = category switch
            {
                EventAwardPointsCategory.Batting => $"{row.Runs} runs · {row.Balls} balls · {row.Fours} fours · {row.Sixes} sixes",
                EventAwardPointsCategory.Bowling => $"{row.Wickets} wickets · {row.Maidens} maidens",
                EventAwardPointsCategory.Fielding => $"{row.Catches} catches · {row.RunOuts} run-outs · {row.Stumpings} stumpings",
                _ => null
            },
            ShowBattingFactors = category is EventAwardPointsCategory.Overall or EventAwardPointsCategory.Batting,
            ShowBowlingFactors = category is EventAwardPointsCategory.Overall or EventAwardPointsCategory.Bowling,
            ShowFieldingFactors = category is EventAwardPointsCategory.Overall or EventAwardPointsCategory.Fielding,
            AwardPositiveFactors = positive,
            AwardNegativeFactors = negative,
            AwardPositiveTotalText = $"+{factors.Where(x => x.Part.Pts > 0).Sum(x => x.Part.Pts):0.#}",
            AwardNegativeTotalText = negativeSum < 0.05 ? "0" : $"−{negativeSum:0.#}",
            BattingFactors = row.BattingFactors,
            BowlingFactors = row.BowlingFactors,
            FieldingFactors = row.FieldingFactors
        };
    }

    private static IEnumerable<(RankingPart Part, string Category)> FactorsFor(MatchPointsRow row, EventAwardPointsCategory category)
    {
        if (category is EventAwardPointsCategory.Overall or EventAwardPointsCategory.Batting)
            foreach (var factor in row.BattingFactors) yield return (factor, "Batting");
        if (category is EventAwardPointsCategory.Overall or EventAwardPointsCategory.Bowling)
            foreach (var factor in row.BowlingFactors) yield return (factor, "Bowling");
        if (category is EventAwardPointsCategory.Overall or EventAwardPointsCategory.Fielding)
            foreach (var factor in row.FieldingFactors) yield return (factor, "Fielding");
    }

    private static IEnumerable<(RankingPart Part, string Category)> CollapseFactors(
        IEnumerable<(RankingPart Part, string Category)> source)
    {
        foreach (var group in source.GroupBy(x => (x.Category, x.Part.Label)))
        {
            var items = group.ToList();
            var stats = items.Select(x => x.Part.Stat).ToList();
            var stat = TryCombineMultiplierStats(stats, out var combined)
                ? combined
                : string.Join(" · ", stats.Distinct(StringComparer.Ordinal));
            var matchTitles = items.Select(x => x.Part.MatchTitle)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            yield return (
                new RankingPart(group.Key.Label, stat, items.Sum(x => x.Part.Pts), string.Join(", ", matchTitles)),
                group.Key.Category);
        }
    }

    private static bool TryCombineMultiplierStats(IReadOnlyList<string> stats, out string combined)
    {
        combined = string.Empty;
        if (stats.Count == 0) return false;
        var values = new List<int>(stats.Count);
        string? multiplier = null;
        foreach (var value in stats)
        {
            var parts = value.Split('×', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var count))
                return false;
            if (multiplier is null) multiplier = parts[1];
            else if (!string.Equals(multiplier, parts[1], StringComparison.Ordinal)) return false;
            values.Add(count);
        }
        combined = $"{values.Sum()} × {multiplier}";
        return true;
    }

    private static RankingFactorLine ToAwardFactor(RankingPart part, string category, bool includeCategory, bool positive)
        => new()
        {
            Label = includeCategory ? $"{category} · {part.Label}" : part.Label,
            Stat = part.Stat,
            MatchTitle = part.MatchTitle,
            PointsText = part.Pts.ToString("+0.#;-0.#;0", System.Globalization.CultureInfo.InvariantCulture),
            IsPositive = positive
        };
}
