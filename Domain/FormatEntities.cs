namespace JPL.Domain;

/// <summary>League-owned match format preset. Matches store FormatId (FK) plus a snapshot of the settings.</summary>
public sealed class Format
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public MatchFormat Kind { get; set; } = MatchFormat.SingleInning;
    public int OversLimit { get; set; } = 8;
    public OversMode OversMode { get; set; } = OversMode.MatchTotal;
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
}

public static class FormatRules
{
    public static string TypeLabel(MatchFormat format)
        => format == MatchFormat.DoubleInnings
            ? "Double innings (2 turns each)"
            : "Single inning (1 turn each)";

    public static string ShortType(MatchFormat format)
        => format == MatchFormat.DoubleInnings ? "Double innings" : "Single inning";

    public static string DetailsLine(MatchFormat format, OversMode mode, int oversLimit)
        => $"{ShortType(format)} · {OversRules.ShortLabel(mode, oversLimit)}";

    public static string DefaultName(MatchFormat format, OversMode mode, int oversLimit)
        => DetailsLine(format, mode, oversLimit);

    public static string Hint(MatchFormat format, OversMode mode, int oversLimit)
    {
        if (mode == OversMode.Unlimited)
        {
            return format == MatchFormat.DoubleInnings
                ? "Each side bats twice. Innings end only on all out or declare — overs never stop a turn."
                : "Each side bats once. Innings end only on all out or declare — overs never stop a turn.";
        }

        if (mode == OversMode.MatchTotal)
        {
            return format == MatchFormat.DoubleInnings
                ? $"Each side bats twice. All turns share {oversLimit} overs for the whole match. All out or declare still ends a turn."
                : $"Each side bats once. Both sides share {oversLimit} overs for the whole match. All out or declare still ends a turn.";
        }

        return format == MatchFormat.DoubleInnings
            ? $"Each side bats twice ({oversLimit} overs each turn). Follow-on available after 2nd innings if leading."
            : $"Each side bats once ({oversLimit} overs each).";
    }

    /// <summary>Stable id so API and local SQLite backfill the same Format row for the same settings.</summary>
    public static Guid MigratedId(MatchFormat format, OversMode mode, int oversLimit)
    {
        var overs = mode == OversMode.Unlimited ? 0 : Math.Max(0, oversLimit);
        return Guid.Parse($"a7f04700-0000-4000-8000-{(byte)format:x2}{(byte)mode:x2}{overs:x8}");
    }
}
