namespace JPL.Services;

public readonly record struct RankingPart(string Label, string Stat, double Pts);

/// <summary>
/// Club ranking points from every stored stat. Positive for output, negative for real costs
/// (dots, ducks, extras, expensive overs, losses) — not “short of a 1000 cap”.
/// </summary>
public static class RankingPoints
{
    public static double Net(IReadOnlyList<RankingPart> parts)
        => Math.Round(parts.Sum(p => p.Pts));

    public static IReadOnlyList<RankingPart> Batting(PlayerCareerSummary p)
    {
        var list = new List<RankingPart>();
        if (p.BallsFaced <= 0 && p.Runs <= 0)
            return list;

        Add(list, "Runs", $"{p.Runs} × 1", p.Runs);
        Add(list, "Fours", $"{p.Fours} × 2", p.Fours * 2);
        Add(list, "Sixes", $"{p.Sixes} × 4", p.Sixes * 4);
        Add(list, "Fifties", $"{p.Fifties} × 12", p.Fifties * 12);
        Add(list, "Hundreds", $"{p.Hundreds} × 25", p.Hundreds * 25);
        Add(list, "Not outs", $"{p.NotOuts} × 5", p.NotOuts * 5);
        Add(list, "Highest score", $"{p.HighestScore} × 0.5", p.HighestScore * 0.5);

        if (p.BallsFaced >= 6)
        {
            var sr = SkillRatings.BatSr(p);
            Add(list, "Strike rate", $"SR {sr:0.#} vs 100 · {p.BallsFaced} balls", (sr - 100) * p.BallsFaced / 200.0);
        }

        if (p.InningsBatted >= 2)
        {
            var avg = SkillRatings.BatAvg(p);
            Add(list, "Average", $"Avg {avg:0.##} vs 10 · {p.InningsBatted} inns",
                (avg - 10) * Math.Min(p.InningsBatted, 15) * 0.20);
        }

        Add(list, "Dots", $"{p.Dots} × −0.2", p.Dots * -0.2);
        Add(list, "Ducks", $"{p.Ducks} × −10", p.Ducks * -10);
        Add(list, "Golden ducks", $"{p.GoldenDucks} × −15", p.GoldenDucks * -15);
        Add(list, "Run outs", $"{p.RunOut} × −5", p.RunOut * -5);
        Add(list, "Stumped", $"{p.StumpedOut} × −5", p.StumpedOut * -5);
        Add(list, "Hit wicket", $"{p.HitWicketOut} × −5", p.HitWicketOut * -5);
        Add(list, "Bowled", $"{p.BowledOut} × −5", p.BowledOut * -5);
        Add(list, "LBW", $"{p.LbwOut} × −5", p.LbwOut * -5);
        Add(list, "Caught", $"{p.CaughtOut} × −5", p.CaughtOut * -5);
        return list;
    }

    public static IReadOnlyList<RankingPart> Bowling(PlayerCareerSummary p)
    {
        var list = new List<RankingPart>();
        if (p.Wickets <= 0 && p.OversBowledBalls <= 0)
            return list;

        var overs = p.OversBowledBalls / 6.0;
        Add(list, "Wickets", $"{p.Wickets} × 10", p.Wickets * 10);
        Add(list, "Bowled wickets", $"{p.BowledWkts} × 3", p.BowledWkts * 3);
        Add(list, "LBW wickets", $"{p.LbwWkts} × 2", p.LbwWkts * 2);
        Add(list, "Stumped wickets", $"{p.StumpedWkts} × 2", p.StumpedWkts * 2);
        Add(list, "Hit-wicket wickets", $"{p.HitWicketWkts} × 2", p.HitWicketWkts * 2);
        Add(list, "Maidens", $"{p.Maidens} × 8", p.Maidens * 8);
        Add(list, "Dot balls", $"{p.DotBallsBowled} × 0.35", p.DotBallsBowled * 0.35);
        Add(list, "Hat-tricks", $"{p.HatTricks} × 20", p.HatTricks * 20);
        Add(list, "Best figures", $"Best {p.BestBowlingText} · {p.BestBowlingWickets} × 2", p.BestBowlingWickets * 2);

        if (p.OversBowledBalls >= 6)
        {
            var econ = SkillRatings.BowlEcon(p);
            Add(list, "Economy vs 8", $"Econ {econ:0.##} · {overs:0.#} ov", (8.0 - econ) * overs);
        }

        if (p.Wickets > 0 && p.OversBowledBalls >= 6)
        {
            var sr = p.OversBowledBalls / (double)p.Wickets;
            Add(list, "Bowling SR vs 18", $"{sr:0.#} balls/wkt", (18.0 - sr) * p.Wickets * 0.3);
        }
        else if (p.Wickets == 0 && p.OversBowledBalls >= 6)
        {
            Add(list, "Wicketless spell", $"{overs:0.#} ov, 0 wkts", -overs);
        }

        Add(list, "Wide runs", $"{p.WidesBowled} × −1.5", p.WidesBowled * -1.5);
        Add(list, "No-ball runs", $"{p.NoBallsBowled} × −2", p.NoBallsBowled * -2);
        Add(list, "Byes", $"{p.ByesConceded} × −0.2", p.ByesConceded * -0.2);
        Add(list, "Leg byes", $"{p.LegByesConceded} × −0.2", p.LegByesConceded * -0.2);
        return list;
    }

    public static IReadOnlyList<RankingPart> Fielding(PlayerCareerSummary p)
    {
        var list = new List<RankingPart>();
        var actions = SkillRatings.FieldActions(p);
        if (actions <= 0 && p.DroppedCatches <= 0)
            return list;

        Add(list, "Catches", $"{p.Catches} × 10", p.Catches * 10);
        Add(list, "Run-outs", $"{p.RunOutsEffected} × 12", p.RunOutsEffected * 12);
        Add(list, "Stumpings", $"{p.Stumpings} × 15", p.Stumpings * 15);
        if (p.DroppedCatches > 0)
            Add(list, "Dropped catches", $"{p.DroppedCatches} × −10", p.DroppedCatches * -10);

        if (p.Matches >= 4 && actions > 0)
        {
            var expected = p.Matches * 0.25;
            if (actions < expected)
                Add(list, "Low dismissals / match", $"{actions} in {p.Matches} matches (par {expected:0.#})", (actions - expected) * 4);
        }

        return list;
    }

    public static IReadOnlyList<RankingPart> Captaincy(PlayerCareerSummary p)
    {
        var list = new List<RankingPart>();
        if (p.CaptainMatches <= 0)
            return list;

        Add(list, "Wins", $"{p.CaptainWon} × 4", p.CaptainWon * 4);
        Add(list, "Ties", $"{p.CaptainTied} × 1", p.CaptainTied);
        Add(list, "Losses", $"{p.CaptainLoss} × −2", p.CaptainLoss * -2);
        return list;
    }

    public static IReadOnlyList<RankingPart> AllRounder(PlayerCareerSummary p)
    {
        var bat = SkillRatings.BattingPts(p);
        var bowl = SkillRatings.BowlingPts(p);
        return
        [
            new RankingPart("Batting", $"{bat:0} batting pts", bat),
            new RankingPart("Bowling", $"{bowl:0} bowling pts", bowl)
        ];
    }

    public static IReadOnlyList<RankingPart> Team(int played, int won, int lost, int tied, double nrr)
    {
        var list = new List<RankingPart>();
        Add(list, "Wins", $"{won} × 2", won * 2);
        Add(list, "Ties", $"{tied} × 1", tied);
        Add(list, "Losses", $"{lost} × −1", lost * -1);
        Add(list, "NRR", $"{nrr:+0.000;-0.000;0.000} × 10", nrr * 10);
        if (played > 0 && list.Count == 0)
            Add(list, "League points", $"P {played}", 0);
        return list;
    }

    private static void Add(List<RankingPart> list, string label, string stat, double pts)
    {
        if (Math.Abs(pts) < 0.05)
            return;
        list.Add(new RankingPart(label, stat, pts));
    }
}

public static class RankingBars
{
    private static readonly string[] Palette =
    [
        "#F5B400", "#FF7043", "#42A5F5", "#66BB6A", "#AB47BC", "#26C6DA",
        "#EC407A", "#7E57C2", "#FFA726", "#26A69A", "#5C6BC0", "#EF5350"
    ];

    public static List<RankingRow> Apply(List<RankingRow> rows)
    {
        Apply((IReadOnlyList<RankingRow>)rows);
        return rows;
    }

    public static void Apply(IReadOnlyList<RankingRow> rows)
    {
        var max = 1.0;
        foreach (var row in rows)
        {
            if (TryPts(row, out var pts))
                max = Math.Max(max, Math.Abs(pts));
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            TryPts(row, out var pts);
            var ratio = Math.Abs(pts) / max;
            row.BarRatio = pts == 0 ? 0 : Math.Clamp(ratio, 0.06, 1);
            row.BarColor = Color.FromArgb(pts < 0 ? "#C62828" : Palette[i % Palette.Length]);
        }
    }

    private static bool TryPts(RankingRow row, out double pts)
        => double.TryParse(row.PrimaryValue, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out pts);
}
