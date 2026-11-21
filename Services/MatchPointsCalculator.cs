using JPL.Domain;

namespace JPL.Services;

/// <summary>
/// One row of per-match batting, bowling and fielding ranking points.
/// </summary>
public sealed class MatchPointsRow
{
    public Guid PlayerId { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public string TeamName { get; init; } = string.Empty;

    /// <summary>1-based position after sorting by total points.</summary>
    public int Rank { get; set; }

    /// <summary>Insertion order while walking the ball-by-ball data — keeps legacy tie-breaks stable.</summary>
    public int Order { get; init; }

    public int BattingPoints { get; init; }
    public int BowlingPoints { get; init; }
    public int FieldingPoints { get; init; }
    public int TotalPoints => BattingPoints + BowlingPoints + FieldingPoints;

    public int Runs { get; init; }
    public int Balls { get; init; }
    public int Fours { get; init; }
    public int Sixes { get; init; }
    public int Wickets { get; init; }
    public int Maidens { get; init; }
    public int Catches { get; init; }
    public int RunOuts { get; init; }
    public int Stumpings { get; init; }
    public PlayerCareerSummary MatchStats { get; init; } = new();

    public bool IsPlayerOfTheMatch { get; init; }
    public string? PointsTextOverride { get; init; }
    public string? BreakdownTextOverride { get; init; }
    public string? ImpactTextOverride { get; init; }
    public bool ShowBattingFactors { get; init; } = true;
    public bool ShowBowlingFactors { get; init; } = true;
    public bool ShowFieldingFactors { get; init; } = true;
    public IReadOnlyList<RankingFactorLine> AwardPositiveFactors { get; init; } = [];
    public IReadOnlyList<RankingFactorLine> AwardNegativeFactors { get; init; } = [];
    public string AwardPositiveTotalText { get; init; } = "+0";
    public string AwardNegativeTotalText { get; init; } = "0";
    public bool HasAwardPositiveFactors => AwardPositiveFactors.Count > 0;
    public bool HasAwardNegativeFactors => AwardNegativeFactors.Count > 0;
    public bool HasAwardFactors => HasAwardPositiveFactors || HasAwardNegativeFactors;
    public bool NoAwardFactors => !HasAwardFactors;
    public bool HasPoints => TotalPoints != 0;

    public IReadOnlyList<RankingPart> BattingFactors { get; init; } = [];
    public IReadOnlyList<RankingPart> BowlingFactors { get; init; } = [];
    public IReadOnlyList<RankingPart> FieldingFactors { get; init; } = [];

    public string RankText => Rank.ToString();
    public string PointsText => PointsTextOverride ?? TotalPoints.ToString();
    public string BattingPointsText => $"{BattingPoints:+#;-#;0} pts";
    public string BowlingPointsText => $"{BowlingPoints:+#;-#;0} pts";
    public string FieldingPointsText => $"{FieldingPoints:+#;-#;0} pts";

    /// <summary>e.g. "Bat 42 · Bowl 25 · Field 12".</summary>
    public string BreakdownText
    {
        get
        {
            if (BreakdownTextOverride is not null) return BreakdownTextOverride;
            var parts = new List<string>(3);
            if (BattingPoints != 0) parts.Add($"Bat {BattingPoints:+#;-#;0}");
            if (BowlingPoints != 0) parts.Add($"Bowl {BowlingPoints:+#;-#;0}");
            if (FieldingPoints != 0) parts.Add($"Field {FieldingPoints:+#;-#;0}");
            return parts.Count == 0 ? "0 points" : string.Join("  ·  ", parts);
        }
    }

    /// <summary>e.g. "42 runs · 2 wkts · 1 catch".</summary>
    public string ImpactText
    {
        get
        {
            if (ImpactTextOverride is not null) return ImpactTextOverride;
            var parts = new List<string>(4);
            if (Runs > 0)
                parts.Add($"{Runs} run{(Runs == 1 ? "" : "s")}");
            if (Wickets > 0)
                parts.Add($"{Wickets} wkt{(Wickets == 1 ? "" : "s")}");
            var fielding = Catches + RunOuts + Stumpings;
            if (fielding > 0)
                parts.Add($"{fielding} field{(fielding == 1 ? "" : "s")}");
            if (Maidens > 0)
                parts.Add($"{Maidens} maiden{(Maidens == 1 ? "" : "s")}");
            return parts.Count == 0 ? "Did not contribute" : string.Join(" · ", parts);
        }
    }

    /// <summary>Explains how each category total was earned.</summary>
    public string PointsDetailText
    {
        get
        {
            var parts = new List<string>(3);
            AddFactors(parts, "Batting", BattingFactors);
            AddFactors(parts, "Bowling", BowlingFactors);
            AddFactors(parts, "Fielding", FieldingFactors);
            return parts.Count == 0 ? "No points earned in this match." : string.Join("\n", parts);
        }
    }

    private static void AddFactors(List<string> lines, string title, IReadOnlyList<RankingPart> factors)
    {
        if (factors.Count == 0) return;
        var detail = string.Join(" · ", factors.Select(f =>
            $"{(string.IsNullOrWhiteSpace(f.MatchTitle) ? string.Empty : f.MatchTitle + " · ")}{f.Label} {f.PointsText}"));
        lines.Add($"{title}: {detail}");
    }
}

/// <summary>
/// The single source of truth for per-match ranking points.
///
/// This is the same formula used by:
///  - <c>MatchService.AssignPlayerOfTheMatchByPerformance</c> (auto Player of the Match)
///  - <c>JPL.Api.Services.EventAwardsCalculator</c> (Player of the Tournament / Series)
///
/// Rules:
/// Uses the club RankingPoints formulas on match-only stats, including negative factors.
/// </summary>
public static class MatchPointsCalculator
{
    public const string RulesText =
        "Ranking rules applied per match: batting, bowling and fielding bonuses and deductions are included.";

    public sealed record PlayerInfo(string Name, string TeamName, bool IsInPlayingXi = true);

    private sealed class Tally
    {
        public int Order { get; init; }
        public PlayerCareerSummary Stats { get; init; } = new();
    }

    /// <summary>
    /// Builds the full points table for one match, highest impact first.
    /// </summary>
    /// <param name="deliveries">Every delivery in the match (all innings).</param>
    /// <param name="players">
    /// Optional roster lookup (id → name/team). When supplied, squad members with no
    /// contribution are included with 0 points so the table lists every player.
    /// </param>
    /// <param name="playerOfTheMatchId">The saved Player of the Match, highlighted in the list.</param>
    public static IReadOnlyList<MatchPointsRow> Calculate(
        IReadOnlyList<Delivery> deliveries,
        IReadOnlyDictionary<Guid, PlayerInfo>? players = null,
        Guid? playerOfTheMatchId = null)
    {
        var tallies = new Dictionary<Guid, Tally>();
        var nextOrder = 0;
        Tally Get(Guid id)
        {
            if (tallies.TryGetValue(id, out var existing)) return existing;
            var created = new Tally
            {
                Order = nextOrder++,
                Stats = new PlayerCareerSummary { PlayerId = id }
            };
            tallies[id] = created;
            return created;
        }

        foreach (var d in deliveries)
        {
            if (d.BatterId != Guid.Empty)
            {
                var bat = Get(d.BatterId).Stats;
                if (d.ExtraType == ExtraType.NoBall) bat.NoBallsFaced++;
                if (d.ExtraType != ExtraType.Wide && d.ExtraType != ExtraType.NoBall && d.IsLegalDelivery)
                {
                    bat.BallsFaced++;
                    if (d.RunsOffBat == 0 && d.ExtraRuns == 0 && !d.IsWicket) bat.Dots++;
                }
                if (d.ExtraType != ExtraType.Wide) bat.Runs += d.RunsOffBat;
                if (d.RunsOffBat == 4 && d.ExtraType != ExtraType.Wide) bat.Fours++;
                if (d.RunsOffBat >= 6 && d.ExtraType != ExtraType.Wide) bat.Sixes++;
            }

            if (d.IsWicket)
            {
                var dismissed = d.DismissedPlayerId ?? d.BatterId;
                if (dismissed != Guid.Empty)
                {
                    var batter = Get(dismissed).Stats;
                    batter.Dismissals++;
                    switch (d.WicketType)
                    {
                        case WicketType.Bowled: batter.BowledOut++; break;
                        case WicketType.Caught: batter.CaughtOut++; break;
                        case WicketType.Lbw: batter.LbwOut++; break;
                        case WicketType.RunOut: batter.RunOut++; break;
                        case WicketType.Stumped: batter.StumpedOut++; break;
                        case WicketType.HitWicket: batter.HitWicketOut++; break;
                        case WicketType.Retired: batter.RetiredOut++; break;
                        default: batter.OtherOut++; break;
                    }
                }
            }

            if (d.BowlerId != Guid.Empty)
            {
                var bowl = Get(d.BowlerId).Stats;
                switch (d.ExtraType)
                {
                    case ExtraType.Wide: bowl.WidesBowled += d.ExtraRuns; break;
                    case ExtraType.NoBall: bowl.NoBallsBowled += d.ExtraRuns; break;
                    case ExtraType.Bye: bowl.ByesConceded += d.ExtraRuns; break;
                    case ExtraType.LegBye: bowl.LegByesConceded += d.ExtraRuns; break;
                }
                if (d.IsLegalDelivery)
                {
                    bowl.OversBowledBalls++;
                    if (d.RunsOffBat == 0 && d.ExtraRuns == 0 && !d.IsWicket) bowl.DotBallsBowled++;
                }
                bowl.RunsConceded += BowlerRunsConceded(d);
                if (d.IsWicket)
                {
                    switch (d.WicketType)
                    {
                        case WicketType.Bowled: bowl.BowledWkts++; bowl.Wickets++; break;
                        case WicketType.Caught: bowl.CaughtWkts++; bowl.Wickets++; break;
                        case WicketType.Lbw: bowl.LbwWkts++; bowl.Wickets++; break;
                        case WicketType.Stumped: bowl.StumpedWkts++; bowl.Wickets++; break;
                        case WicketType.HitWicket: bowl.HitWicketWkts++; bowl.Wickets++; break;
                    }
                }
            }

            if (d.FielderId is Guid fielder && fielder != Guid.Empty)
            {
                var field = Get(fielder).Stats;
                if (d.IsDroppedCatch) field.DroppedCatches++;
                if (d.IsWicket)
                {
                    switch (d.WicketType)
                    {
                        case WicketType.Caught: field.Catches++; break;
                        case WicketType.RunOut: field.RunOutsEffected++; break;
                        case WicketType.Stumped: field.Stumpings++; break;
                    }
                }
            }
        }

        foreach (var innings in deliveries.GroupBy(d => d.InningsId))
        {
            var balls = innings.OrderBy(d => d.Sequence).ToList();
            foreach (var batterGroup in balls.Where(d => d.BatterId != Guid.Empty).GroupBy(d => d.BatterId))
            {
                var stats = Get(batterGroup.Key).Stats;
                var entries = batterGroup.ToList();
                var runs = entries.Where(d => d.ExtraType != ExtraType.Wide).Sum(d => d.RunsOffBat);
                var faced = entries.Count(d => d.IsLegalDelivery && d.ExtraType != ExtraType.Wide && d.ExtraType != ExtraType.NoBall);
                var wasOut = balls.Any(d => d.IsWicket && (d.DismissedPlayerId ?? d.BatterId) == batterGroup.Key);
                stats.InningsBatted++;
                if (!wasOut) stats.NotOuts++;
                stats.HighestScore = Math.Max(stats.HighestScore, runs);
                if (wasOut && runs == 0) { stats.Ducks++; if (faced == 1) stats.GoldenDucks++; }
                if (runs >= 100) stats.Hundreds++; else if (runs >= 50) stats.Fifties++;
            }

            foreach (var bowlerGroup in balls.Where(d => d.BowlerId != Guid.Empty).GroupBy(d => d.BowlerId))
            {
                var stats = Get(bowlerGroup.Key).Stats;
                var spell = bowlerGroup.OrderBy(d => d.Sequence).ToList();
                stats.InningsBowled++;
                stats.Maidens += CountMaidens(spell);
                var wickets = spell.Count(d => d.IsWicket && d.WicketType is not WicketType.RunOut and not WicketType.Retired and not WicketType.None);
                var runs = spell.Sum(BowlerRunsConceded);
                if (wickets > stats.BestBowlingWickets
                    || (wickets == stats.BestBowlingWickets && wickets > 0 && runs < stats.BestBowlingRuns))
                {
                    stats.BestBowlingWickets = wickets;
                    stats.BestBowlingRuns = runs;
                    stats.BestBowlingText = $"{wickets}/{runs}";
                }
                var streak = 0;
                foreach (var d in spell)
                {
                    if (d.IsWicket && d.WicketType is not WicketType.RunOut and not WicketType.Retired and not WicketType.None)
                    {
                        if (++streak == 3) { stats.HatTricks++; streak = 0; }
                    }
                    else if (d.IsLegalDelivery) streak = 0;
                }
            }
        }

        if (players is not null)
            foreach (var id in players.Keys) Get(id);

        var activePlayers = deliveries
            .SelectMany(d => new Guid?[] { d.BatterId, d.BowlerId, d.FielderId, d.DismissedPlayerId })
            .Where(id => id is Guid value && value != Guid.Empty)
            .Select(id => id!.Value)
            .ToHashSet();

        var rows = tallies
            .Select(kv =>
            {
                var info = players is not null && players.TryGetValue(kv.Key, out var p) ? p : null;
                var name = string.IsNullOrWhiteSpace(info?.Name) ? kv.Key.ToString("N")[..8] : info!.Name;
                var stats = kv.Value.Stats;
                var isInPlayingXi = players is null
                                    || !players.TryGetValue(kv.Key, out var listedPlayer)
                                    || listedPlayer.IsInPlayingXi
                                    || activePlayers.Contains(kv.Key);
                stats.Matches = isInPlayingXi ? 1 : 0;
                var batting = RankingPoints.Batting(stats);
                var bowling = RankingPoints.Bowling(stats);
                var fielding = RankingPoints.Fielding(stats);
                return new MatchPointsRow
                {
                    PlayerId = kv.Key,
                    PlayerName = name,
                    TeamName = info?.TeamName ?? string.Empty,
                    Order = kv.Value.Order,
                    BattingPoints = (int)RankingPoints.Net(batting),
                    BowlingPoints = (int)RankingPoints.Net(bowling),
                    FieldingPoints = (int)RankingPoints.Net(fielding),
                    Runs = stats.Runs,
                    Balls = stats.BallsFaced,
                    Fours = stats.Fours,
                    Sixes = stats.Sixes,
                    Wickets = stats.Wickets,
                    Maidens = stats.Maidens,
                    Catches = stats.Catches,
                    RunOuts = stats.RunOutsEffected,
                    Stumpings = stats.Stumpings,
                    MatchStats = stats,
                    BattingFactors = batting,
                    BowlingFactors = bowling,
                    FieldingFactors = fielding,
                    IsPlayerOfTheMatch = playerOfTheMatchId is Guid potm && potm == kv.Key
                };
            })
            .OrderByDescending(r => r.TotalPoints)
            .ThenByDescending(r => r.Wickets)
            .ThenByDescending(r => r.Runs)
            .ThenBy(r => r.Order)
            .ToList();

        for (var i = 0; i < rows.Count; i++)
            rows[i].Rank = i + 1;

        return rows;
    }

    /// <summary>Runs charged to the bowler; byes and leg-byes belong to the batting side.</summary>
    public static int BowlerRunsConceded(Delivery delivery)
        => delivery.RunsOffBat + (delivery.ExtraType is ExtraType.Bye or ExtraType.LegBye ? 0 : delivery.ExtraRuns);

    /// <summary>Legal-ball, run-less overs for one bowler (sequence order preserved).</summary>
    public static int CountMaidens(List<Delivery> bowled)
    {
        var maidens = 0;
        var legalInOver = 0;
        var runsInOver = 0;
        foreach (var d in bowled.OrderBy(x => x.Sequence))
        {
            runsInOver += BowlerRunsConceded(d);
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
}
