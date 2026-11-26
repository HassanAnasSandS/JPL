using JPL.Data;
using JPL.Domain;
using Microsoft.EntityFrameworkCore;

namespace JPL.Services;

/// <summary>
/// Local storage gate for League Admin feature data. Sync remains restricted to the home league.
/// </summary>
public static class LocalCacheGate
{
    public static Task<bool> PreferLocalAsync(
        JplDbContext db,
        AuthSession? session,
        CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(session))
            return Task.FromResult(false);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Ensure a SyncCursor row exists for the current home league so empty installs can write
    /// locally and later bootstrap/pull without waiting on the network first.
    /// Call inside the same transaction as the first local write / outbox enqueue.
    /// </summary>
    public static async Task EnsureLeagueWriteReadyAsync(JplDbContext db, CancellationToken ct = default)
    {
        if (db.CurrentLeagueId == Guid.Empty)
            return;

        var exists = await db.SyncCursors
            .AnyAsync(cursor => cursor.LeagueId == db.CurrentLeagueId, ct)
            .ConfigureAwait(false);
        if (exists)
            return;

        db.SyncCursors.Add(new SyncCursor
        {
            LeagueId = db.CurrentLeagueId,
            LastSequence = 0,
            IsBootstrapped = false,
            LastSuccessfulSyncUtc = null
        });
    }

    /// <summary>
    /// Linked CTS for list/bootstrap refresh. Keep enough headroom for Cloudflare + large
    /// team/player payloads (teams alone can take 3s+); still bound so a dead host cannot hang forever.
    /// </summary>
    public static CancellationTokenSource ShortNetworkCts(CancellationToken ct, int seconds = 25)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 45)));
        return linked;
    }

    /// <summary>
    /// Legacy schema backfill stamped sports rows with <see cref="LeagueIds.Jpl"/> (b1000001…).
    /// Production home leagues use real Guids (e.g. 57c7bf64…). Remap orphan sentinel rows
    /// onto the selected league so Team/Player filters stop returning empty.
    /// </summary>
    public static async Task RemapLegacyDefaultLeagueAsync(JplDbContext db, CancellationToken ct = default)
    {
        var home = db.CurrentLeagueId;
        if (home == Guid.Empty || home == LeagueIds.Jpl)
            return;

        var homeText = home.ToString("D");
        var legacyText = LeagueIds.Jpl.ToString("D");
        try
        {
            // Sentinel rows were never visible under the real home-league query filter.
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE "Teams" SET "LeagueId" = {0} WHERE lower("LeagueId") = lower({1});""",
                homeText, legacyText).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE "Players" SET "LeagueId" = {0} WHERE lower("LeagueId") = lower({1});""",
                homeText, legacyText).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE "TeamPlayers" SET "LeagueId" = {0} WHERE lower("LeagueId") = lower({1});""",
                homeText, legacyText).ConfigureAwait(false);
        }
        catch
        {
            /* older schema / missing columns — lists still try live API */
        }
    }

}
