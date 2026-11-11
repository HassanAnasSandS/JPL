namespace JPL.Api.Services;

internal static class StatsListSort
{
    public static List<PlayerCareerDto> OrderPlayers(
        IEnumerable<PlayerCareerDto> source,
        string? column,
        bool ascending)
    {
        var key = string.IsNullOrWhiteSpace(column) ? "PlayerName" : column;
        IOrderedEnumerable<PlayerCareerDto> ordered = ascending
            ? source.OrderBy(r => PlayerKey(r, key), Comparer<object?>.Create(CompareKeys))
            : source.OrderByDescending(r => PlayerKey(r, key), Comparer<object?>.Create(CompareKeys));
        var list = ordered.ThenBy(r => r.PlayerName, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < list.Count; i++)
            list[i].Rank = i + 1;
        return list;
    }

    public static List<TeamCareerDto> OrderTeams(
        IEnumerable<TeamCareerDto> source,
        string? column,
        bool ascending)
    {
        var key = string.IsNullOrWhiteSpace(column) ? "TeamName" : column;
        IOrderedEnumerable<TeamCareerDto> ordered = ascending
            ? source.OrderBy(r => TeamKey(r, key), Comparer<object?>.Create(CompareKeys))
            : source.OrderByDescending(r => TeamKey(r, key), Comparer<object?>.Create(CompareKeys));
        var list = ordered.ThenBy(r => r.TeamName, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < list.Count; i++)
            list[i].Rank = i + 1;
        return list;
    }

    public static bool Ascending(string? dir)
        => string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase)
           || string.Equals(dir, "ascending", StringComparison.OrdinalIgnoreCase);

    private static int CompareKeys(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        if (a is IComparable ca && a.GetType() == b.GetType())
            return ca.CompareTo(b);
        return string.CompareOrdinal(a.ToString(), b.ToString());
    }

    private static object PlayerKey(PlayerCareerDto r, string column) => column switch
    {
        "PlayerName" => r.PlayerName,
        "Sessions" => r.Sessions,
        "Matches" => r.Matches,
        "Won" => r.Won,
        "Loss" => r.Loss,
        "Tied" => r.Tied,
        "MatchWinPct" => r.Matches == 0 ? double.MinValue : (r.Won + r.Tied * 0.5) / r.Matches,
        "CaptainMatches" => r.CaptainMatches,
        "CaptainWon" => r.CaptainWon,
        "CaptainLoss" => r.CaptainLoss,
        "CaptainTied" => r.CaptainTied,
        "CaptainWinPct" => r.CaptainMatches == 0
            ? double.MinValue
            : (r.CaptainWon + r.CaptainTied * 0.5) / r.CaptainMatches,
        "CaptainPoints" => r.CaptainPoints,
        "CaptainNrr" => r.CaptainMatches == 0 ? double.MinValue : r.CaptainNrr,
        "CaptainRunsFor" => r.CaptainRunsFor,
        "CaptainRunsAgainst" => r.CaptainRunsAgainst,
        "Tournaments" => r.Tournaments,
        "TournamentWon" => r.TournamentWon,
        "TournamentLoss" => r.TournamentLoss,
        "TournamentTied" => r.TournamentTied,
        "TournamentWinPct" => r.Tournaments == 0
            ? double.MinValue
            : (r.TournamentWon + r.TournamentTied * 0.5) / r.Tournaments,
        "CaptainTournaments" => r.CaptainTournaments,
        "CaptainTournamentWon" => r.CaptainTournamentWon,
        "CaptainTournamentLoss" => r.CaptainTournamentLoss,
        "CaptainTournamentTied" => r.CaptainTournamentTied,
        "CaptainTournamentWinPct" => r.CaptainTournaments == 0
            ? double.MinValue
            : (r.CaptainTournamentWon + r.CaptainTournamentTied * 0.5) / r.CaptainTournaments,
        "Series" => r.Series,
        "SeriesWon" => r.SeriesWon,
        "SeriesLoss" => r.SeriesLoss,
        "SeriesTied" => r.SeriesTied,
        "SeriesWinPct" => r.Series == 0
            ? double.MinValue
            : (r.SeriesWon + r.SeriesTied * 0.5) / r.Series,
        "CaptainSeries" => r.CaptainSeries,
        "CaptainSeriesWon" => r.CaptainSeriesWon,
        "CaptainSeriesLoss" => r.CaptainSeriesLoss,
        "CaptainSeriesTied" => r.CaptainSeriesTied,
        "CaptainSeriesWinPct" => r.CaptainSeries == 0
            ? double.MinValue
            : (r.CaptainSeriesWon + r.CaptainSeriesTied * 0.5) / r.CaptainSeries,
        "InningsBatted" => r.InningsBatted,
        "InningsBowled" => r.InningsBowled,
        "BallsFaced" => r.BallsFaced,
        "Runs" => r.Runs,
        "Dots" => r.Dots,
        "Ones" => r.Ones,
        "Twos" => r.Twos,
        "Threes" => r.Threes,
        "Fours" => r.Fours,
        "Fives" => r.Fives,
        "Sixes" => r.Sixes,
        "WidesFaced" => r.WidesFaced,
        "NoBallsFaced" => r.NoBallsFaced,
        "Dismissals" => r.Dismissals,
        "NotOuts" => r.NotOuts,
        "Fifties" => r.Fifties,
        "Hundreds" => r.Hundreds,
        "BoundaryPct" => r.Runs <= 0 ? double.MinValue : (r.Fours * 4.0 + r.Sixes * 6.0) / r.Runs,
        "BowledOut" => r.BowledOut,
        "CaughtOut" => r.CaughtOut,
        "LbwOut" => r.LbwOut,
        "RunOut" => r.RunOut,
        "PartnerRunOuts" => r.PartnerRunOuts,
        "StumpedOut" => r.StumpedOut,
        "HitWicketOut" => r.HitWicketOut,
        "BattingStrikeRate" => r.BallsFaced == 0 ? double.MinValue : r.Runs * 100.0 / r.BallsFaced,
        "BattingAverage" => r.Dismissals == 0 ? r.Runs : (double)r.Runs / r.Dismissals,
        "AvgPerSession" => r.Sessions <= 0 ? double.MinValue : (double)r.Runs / r.Sessions,
        "AvgPerOut" => r.Dismissals <= 0 ? double.MinValue : (double)r.Runs / r.Dismissals,
        "HighestScore" => r.InningsBatted <= 0
            ? (int.MinValue, 0)
            : (r.HighestScore, r.HighestScoreNotOut ? 1 : 0),
        "Ducks" => r.Ducks,
        "GoldenDucks" => r.GoldenDucks,
        "OversBowled" => r.OversBowledBalls,
        "OversBowledBalls" => r.OversBowledBalls,
        "RunsConceded" => r.RunsConceded,
        "Wickets" => r.Wickets,
        "BestBowling" => r.BestBowlingText == "-"
            ? (int.MinValue, 0)
            : (r.BestBowlingWickets, -r.BestBowlingRuns),
        "FiveWicketHauls" => r.FiveWicketHauls,
        "Maidens" => r.Maidens,
        "HatTricks" => r.HatTricks,
        "DuckWkts" => r.DuckWkts,
        "GoldenDuckWkts" => r.GoldenDuckWkts,
        "DotBallsBowled" => r.DotBallsBowled,
        "FoursConceded" => r.FoursConceded,
        "SixesConceded" => r.SixesConceded,
        "WidesBowled" => r.WidesBowled,
        "NoBallsBowled" => r.NoBallsBowled,
        "ByesConceded" => r.ByesConceded,
        "LegByesConceded" => r.LegByesConceded,
        "BowledWkts" => r.BowledWkts,
        "CaughtWkts" => r.CaughtWkts,
        "LbwWkts" => r.LbwWkts,
        "StumpedWkts" => r.StumpedWkts,
        "HitWicketWkts" => r.HitWicketWkts,
        "RunOutWkts" => r.RunOutWkts,
        "BowlingStrikeRate" => r.Wickets == 0 ? double.MaxValue : r.OversBowledBalls / (double)r.Wickets,
        "BowlingAverage" => r.Wickets == 0 ? double.MaxValue : r.RunsConceded / (double)r.Wickets,
        "BowlingEconomy" => r.OversBowledBalls == 0 ? double.MaxValue : r.RunsConceded * 6.0 / r.OversBowledBalls,
        "Catches" => r.Catches,
        "RunOutsEffected" => r.RunOutsEffected,
        "Stumpings" => r.Stumpings,
        "FieldingTotal" => r.Catches + r.RunOutsEffected + r.Stumpings,
        "FieldingPerMatch" => r.Matches == 0
            ? double.MinValue
            : (r.Catches + r.RunOutsEffected + r.Stumpings) / (double)r.Matches,
        "DroppedCatches" => r.DroppedCatches,
        "AttendanceTotal" => r.AttendanceTotal,
        "AttendancePresent" => r.AttendancePresent,
        "AttendanceAbsent" => r.AttendanceAbsent,
        "AttendancePct" => r.AttendanceTotal == 0 ? double.MinValue : r.AttendancePresent * 100.0 / r.AttendanceTotal,
        "PaymentTotal" => r.PaymentTotal,
        "PaymentPaid" => r.PaymentPaid,
        "PaymentRemaining" => r.PaymentRemaining,
        "PaymentPaidPct" => r.PaymentTotal <= 0
            ? double.MinValue
            : (double)(r.PaymentPaid / r.PaymentTotal) * 100,
        _ => r.PlayerName
    };

    private static object TeamKey(TeamCareerDto r, string column) => column switch
    {
        "TeamName" => r.TeamName,
        "Played" => r.Played,
        "Won" => r.Won,
        "Lost" => r.Lost,
        "Tied" => r.Tied,
        "Points" => r.Points,
        "WinPct" => r.Played == 0 ? double.MinValue : r.WinPct,
        "Nrr" => r.Played == 0 ? double.MinValue : r.Nrr,
        "RunsFor" => r.RunsFor,
        "RunsAgainst" => r.RunsAgainst,
        "Tournaments" => r.Tournaments,
        "TournamentWon" => r.TournamentWon,
        "TournamentLoss" => r.TournamentLoss,
        "TournamentTied" => r.TournamentTied,
        "TournamentWinPct" => r.Tournaments == 0
            ? double.MinValue
            : (r.TournamentWon + r.TournamentTied * 0.5) / r.Tournaments,
        "Series" => r.Series,
        "SeriesWon" => r.SeriesWon,
        "SeriesLoss" => r.SeriesLoss,
        "SeriesTied" => r.SeriesTied,
        "SeriesWinPct" => r.Series == 0
            ? double.MinValue
            : (r.SeriesWon + r.SeriesTied * 0.5) / r.Series,
        _ => r.TeamName
    };
}
