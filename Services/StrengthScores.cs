namespace JPL.Services;

/// <summary>
/// Strength Meter 0–100 scores from actual career stats.
/// Rankings keep ICC 0–1000 qualification; this does not.
/// Best in the league for a skill maps near 100 so standout batters (high runs/avg/SR) show as such.
/// </summary>
public static class StrengthScores
{
    public static IReadOnlyDictionary<Guid, SkillRating> RateLeague(IEnumerable<PlayerCareerSummary> board)
    {
        var players = board.ToList();
        if (players.Count == 0)
            return new Dictionary<Guid, SkillRating>();

        var bat = new Dictionary<Guid, double>(players.Count);
        var bowl = new Dictionary<Guid, double>(players.Count);
        var field = new Dictionary<Guid, double>(players.Count);
        var cap = new Dictionary<Guid, double>(players.Count);
        var exp = new Dictionary<Guid, double>(players.Count);

        foreach (var p in players)
        {
            bat[p.PlayerId] = BattingRaw(p);
            bowl[p.PlayerId] = BowlingRaw(p);
            field[p.PlayerId] = FieldingRaw(p);
            cap[p.PlayerId] = CaptaincyRaw(p);
            exp[p.PlayerId] = ExperienceRaw(p);
        }

        var arRaw = new Dictionary<Guid, double>(players.Count);
        foreach (var p in players)
        {
            var b = bat[p.PlayerId];
            var w = bowl[p.PlayerId];
            arRaw[p.PlayerId] = b > 0 && w > 0 ? Math.Sqrt(b * w) : 0;
        }

        var batMax = Max(bat.Values);
        var bowlMax = Max(bowl.Values);
        var fieldMax = Max(field.Values);
        var arMax = Max(arRaw.Values);
        var capMax = Max(cap.Values);
        var expMax = Max(exp.Values);

        var result = new Dictionary<Guid, SkillRating>(players.Count);
        foreach (var p in players)
        {
            result[p.PlayerId] = new SkillRating(
                Scale(bat[p.PlayerId], batMax),
                Scale(bowl[p.PlayerId], bowlMax),
                Scale(field[p.PlayerId], fieldMax),
                Scale(arRaw[p.PlayerId], arMax),
                Scale(cap[p.PlayerId], capMax),
                Scale(exp[p.PlayerId], expMax));
        }

        return result;
    }

    public static SkillRating For(Guid playerId, IReadOnlyDictionary<Guid, SkillRating> league)
        => league.TryGetValue(playerId, out var r) ? r : default;

    /// <summary>Runs, average, SR, boundaries, milestones — club sample, not 20-innings ICC.</summary>
    public static double BattingRaw(PlayerCareerSummary p)
    {
        if (p.Runs <= 0 && p.BallsFaced <= 0)
            return 0;

        var innings = Math.Max(p.InningsBatted, 1);
        var avg = SkillRatings.BatAvg(p);
        var sr = SkillRatings.BatSr(p);
        var boundaries = p.Fours + p.Sixes * 1.5;

        var avgS = Unit(avg, 8, 42) * 100;
        var srS = Unit(sr, 70, 155) * 100;
        var runsS = Unit(p.Runs, 8, 90) * 100;
        var hsS = Unit(p.HighestScore, 8, 55) * 100;
        var boundS = Unit(boundaries, 2, 25) * 100;
        var mileS = Math.Min(100, p.Fifties * 18 + p.Hundreds * 35);

        var quality = avgS * 0.34
                      + srS * 0.24
                      + runsS * 0.24
                      + hsS * 0.08
                      + boundS * 0.05
                      + mileS * 0.05;

        // 1 solid knock still counts; 4 innings or 40 balls = full trust
        var trust = 0.82 + 0.18 * Math.Min(1.0, Math.Max(innings / 4.0, p.BallsFaced / 40.0));
        return Clamp(quality * trust);
    }

    public static double BowlingRaw(PlayerCareerSummary p)
    {
        if (p.Wickets <= 0 && p.OversBowledBalls <= 0)
            return 0;

        var overs = p.OversBowledBalls / 6.0;
        var avg = SkillRatings.BowlAvg(p);
        var econ = SkillRatings.BowlEcon(p);
        var sr = p.Wickets == 0 ? double.MaxValue : p.OversBowledBalls / (double)p.Wickets;

        var econS = LowerIsBetter(econ, best: 5.0, worst: 11.0) * 100;
        var avgS = LowerIsBetter(avg, best: 12.0, worst: 36.0) * 100;
        var srS = LowerIsBetter(sr, best: 10.0, worst: 28.0) * 100;
        var wktsS = Unit(p.Wickets, 1, 8) * 100;
        var maidenS = Unit(p.Maidens, 0, 4) * 100;

        var quality = econS * 0.28
                      + avgS * 0.22
                      + srS * 0.12
                      + wktsS * 0.32
                      + maidenS * 0.06;

        var trust = 0.80 + 0.20 * Math.Min(1.0, Math.Max(overs / 6.0, p.Wickets / 4.0));
        return Clamp(quality * trust);
    }

    public static double FieldingRaw(PlayerCareerSummary p)
    {
        var actions = SkillRatings.FieldActions(p);
        if (actions <= 0)
            return 0;

        var matches = Math.Max(p.Matches, 1);
        var dpm = actions / (double)matches;
        var quality = Unit(dpm, 0.15, 1.1) * 55
                      + Unit(actions, 1, 8) * 30
                      + Unit(p.Stumpings, 0, 4) * 15;
        var trust = 0.80 + 0.20 * Math.Min(1.0, matches / 5.0);
        return Clamp(quality * trust);
    }

    public static double CaptaincyRaw(PlayerCareerSummary p)
    {
        if (p.CaptainMatches <= 0)
            return 0;

        var winPct = (p.CaptainWon + p.CaptainTied * 0.5) / p.CaptainMatches * 100;
        var quality = winPct * 0.62
                      + Unit(p.CaptainWon, 0, 6) * 100 * 0.25
                      + Unit(p.CaptainMatches, 1, 8) * 100 * 0.13;
        var trust = 0.78 + 0.22 * Math.Min(1.0, p.CaptainMatches / 4.0);
        return Clamp(quality * trust);
    }

    public static double ExperienceRaw(PlayerCareerSummary p)
    {
        if (p.Matches <= 0 && p.Sessions <= 0 && p.InningsBatted <= 0 && p.OversBowledBalls <= 0)
            return 0;

        var overs = p.OversBowledBalls / 6.0;
        return Clamp(
            Unit(p.Matches, 0, 10) * 100 * 0.42
            + Unit(p.InningsBatted, 0, 12) * 100 * 0.22
            + Unit(overs, 0, 16) * 100 * 0.14
            + Unit(p.Tournaments + p.Series, 0, 5) * 100 * 0.12
            + Unit(p.Sessions, 0, 8) * 100 * 0.10);
    }

    /// <summary>
    /// Mix absolute quality with league rank so the best batter is ~90–100,
    /// without giving 100 to a player who barely scored.
    /// </summary>
    private static int Scale(double raw, double leagueMax)
    {
        if (raw <= 0)
            return 0;
        if (leagueMax <= 0.01)
            return ToInt(raw);

        var relative = 100.0 * raw / leagueMax;
        var blended = raw * 0.28 + relative * 0.72;
        var cap = Math.Max(raw + 18, raw * 1.22);
        return ToInt(Math.Min(blended, cap));
    }

    private static double Unit(double value, double start, double full)
    {
        if (full <= start)
            return value >= full ? 1 : 0;
        return Math.Clamp((value - start) / (full - start), 0, 1);
    }

    private static double LowerIsBetter(double value, double best, double worst)
    {
        if (double.IsInfinity(value) || value >= double.MaxValue / 4)
            return 0;
        if (value <= best)
            return 1;
        if (value >= worst)
            return 0;
        return Math.Clamp((worst - value) / (worst - best), 0, 1);
    }

    private static double Max(IEnumerable<double> values)
    {
        var max = 0.0;
        foreach (var v in values)
        {
            if (v > max)
                max = v;
        }
        return max;
    }

    private static double Clamp(double v) => Math.Clamp(v, 0, 100);
    private static int ToInt(double v) => (int)Math.Round(Math.Clamp(v, 0, 100));
}
