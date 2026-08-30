namespace JPL.Api.Contracts;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Skip,
    int Take,
    int? ActiveCount = null,
    int? InactiveCount = null);
