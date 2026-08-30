using JPL.Domain;

namespace JPL.Services;

public sealed class FormatListItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public MatchFormat Format { get; init; }
    public OversMode OversMode { get; init; }
    public int OversLimit { get; init; }
    public string DetailsLine { get; init; } = string.Empty;
    public string Hint { get; init; } = string.Empty;
}

public interface IFormatService
{
    Task<IReadOnlyList<FormatListItem>> GetAllAsync(CancellationToken ct = default);
    Task<ApiPagedResult<FormatListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default);
    Task<FormatListItem?> GetAsync(Guid id, CancellationToken ct = default);
    Task<FormatListItem> CreateAsync(string name, MatchFormat format, OversMode oversMode, int oversLimit, CancellationToken ct = default);
    Task<FormatListItem> UpdateAsync(Guid id, string name, MatchFormat format, OversMode oversMode, int oversLimit, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class FormatService : IFormatService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;

    public FormatService(IApiClient api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    public async Task<IReadOnlyList<FormatListItem>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _api.GetFormatsAsync(ct).ConfigureAwait(false);
        return list.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToItem)
            .ToList();
    }

    public async Task<ApiPagedResult<FormatListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default)
    {
        var page = await _api.GetFormatsPageAsync(skip, q, ct).ConfigureAwait(false);
        var items = (page.Items ?? []).Select(ToItem).ToList();
        return page.WithItems(items);
    }

    public async Task<FormatListItem?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _api.GetFormatAsync(id, ct).ConfigureAwait(false);
        return dto is null ? null : ToItem(dto);
    }

    public async Task<FormatListItem> CreateAsync(
        string name, MatchFormat format, OversMode oversMode, int oversLimit, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        OversRules.Validate(oversMode, oversLimit, bowlerMaxOvers: 0);
        var remote = await _api.CreateFormatAsync(ToBody(name, format, oversMode, oversLimit), ct)
            .ConfigureAwait(false);
        return ToItem(remote);
    }

    public async Task<FormatListItem> UpdateAsync(
        Guid id, string name, MatchFormat format, OversMode oversMode, int oversLimit, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        OversRules.Validate(oversMode, oversLimit, bowlerMaxOvers: 0);
        var remote = await _api.UpdateFormatAsync(id, ToBody(name, format, oversMode, oversLimit), ct)
            .ConfigureAwait(false);
        return ToItem(remote);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await _api.DeleteFormatAsync(id, ct).ConfigureAwait(false);
    }

    private static object ToBody(string name, MatchFormat format, OversMode oversMode, int oversLimit)
        => new
        {
            name = RequireName(name),
            format = (byte)format,
            oversMode = (byte)oversMode,
            oversLimit = OversRules.StoredOversLimit(oversMode, oversLimit)
        };

    private static FormatListItem ToItem(ApiFormatDto dto)
    {
        var format = (MatchFormat)dto.Format;
        var mode = (OversMode)dto.OversMode;
        return new FormatListItem
        {
            Id = dto.Id,
            Name = dto.Name,
            Format = format,
            OversMode = mode,
            OversLimit = dto.OversLimit,
            DetailsLine = string.IsNullOrWhiteSpace(dto.DetailsLine)
                ? FormatRules.DetailsLine(format, mode, dto.OversLimit)
                : dto.DetailsLine,
            Hint = string.IsNullOrWhiteSpace(dto.Hint)
                ? FormatRules.Hint(format, mode, dto.OversLimit)
                : dto.Hint
        };
    }

    private static string RequireName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) throw new ArgumentException("Format name is required.");
        if (n.Length > 160) throw new ArgumentException("Format name must be 160 characters or fewer.");
        return n;
    }
}
