using JPL.Api.Models;

namespace JPL.Api.Services;

/// <summary>
/// Auto awards for a completed tournament or series — same spirit as Player of the Match,
/// aggregated across every linked completed match.
/// </summary>
public static class EventAwardsCalculator
{
    public sealed record Awards(
        Guid? PlayerOfEventId,
        Guid? BestBatsmanId,
        Guid? BestBowlerId,
        Guid? BestFielderId);

    /// <summary>
    /// Assigns awards once when the tournament is completed and Player of the Tournament
    /// has not been set yet (also backfills older completed events).
    /// </summary>
    public static bool TryAssignTournament(Tournament tournament, IReadOnlyList<Delivery> deliveries)
    {
        if (tournament.Status != TournamentStatus.Completed)
            return false;
        if (tournament.PlayerOfTheTournamentPlayerId is Guid)
            return false;
        if (deliveries.Count == 0)
            return false;

        var awards = Compute(deliveries);
        tournament.PlayerOfTheTournamentPlayerId = awards.PlayerOfEventId;
        tournament.BestBatsmanPlayerId = awards.BestBatsmanId;
        tournament.BestBowlerPlayerId = awards.BestBowlerId;
        tournament.BestFielderPlayerId = awards.BestFielderId;
        return awards.PlayerOfEventId is Guid
               || awards.BestBatsmanId is Guid
               || awards.BestBowlerId is Guid
               || awards.BestFielderId is Guid;
    }

    /// <summary>
    /// Assigns awards once when the series is completed and Player of the Series
    /// has not been set yet (also backfills older completed series).
    /// </summary>
    public static bool TryAssignSeries(Series series, IReadOnlyList<Delivery> deliveries)
    {
        if (series.Status != SeriesStatus.Completed)
            return false;
        if (series.PlayerOfTheSeriesPlayerId is Guid)
            return false;
        if (deliveries.Count == 0)
            return false;

        var awards = Compute(deliveries);
        series.PlayerOfTheSeriesPlayerId = awards.PlayerOfEventId;
        series.BestBatsmanPlayerId = awards.BestBatsmanId;
        series.BestBowlerPlayerId = awards.BestBowlerId;
        series.BestFielderPlayerId = awards.BestFielderId;
        return awards.PlayerOfEventId is Guid
               || awards.BestBatsmanId is Guid
               || awards.BestBowlerId is Guid
               || awards.BestFielderId is Guid;
    }

    public static Awards Compute(IReadOnlyList<Delivery> deliveries)
    {
        if (deliveries.Count == 0)
            return new Awards(null, null, null, null);

        var batRuns = new Dictionary<Guid, int>();
        var batBalls = new Dictionary<Guid, int>();
        var bowlWkts = new Dictionary<Guid, int>();
        var bowlRuns = new Dictionary<Guid, int>();
        var bowlBalls = new Dictionary<Guid, int>();
        var fieldPts = new Dictionary<Guid, int>();
        var overall = new Dictionary<Guid, double>();

        void Touch(Dictionary<Guid, int> map, Guid id, int add)
        {
            if (id == Guid.Empty) return;
            map[id] = map.GetValueOrDefault(id) + add;
        }

        void TouchD(Dictionary<Guid, double> map, Guid id, double add)
        {
            if (id == Guid.Empty) return;
            map[id] = map.GetValueOrDefault(id) + add;
        }

        foreach (var d in deliveries)
        {
            Touch(batRuns, d.BatterId, d.RunsOffBat);
            TouchD(overall, d.BatterId, d.RunsOffBat);
            if (d.RunsOffBat == 4) TouchD(overall, d.BatterId, 1);
            if (d.RunsOffBat == 6) TouchD(overall, d.BatterId, 2);
            if (d.IsLegalDelivery && d.ExtraType != ExtraType.Wide)
                Touch(batBalls, d.BatterId, 1);

            Touch(bowlRuns, d.BowlerId, d.RunsOffBat + d.ExtraRuns);
            if (d.IsLegalDelivery)
                Touch(bowlBalls, d.BowlerId, 1);

            var bowlerWicket = d.IsWicket
                               && d.WicketType is not WicketType.RunOut
                                   and not WicketType.Retired
                                   and not WicketType.None;
            if (bowlerWicket)
            {
                Touch(bowlWkts, d.BowlerId, 1);
                TouchD(overall, d.BowlerId, 25);
            }

            if (d.IsWicket && d.FielderId is Guid fielderId
                && d.WicketType is WicketType.Caught or WicketType.RunOut or WicketType.Stumped)
            {
                var pts = d.WicketType == WicketType.Caught ? 12 : 10;
                Touch(fieldPts, fielderId, pts);
                TouchD(overall, fielderId, pts);
            }
        }

        // Maiden overs (per bowler, chronological).
        foreach (var group in deliveries.GroupBy(d => d.BowlerId))
        {
            var maidens = CountMaidens(group.OrderBy(d => d.Sequence).ToList());
            if (maidens > 0)
                TouchD(overall, group.Key, maidens * 8);
        }

        var bestBat = batRuns
            .OrderByDescending(kv => kv.Value)
            .ThenByDescending(kv =>
            {
                var balls = batBalls.GetValueOrDefault(kv.Key);
                return balls <= 0 ? 0 : kv.Value * 100.0 / balls;
            })
            .ThenBy(kv => kv.Key)
            .Select(kv => (Guid?)kv.Key)
            .FirstOrDefault();

        var bestBowl = bowlWkts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => bowlRuns.GetValueOrDefault(kv.Key))
            .ThenByDescending(kv => bowlBalls.GetValueOrDefault(kv.Key))
            .ThenBy(kv => kv.Key)
            .Select(kv => (Guid?)kv.Key)
            .FirstOrDefault();

        var bestField = fieldPts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv => (Guid?)kv.Key)
            .FirstOrDefault();

        var pot = overall
            .OrderByDescending(kv => kv.Value)
            .ThenByDescending(kv => bowlWkts.GetValueOrDefault(kv.Key))
            .ThenByDescending(kv => batRuns.GetValueOrDefault(kv.Key))
            .ThenBy(kv => kv.Key)
            .Select(kv => (Guid?)kv.Key)
            .FirstOrDefault();

        return new Awards(pot, bestBat, bestBowl, bestField);
    }

    private static int CountMaidens(List<Delivery> bowled)
    {
        var maidens = 0;
        var legalInOver = 0;
        var runsInOver = 0;
        foreach (var d in bowled)
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
}
