using System.Globalization;
using System.Text.RegularExpressions;

namespace JPL.Services;

/// <summary>Breakdown of RankingPoints — real plus and minus lines, not a 1000-cap shortfall.</summary>
public static class RankingExplain
{
    public static RankingBreakdown Batting(PlayerCareerSummary p)
        => FromParts(
            "Batting points",
            "Runs, boundaries, 50/100, not-outs, HS, SR vs 100, avg vs 20. Minus: dots, ducks, cheap dismissals.",
            RankingPoints.Batting(p),
            $"Inns {p.InningsBatted} · Balls {p.BallsFaced} · Avg {SkillRatings.BatAvg(p):0.##} · SR {SkillRatings.BatSr(p):0.#} · Dismissals {p.Dismissals}.");

    public static RankingBreakdown Bowling(PlayerCareerSummary p)
    {
        var overs = p.OversBowledBalls / 6.0;
        var econ = p.OversBowledBalls == 0 ? 0 : SkillRatings.BowlEcon(p);
        return FromParts(
            "Bowling points",
            "Wickets, maidens, dots, wicket type, hat-tricks, SR vs 18, economy vs 8. Minus: wides, no-balls, expensive overs, wicketless spells.",
            RankingPoints.Bowling(p),
            $"Overs {overs:0.#} · Runs {p.RunsConceded} · Econ {(p.OversBowledBalls == 0 ? "-" : econ.ToString("0.##", CultureInfo.InvariantCulture))} · Wkts {p.Wickets}.");
    }

    public static RankingBreakdown AllRounder(PlayerCareerSummary p)
        => FromParts(
            "All-rounder points",
            "Batting pts + bowling pts (each already includes plus and minus).",
            RankingPoints.AllRounder(p),
            $"Runs {p.Runs} · Wickets {p.Wickets}.");

    public static RankingBreakdown Fielding(PlayerCareerSummary p)
        => FromParts(
            "Fielding points",
            "Catch×10, run-out×12, stumping×15. Minus if dismissals stay below 0.25 per match after 4 games.",
            RankingPoints.Fielding(p),
            $"Matches {p.Matches} · Ct {p.Catches} · RO {p.RunOutsEffected} · St {p.Stumpings}.");

    public static RankingBreakdown Captaincy(PlayerCareerSummary p)
        => FromParts(
            "Captain points",
            "Win×4 + tie×1 − loss×2.",
            RankingPoints.Captaincy(p),
            $"Games {p.CaptainMatches} · W {p.CaptainWon} · L {p.CaptainLoss} · T {p.CaptainTied}.");

    public static RankingBreakdown Team(RankingRow row)
    {
        var played = 0;
        var won = 0;
        var lost = 0;
        var tied = 0;
        var m = Regex.Match(row.Detail ?? string.Empty, @"P\s+(\d+)\s*·\s*W\s+(\d+)\s*·\s*L\s+(\d+)\s*·\s*T\s+(\d+)");
        if (m.Success)
        {
            played = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            won = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            lost = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            tied = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
        }

        double.TryParse(row.SecondaryValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var nrr);
        return FromParts(
            "Team points",
            "Win×2 + tie×1 − loss×1 + NRR×10.",
            RankingPoints.Team(played, won, lost, tied, nrr),
            played > 0
                ? $"P {played} · W {won} · L {lost} · T {tied} · NRR {row.SecondaryValue}."
                : (row.Detail ?? string.Empty));
    }

    public static RankingBreakdown ForRow(int category, RankingRow row)
        => row.Breakdown ?? (category == 5 ? Team(row) : FromRowOnly(row));

    private static RankingBreakdown FromRowOnly(RankingRow row)
        => FromParts("Points", row.Detail ?? string.Empty, [new RankingPart(row.PrimaryLabel, row.SecondaryLine, 0)], row.Detail ?? string.Empty);

    private static RankingBreakdown FromParts(
        string title,
        string formula,
        IReadOnlyList<RankingPart> parts,
        string note)
    {
        var plus = new List<RankingFactorLine>();
        var minus = new List<RankingFactorLine>();
        double pos = 0, neg = 0;
        foreach (var p in parts)
        {
            if (p.Pts >= 0.05)
            {
                plus.Add(Line(p, positive: true));
                pos += p.Pts;
            }
            else if (p.Pts <= -0.05)
            {
                minus.Add(Line(p, positive: false));
                neg += Math.Abs(p.Pts);
            }
        }

        var net = RankingPoints.Net(parts);
        if (plus.Count == 0 && minus.Count == 0)
            plus.Add(new RankingFactorLine { Label = "Points", Stat = "Nothing scored yet", PointsText = "+0", IsPositive = true });

        return new RankingBreakdown
        {
            Title = title,
            Formula = formula,
            NetText = $"{net:0} pts",
            PositiveTotalText = $"+{pos:0}",
            NegativeTotalText = neg < 0.05 ? "0" : $"−{neg:0}",
            Note = note,
            Positive = plus,
            Negative = minus
        };
    }

    private static RankingFactorLine Line(RankingPart p, bool positive)
        => new()
        {
            Label = p.Label,
            Stat = p.Stat,
            PointsText = p.Pts >= 0 ? $"+{p.Pts:0.#}" : $"−{Math.Abs(p.Pts):0.#}",
            IsPositive = positive
        };
}
