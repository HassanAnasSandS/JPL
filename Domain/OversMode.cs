namespace JPL.Domain;

/// <summary>
/// How <see cref="Match.OversLimit"/> is applied.
/// Existing rows default to <see cref="PerInnings"/> so live matches keep their original quota.
/// </summary>
public enum OversMode : byte
{
    /// <summary>Each innings / each side's turn gets <c>OversLimit</c> overs (legacy).</summary>
    PerInnings = 0,
    /// <summary>All innings share one pool of <c>OversLimit</c> overs for the whole match.</summary>
    MatchTotal = 1,
    /// <summary>No over cap. Innings end only on all out or declare.</summary>
    Unlimited = 2
}

public static class OversRules
{
    public const int MaxOvers = 100;

    public static bool HasOversCap(OversMode mode) => mode != OversMode.Unlimited;

    public static string ShortLabel(OversMode mode, int oversLimit) => mode switch
    {
        OversMode.Unlimited => "unlimited",
        OversMode.MatchTotal => $"{oversLimit} ov match",
        _ => $"{oversLimit} ov/side"
    };

    /// <summary>Legal balls still allowed in the current innings. Null = unlimited.</summary>
    public static int? BallsRemaining(OversMode mode, int oversLimit, int inningsLegal, int matchLegal)
    {
        if (mode == OversMode.Unlimited)
            return null;
        var used = mode == OversMode.MatchTotal ? matchLegal : inningsLegal;
        return Math.Max(0, oversLimit * 6 - used);
    }

    public static bool InningsOversComplete(OversMode mode, int oversLimit, int inningsLegal, int matchLegal)
        => BallsRemaining(mode, oversLimit, inningsLegal, matchLegal) == 0;

    public static bool MatchPoolExhausted(OversMode mode, int oversLimit, int matchLegal)
        => mode == OversMode.MatchTotal && matchLegal >= oversLimit * 6;

    public static int StoredOversLimit(OversMode mode, int oversLimit)
        => mode == OversMode.Unlimited ? 0 : oversLimit;

    public static string ScoreOversText(OversMode mode, int oversLimit, int overs, int balls, int? ballsRemaining)
    {
        var faced = $"{overs}.{balls}";
        if (mode == OversMode.Unlimited)
            return faced;
        if (mode == OversMode.MatchTotal && ballsRemaining is int left)
        {
            var remOvers = left / 6;
            var remBalls = left % 6;
            return $"{faced}/{oversLimit} · {remOvers}.{remBalls} left";
        }

        return $"{faced}/{oversLimit}";
    }

    public static void Validate(OversMode mode, int oversLimit, int bowlerMaxOvers)
    {
        if ((byte)mode > (byte)OversMode.Unlimited)
            throw new ArgumentException("Invalid overs mode.");
        if (bowlerMaxOvers < 0)
            throw new ArgumentException("Bowler maximum overs cannot be negative.");
        if (mode == OversMode.Unlimited)
            return;
        if (oversLimit is < 1 or > MaxOvers)
        {
            throw new ArgumentException(mode == OversMode.MatchTotal
                ? $"Match overs must be between 1 and {MaxOvers}."
                : $"Overs per innings must be between 1 and {MaxOvers}.");
        }

        if (bowlerMaxOvers > oversLimit)
        {
            throw new ArgumentException(mode == OversMode.MatchTotal
                ? "Bowler maximum overs cannot exceed match overs."
                : "Bowler maximum overs cannot exceed overs per innings.");
        }
    }
}
