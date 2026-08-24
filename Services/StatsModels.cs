namespace JPL.Services;

/// <summary>Batter vs a specific bowler (or bowler vs batter) career confrontation.</summary>
public sealed class ConfrontationStat
{
    public Guid BatterId { get; init; }
    public string BatterName { get; init; } = string.Empty;
    public Guid BowlerId { get; init; }
    public string BowlerName { get; init; } = string.Empty;

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

    public double StrikeRate => Balls == 0 ? 0 : Math.Round(Runs * 100.0 / Balls, 1);
    public double Average => Outs == 0 ? 0 : Math.Round((double)Runs / Outs, 2);
    public string AverageText => Outs == 0 ? "-" : Average.ToString("0.##");

    public int RunsConcededTotal => Runs + ExtrasConceded;
    public double BowlingEconomy => Balls == 0 ? 0 : Math.Round(RunsConcededTotal * 6.0 / Balls, 2);
    public double BowlingStrikeRate => Outs == 0 ? 0 : Math.Round((double)Balls / Outs, 2);
    public string BowlingStrikeRateText => Outs == 0 ? "-" : BowlingStrikeRate.ToString("0.##");
}

/// <summary>Full leaderboard row: Match · Batting · Bowling · Fielding · Attendance.</summary>
public sealed class PlayerCareerSummary
{
    public Guid PlayerId { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    /// <summary>Completed nets sessions this player attended.</summary>
    public int Sessions { get; set; }
    /// <summary>1-based rank in the current sorted table. Not from the API.</summary>
    public int Rank { get; set; }
    /// <summary>Zebra stripe for the current table. Not from the API.</summary>
    public bool IsAltRow { get; set; }

    // Match
    public int Matches { get; set; }
    public int Won { get; set; }
    public int Loss { get; set; }
    public int Tied { get; set; }
    public int CaptainMatches { get; set; }
    public int CaptainWon { get; set; }
    public int CaptainLoss { get; set; }
    public int CaptainTied { get; set; }

    // Tournament
    public int Tournaments { get; set; }
    public int TournamentWon { get; set; }
    public int TournamentLoss { get; set; }
    public int TournamentTied { get; set; }
    public int CaptainTournaments { get; set; }
    public int CaptainTournamentWon { get; set; }
    public int CaptainTournamentLoss { get; set; }
    public int CaptainTournamentTied { get; set; }

    // Series
    public int Series { get; set; }
    public int SeriesWon { get; set; }
    public int SeriesLoss { get; set; }
    public int SeriesTied { get; set; }
    public int CaptainSeries { get; set; }
    public int CaptainSeriesWon { get; set; }
    public int CaptainSeriesLoss { get; set; }
    public int CaptainSeriesTied { get; set; }

    // Batting
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

    // Bowling
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

    // Fielding
    public int Catches { get; set; }
    public int RunOutsEffected { get; set; }
    public int Stumpings { get; set; }

    // Attendance (venue bookings)
    public int AttendanceTotal { get; set; }
    public int AttendancePresent { get; set; }
    public int AttendanceAbsent { get; set; }
    public double AttendancePct => AttendanceTotal == 0
        ? 0
        : Math.Round(AttendancePresent * 100.0 / AttendanceTotal, 1);
    public string AttendancePctText => AttendanceTotal == 0 ? "-" : AttendancePct.ToString("0.#") + "%";

    // Batting milestones
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
            if (HighestScoreBalls <= 0 && HighestScore > 0)
                return $"{HighestScore}{star}";
            return $"{HighestScore}({HighestScoreBalls}){star}";
        }
    }

    public double BattingStrikeRate => BallsFaced == 0 ? 0 : Math.Round(Runs * 100.0 / BallsFaced, 1);
    public string BattingStrikeRateText => BallsFaced == 0 ? "-" : BattingStrikeRate.ToString("0.#");
    /// <summary>Runs / dismissals. Never dismissed (all not-outs) → average = Runs (same as rankings).</summary>
    public double BattingAverage => Dismissals == 0
        ? Runs
        : Math.Round((double)Runs / Dismissals, 2);
    public string BattingAverageText =>
        InningsBatted <= 0 && BallsFaced == 0 && Runs == 0
            ? "-"
            : BattingAverage.ToString("0.##");
    /// <summary>Nets: runs per completed session attended.</summary>
    public double AvgPerSession => Sessions <= 0 ? 0 : Math.Round((double)Runs / Sessions, 2);
    public string AvgPerSessionText => Sessions <= 0 ? "-" : AvgPerSession.ToString("0.##");
    /// <summary>Nets: runs per dismissal.</summary>
    public double AvgPerOut => Dismissals <= 0 ? 0 : Math.Round((double)Runs / Dismissals, 2);
    public string AvgPerOutText => Dismissals <= 0 ? "-" : AvgPerOut.ToString("0.##");
    public double BowlingAverage => Wickets == 0 ? 0 : Math.Round((double)RunsConceded / Wickets, 2);
    public string BowlingAverageText => Wickets == 0 ? "-" : BowlingAverage.ToString("0.##");
    public double BowlingEconomy => OversBowledBalls == 0 ? 0 : Math.Round(RunsConceded * 6.0 / OversBowledBalls, 2);
    public string BowlingEconomyText => OversBowledBalls == 0 ? "-" : BowlingEconomy.ToString("0.##");
    public double BowlingStrikeRate => Wickets == 0 ? 0 : Math.Round((double)OversBowledBalls / Wickets, 2);
    public string BowlingStrikeRateText => Wickets == 0 ? "-" : BowlingStrikeRate.ToString("0.##");
}

public sealed class RankingRow
{
    public int Rank { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string PrimaryLabel { get; set; } = string.Empty;
    public string PrimaryValue { get; set; } = string.Empty;
    public string SecondaryLabel { get; set; } = string.Empty;
    public string SecondaryValue { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string RankText => $"#{Rank}";
    public string PrimaryLine => $"{PrimaryLabel} {PrimaryValue}";
    public string SecondaryLine => $"{SecondaryLabel} {SecondaryValue}";
    public RankingBreakdown? Breakdown { get; set; }

    /// <summary>0–1 share of the leader’s |pts| for the horizontal ranking bar.</summary>
    public double BarRatio { get; set; }
    public Color BarColor { get; set; } = Color.FromArgb("#1E88E5");
}

public sealed class RankingFactorLine
{
    public string Label { get; init; } = string.Empty;
    public string Stat { get; init; } = string.Empty;
    public string PointsText { get; init; } = string.Empty;
    public bool IsPositive { get; init; }
    public Color PointsColor => IsPositive ? Color.FromArgb("#15803D") : Color.FromArgb("#C62828");
}

public sealed class RankingBreakdown
{
    public string Title { get; init; } = string.Empty;
    public string Formula { get; init; } = string.Empty;
    public string NetText { get; init; } = string.Empty;
    public string PositiveTotalText { get; init; } = string.Empty;
    public string NegativeTotalText { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public IReadOnlyList<RankingFactorLine> Positive { get; init; } = Array.Empty<RankingFactorLine>();
    public IReadOnlyList<RankingFactorLine> Negative { get; init; } = Array.Empty<RankingFactorLine>();
    public bool HasPositive => Positive.Count > 0;
    public bool HasNegative => Negative.Count > 0;
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
}

public sealed class RankingsBundle
{
    public IReadOnlyList<RankingRow> Batting { get; set; } = Array.Empty<RankingRow>();
    public IReadOnlyList<RankingRow> Bowling { get; set; } = Array.Empty<RankingRow>();
    public IReadOnlyList<RankingRow> AllRounder { get; set; } = Array.Empty<RankingRow>();
    public IReadOnlyList<RankingRow> Fielder { get; set; } = Array.Empty<RankingRow>();
    public IReadOnlyList<RankingRow> Captain { get; set; } = Array.Empty<RankingRow>();
    public IReadOnlyList<RankingRow> Team { get; set; } = Array.Empty<RankingRow>();
}
