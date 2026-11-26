using JPL.Domain;

namespace JPL.Services;

/// <summary>
/// Live / completed scoring entities: the scoring device is authoritative.
/// Conflicts auto keep-local so multi-device 409s never block the outbox FIFO on a dialog.
/// Catalog entities (Team/Player/…) stay interactive.
/// </summary>
public static class LiveScoringConflict
{
    public static bool IsAutoKeepLocal(string? entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            return false;

        return entityType.Equals(nameof(Delivery), StringComparison.Ordinal)
               || entityType.Equals(nameof(Innings), StringComparison.Ordinal)
               || entityType.Equals(nameof(Match), StringComparison.Ordinal)
               || entityType.Equals(nameof(NetDelivery), StringComparison.Ordinal)
               || entityType.Equals(nameof(NetSession), StringComparison.Ordinal);
    }
}
