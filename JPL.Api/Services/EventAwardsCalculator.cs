using JPL.Api.Models;

namespace JPL.Api.Services;

/// <summary>Assigns tournament/series awards from event-aggregated ranking stats and rules.</summary>
public static class EventAwardsCalculator
{
    public sealed record Awards(
        Guid? PlayerOfEventId,
        Guid? BestBatsmanId,
        Guid? BestBowlerId,
        Guid? BestFielderId);

    private sealed class PlayerMatchTally
    {
        public Guid PlayerId { get; init; }
        public int Matches { get; set; }
        public int Runs { get; set; }
        public int BallsFaced { get; set; }
        public int Dots { get; set; }
        public int Fours { get; set; }
        public int Sixes { get; set; }
        public int Dismissals { get; set; }
        public int BowledOut { get; set; }
        public int CaughtOut { get; set; }
        public int LbwOut { get; set; }
        public int RunOut { get; set; }
        public int StumpedOut { get; set; }
        public int HitWicketOut { get; set; }
        public int Ducks { get; set; }
        public int GoldenDucks { get; set; }
        public int Fifties { get; set; }
        public int Hundreds { get; set; }
        public int NotOuts { get; set; }
        public int HighestScore { get; set; }
        public int InningsBatted { get; set; }

        public int OversBowledBalls { get; set; }
        public int RunsConceded { get; set; }
        public int Wickets { get; set; }
        public int DotBallsBowled { get; set; }
        public int WidesBowled { get; set; }
        public int NoBallsBowled { get; set; }
        public int ByesConceded { get; set; }
        public int LegByesConceded { get; set; }
        public int BowledWkts { get; set; }
        public int LbwWkts { get; set; }
        public int StumpedWkts { get; set; }
        public int HitWicketWkts { get; set; }
        public int Maidens { get; set; }
        public int HatTricks { get; set; }
        public int BestBowlingWickets { get; set; }
        public int BestBowlingRuns { get; set; }

        public int Catches { get; set; }
        public int RunOutsEffected { get; set; }
        public int Stumpings { get; set; }
        public int DroppedCatches { get; set; }

        public int BattingPoints => (int)Math.Round(BattingNet(this));
        public int BowlingPoints => (int)Math.Round(BowlingNet(this));
        public int FieldingPoints => (int)Math.Round(FieldingNet(this));
        public int TotalPoints => BattingPoints + BowlingPoints + FieldingPoints;
    }

    private sealed class EventTally
    {
        public PlayerMatchTally Stats { get; init; } = null!;
        public int Order { get; init; }
        public Guid PlayerId => Stats.PlayerId;
        public int BattingPoints => Stats.BattingPoints;
        public int BowlingPoints => Stats.BowlingPoints;
        public int FieldingPoints => Stats.FieldingPoints;
        public int TotalPoints => Stats.TotalPoints;
        public int Runs => Stats.Runs;
        public int Wickets => Stats.Wickets;
    }

    public static bool TryAssignTournament(Tournament tournament, IReadOnlyList<Delivery> deliveries)
    {
        if (tournament.Status != TournamentStatus.Completed || deliveries.Count == 0)
            return false;

        var awards = Compute(deliveries);
        var changed = tournament.PlayerOfTheTournamentPlayerId != awards.PlayerOfEventId
                      || tournament.BestBatsmanPlayerId != awards.BestBatsmanId
                      || tournament.BestBowlerPlayerId != awards.BestBowlerId
                      || tournament.BestFielderPlayerId != awards.BestFielderId;
        tournament.PlayerOfTheTournamentPlayerId = awards.PlayerOfEventId;
        tournament.BestBatsmanPlayerId = awards.BestBatsmanId;
        tournament.BestBowlerPlayerId = awards.BestBowlerId;
        tournament.BestFielderPlayerId = awards.BestFielderId;
        return changed;
    }

    public static bool TryAssignSeries(Series series, IReadOnlyList<Delivery> deliveries)
    {
        if (series.Status != SeriesStatus.Completed || deliveries.Count == 0)
            return false;

        var awards = Compute(deliveries);
        var changed = series.PlayerOfTheSeriesPlayerId != awards.PlayerOfEventId
                      || series.BestBatsmanPlayerId != awards.BestBatsmanId
                      || series.BestBowlerPlayerId != awards.BestBowlerId
                      || series.BestFielderPlayerId != awards.BestFielderId;
        series.PlayerOfTheSeriesPlayerId = awards.PlayerOfEventId;
        series.BestBatsmanPlayerId = awards.BestBatsmanId;
        series.BestBowlerPlayerId = awards.BestBowlerId;
        series.BestFielderPlayerId = awards.BestFielderId;
        return changed;
    }

    /// <summary>Aggregates raw match stats, then applies the same ranking formulas used by the app.</summary>
    public static Awards Compute(IReadOnlyList<Delivery> deliveries)
    {
        if (deliveries.Count == 0)
            return new Awards(null, null, null, null);

        var totals = new Dictionary<Guid, EventTally>();
        var order = 0;
        foreach (var match in deliveries.GroupBy(d => d.MatchId))
        {
            var matchRows = CalculateMatch(match.ToList());
            foreach (var row in matchRows)
            {
                if (!totals.TryGetValue(row.PlayerId, out var eventRow))
                {
                    eventRow = new EventTally { Stats = row, Order = order++ };
                    totals[row.PlayerId] = eventRow;
                }
                else
                {
                    Merge(eventRow.Stats, row);
                }
            }
        }

        if (totals.Count == 0)
            return new Awards(null, null, null, null);

        var overall = totals.Values
            .OrderByDescending(x => x.BattingPoints + x.BowlingPoints + x.FieldingPoints)
            .ThenByDescending(x => x.Wickets)
            .ThenByDescending(x => x.Runs)
            .ThenBy(x => x.Order)
            .First();
        var bestBat = totals.Values
            .OrderByDescending(x => x.BattingPoints)
            .ThenByDescending(x => x.BattingPoints + x.BowlingPoints + x.FieldingPoints)
            .ThenByDescending(x => x.Wickets)
            .ThenByDescending(x => x.Runs)
            .ThenBy(x => x.Order)
            .First();
        var bestBowl = totals.Values
            .OrderByDescending(x => x.BowlingPoints)
            .ThenByDescending(x => x.BattingPoints + x.BowlingPoints + x.FieldingPoints)
            .ThenByDescending(x => x.Wickets)
            .ThenByDescending(x => x.Runs)
            .ThenBy(x => x.Order)
            .First();
        var bestField = totals.Values
            .OrderByDescending(x => x.FieldingPoints)
            .ThenByDescending(x => x.BattingPoints + x.BowlingPoints + x.FieldingPoints)
            .ThenByDescending(x => x.Wickets)
            .ThenByDescending(x => x.Runs)
            .ThenBy(x => x.Order)
            .First();

        return new Awards(overall.PlayerId, bestBat.PlayerId, bestBowl.PlayerId, bestField.PlayerId);
    }

    private static IReadOnlyList<PlayerMatchTally> CalculateMatch(IReadOnlyList<Delivery> deliveries)
    {
        var players = new Dictionary<Guid, PlayerMatchTally>();
        PlayerMatchTally Get(Guid id)
        {
            if (id == Guid.Empty)
                return null!;
            if (players.TryGetValue(id, out var current))
                return current;
            current = new PlayerMatchTally { PlayerId = id };
            players[id] = current;
            return current;
        }

        foreach (var d in deliveries)
        {
            if (d.BatterId != Guid.Empty)
            {
                var batter = Get(d.BatterId);
                if (d.ExtraType == ExtraType.NoBall)
                {
                    // No-ball batter runs count, but not as a ball faced.
                }
                else if (d.ExtraType != ExtraType.Wide && d.IsLegalDelivery)
                {
                    batter.BallsFaced++;
                    if (d.RunsOffBat == 0 && d.ExtraRuns == 0 && !d.IsWicket)
                        batter.Dots++;
                }
                if (d.ExtraType != ExtraType.Wide)
                    batter.Runs += d.RunsOffBat;
                if (d.ExtraType != ExtraType.Wide && d.RunsOffBat == 4)
                    batter.Fours++;
                if (d.ExtraType != ExtraType.Wide && d.RunsOffBat >= 6)
                    batter.Sixes++;
            }

            if (d.IsWicket)
            {
                var dismissedId = d.DismissedPlayerId ?? d.BatterId;
                if (dismissedId != Guid.Empty)
                {
                    var batter = Get(dismissedId);
                    batter.Dismissals++;
                    switch (d.WicketType)
                    {
                        case WicketType.Bowled: batter.BowledOut++; break;
                        case WicketType.Caught: batter.CaughtOut++; break;
                        case WicketType.Lbw: batter.LbwOut++; break;
                        case WicketType.RunOut: batter.RunOut++; break;
                        case WicketType.Stumped: batter.StumpedOut++; break;
                        case WicketType.HitWicket: batter.HitWicketOut++; break;
                    }
                }
            }

            if (d.BowlerId != Guid.Empty)
            {
                var bowler = Get(d.BowlerId);
                switch (d.ExtraType)
                {
                    case ExtraType.Wide: bowler.WidesBowled += d.ExtraRuns; break;
                    case ExtraType.NoBall: bowler.NoBallsBowled += d.ExtraRuns; break;
                    case ExtraType.Bye: bowler.ByesConceded += d.ExtraRuns; break;
                    case ExtraType.LegBye: bowler.LegByesConceded += d.ExtraRuns; break;
                }
                if (d.IsLegalDelivery)
                {
                    bowler.OversBowledBalls++;
                    if (d.RunsOffBat == 0 && d.ExtraRuns == 0 && !d.IsWicket)
                        bowler.DotBallsBowled++;
                }
                bowler.RunsConceded += RunsChargedToBowler(d);
                if (d.IsWicket)
                {
                    switch (d.WicketType)
                    {
                        case WicketType.Bowled: bowler.BowledWkts++; bowler.Wickets++; break;
                        case WicketType.Caught: bowler.Wickets++; break;
                        case WicketType.Lbw: bowler.LbwWkts++; bowler.Wickets++; break;
                        case WicketType.Stumped: bowler.StumpedWkts++; bowler.Wickets++; break;
                        case WicketType.HitWicket: bowler.HitWicketWkts++; bowler.Wickets++; break;
                    }
                }
            }

            if (d.FielderId is Guid fielderId && fielderId != Guid.Empty)
            {
                var fielder = Get(fielderId);
                if (d.IsDroppedCatch)
                    fielder.DroppedCatches++;
                if (d.IsWicket)
                {
                    switch (d.WicketType)
                    {
                        case WicketType.Caught: fielder.Catches++; break;
                        case WicketType.RunOut: fielder.RunOutsEffected++; break;
                        case WicketType.Stumped: fielder.Stumpings++; break;
                    }
                }
            }
        }

        foreach (var innings in deliveries.GroupBy(d => d.InningsId))
        {
            var balls = innings.OrderBy(d => d.Sequence).ToList();
            foreach (var batterGroup in balls.Where(d => d.BatterId != Guid.Empty).GroupBy(d => d.BatterId))
            {
                var batter = Get(batterGroup.Key);
                var entries = batterGroup.ToList();
                var runs = entries.Where(d => d.ExtraType != ExtraType.Wide).Sum(d => d.RunsOffBat);
                var ballsFaced = entries.Count(d => d.IsLegalDelivery && d.ExtraType is not ExtraType.Wide and not ExtraType.NoBall);
                var dismissed = balls.Any(d => d.IsWicket && (d.DismissedPlayerId ?? d.BatterId) == batterGroup.Key);
                batter.InningsBatted++;
                if (!dismissed) batter.NotOuts++;
                batter.HighestScore = Math.Max(batter.HighestScore, runs);
                if (dismissed && runs == 0)
                {
                    batter.Ducks++;
                    if (ballsFaced == 1) batter.GoldenDucks++;
                }
                if (runs >= 100) batter.Hundreds++;
                else if (runs >= 50) batter.Fifties++;
            }

            foreach (var bowlerGroup in balls.Where(d => d.BowlerId != Guid.Empty).GroupBy(d => d.BowlerId))
            {
                var bowler = Get(bowlerGroup.Key);
                var spell = bowlerGroup.OrderBy(d => d.Sequence).ToList();
                bowler.Maidens += CountMaidens(spell);
                var wickets = spell.Count(d => IsBowlerWicket(d));
                var runs = spell.Sum(RunsChargedToBowler);
                if (wickets > bowler.BestBowlingWickets
                    || (wickets == bowler.BestBowlingWickets && wickets > 0 && runs < bowler.BestBowlingRuns))
                {
                    bowler.BestBowlingWickets = wickets;
                    bowler.BestBowlingRuns = runs;
                }
                var streak = 0;
                foreach (var d in spell)
                {
                    if (IsBowlerWicket(d))
                    {
                        if (++streak == 3)
                        {
                            bowler.HatTricks++;
                            streak = 0;
                        }
                    }
                    else if (d.IsLegalDelivery)
                    {
                        streak = 0;
                    }
                }
            }
        }

        foreach (var player in players.Values)
            player.Matches = 1;
        return players.Values.ToList();
    }

    private static void Merge(PlayerMatchTally total, PlayerMatchTally match)
    {
        total.Matches += match.Matches;
        total.Runs += match.Runs;
        total.BallsFaced += match.BallsFaced;
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
        total.Ducks += match.Ducks;
        total.GoldenDucks += match.GoldenDucks;
        total.Fifties += match.Fifties;
        total.Hundreds += match.Hundreds;
        total.NotOuts += match.NotOuts;
        total.HighestScore = Math.Max(total.HighestScore, match.HighestScore);
        total.InningsBatted += match.InningsBatted;
        total.OversBowledBalls += match.OversBowledBalls;
        total.RunsConceded += match.RunsConceded;
        total.Wickets += match.Wickets;
        total.DotBallsBowled += match.DotBallsBowled;
        total.WidesBowled += match.WidesBowled;
        total.NoBallsBowled += match.NoBallsBowled;
        total.ByesConceded += match.ByesConceded;
        total.LegByesConceded += match.LegByesConceded;
        total.BowledWkts += match.BowledWkts;
        total.LbwWkts += match.LbwWkts;
        total.StumpedWkts += match.StumpedWkts;
        total.HitWicketWkts += match.HitWicketWkts;
        total.Maidens += match.Maidens;
        total.HatTricks += match.HatTricks;
        if (match.BestBowlingWickets > total.BestBowlingWickets
            || (match.BestBowlingWickets == total.BestBowlingWickets
                && match.BestBowlingWickets > 0 && match.BestBowlingRuns < total.BestBowlingRuns))
        {
            total.BestBowlingWickets = match.BestBowlingWickets;
            total.BestBowlingRuns = match.BestBowlingRuns;
        }
        total.Catches += match.Catches;
        total.RunOutsEffected += match.RunOutsEffected;
        total.Stumpings += match.Stumpings;
        total.DroppedCatches += match.DroppedCatches;
    }

    private static bool IsBowlerWicket(Delivery d)
        => d.IsWicket && d.WicketType is not WicketType.RunOut and not WicketType.Retired and not WicketType.None;

    private static int CountMaidens(IReadOnlyList<Delivery> bowled)
    {
        var maidens = 0;
        var legalInOver = 0;
        var runsInOver = 0;
        foreach (var d in bowled)
        {
            runsInOver += RunsChargedToBowler(d);
            if (!d.IsLegalDelivery) continue;
            legalInOver++;
            if (legalInOver < 6) continue;
            if (runsInOver == 0) maidens++;
            legalInOver = 0;
            runsInOver = 0;
        }
        return maidens;
    }

    private static int RunsChargedToBowler(Delivery delivery)
        => delivery.RunsOffBat + (delivery.ExtraType is ExtraType.Bye or ExtraType.LegBye ? 0 : delivery.ExtraRuns);

    private static double BattingNet(PlayerMatchTally p)
    {
        if (p.BallsFaced <= 0 && p.Runs <= 0) return 0;
        var total = p.Runs + p.Fours * 4 + p.Sixes * 6 + p.Fifties * 12 + p.Hundreds * 25
                    + p.NotOuts * 4 + p.HighestScore * 0.5
                    + p.Dots * -0.2 + p.Ducks * -10 + p.GoldenDucks * -15
                    + (p.RunOut + p.StumpedOut + p.HitWicketOut + p.BowledOut + p.LbwOut + p.CaughtOut) * -5;
        if (p.BallsFaced >= 6)
        {
            var strikeRate = p.Runs * 100.0 / p.BallsFaced;
            total += (strikeRate - 100) * p.BallsFaced / 200.0;
        }
        if (p.InningsBatted >= 2)
        {
            var average = p.Dismissals == 0 ? p.Runs : (double)p.Runs / p.Dismissals;
            total += (average - 10) * Math.Min(p.InningsBatted, 15) * 0.20;
        }
        return total;
    }

    private static double BowlingNet(PlayerMatchTally p)
    {
        if (p.Wickets <= 0 && p.OversBowledBalls <= 0) return 0;
        var overs = p.OversBowledBalls / 6.0;
        var total = p.Wickets * 25 + p.BowledWkts * 5 + p.Maidens * 8 + p.DotBallsBowled * 0.35
                    + p.HatTricks * 20 + p.BestBowlingWickets * 2;
        if (p.OversBowledBalls >= 6)
        {
            var economy = p.RunsConceded * 6.0 / p.OversBowledBalls;
            total += (8.0 - economy) * overs;
            if (p.Wickets > 0)
            {
                var strikeRate = p.OversBowledBalls / (double)p.Wickets;
                total += (18.0 - strikeRate) * p.Wickets * 0.3;
            }
        }
        total += p.WidesBowled * -1 + p.NoBallsBowled * -1
                 + p.ByesConceded * -0.2 + p.LegByesConceded * -0.2;
        return total;
    }

    private static double FieldingNet(PlayerMatchTally p)
    {
        if (p.Catches + p.RunOutsEffected + p.Stumpings <= 0 && p.DroppedCatches <= 0)
            return 0;
        return p.Catches * 10 + p.RunOutsEffected * 12 + p.Stumpings * 15 + p.DroppedCatches * -10;
    }
}
