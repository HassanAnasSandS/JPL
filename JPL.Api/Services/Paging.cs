using JPL.Api.Contracts;

namespace JPL.Api.Services;

public static class Paging
{
    public const int DefaultTake = 5;
    public const int MaxTake = 50;

    public static (int Skip, int Take) Clamp(int skip, int take)
    {
        if (skip < 0)
            skip = 0;
        if (take <= 0)
            take = DefaultTake;
        else if (take > MaxTake)
            take = MaxTake;
        return (skip, take);
    }

    public static string? LikePattern(string? q)
    {
        var s = q?.Trim();
        if (string.IsNullOrEmpty(s))
            return null;
        s = s.Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
        return $"%{s}%";
    }

    public static PagedResult<T> Slice<T>(
        IReadOnlyList<T> source,
        int skip,
        int take,
        Func<T, string>? name = null,
        string? q = null)
    {
        (skip, take) = Clamp(skip, take);
        IEnumerable<T> rows = source;
        if (!string.IsNullOrWhiteSpace(q) && name is not null)
        {
            var f = q.Trim();
            rows = rows.Where(x => name(x).Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        var list = rows as IList<T> ?? rows.ToList();
        var total = list.Count;
        var page = list.Skip(skip).Take(take).ToList();
        return new PagedResult<T>(page, total, skip, take);
    }
}
