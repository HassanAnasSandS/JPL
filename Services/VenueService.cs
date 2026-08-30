namespace JPL.Services;

public sealed class VenueListItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public interface IVenueService
{
    Task<IReadOnlyList<VenueListItem>> GetAllAsync(CancellationToken ct = default);
    Task<ApiPagedResult<VenueListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default);
    Task<VenueListItem?> GetAsync(Guid id, CancellationToken ct = default);
    Task<VenueListItem> CreateAsync(string name, CancellationToken ct = default);
    Task<VenueListItem> UpdateAsync(Guid id, string name, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class VenueService : IVenueService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;

    public VenueService(IApiClient api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    public async Task<IReadOnlyList<VenueListItem>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _api.GetVenuesAsync(ct).ConfigureAwait(false);
        return list.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Select(v => new VenueListItem { Id = v.Id, Name = v.Name })
            .ToList();
    }

    public async Task<ApiPagedResult<VenueListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default)
    {
        var page = await _api.GetVenuesPageAsync(skip, q, ct).ConfigureAwait(false);
        var items = (page.Items ?? []).Select(v => new VenueListItem { Id = v.Id, Name = v.Name }).ToList();
        return page.WithItems(items);
    }

    public async Task<VenueListItem?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _api.GetVenueAsync(id, ct).ConfigureAwait(false);
        return dto is null ? null : new VenueListItem { Id = dto.Id, Name = dto.Name };
    }

    public async Task<VenueListItem> CreateAsync(string name, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var n = RequireName(name);
        var remote = await _api.CreateVenueAsync(new { name = n }, ct).ConfigureAwait(false);
        return new VenueListItem { Id = remote.Id, Name = remote.Name };
    }

    public async Task<VenueListItem> UpdateAsync(Guid id, string name, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var n = RequireName(name);
        var remote = await _api.UpdateVenueAsync(id, new { name = n }, ct).ConfigureAwait(false);
        return new VenueListItem { Id = remote.Id, Name = remote.Name };
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await _api.DeleteVenueAsync(id, ct).ConfigureAwait(false);
    }

    private static string RequireName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) throw new ArgumentException("Venue name is required.");
        if (n.Length > 160) throw new ArgumentException("Venue name must be 160 characters or fewer.");
        return n;
    }
}
