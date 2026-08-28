using Microsoft.EntityFrameworkCore;
using JPL.Api.Data;
using JPL.Api.Models;

namespace JPL.Api.Services;

/// <summary>
/// Same display name can exist as two rows (each Create used a new Guid).
/// Keep one active league per name so the picker does not list JPL twice.
/// </summary>
internal static class LeagueNameDeduper
{
    /// <summary>Stable JPL id from the MAUI app (Domain.LeagueIds.Jpl).</summary>
    private static readonly Guid CanonicalJplId = Guid.Parse("b1000001-0000-4000-8000-000000000001");

    public static League PickKeep(IEnumerable<League> group, IReadOnlyDictionary<Guid, int> userCounts)
        => group
            .OrderByDescending(x => x.Id == CanonicalJplId)
            .ThenByDescending(x => userCounts.GetValueOrDefault(x.Id))
            .ThenBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .First();

    public static IReadOnlyList<League> CollapseActiveByName(
        IReadOnlyList<League> leagues,
        IReadOnlyDictionary<Guid, int> userCounts)
        => leagues
            .GroupBy(l => l.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => PickKeep(g, userCounts))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static async Task<Dictionary<Guid, int>> LoadUserCountsAsync(MasterDbContext db, CancellationToken ct = default)
        => await db.Users.AsNoTracking()
            .Where(u => u.LeagueId != null)
            .GroupBy(u => u.LeagueId!.Value)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.N, ct)
            .ConfigureAwait(false);

    public static async Task CollapsePersistedAsync(MasterDbContext db, CancellationToken ct = default)
    {
        var active = await db.Leagues.Where(l => l.IsActive).ToListAsync(ct).ConfigureAwait(false);
        var userCounts = await LoadUserCountsAsync(db, ct).ConfigureAwait(false);
        var users = await db.Users.Where(u => u.LeagueId != null).ToListAsync(ct).ConfigureAwait(false);
        var changed = false;

        foreach (var group in active.GroupBy(l => l.Name.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() < 2)
                continue;

            var keep = PickKeep(group, userCounts);
            var extraIds = group.Where(x => x.Id != keep.Id).Select(x => x.Id).ToHashSet();
            foreach (var extra in group.Where(x => x.Id != keep.Id))
            {
                extra.IsActive = false;
                changed = true;
            }

            foreach (var user in users.Where(u => u.LeagueId is Guid id && extraIds.Contains(id)))
            {
                user.LeagueId = keep.Id;
                changed = true;
            }
        }

        if (changed)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public static Task<bool> NameTakenAsync(MasterDbContext db, string name, Guid? exceptId, CancellationToken ct)
    {
        var key = name.Trim().ToLower();
        var q = db.Leagues.AsNoTracking().Where(l => l.IsActive && l.Name.ToLower() == key);
        if (exceptId is Guid id)
            q = q.Where(l => l.Id != id);
        return q.AnyAsync(ct);
    }
}
