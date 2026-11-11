namespace JPL.Grounds.Api.Services;

public static class Paging
{
    public const int DefaultTake = 15;
    public const int MaxTake = 50;

    public static int ClampTake(int take)
    {
        if (take <= 0)
            return DefaultTake;
        return take > MaxTake ? MaxTake : take;
    }

    public static int ClampSkip(int skip) => skip < 0 ? 0 : skip;

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
}
