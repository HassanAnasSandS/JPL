namespace JPL.Api.Services;

internal static class OversRules
{
    public const int MaxOvers = 100;

    public static string ShortLabel(Models.OversMode mode, int oversLimit) => mode switch
    {
        Models.OversMode.Unlimited => "unlimited",
        Models.OversMode.MatchTotal => $"{oversLimit} ov match",
        _ => $"{oversLimit} ov/side"
    };

    public static int StoredOversLimit(Models.OversMode mode, int oversLimit)
        => mode == Models.OversMode.Unlimited ? 0 : oversLimit;

    public static int? BallsRemaining(Models.OversMode mode, int oversLimit, int inningsLegal, int matchLegal)
    {
        if (mode == Models.OversMode.Unlimited)
            return null;
        var used = mode == Models.OversMode.MatchTotal ? matchLegal : inningsLegal;
        return Math.Max(0, oversLimit * 6 - used);
    }

    public static void Validate(Models.OversMode mode, int oversLimit, int bowlerMaxOvers)
    {
        if ((byte)mode > (byte)Models.OversMode.Unlimited)
            throw new InvalidOperationException("Invalid overs mode.");
        if (bowlerMaxOvers < 0)
            throw new InvalidOperationException("Bowler maximum overs cannot be negative.");
        if (mode == Models.OversMode.Unlimited)
            return;
        if (oversLimit is < 1 or > MaxOvers)
        {
            throw new InvalidOperationException(mode == Models.OversMode.MatchTotal
                ? $"Match overs must be between 1 and {MaxOvers}."
                : $"Overs per innings must be between 1 and {MaxOvers}.");
        }

        if (bowlerMaxOvers > oversLimit)
        {
            throw new InvalidOperationException(mode == Models.OversMode.MatchTotal
                ? "Bowler maximum overs cannot exceed match overs."
                : "Bowler maximum overs cannot exceed overs per innings.");
        }
    }
}
