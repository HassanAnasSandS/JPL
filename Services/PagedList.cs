namespace JPL.Services;

public sealed record ApiPagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Skip = 0,
    int Take = ListPaging.PageSize,
    int? ActiveCount = null,
    int? InactiveCount = null)
{
    public bool HasMore => Skip + (Items?.Count ?? 0) < TotalCount;

    public ApiPagedResult<TDest> WithItems<TDest>(IReadOnlyList<TDest> items)
        => new(items, TotalCount, Skip, Take, ActiveCount, InactiveCount);
}

public static class ListPaging
{
    public const int PageSize = 5;

    public static string Query(int skip, params (string Key, string? Value)[] extra)
    {
        var parts = new List<string>
        {
            $"skip={skip}",
            $"take={PageSize}"
        };
        foreach (var (key, value) in extra)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value.Trim())}");
        }
        return "?" + string.Join("&", parts);
    }

    public static void Append<T, TKey>(
        System.Collections.ObjectModel.ObservableCollection<T> items,
        IEnumerable<T> next,
        Func<T, TKey> key)
        where TKey : notnull
    {
        var have = new HashSet<TKey>(items.Select(key));
        foreach (var row in next)
        {
            if (have.Add(key(row)))
                items.Add(row);
        }
    }
}
