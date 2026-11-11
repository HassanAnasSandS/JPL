using System.Globalization;
using System.Text.RegularExpressions;

namespace JPL.Services;

/// <summary>Breakdown of RankingPoints — real plus and minus lines, not a 1000-cap shortfall.</summary>
public static class RankingExplain
{
    public static RankingBreakdown Batting(PlayerCareerSummary p)
        => FromParts("Batting points", RankingPoints.Batting(p));

    public static RankingBreakdown Bowling(PlayerCareerSummary p)
        => FromParts("Bowling points", RankingPoints.Bowling(p));

    public static RankingBreakdown AllRounder(PlayerCareerSummary p)
        => FromParts("All-rounder points", RankingPoints.AllRounder(p));

    public static RankingBreakdown Fielding(PlayerCareerSummary p)
        => FromParts("Fielding points", RankingPoints.Fielding(p));

    public static RankingBreakdown Captaincy(PlayerCareerSummary p)
        => FromParts("Captain points", RankingPoints.Captaincy(p));

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
        return FromParts("Team points", RankingPoints.Team(played, won, lost, tied, nrr));
    }

    public static RankingBreakdown ForRow(int category, RankingRow row)
        => row.Breakdown ?? (category == 5 ? Team(row) : FromRowOnly(row));

    private static RankingBreakdown FromRowOnly(RankingRow row)
        => FromParts("Points", [new RankingPart(row.PrimaryLabel, string.Empty, 0)]);

    private static RankingBreakdown FromParts(string title, IReadOnlyList<RankingPart> parts)
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
            plus.Add(new RankingFactorLine { Label = "Points", PointsText = "+0", IsPositive = true });

        return new RankingBreakdown
        {
            Title = title,
            NetText = $"{net:0} pts",
            PositiveTotalText = $"+{pos:0}",
            NegativeTotalText = neg < 0.05 ? "0" : $"−{neg:0}",
            Positive = plus,
            Negative = minus
        };
    }

    private static RankingFactorLine Line(RankingPart p, bool positive)
        => new()
        {
            Label = p.Label,
            PointsText = p.Pts >= 0 ? $"+{p.Pts:0.#}" : $"−{Math.Abs(p.Pts):0.#}",
            IsPositive = positive
        };
}
