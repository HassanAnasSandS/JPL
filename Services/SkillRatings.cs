namespace JPL.Services;

/// <summary>Ranking points from RankingPoints (Strength Meter uses StrengthScores).</summary>
public readonly record struct SkillRating(
    int Batting,
    int Bowling,
    int Fielding,
    int AllRounder,
    int Captaincy,
    int Experience)
{
    public int Overall(bool includeCaptaincy)
    {
        var n = includeCaptaincy ? 6 : 5;
        var sum = Batting + Bowling + Fielding + AllRounder + Experience + (includeCaptaincy ? Captaincy : 0);
        return (int)Math.Round(sum / (double)n);
    }
}

public static class SkillRatings
{
    public static SkillRating From(PlayerCareerSummary? p)
    {
        p ??= Empty;
        return new SkillRating(
            To100(BattingPts(p)),
            To100(BowlingPts(p)),
            To100(FieldingPts(p)),
            To100(AllRounderPts(p)),
            To100(CaptaincyPts(p)),
            To100(ExperiencePts(p)));
    }

    public static double BattingPts(PlayerCareerSummary p) => RankingPoints.Net(RankingPoints.Batting(p));

    public static double BowlingPts(PlayerCareerSummary p) => RankingPoints.Net(RankingPoints.Bowling(p));

    public static bool HasBatted(PlayerCareerSummary p) => p.BallsFaced > 0 || p.Runs > 0;
    public static bool HasBowled(PlayerCareerSummary p) => p.OversBowledBalls > 0 || p.Wickets > 0;

    public static double AllRounderPts(PlayerCareerSummary p)
        => !HasBatted(p) || !HasBowled(p) ? 0 : BattingPts(p) + BowlingPts(p);

    public static int FieldActions(PlayerCareerSummary p) => p.Catches + p.RunOutsEffected + p.Stumpings;

    public static double FieldingPts(PlayerCareerSummary p) => RankingPoints.Net(RankingPoints.Fielding(p));

    public static double CaptaincyPts(PlayerCareerSummary p) => RankingPoints.Net(RankingPoints.Captaincy(p));

    public static double ExperiencePts(PlayerCareerSummary p)
    {
        if (p.Matches <= 0 && p.Sessions <= 0 && p.InningsBatted <= 0 && p.OversBowledBalls <= 0)
            return 0;
        var matchesF = Math.Min(p.Matches / 20.0, 1.25);
        var innF = Math.Min(p.InningsBatted / 25.0, 1.0);
        var overs = p.OversBowledBalls / 6.0;
        var bowlF = Math.Min(overs / 20.0, 1.0);
        var tourF = Math.Min((p.Tournaments + p.Series) / 8.0, 1.0);
        var netsF = Math.Min(p.Sessions / 15.0, 1.0);
        var index = matchesF * 0.40 + innF * 0.20 + bowlF * 0.15 + tourF * 0.15 + netsF * 0.10;
        var qualify = Math.Min(1.0, Math.Max(p.Matches / 8.0, p.Sessions / 8.0));
        return Math.Round(Math.Clamp(1000.0 * index * qualify, 0, 1000));
    }

    public static double BatAvg(PlayerCareerSummary p) => p.Dismissals == 0 ? p.Runs : (double)p.Runs / p.Dismissals;
    public static double BatSr(PlayerCareerSummary p) => p.BallsFaced == 0 ? 0 : p.Runs * 100.0 / p.BallsFaced;
    public static double BowlEcon(PlayerCareerSummary p)
        => p.OversBowledBalls == 0 ? double.MaxValue : p.RunsConceded * 6.0 / p.OversBowledBalls;
    public static double BowlAvg(PlayerCareerSummary p)
        => p.Wickets == 0 ? double.MaxValue : (double)p.RunsConceded / p.Wickets;

    private static int To100(double pts1000)
        => (int)Math.Round(Math.Clamp(pts1000 / 10.0, 0, 100));

    private static readonly PlayerCareerSummary Empty = new()
    {
        PlayerId = Guid.Empty,
        PlayerName = string.Empty
    };
}
