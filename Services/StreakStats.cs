namespace JPL.Services;

/// <summary>Win/loss streak helpers shared by Statistics Match / Tournament / Series.</summary>
public static class StreakStats
{
    /// <summary>1 = win, -1 = loss, 0 = tie (breaks both streaks).</summary>
    public static (int BestWin, int CurrentWin, int WorstLoss, int CurrentLoss) Compute(
        IEnumerable<(DateTime Date, Guid Id, int Outcome)> rows)
        => Compute(rows.Select(r => (r.Date, r.Date, r.Id, r.Outcome)));

    /// <summary>1 = win, -1 = loss, 0 = tie. Order: date → created → id.</summary>
    public static (int BestWin, int CurrentWin, int WorstLoss, int CurrentLoss) Compute(
        IEnumerable<(DateTime Date, DateTime Created, Guid Id, int Outcome)> rows)
    {
        var ordered = rows
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Created)
            .ThenBy(r => r.Id)
            .ToList();
        if (ordered.Count == 0)
            return (0, 0, 0, 0);

        var bestWin = 0;
        var worstLoss = 0;
        var runWin = 0;
        var runLoss = 0;
        foreach (var row in ordered)
        {
            if (row.Outcome > 0)
            {
                runWin++;
                runLoss = 0;
                if (runWin > bestWin)
                    bestWin = runWin;
            }
            else if (row.Outcome < 0)
            {
                runLoss++;
                runWin = 0;
                if (runLoss > worstLoss)
                    worstLoss = runLoss;
            }
            else
            {
                runWin = 0;
                runLoss = 0;
            }
        }

        var currentWin = 0;
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            if (ordered[i].Outcome <= 0)
                break;
            currentWin++;
        }

        var currentLoss = 0;
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            if (ordered[i].Outcome >= 0)
                break;
            currentLoss++;
        }

        return (bestWin, currentWin, worstLoss, currentLoss);
    }

    /// <summary>
    /// Known result only. Null = skip (unknown team) — must NOT be treated as a tie
    /// or it falsely breaks winning/losing streaks.
    /// </summary>
    public static int? TryMatchOutcome(ApiMatchSummaryDto match)
    {
        if (match.PlayerTeamId is not Guid teamId)
            return null;
        if (match.WinnerTeamId is not Guid winnerId)
            return 0; // true tie / no result with equal scores
        return winnerId == teamId ? 1 : -1;
    }

    public static int MatchOutcome(ApiMatchSummaryDto match)
        => TryMatchOutcome(match) ?? 0;

    /// <summary>
    /// Always recompute from match history while hosted API streak fields may be missing/stale.
    /// </summary>
    public static bool NeedsMatchStreakFill(PlayerCareerSummary row)
        => row.Won + row.Loss > 0;

    public static bool NeedsTournamentStreakFill(PlayerCareerSummary row)
        => row.TournamentWon + row.TournamentLoss > 0
           && row.BestTournamentWinningStreak == 0
           && row.CurrentTournamentWinningStreak == 0
           && row.WorstTournamentLosingStreak == 0
           && row.CurrentTournamentLosingStreak == 0;

    public static bool NeedsSeriesStreakFill(PlayerCareerSummary row)
        => row.SeriesWon + row.SeriesLoss > 0
           && row.BestSeriesWinningStreak == 0
           && row.CurrentSeriesWinningStreak == 0
           && row.WorstSeriesLosingStreak == 0
           && row.CurrentSeriesLosingStreak == 0;

    public static void ApplyMatchStreaks(PlayerCareerSummary row, IEnumerable<ApiMatchSummaryDto> matches)
    {
        var outcomes = new List<(DateTime Date, DateTime Created, Guid Id, int Outcome)>();
        foreach (var m in matches)
        {
            var outcome = TryMatchOutcome(m);
            if (outcome is null)
                continue; // unknown side — omit, do not break streak as a fake tie
            outcomes.Add((m.MatchDateUtc, m.MatchDateUtc, m.Id, outcome.Value));
        }

        var (bestWin, currentWin, worstLoss, currentLoss) = Compute(outcomes);
        row.BestWinningStreak = bestWin;
        row.CurrentWinningStreak = currentWin;
        row.WorstLosingStreak = worstLoss;
        row.CurrentLosingStreak = currentLoss;
    }
}
