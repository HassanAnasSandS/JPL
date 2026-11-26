using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JPL.Services;

/// <summary>One completed Nets session with its four automatically calculated awards.</summary>
public sealed class NetSessionAwardsRow
{
    public Guid SessionId { get; set; }
    public string SessionName { get; set; } = string.Empty;
    public DateTime SessionDateUtc { get; set; }
    public Guid? PlayerOfTheSessionPlayerId { get; set; }
    public string? PlayerOfTheSessionName { get; set; }
    public Guid? BestBatsmanPlayerId { get; set; }
    public string? BestBatsmanName { get; set; }
    public Guid? BestBowlerPlayerId { get; set; }
    public string? BestBowlerName { get; set; }
    public Guid? BestFielderPlayerId { get; set; }
    public string? BestFielderName { get; set; }
    public bool IsAltRow { get; set; }
    public string SessionDisplay => string.IsNullOrWhiteSpace(SessionName)
        ? SessionDateUtc.ToLocalTime().ToString("dd MMM yyyy")
        : $"{SessionName} · {SessionDateUtc.ToLocalTime():dd MMM yyyy}";
    public string PlayerOfTheSessionDisplay => PlayerOfTheSessionName ?? "—";
    public string BestBatsmanDisplay => BestBatsmanName ?? "—";
    public string BestBowlerDisplay => BestBowlerName ?? "—";
    public string BestFielderDisplay => BestFielderName ?? "—";
}

public sealed partial class PlayerTournamentHistoryItem : ObservableObject
{
    public Guid TournamentId { get; init; }
    public string TournamentName { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public ObservableCollection<ApiMatchSummaryDto> Matches { get; } = new();
    public int MatchCount => Matches.Count;
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

public sealed class PlayerSeriesHistoryItem : ObservableObject
{
    public Guid SeriesId { get; init; }
    public string SeriesName { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string TeamAName { get; init; } = string.Empty;
    public string TeamBName { get; init; } = string.Empty;
    public int TeamAWins { get; init; }
    public int TeamBWins { get; init; }
    public string SummaryText => $"{TeamAName} {TeamAWins}–{TeamBWins} {TeamBName}";
    public ObservableCollection<ApiMatchSummaryDto> Matches { get; } = new();
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

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
    public int Bowled { get; set; }
    public int Caught { get; set; }
    public int Lbw { get; set; }
    public int RunOut { get; set; }
    public int Stumped { get; set; }
    public int HitWicket { get; set; }
    public int ExtrasConceded { get; set; }

    public double StrikeRate => Balls == 0 ? 0 : Math.Round(Runs * 100.0 / Balls, 1);
    public double Average => Outs == 0 ? 0 : Math.Round((double)Runs / Outs, 2);
    public string AverageText => Outs == 0 ? "-" : Average.ToString("0.##");

    public int RunsConcededTotal => Runs + ExtrasConceded;
    public double BowlingEconomy => Balls == 0 ? 0 : Math.Round(RunsConcededTotal * 6.0 / Balls, 2);
    public double BowlingStrikeRate => Outs == 0 ? 0 : Math.Round((double)Balls / Outs, 2);
    public string BowlingStrikeRateText => Outs == 0 ? "-" : BowlingStrikeRate.ToString("0.##");
    public bool HasWicketTypes => Outs > 0;
}

/// <summary>This fielder vs a batter they dismissed (or dropped against).</summary>
public sealed class FieldingVsStat
{
    public Guid FielderId { get; set; }
    public string FielderName { get; set; } = string.Empty;
    public Guid BatterId { get; set; }
    public string BatterName { get; set; } = string.Empty;
    public int Catches { get; set; }
    public int RunOuts { get; set; }
    public int Stumpings { get; set; }
    public int DroppedCatches { get; set; }
    public int Total => Catches + RunOuts + Stumpings;
}

/// <summary>This captain vs an opposing captain (player 1 vs 1 · Captaincy).</summary>
public sealed class CaptainVsStat
{
    public Guid CaptainId { get; set; }
    public string CaptainName { get; set; } = string.Empty;
    public Guid OpponentCaptainId { get; set; }
    public string OpponentCaptainName { get; set; } = string.Empty;

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

    public int Points => Won * 2 + Tied;
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
    public bool HasSeries => Series > 0;
    public string SeriesRecordText => Series == 0
        ? "-"
        : $"{SeriesWon}–{SeriesLoss}–{SeriesTied}";
    public double SeriesWinPct => Series == 0
        ? 0
        : Math.Round(((SeriesWon + SeriesTied * 0.5) / Series) * 100, 1);
    public string SeriesWinPctText => Series == 0 ? "-" : SeriesWinPct.ToString("0.#") + "%";
}

public sealed class PartnershipStat
{
    public int Rank { get; set; }
    public bool IsAltRow { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public Guid PartnerId { get; set; }
    public string PartnerName { get; set; } = string.Empty;
    public string PairName => $"{PlayerName} & {PartnerName}";
    public int PairRowHeight
    {
        get
        {
            var longestName = Math.Max(PlayerName.Length, PartnerName.Length);
            var playerRowHeight = Math.Max(44, (int)Math.Ceiling(longestName / 24d) * 20 + 4);
            return playerRowHeight * 2;
        }
    }
    public int Innings { get; set; }
    public int Runs { get; set; }
    public int Balls { get; set; }
    public int Outs { get; set; }
    public int PlayerRuns { get; set; }
    public int PlayerBalls { get; set; }
    public int PartnerRuns { get; set; }
    public int PartnerBalls { get; set; }
    public int PlayerOuts { get; set; }
    public int PartnerOuts { get; set; }
    public int NotOut { get; set; }
    public double Average => Outs == 0 ? 0 : Math.Round((double)Runs / Outs, 1);
    public string AverageText => Outs == 0 ? "-" : Average.ToString("0.0");
    public double PartnershipStrikeRate => Balls == 0 ? 0 : Math.Round(Runs * 100d / Balls, 1);
    public double PlayerAverage => PlayerOuts == 0 ? PlayerRuns : Math.Round((double)PlayerRuns / PlayerOuts, 1);
    public double PartnerAverage => PartnerOuts == 0 ? PartnerRuns : Math.Round((double)PartnerRuns / PartnerOuts, 1);
    public double PlayerStrikeRate => PlayerBalls == 0 ? 0 : Math.Round(PlayerRuns * 100d / PlayerBalls, 1);
    public double PartnerStrikeRate => PartnerBalls == 0 ? 0 : Math.Round(PartnerRuns * 100d / PartnerBalls, 1);
    public string PlayerAverageText => PlayerOuts == 0 ? "-" : PlayerAverage.ToString("0.0");
    public string PartnerAverageText => PartnerOuts == 0 ? "-" : PartnerAverage.ToString("0.0");
    public string PlayerContributionText => $"{PlayerName}: {PlayerRuns} runs · {PlayerBalls} balls · SR {PlayerStrikeRate:0.0} · Avg {PlayerAverageText} · Out {PlayerOuts}";
    public string PartnerContributionText => $"{PartnerName}: {PartnerRuns} runs · {PartnerBalls} balls · SR {PartnerStrikeRate:0.0} · Avg {PartnerAverageText} · Out {PartnerOuts}";
}

public sealed class PartnershipMatchStat
{
    public Guid MatchId { get; set; }
    public string MatchName { get; set; } = string.Empty;
    public DateTime MatchDateUtc { get; set; }
    public int InningsNumber { get; set; }
    public string BattingTeamName { get; set; } = string.Empty;
    public int Runs { get; set; }
    public int Balls { get; set; }
    public int Outs { get; set; }
    public int PlayerRuns { get; set; }
    public int PlayerBalls { get; set; }
    public int PlayerOuts { get; set; }
    public int PartnerRuns { get; set; }
    public int PartnerBalls { get; set; }
    public int PartnerOuts { get; set; }
    public double Average => Outs == 0 ? 0 : Math.Round((double)Runs / Outs, 1);
    public string AverageText => Outs == 0 ? "-" : Average.ToString("0.0");
    public double StrikeRate => Balls == 0 ? 0 : Math.Round(Runs * 100d / Balls, 1);
    public string StrikeRateText => StrikeRate.ToString("0.0");
    public string MatchDateText => MatchDateUtc == default ? string.Empty : MatchDateUtc.ToLocalTime().ToString("dd MMM yyyy");
    public string InningsSummary => string.IsNullOrWhiteSpace(BattingTeamName)
        ? $"Innings {InningsNumber}"
        : $"Innings {InningsNumber} · {BattingTeamName}";
}

/// <summary>Full leaderboard row: Match · Batting · Bowling · Fielding · Attendance.</summary>
public sealed partial class PlayerCareerSummary : ObservableObject
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
    /// <summary>Count displayed in the first discipline column: sessions for Nets, matches otherwise.</summary>
    public int MatchesOrSessions => Sessions > 0 ? Sessions : Matches;
    public int Won { get; set; }
    public int Loss { get; set; }
    public int Tied { get; set; }
    private int _bestWinningStreak;
    public int BestWinningStreak
    {
        get => _bestWinningStreak;
        set => SetProperty(ref _bestWinningStreak, value);
    }
    private int _currentWinningStreak;
    public int CurrentWinningStreak
    {
        get => _currentWinningStreak;
        set => SetProperty(ref _currentWinningStreak, value);
    }
    private int _worstLosingStreak;
    public int WorstLosingStreak
    {
        get => _worstLosingStreak;
        set => SetProperty(ref _worstLosingStreak, value);
    }
    private int _currentLosingStreak;
    public int CurrentLosingStreak
    {
        get => _currentLosingStreak;
        set => SetProperty(ref _currentLosingStreak, value);
    }
    public double MatchWinPct => Matches == 0
        ? 0
        : Math.Round(((Won + Tied * 0.5) / Matches) * 100, 1);
    public string MatchWinPctText => Matches == 0 ? "-" : MatchWinPct.ToString("0.#") + "%";
    public int CaptainMatches { get; set; }
    public int CaptainWon { get; set; }
    public int CaptainLoss { get; set; }
    public int CaptainTied { get; set; }
    public int CaptainPoints => CaptainWon * 2 + CaptainTied;
    public int CaptainRunsFor { get; set; }
    public int CaptainRunsAgainst { get; set; }
    public int CaptainBallsFor { get; set; }
    public int CaptainBallsAgainst { get; set; }
    public double CaptainWinPct => CaptainMatches == 0
        ? 0
        : Math.Round(((CaptainWon + CaptainTied * 0.5) / CaptainMatches) * 100, 1);
    public string CaptainWinPctText => CaptainMatches == 0 ? "-" : CaptainWinPct.ToString("0.#") + "%";
    public double CaptainNrr
    {
        get
        {
            var of = CaptainBallsFor <= 0 ? 0 : CaptainRunsFor / (CaptainBallsFor / 6.0);
            var oa = CaptainBallsAgainst <= 0 ? 0 : CaptainRunsAgainst / (CaptainBallsAgainst / 6.0);
            return Math.Round(of - oa, 3);
        }
    }
    public string CaptainNrrText => CaptainMatches == 0 ? "-" : CaptainNrr.ToString("+0.000;-0.000;0.000");

    // Tournament
    public int Tournaments { get; set; }
    public int TournamentWon { get; set; }
    public int TournamentLoss { get; set; }
    public int TournamentTied { get; set; }
    private int _bestTournamentWinningStreak;
    public int BestTournamentWinningStreak
    {
        get => _bestTournamentWinningStreak;
        set => SetProperty(ref _bestTournamentWinningStreak, value);
    }
    private int _currentTournamentWinningStreak;
    public int CurrentTournamentWinningStreak
    {
        get => _currentTournamentWinningStreak;
        set => SetProperty(ref _currentTournamentWinningStreak, value);
    }
    private int _worstTournamentLosingStreak;
    public int WorstTournamentLosingStreak
    {
        get => _worstTournamentLosingStreak;
        set => SetProperty(ref _worstTournamentLosingStreak, value);
    }
    private int _currentTournamentLosingStreak;
    public int CurrentTournamentLosingStreak
    {
        get => _currentTournamentLosingStreak;
        set => SetProperty(ref _currentTournamentLosingStreak, value);
    }
    public double TournamentWinPct => Tournaments == 0
        ? 0
        : Math.Round(((TournamentWon + TournamentTied * 0.5) / Tournaments) * 100, 1);
    public string TournamentWinPctText => Tournaments == 0 ? "-" : TournamentWinPct.ToString("0.#") + "%";
    public int CaptainTournaments { get; set; }
    public int CaptainTournamentWon { get; set; }
    public int CaptainTournamentLoss { get; set; }
    public int CaptainTournamentTied { get; set; }
    public double CaptainTournamentWinPct => CaptainTournaments == 0
        ? 0
        : Math.Round(((CaptainTournamentWon + CaptainTournamentTied * 0.5) / CaptainTournaments) * 100, 1);
    public string CaptainTournamentWinPctText => CaptainTournaments == 0
        ? "-"
        : CaptainTournamentWinPct.ToString("0.#") + "%";

    // Series
    public int Series { get; set; }
    public int SeriesWon { get; set; }
    public int SeriesLoss { get; set; }
    public int SeriesTied { get; set; }
    private int _bestSeriesWinningStreak;
    public int BestSeriesWinningStreak
    {
        get => _bestSeriesWinningStreak;
        set => SetProperty(ref _bestSeriesWinningStreak, value);
    }
    private int _currentSeriesWinningStreak;
    public int CurrentSeriesWinningStreak
    {
        get => _currentSeriesWinningStreak;
        set => SetProperty(ref _currentSeriesWinningStreak, value);
    }
    private int _worstSeriesLosingStreak;
    public int WorstSeriesLosingStreak
    {
        get => _worstSeriesLosingStreak;
        set => SetProperty(ref _worstSeriesLosingStreak, value);
    }
    private int _currentSeriesLosingStreak;
    public int CurrentSeriesLosingStreak
    {
        get => _currentSeriesLosingStreak;
        set => SetProperty(ref _currentSeriesLosingStreak, value);
    }
    public double SeriesWinPct => Series == 0
        ? 0
        : Math.Round(((SeriesWon + SeriesTied * 0.5) / Series) * 100, 1);
    public string SeriesWinPctText => Series == 0 ? "-" : SeriesWinPct.ToString("0.#") + "%";
    public int CaptainSeries { get; set; }
    public int CaptainSeriesWon { get; set; }
    public int CaptainSeriesLoss { get; set; }
    public int CaptainSeriesTied { get; set; }
    public double CaptainSeriesWinPct => CaptainSeries == 0
        ? 0
        : Math.Round(((CaptainSeriesWon + CaptainSeriesTied * 0.5) / CaptainSeries) * 100, 1);
    public string CaptainSeriesWinPctText => CaptainSeries == 0
        ? "-"
        : CaptainSeriesWinPct.ToString("0.#") + "%";

    // Batting
    public int InningsBatted { get; set; }
    /// <summary>Distinct innings in which this player bowled at least one delivery.</summary>
    public int InningsBowled { get; set; }
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
    /// <summary>Times this player got their batting partner run out (surviving partner).</summary>
    public int PartnerRunOuts { get; set; }
    public int StumpedOut { get; set; }
    public int HitWicketOut { get; set; }
    public int RetiredOut { get; set; }
    public int OtherOut { get; set; }

    // Bowling
    public int OversBowledBalls { get; set; }
    public int RunsConceded { get; set; }
    public int Wickets { get; set; }
    public int DotBallsBowled { get; set; }
    /// <summary>Boundaries of 4 off the bat conceded while bowling.</summary>
    public int FoursConceded { get; set; }
    /// <summary>Boundaries of 6+ off the bat conceded while bowling.</summary>
    public int SixesConceded { get; set; }
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
    /// <summary>Catches + run outs + stumpings.</summary>
    public int FieldingTotal => Catches + RunOutsEffected + Stumpings;
    /// <summary>Fielding total per match played.</summary>
    public double FieldingPerMatch => Matches == 0
        ? 0
        : Math.Round(FieldingTotal / (double)Matches, 2);
    public string FieldingPerMatchText => Matches == 0 ? "-" : FieldingPerMatch.ToString("0.##");
    public double FieldingPerSession => Sessions == 0 ? 0 : Math.Round(FieldingTotal / (double)Sessions, 2);
    public string FieldingPerSessionText => Sessions == 0 ? "-" : FieldingPerSession.ToString("0.##");
    public string FieldingRateText => Sessions > 0 ? FieldingPerSessionText : FieldingPerMatchText;
    /// <summary>Catches dropped while fielding (credited to the fielder).</summary>
    public int DroppedCatches { get; set; }

    // Attendance (venue bookings)
    public int AttendanceTotal { get; set; }
    public int AttendancePresent { get; set; }
    public int AttendanceAbsent { get; set; }
    public double AttendancePct => AttendanceTotal == 0
        ? 0
        : Math.Round(AttendancePresent * 100.0 / AttendanceTotal, 1);
    public string AttendancePctText => AttendanceTotal == 0 ? "-" : AttendancePct.ToString("0.#") + "%";
    /// <summary>Sum of this player's venue-booking share amounts.</summary>
    public decimal PaymentTotal { get; set; }
    /// <summary>Sum of shares marked Paid.</summary>
    public decimal PaymentPaid { get; set; }
    /// <summary>Unpaid share (PaymentTotal − PaymentPaid).</summary>
    public decimal PaymentRemaining => Math.Max(0, PaymentTotal - PaymentPaid);
    public double PaymentPaidPct => PaymentTotal <= 0
        ? 0
        : Math.Round((double)(PaymentPaid / PaymentTotal) * 100, 1);
    public string PaymentTotalText => PaymentTotal.ToString("0.##");
    public string PaymentPaidText => PaymentPaid.ToString("0.##");
    public string PaymentRemainingText => PaymentRemaining.ToString("0.##");
    public string PaymentPaidPctText => PaymentTotal <= 0 ? "-" : PaymentPaidPct.ToString("0.#") + "%";

    public decimal ExpensePurchased { get; set; }
    public decimal ExpenseContributed { get; set; }
    public decimal ExpenseRemaining { get; set; }
    public string ExpensePurchasedText => ExpensePurchased.ToString("0.##");
    public string ExpenseContributedText => ExpenseContributed.ToString("0.##");
    public string ExpenseRemainingText => ExpenseRemaining.ToString("0.##");

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
    /// <summary>Share of runs from 4s and 6s: (4×Fours + 6×Sixes) / Runs.</summary>
    public double BoundaryPct => Runs <= 0
        ? 0
        : Math.Round((Fours * 4 + Sixes * 6) * 100.0 / Runs, 1);
    public string BoundaryPctText => Runs <= 0 ? "-" : BoundaryPct.ToString("0.#") + "%";

    // Bowling milestones
    public int Maidens { get; set; }
    /// <summary>Innings with 5+ bowler-credited wickets.</summary>
    public int FiveWicketHauls { get; set; }
    public int BestBowlingWickets { get; set; }
    public int BestBowlingRuns { get; set; }
    public string BestBowlingText { get; set; } = "-";
    /// <summary>Bowler-credited wickets where the batter was dismissed for 0.</summary>
    public int DuckWkts { get; set; }
    /// <summary>Bowler-credited wickets where the batter was dismissed first ball for 0.</summary>
    public int GoldenDuckWkts { get; set; }
    /// <summary>Bowler-credited wickets in three consecutive deliveries (same innings).</summary>
    public int HatTricks { get; set; }
    /// <summary>Completed overs.balls from legal balls bowled (e.g. 17.3).</summary>
    public string OversBowledText =>
        OversBowledBalls <= 0 ? "-" : $"{OversBowledBalls / 6}.{OversBowledBalls % 6}";

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

/// <summary>One player on a team squad (Statistics · Teams detail).</summary>
public sealed class TeamSquadPlayerRow
{
    public Guid PlayerId { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public bool IsCaptain { get; init; }
    public string RoleText => IsCaptain ? "Captain" : "Squad";
}

/// <summary>Statistics · Teams row: matches, NRR, tournaments, series.</summary>
public sealed class TeamCareerSummary
{
    public Guid TeamId { get; init; }
    public string TeamName { get; init; } = string.Empty;
    public Guid? CaptainPlayerId { get; set; }
    public string? CaptainName { get; set; }
    public string CaptainLine => string.IsNullOrWhiteSpace(CaptainName) ? "No captain" : CaptainName!;
    public bool HasCaptain => !string.IsNullOrWhiteSpace(CaptainName);
    public int Rank { get; set; }
    public bool IsAltRow { get; set; }

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
    public int TournamentTied { get; set; }
    public double TournamentWinPct => Tournaments == 0
        ? 0
        : Math.Round(((TournamentWon + TournamentTied * 0.5) / Tournaments) * 100, 1);
    public string TournamentWinPctText => Tournaments == 0 ? "-" : TournamentWinPct.ToString("0.#") + "%";
    public int Series { get; set; }
    public int SeriesWon { get; set; }
    public int SeriesLoss { get; set; }
    public int SeriesTied { get; set; }
    public double SeriesWinPct => Series == 0
        ? 0
        : Math.Round(((SeriesWon + SeriesTied * 0.5) / Series) * 100, 1);
    public string SeriesWinPctText => Series == 0 ? "-" : SeriesWinPct.ToString("0.#") + "%";

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
}

/// <summary>Head-to-head row: this team vs one opponent.</summary>
public sealed class TeamVsStat
{
    public Guid OpponentTeamId { get; set; }
    public string OpponentTeamName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
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
    public string VsTitle => $"vs {OpponentTeamName}";
    public bool HasSeries => Series > 0;
    public string SeriesRecordText => Series == 0
        ? "-"
        : $"{SeriesWon}–{SeriesLoss}–{SeriesTied}";
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
    public string MatchTitle { get; init; } = string.Empty;
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
