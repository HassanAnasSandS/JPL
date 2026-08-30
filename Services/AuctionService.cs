using JPL.Domain;

namespace JPL.Services;

public sealed record AuctionListItem(
    Guid Id,
    string Title,
    DateTime AuctionDateUtc,
    int CaptainCount,
    int PlayerCount,
    int SoldCount,
    int UnsoldCount,
    int TotalSoldAmount);

public sealed record AuctionCaptainRow(Guid PlayerId, string PlayerName, int WalletAmount);

public sealed record AuctionLotRow(
    Guid Id,
    Guid PlayerId,
    string PlayerName,
    int BasePrice,
    bool IsSold,
    int? SoldPrice,
    Guid? SoldToCaptainPlayerId,
    string? SoldToCaptainName);

public sealed record AuctionDetail(
    Guid Id,
    string Title,
    DateTime AuctionDateUtc,
    string? Notes,
    IReadOnlyList<AuctionCaptainRow> Captains,
    IReadOnlyList<AuctionLotRow> Lots);

public sealed record AuctionLotDraft(
    Guid PlayerId,
    int BasePrice,
    bool IsSold,
    int? SoldPrice,
    Guid? SoldToCaptainPlayerId);

public sealed record AuctionCaptainDraft(Guid PlayerId, int WalletAmount);

public sealed record AuctionDraft(
    string Title,
    DateTime AuctionDateUtc,
    string? Notes,
    IReadOnlyList<AuctionCaptainDraft> Captains,
    IReadOnlyList<AuctionLotDraft> Lots);

/// <summary>JSON-friendly auction career totals (API + local report).</summary>
public sealed class AuctionReportRow
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int AuctionsParticipated { get; set; }
    public int TimesSold { get; set; }
    public int TimesUnsold { get; set; }
    public int TotalBasePrice { get; set; }
    public int TotalSoldAmount { get; set; }
    public double AverageSoldPrice { get; set; }
    public int HighestSoldPrice { get; set; }
    public int TimesAsCaptain { get; set; }
    public int TotalWalletAssigned { get; set; }
    public int TotalSpentAsCaptain { get; set; }
    public int WalletRemaining { get; set; }

    public bool HasCaptainWallet => TimesAsCaptain > 0;
    public string WalletLine =>
        $"Captain ×{TimesAsCaptain} · Wallet {TotalWalletAssigned:N0} · Spent {TotalSpentAsCaptain:N0} · Left {WalletRemaining:N0}";
    public string AverageSoldPriceText => TimesSold == 0 ? "-" : AverageSoldPrice.ToString("0.#");
}

/// <summary>One auction lot appearance for a player detail screen.</summary>
public sealed class AuctionPlayerLotEvent
{
    public Guid AuctionId { get; set; }
    public string AuctionTitle { get; set; } = string.Empty;
    public DateTime AuctionDateUtc { get; set; }
    public int BasePrice { get; set; }
    public bool IsSold { get; set; }
    public int? SoldPrice { get; set; }
    public string? SoldToCaptainName { get; set; }

    public string DateText => AuctionDateUtc.ToString("dd MMM yyyy");
    public string ResultLine => IsSold
        ? $"Sold {SoldPrice:N0} → {SoldToCaptainName ?? "?"}"
        : "Unsold";
    public string BaseLine => $"Base {BasePrice:N0}";
}

/// <summary>One auction where the player was a captain/buyer.</summary>
public sealed class AuctionPlayerCaptainEvent
{
    public Guid AuctionId { get; set; }
    public string AuctionTitle { get; set; } = string.Empty;
    public DateTime AuctionDateUtc { get; set; }
    public int WalletAmount { get; set; }
    public int Spent { get; set; }
    public int PlayersBought { get; set; }

    public int Remaining => WalletAmount - Spent;
    public string DateText => AuctionDateUtc.ToString("dd MMM yyyy");
    public string WalletLine =>
        $"Wallet {WalletAmount:N0} · Spent {Spent:N0} · Left {Remaining:N0} · Bought {PlayersBought}";
}

public sealed class AuctionPlayerDetail
{
    public required AuctionReportRow Summary { get; init; }
    public IReadOnlyList<AuctionPlayerLotEvent> Lots { get; init; } = Array.Empty<AuctionPlayerLotEvent>();
    public IReadOnlyList<AuctionPlayerCaptainEvent> CaptainEvents { get; init; } = Array.Empty<AuctionPlayerCaptainEvent>();
}

public interface IAuctionService
{
    Task<IReadOnlyList<AuctionListItem>> GetAllAsync(CancellationToken ct = default);
    Task<ApiPagedResult<AuctionListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default);
    Task<AuctionDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Auction> CreateAsync(AuctionDraft draft, CancellationToken ct = default);
    Task<Auction> UpdateAsync(Guid id, AuctionDraft draft, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AuctionReportRow>> GetAllTimeReportAsync(CancellationToken ct = default);
    Task<AuctionPlayerDetail?> GetPlayerDetailAsync(Guid playerId, CancellationToken ct = default);
}

public sealed class AuctionService : IAuctionService
{
    private readonly IAuthService _auth;
    private readonly IApiClient _api;

    public AuctionService(IAuthService auth, IApiClient api)
    {
        _auth = auth;
        _api = api;
    }

    public async Task<IReadOnlyList<AuctionListItem>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _api.GetAuctionsAsync(ct).ConfigureAwait(false);
        return list
            .Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.AuctionDateUtc)
            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AuctionListItem(
                a.Id,
                a.Title,
                a.AuctionDateUtc,
                a.Captains.Count,
                a.Lots.Count,
                a.Lots.Count(l => l.IsSold),
                a.Lots.Count(l => !l.IsSold),
                a.Lots.Where(l => l.IsSold).Sum(l => l.SoldPrice ?? 0)))
            .ToList();
    }

    public async Task<ApiPagedResult<AuctionListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default)
    {
        var page = await _api.GetAuctionsPageAsync(skip, q, ct).ConfigureAwait(false);
        var items = (page.Items ?? [])
            .Where(a => !a.IsDeleted)
            .Select(a => new AuctionListItem(
                a.Id,
                a.Title,
                a.AuctionDateUtc,
                a.Captains.Count,
                a.Lots.Count,
                a.Lots.Count(l => l.IsSold),
                a.Lots.Count(l => !l.IsSold),
                a.Lots.Where(l => l.IsSold).Sum(l => l.SoldPrice ?? 0)))
            .ToList();
        return page.WithItems(items);
    }

    public async Task<AuctionDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var remote = (await _api.GetAuctionsAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(a => a.Id == id && !a.IsDeleted);
        if (remote is null)
            return null;

        var names = await PlayerNamesAsync(ct).ConfigureAwait(false);
        return ToDetail(remote, names);
    }

    public async Task<Auction> CreateAsync(AuctionDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        Validate(draft);

        var id = Guid.NewGuid();
        var now = DateTime.Now;
        var entity = BuildEntity(id, draft, now, now, version: 1);
        var remote = await _api.CreateAuctionAsync(ToApiBody(entity), ct).ConfigureAwait(false);
        return ToEntity(remote);
    }

    public async Task<Auction> UpdateAsync(Guid id, AuctionDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        Validate(draft);

        var captains = draft.Captains
            .GroupBy(x => x.PlayerId)
            .Select(g => g.Last())
            .ToList();
        var lots = draft.Lots
            .GroupBy(x => x.PlayerId)
            .Select(g => g.Last())
            .Select(l => ToLot(id, l))
            .ToList();

        var body = new
        {
            Id = id,
            Title = draft.Title.Trim(),
            AuctionDateUtc = draft.AuctionDateUtc,
            Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim(),
            Captains = captains.Select(c => new { c.PlayerId, c.WalletAmount }).ToList(),
            Lots = lots.Select(l => new
            {
                l.Id,
                l.PlayerId,
                l.BasePrice,
                l.IsSold,
                l.SoldPrice,
                l.SoldToCaptainPlayerId
            }).ToList()
        };

        var remote = await _api.UpdateAuctionAsync(id, body, ct).ConfigureAwait(false);
        return ToEntity(remote);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await _api.DeleteAuctionAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuctionReportRow>> GetAllTimeReportAsync(CancellationToken ct = default)
    {
        try
        {
            var remote = await _api.GetAuctionReportAsync(ct).ConfigureAwait(false);
            if (remote.Count > 0)
                return remote;
        }
        catch
        {
            /* build from auction list */
        }

        var auctions = await LoadAuctionEntitiesAsync(ct).ConfigureAwait(false);
        var names = await PlayerNamesAsync(ct).ConfigureAwait(false);
        return BuildLocalReport(auctions, names);
    }

    public async Task<AuctionPlayerDetail?> GetPlayerDetailAsync(Guid playerId, CancellationToken ct = default)
    {
        var auctions = await LoadAuctionEntitiesAsync(ct).ConfigureAwait(false);
        var involved = auctions
            .Where(a => a.Captains.Any(c => c.PlayerId == playerId) || a.Lots.Any(l => l.PlayerId == playerId))
            .ToList();
        if (involved.Count == 0)
            return null;

        var names = await PlayerNamesAsync(ct).ConfigureAwait(false);
        var report = BuildLocalReport(involved, names)
            .FirstOrDefault(r => r.PlayerId == playerId);
        if (report is null)
            return null;

        var lots = involved
            .SelectMany(a => a.Lots
                .Where(l => l.PlayerId == playerId)
                .Select(l => new AuctionPlayerLotEvent
                {
                    AuctionId = a.Id,
                    AuctionTitle = a.Title,
                    AuctionDateUtc = a.AuctionDateUtc,
                    BasePrice = l.BasePrice,
                    IsSold = l.IsSold,
                    SoldPrice = l.SoldPrice,
                    SoldToCaptainName = l.SoldToCaptainPlayerId is Guid cid
                        ? names.GetValueOrDefault(cid, "?")
                        : null
                }))
            .OrderByDescending(x => x.AuctionDateUtc)
            .ThenBy(x => x.AuctionTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var captainEvents = involved
            .Where(a => a.Captains.Any(c => c.PlayerId == playerId))
            .Select(a =>
            {
                var cap = a.Captains.First(c => c.PlayerId == playerId);
                var bought = a.Lots.Where(l => l.IsSold && l.SoldToCaptainPlayerId == playerId).ToList();
                return new AuctionPlayerCaptainEvent
                {
                    AuctionId = a.Id,
                    AuctionTitle = a.Title,
                    AuctionDateUtc = a.AuctionDateUtc,
                    WalletAmount = cap.WalletAmount,
                    Spent = bought.Sum(l => l.SoldPrice ?? 0),
                    PlayersBought = bought.Count
                };
            })
            .OrderByDescending(x => x.AuctionDateUtc)
            .ThenBy(x => x.AuctionTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AuctionPlayerDetail
        {
            Summary = report,
            Lots = lots,
            CaptainEvents = captainEvents
        };
    }

    private async Task<Dictionary<Guid, string>> PlayerNamesAsync(CancellationToken ct)
    {
        var players = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
        return players.Where(p => !p.IsDeleted).ToDictionary(p => p.Id, p => p.FullName);
    }

    private async Task<List<Auction>> LoadAuctionEntitiesAsync(CancellationToken ct)
    {
        var remote = await _api.GetAuctionsAsync(ct).ConfigureAwait(false);
        return remote.Where(a => !a.IsDeleted).Select(ToEntity).ToList();
    }

    private static AuctionDetail ToDetail(ApiAuctionDto a, IReadOnlyDictionary<Guid, string> names)
        => new(
            a.Id,
            a.Title,
            a.AuctionDateUtc,
            a.Notes,
            a.Captains
                .OrderBy(c => names.GetValueOrDefault(c.PlayerId, "?"))
                .Select(c => new AuctionCaptainRow(
                    c.PlayerId,
                    names.GetValueOrDefault(c.PlayerId, "?"),
                    c.WalletAmount))
                .ToList(),
            a.Lots
                .OrderBy(l => names.GetValueOrDefault(l.PlayerId, "?"))
                .Select(l => new AuctionLotRow(
                    l.Id,
                    l.PlayerId,
                    names.GetValueOrDefault(l.PlayerId, "?"),
                    l.BasePrice,
                    l.IsSold,
                    l.SoldPrice,
                    l.SoldToCaptainPlayerId,
                    l.SoldToCaptainPlayerId is Guid cid
                        ? names.GetValueOrDefault(cid, "?")
                        : null))
                .ToList());

    private static Auction ToEntity(ApiAuctionDto dto)
    {
        var row = new Auction { Id = dto.Id };
        ApplyDtoScalars(row, dto);
        RebuildChildren(row, dto);
        return row;
    }

    private static void Validate(AuctionDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Title))
            throw new ArgumentException("Auction title is required.");
        if (draft.Title.Trim().Length > 160)
            throw new ArgumentException("Auction title must be 160 characters or fewer.");
        if (draft.Captains.Count < 2)
            throw new InvalidOperationException("Select at least 2 captains.");
        if (draft.Lots.Count == 0)
            throw new InvalidOperationException("Select at least 1 player for the auction.");

        var wallets = draft.Captains
            .GroupBy(c => c.PlayerId)
            .ToDictionary(g => g.Key, g => g.Last().WalletAmount);
        if (wallets.Values.Any(w => w < 0))
            throw new ArgumentException("Captain wallet cannot be negative.");

        var spent = wallets.Keys.ToDictionary(id => id, _ => 0);
        var seenPlayers = new HashSet<Guid>();
        foreach (var lot in draft.Lots)
        {
            if (!seenPlayers.Add(lot.PlayerId))
                throw new InvalidOperationException("Duplicate player in auction list.");
            if (lot.BasePrice < 0)
                throw new ArgumentException("Base price cannot be negative.");
            if (lot.IsSold)
            {
                if (lot.SoldPrice is null || lot.SoldPrice < 0)
                    throw new ArgumentException("Sold players need a valid sold price.");
                if (lot.SoldToCaptainPlayerId is not Guid cap || !wallets.ContainsKey(cap))
                    throw new InvalidOperationException("Sold player must be assigned to a selected captain.");
                spent[cap] += lot.SoldPrice.Value;
            }
        }

        foreach (var (capId, total) in spent)
        {
            if (total > wallets[capId])
            {
                throw new InvalidOperationException(
                    $"A captain spent {total:N0} but wallet is only {wallets[capId]:N0}.");
            }
        }
    }

    private static Auction BuildEntity(
        Guid id, AuctionDraft draft, DateTime created, DateTime updated, long version)
        => new()
        {
            Id = id,
            Title = draft.Title.Trim(),
            AuctionDateUtc = draft.AuctionDateUtc,
            Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim(),
            Version = version,
            CreatedAtUtc = created,
            UpdatedAtUtc = updated,
            Captains = draft.Captains
                .GroupBy(c => c.PlayerId)
                .Select(g => g.Last())
                .Select(c => new AuctionCaptain
                {
                    AuctionId = id,
                    PlayerId = c.PlayerId,
                    WalletAmount = Math.Max(0, c.WalletAmount)
                })
                .ToList(),
            Lots = draft.Lots.Select(l => ToLot(id, l)).ToList()
        };

    private static AuctionLot ToLot(Guid auctionId, AuctionLotDraft l)
        => new()
        {
            Id = Guid.NewGuid(),
            AuctionId = auctionId,
            PlayerId = l.PlayerId,
            BasePrice = Math.Max(0, l.BasePrice),
            IsSold = l.IsSold,
            SoldPrice = l.IsSold ? Math.Max(0, l.SoldPrice ?? 0) : null,
            SoldToCaptainPlayerId = l.IsSold ? l.SoldToCaptainPlayerId : null
        };

    private static object ToApiBody(Auction a) => new
    {
        a.Id,
        a.Title,
        a.AuctionDateUtc,
        a.Notes,
        Captains = a.Captains.Select(c => new { c.PlayerId, c.WalletAmount }).ToList(),
        Lots = a.Lots.Select(l => new
        {
            l.Id,
            l.PlayerId,
            l.BasePrice,
            l.IsSold,
            l.SoldPrice,
            l.SoldToCaptainPlayerId
        }).ToList()
    };

    private static void ApplyDtoScalars(Auction row, ApiAuctionDto dto)
    {
        row.Title = dto.Title;
        row.AuctionDateUtc = dto.AuctionDateUtc;
        row.Notes = dto.Notes;
        row.Version = dto.Version;
        row.CreatedAtUtc = dto.CreatedAtUtc;
        row.UpdatedAtUtc = dto.UpdatedAtUtc;
        row.IsDeleted = dto.IsDeleted;
    }

    private static void RebuildChildren(Auction row, ApiAuctionDto dto)
    {
        row.Captains.Clear();
        foreach (var c in dto.Captains.GroupBy(x => x.PlayerId).Select(g => g.Last()))
        {
            row.Captains.Add(new AuctionCaptain
            {
                AuctionId = row.Id,
                PlayerId = c.PlayerId,
                WalletAmount = Math.Max(0, c.WalletAmount)
            });
        }

        row.Lots.Clear();
        foreach (var l in dto.Lots)
        {
            row.Lots.Add(new AuctionLot
            {
                Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id,
                AuctionId = row.Id,
                PlayerId = l.PlayerId,
                BasePrice = l.BasePrice,
                IsSold = l.IsSold,
                SoldPrice = l.IsSold ? l.SoldPrice : null,
                SoldToCaptainPlayerId = l.IsSold ? l.SoldToCaptainPlayerId : null
            });
        }
    }

    private static List<AuctionReportRow> BuildLocalReport(
        IReadOnlyList<Auction> auctions,
        IReadOnlyDictionary<Guid, string> names)
    {
        var map = new Dictionary<Guid, AuctionReportRow>();

        AuctionReportRow Row(Guid id)
        {
            if (!map.TryGetValue(id, out var row))
            {
                row = new AuctionReportRow
                {
                    PlayerId = id,
                    PlayerName = names.GetValueOrDefault(id, "?")
                };
                map[id] = row;
            }
            return row;
        }

        foreach (var a in auctions)
        {
            foreach (var c in a.Captains)
            {
                var row = Row(c.PlayerId);
                row.TimesAsCaptain++;
                row.TotalWalletAssigned += c.WalletAmount;
                row.TotalSpentAsCaptain += a.Lots
                    .Where(l => l.IsSold && l.SoldToCaptainPlayerId == c.PlayerId)
                    .Sum(l => l.SoldPrice ?? 0);
            }

            foreach (var g in a.Lots.GroupBy(l => l.PlayerId))
            {
                var row = Row(g.Key);
                row.AuctionsParticipated++;
                row.TotalBasePrice += g.Sum(x => x.BasePrice);
                var sold = g.Where(x => x.IsSold && x.SoldPrice is int).Select(x => x.SoldPrice!.Value).ToList();
                row.TimesSold += sold.Count;
                row.TimesUnsold += g.Count(x => !x.IsSold);
                row.TotalSoldAmount += sold.Sum();
                if (sold.Count > 0)
                    row.HighestSoldPrice = Math.Max(row.HighestSoldPrice, sold.Max());
            }
        }

        foreach (var row in map.Values)
        {
            row.WalletRemaining = row.TotalWalletAssigned - row.TotalSpentAsCaptain;
            row.AverageSoldPrice = row.TimesSold == 0
                ? 0
                : Math.Round(row.TotalSoldAmount * 1.0 / row.TimesSold, 1);
        }

        return map.Values
            .OrderByDescending(r => r.TotalSoldAmount)
            .ThenByDescending(r => r.TotalWalletAssigned)
            .ThenBy(r => r.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
