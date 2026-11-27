using JPL.Domain;

namespace JPL.Services;

/// <summary>
/// Separates the League Admin's local working store from permission to synchronize that store.
/// Only the Admin's bound home league can push and pull through Sync.
/// Other roles use live production APIs for league data.
/// </summary>
public static class LeagueSyncPolicy
{
    /// <summary>League Admin feature data always uses the selected league's local database.</summary>
    public static bool UsesLocalStorage(AuthSession? session)
        => session?.Role == AppRole.Admin;

    public static bool AllowsLocalOfflineSync(AuthSession? session, Guid? selectedLeagueId)
    {
        if (session is null)
            return false;

        // League User / SuperAdmin / Guest: always live APIs for sports data.
        if (session.Role != AppRole.Admin)
            return false;

        // Admin may work locally in any selected league, but only their home league can sync.
        return session.IsHomeLeague(selectedLeagueId);
    }
}
