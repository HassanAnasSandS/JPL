using Microsoft.EntityFrameworkCore;
using JPL.Api.Data;
using JPL.Api.Models;

namespace JPL.Api.Services;

internal static class FormatCatalog
{
    public static string TypeLabel(MatchFormat format)
        => format == MatchFormat.DoubleInnings ? "Double innings" : "Single inning";

    public static string DetailsLine(MatchFormat format, OversMode mode, int oversLimit)
        => $"{TypeLabel(format)} · {OversRules.ShortLabel(mode, oversLimit)}";

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

    public static Guid MigratedId(MatchFormat format, OversMode mode, int oversLimit)
    {
        var overs = mode == OversMode.Unlimited ? 0 : Math.Max(0, oversLimit);
        return Guid.Parse($"a7f04700-0000-4000-8000-{(byte)format:x2}{(byte)mode:x2}{overs:x8}");
    }

    public static async Task<(MatchFormat Format, OversMode Mode, int OversLimit, Guid Id)> ResolveAsync(
        ApiDbContext db,
        Guid formatId,
        MatchFormat format,
        OversMode mode,
        int oversLimit,
        CancellationToken ct)
    {
        if (formatId != Guid.Empty)
        {
            var row = await db.Formats.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == formatId && !f.IsDeleted, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Selected format was not found. Open Formats and pick one.");
            return (row.Kind, row.OversMode, row.OversLimit, row.Id);
        }

        OversRules.Validate(mode, oversLimit, 0);
        var id = await EnsureAsync(db, format, mode, oversLimit, ct).ConfigureAwait(false);
        return (format, mode, OversRules.StoredOversLimit(mode, oversLimit), id);
    }

    public static async Task<Guid> EnsureAsync(
        ApiDbContext db,
        MatchFormat format,
        OversMode mode,
        int oversLimit,
        CancellationToken ct)
    {
        var stored = OversRules.StoredOversLimit(mode, oversLimit);
        var id = MigratedId(format, mode, stored);
        var exists = await db.Formats.IgnoreQueryFilters()
            .AnyAsync(f => f.Id == id, ct)
            .ConfigureAwait(false);
        if (exists)
            return id;

        var now = DateTime.Now;
        db.Formats.Add(new Format
        {
            Id = id,
            Name = UniqueName(db, DefaultName(format, mode, stored)),
            Kind = format,
            OversLimit = stored,
            OversMode = mode,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return id;
    }

    public static async Task BackfillAsync(ApiDbContext db, CancellationToken ct)
    {
        var known = (await db.Formats.IgnoreQueryFilters()
            .Select(f => f.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false)).ToHashSet();

        async Task<Guid> EnsureRow(MatchFormat format, OversMode mode, int oversLimit)
        {
            var stored = OversRules.StoredOversLimit(mode, oversLimit);
            var id = MigratedId(format, mode, stored);
            if (known.Contains(id))
                return id;

            var now = DateTime.Now;
            db.Formats.Add(new Format
            {
                Id = id,
                Name = UniqueName(db, DefaultName(format, mode, stored)),
                Kind = format,
                OversLimit = stored,
                OversMode = mode,
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            known.Add(id);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return id;
        }

        foreach (var m in await db.Matches.IgnoreQueryFilters().ToListAsync(ct).ConfigureAwait(false))
        {
            var id = await EnsureRow(m.Format, m.OversMode, m.OversLimit).ConfigureAwait(false);
            if (m.FormatId is null || m.FormatId == Guid.Empty)
                m.FormatId = id;
        }

        foreach (var t in await db.Tournaments.IgnoreQueryFilters().ToListAsync(ct).ConfigureAwait(false))
        {
            var id = await EnsureRow((MatchFormat)t.Format, t.OversMode, t.OversLimit).ConfigureAwait(false);
            if (t.FormatId is null || t.FormatId == Guid.Empty)
                t.FormatId = id;
        }

        foreach (var s in await db.Series.IgnoreQueryFilters().ToListAsync(ct).ConfigureAwait(false))
        {
            var id = await EnsureRow((MatchFormat)s.Format, s.OversMode, s.OversLimit).ConfigureAwait(false);
            if (s.FormatId is null || s.FormatId == Guid.Empty)
                s.FormatId = id;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string UniqueName(ApiDbContext db, string desired)
    {
        var name = desired;
        var n = 2;
        while (db.Formats.Local.Any(f => !f.IsDeleted && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
               || db.Formats.Any(f => !f.IsDeleted && f.Name.ToLower() == name.ToLower()))
        {
            name = $"{desired} ({n})";
            n++;
        }

        return name;
    }
}
