namespace JPL.Api.Services;

/// <summary>
/// Chase margin: remaining wickets follow the batting team's actual squad,
/// not a fixed 10. Last-man-alone adds one extra available wicket.
/// </summary>
internal static class MatchResultRules
{
    public const int DefaultSquadSize = 11;

    /// <summary>
    /// Wickets that can fall before the innings is all out.
    /// Last batsman allowed → squad size. Otherwise → squad size − 1 (need a partner).
    /// </summary>
    public static int MaxWickets(int squadSize, bool lastBatsmanCanBatAsSingleWicket)
    {
        var size = squadSize > 0 ? squadSize : DefaultSquadSize;
        return lastBatsmanCanBatAsSingleWicket ? size : Math.Max(1, size - 1);
    }

    public static int WicketsRemaining(int squadSize, int wicketsLost, bool lastBatsmanCanBatAsSingleWicket)
        => Math.Max(1, MaxWickets(squadSize, lastBatsmanCanBatAsSingleWicket) - Math.Max(0, wicketsLost));
}
