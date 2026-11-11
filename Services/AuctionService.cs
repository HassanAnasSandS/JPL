using System.Text.Json;
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
    int TotalSoldAmount,
    AuctionKind Kind,
    AuctionRunMode RunMode,
    string KindLabel,
    string ModeLabel,
    bool CanEditSetup,
    bool CanEnterAsCaptain,
    bool CanDelete);

public sealed record AuctionAccess(
    bool IsCreator,
    bool IsUpdateAdmin,
    bool CanFullEdit,
    bool CanOperate,
    bool CanDelete,
    Guid? DeviceCaptainPlayerId,
    bool CanEnterAsCaptain = false);

public sealed record AuctionBidRow(
    Guid CaptainPlayerId,
    string CaptainName,
    int Amount,
    bool IsJoker,
    bool IsHidden)
{
    public string DisplayLine => IsHidden
        ? $"{CaptainName} · bid in"
        : IsJoker
            ? $"{CaptainName}  {Amount:N0}  · JOKER"
            : $"{CaptainName}  {Amount:N0}";
}

public sealed record AuctionRoundInfo(
    Guid PlayerId,
    string PlayerName,
    int BasePrice,
    bool IsBlind,
    bool IsRevealed,
    int MinNextBid,
    int BidCount,
    int CaptainCount,
    IReadOnlyList<AuctionBidRow> Bids);

public sealed record AuctionCaptainRow(
    Guid PlayerId,
    string PlayerName,
    string TeamName,
    int WalletAmount,
    string OperatorUsername,
    int JokersLeft,
    int BoughtCount,
    int RemainingSlots,
    int RemainingWallet,
    int MaxBidThisRound);

public sealed record AuctionLotRow(
    Guid Id,
    Guid PlayerId,
    string PlayerName,
    int BasePrice,
    bool IsSold,
    int? SoldPrice,
    Guid? SoldToCaptainPlayerId,
    string? SoldToCaptainName,
    string GroupName = "");

public sealed record AuctionDetail(
    Guid Id,
    string Title,
    DateTime AuctionDateUtc,
    string? Notes,
    AuctionKind Kind,
    AuctionRunMode RunMode,
    Guid? CreatedByUserId,
    string? CreatedByUsername,
    int PlayersPerCaptain,
    int JokersPerCaptain,
    int BlindBidRoundsAllowed,
    int BlindRoundsUsed,
    IReadOnlyList<string> UpdateAdminUsernames,
    IReadOnlyList<AuctionCaptainClaim> Claims,
    AuctionAccess Access,
    AuctionRoundInfo? Round,
    Guid? TurnCaptainId,
    string TurnCaptainName,
    IReadOnlyList<AuctionDraftGroup> DraftGroups,
    IReadOnlyList<Guid> PickOrder,
    IReadOnlyList<AuctionCaptainRow> Captains,
    IReadOnlyList<AuctionLotRow> Lots,
    IReadOnlyList<int>? BlindRoundNumbers = null,
    int DraftTurnIndex = 0);

public sealed record AuctionLotDraft(
    Guid PlayerId,
    int BasePrice,
    bool IsSold,
    int? SoldPrice,
    Guid? SoldToCaptainPlayerId,
    string GroupName = "");

public sealed record AuctionCaptainDraft(
    Guid PlayerId,
    int WalletAmount,
    string TeamName,
    string OperatorUsername = "");

public enum AuctionSaveStage
{
    General = 0,
    Captains = 1,
    Complete = 2
}

public sealed record AuctionDraft(
    string Title,
    DateTime AuctionDateUtc,
    string? Notes,
    AuctionKind Kind,
    AuctionRunMode RunMode,
    int PlayersPerCaptain,
    int JokersPerCaptain,
    int BlindBidRoundsAllowed,
    IReadOnlyList<string> UpdateAdminUsernames,
    IReadOnlyList<AuctionCaptainDraft> Captains,
    IReadOnlyList<AuctionLotDraft> Lots,
    IReadOnlyList<AuctionDraftGroup>? DraftGroups = null,
    IReadOnlyList<Guid>? PickOrder = null,
    IReadOnlyList<AuctionCaptainClaim>? Claims = null,
    AuctionSaveStage Stage = AuctionSaveStage.Complete,
    IReadOnlyList<int>? BlindRoundNumbers = null);

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
    public int PlayersBoughtAsCaptain { get; set; }

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
    public string GroupName { get; set; } = string.Empty;
    public bool IsDrafting { get; set; }
    public string KindLabel { get; set; } = string.Empty;

    public string DateText => AuctionDateUtc.ToString("dd MMM yyyy");
    public string ResultLine => IsSold
        ? (IsDrafting
            ? $"Picked → {SoldToCaptainName ?? "?"}"
            : $"Sold {SoldPrice:N0} → {SoldToCaptainName ?? "?"}")
        : (IsDrafting ? "Not picked" : "Unsold");
    public string BaseLine => !string.IsNullOrWhiteSpace(GroupName)
        ? GroupName
        : IsDrafting
            ? KindLabel
            : $"Base {BasePrice:N0}";
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
    public bool IsDrafting { get; set; }
    public string KindLabel { get; set; } = string.Empty;

    public int Remaining => WalletAmount - Spent;
    public string DateText => AuctionDateUtc.ToString("dd MMM yyyy");
    public string WalletLine => IsDrafting
        ? $"Picked {PlayersBought} player{(PlayersBought == 1 ? "" : "s")}"
        : $"Wallet {WalletAmount:N0} · Spent {Spent:N0} · Left {Remaining:N0} · Bought {PlayersBought}";
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
    Task<AuctionDetail> ClaimCaptainByCodeAsync(Guid auctionId, string code, CancellationToken ct = default);
    Task<AuctionDetail> OpenRoundAsync(Guid auctionId, Guid playerId, bool asBlind, CancellationToken ct = default);
    Task<AuctionDetail> PlaceBidAsync(Guid auctionId, Guid captainPlayerId, int amount, bool useJoker, CancellationToken ct = default);
    Task<AuctionDetail> RevealBlindAsync(Guid auctionId, CancellationToken ct = default);
    Task<AuctionDetail> CloseRoundAsync(Guid auctionId, bool unsold, Guid? simpleWinnerId, int? simplePrice, CancellationToken ct = default);
    Task<AuctionDetail> DraftPickAsync(Guid auctionId, Guid playerId, CancellationToken ct = default);
    Task<AuctionDetail> UndoLastPickAsync(Guid auctionId, CancellationToken ct = default);
    Task<(int AuctionCount, IReadOnlyList<AuctionReportRow> Rows)> GetAllTimeReportAsync(AuctionKind kind, CancellationToken ct = default);
    Task<AuctionPlayerDetail?> GetPlayerDetailAsync(Guid playerId, AuctionKind kind, CancellationToken ct = default);
}

public sealed class AuctionService : IAuctionService
{
    private const string DevicePrefKey = "jpl.auction.deviceId";
    private const string LocalFileName = "jpl-auctions-local.json";
    private const int NotesMax = 500;
    private const int TeamNameMax = 40;
    private const int AccessCodeMin = 4;
    private const int AccessCodeMax = 12;
    private static readonly object LocalLock = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static Dictionary<Guid, string>? _playerNameCache;
    private static DateTime _playerNameCacheAtUtc;

    private readonly IAuthService _auth;
    private readonly IApiClient _api;
    private readonly ILeagueContext _league;

    public AuctionService(IAuthService auth, IApiClient api, ILeagueContext league)
    {
        _auth = auth;
        _api = api;
        _league = league;
    }

    public async Task<IReadOnlyList<AuctionListItem>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await LoadVisibleEntitiesAsync(ct).ConfigureAwait(false);
        return list
            .OrderByDescending(a => a.AuctionDateUtc)
            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .Select(ToListItem)
            .ToList();
    }

    public async Task<ApiPagedResult<AuctionListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default)
    {
        var locals = LoadLocalEntities()
            .Where(a => !a.IsDeleted && VisibleToCurrent(a) && TitleMatches(a.Title, q))
            .ToList();
        var localCount = locals.Count;
        var apiSkip = skip <= 0 ? 0 : Math.Max(0, skip - localCount);

        var page = await _api.GetAuctionsPageAsync(apiSkip, q, ct).ConfigureAwait(false);
        var remote = (page.Items ?? [])
            .Where(a => !a.IsDeleted)
            .Select(ToEntity)
            .Where(VisibleToCurrent)
            .Select(ToListItem)
            .ToList();

        if (skip <= 0)
        {
            var localItems = locals
                .OrderByDescending(a => a.AuctionDateUtc)
                .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                .Select(ToListItem)
                .ToList();
            var merged = localItems.Concat(remote)
                .GroupBy(x => x.Id)
                .Select(g => g.First())
                .ToList();
            return new ApiPagedResult<AuctionListItem>(
                merged,
                page.TotalCount + localCount,
                skip,
                page.Take);
        }

        return page.WithItems(remote) with { TotalCount = page.TotalCount + localCount };
    }

    public async Task<AuctionDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await LoadEntityAsync(id, ct).ConfigureAwait(false);
        if (entity is null || !VisibleToCurrent(entity))
            return null;
        var names = await PlayerNamesAsync(ct).ConfigureAwait(false);
        return ToDetail(entity, names);
    }

    public async Task<Auction> CreateAsync(AuctionDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var session = RequireSession();
        Validate(draft, existingClaims: null);

        var id = Guid.NewGuid();
        var now = DateTime.Now;
        var entity = BuildEntity(id, draft, now, now, version: 1, session, existingClaims: null);
        entity.RunMode = AuctionKindText.RunModeFor(draft.Kind);
        PackNotes(entity);
        // Basic stays on-device; incomplete drafts of other kinds stay local until Complete.
        if (AuctionKindText.IsBasic(draft.Kind) || draft.Stage < AuctionSaveStage.Complete)
        {
            SaveLocal(entity);
            return entity;
        }

        var remote = await _api.CreateAuctionAsync(ToApiBody(entity), ct).ConfigureAwait(false);
        DeleteLocal(entity.Id);
        return ToEntity(remote);
    }

    public async Task<Auction> UpdateAsync(Guid id, AuctionDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var existing = await LoadEntityAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Auction/Drafting not found.");
        if (!VisibleToCurrent(existing))
            throw new InvalidOperationException("Auction/Drafting not found.");

        var access = AccessFor(existing);
        if (!access.CanFullEdit)
            throw new InvalidOperationException("Only the creator admin can update this Auction/Drafting.");

        Validate(draft, existing.Claims);

        var entity = BuildEntity(
            id, draft, existing.CreatedAtUtc, DateTime.Now, existing.Version + 1,
            sessionOverride: null, existing.Claims);
        entity.CreatedByUserId = existing.CreatedByUserId;
        entity.CreatedByUsername = existing.CreatedByUsername;
        entity.Kind = draft.Kind;
        entity.RunMode = AuctionKindText.RunModeFor(draft.Kind);
        entity.PlayersPerCaptain = Math.Max(1, draft.PlayersPerCaptain);
        entity.JokersPerCaptain = AuctionKindText.IsAdvance(draft.Kind) ? Math.Max(0, draft.JokersPerCaptain) : 0;
        ApplyBlindSettings(entity, draft);
        entity.BlindRoundsUsed = existing.BlindRoundsUsed;
        entity.DraftTurnIndex = existing.DraftTurnIndex;
        entity.AwardedPlayerIds = existing.AwardedPlayerIds.ToList();
        entity.PickOrder = (draft.PickOrder is { Count: > 0 } po
            ? po.ToList()
            : existing.PickOrder.ToList());
        entity.DraftGroups = AuctionKindText.IsAdvanceDrafting(draft.Kind)
            ? (draft.DraftGroups ?? existing.DraftGroups)
                .Select(g => new AuctionDraftGroup { Name = g.Name.Trim(), MaxPerCaptain = g.MaxPerCaptain })
                .Where(g => g.Name.Length > 0)
                .ToList()
            : new List<AuctionDraftGroup>();
        entity.CurrentRound = existing.CurrentRound;
        entity.UpdateAdminUsernames = new List<string>();
        entity.Claims = existing.Claims
            .GroupBy(c => c.CaptainPlayerId)
            .Select(g => g.Last())
            .ToList();
        if (existing.Lots.All(l => !l.IsSold) && existing.CurrentRound is null)
        {
            foreach (var c in entity.Captains)
                c.JokersLeft = entity.JokersPerCaptain;
        }
        else
        {
            foreach (var c in entity.Captains)
            {
                var old = existing.Captains.FirstOrDefault(x => x.PlayerId == c.PlayerId);
                if (old is not null)
                    c.JokersLeft = old.JokersLeft;
            }
        }

        PackNotes(entity);
        // Basic Auction: local file only — never push/pull on Save.
        if (AuctionKindText.IsBasic(draft.Kind))
        {
            SaveLocal(entity);
            return entity;
        }

        if (LocalHas(id))
        {
            if (draft.Stage < AuctionSaveStage.Complete)
            {
                SaveLocal(entity);
                return entity;
            }

            var created = await _api.CreateAuctionAsync(ToApiBody(entity), ct).ConfigureAwait(false);
            DeleteLocal(id);
            return ToEntity(created);
        }

        await PersistEntityAsync(entity, ct).ConfigureAwait(false);
        return entity;
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var existing = await LoadEntityAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Auction/Drafting not found.");
        if (!AccessFor(existing).CanDelete)
            throw new InvalidOperationException("Only the creator admin can delete this Auction/Drafting.");

        if (LocalHas(id))
        {
            DeleteLocal(id);
            // Basic may also have an old cloud copy from before local-only — drop it if present.
            if (AuctionKindText.IsBasic(existing.Kind))
            {
                try { await _api.DeleteAuctionAsync(id, ct).ConfigureAwait(false); }
                catch { /* local is source of truth */ }
            }
            return;
        }

        await _api.DeleteAuctionAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<AuctionDetail> ClaimCaptainByCodeAsync(
        Guid auctionId, string code, CancellationToken ct = default)
    {
        var session = RequireSession();
        var entity = await LoadEntityAsync(auctionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Auction/Drafting not found.");
        if (!VisibleToCurrent(entity))
            throw new InvalidOperationException("Auction/Drafting not found.");
        if (!AuctionKindText.RequiresCaptainCode(entity.Kind))
            throw new InvalidOperationException("Captain entry is for Advance Auction and Drafting. Simple Auction is run by the creator only.");
        if (AuctionRules.IsComplete(entity))
            throw new InvalidOperationException("This Auction/Drafting is complete. You can only view it.");
        var joinAccess = AccessFor(entity);
        if (joinAccess.IsCreator)
            throw new InvalidOperationException("The creator admin runs this Auction/Drafting. Use Run, not Enter.");
        if (!joinAccess.CanEnterAsCaptain)
            throw new InvalidOperationException("Only users and admins of this league can enter as a captain.");

        var normalized = NormalizeAccessCode(code);
        if (normalized.Length < AccessCodeMin)
            throw new InvalidOperationException("Enter the captain code.");

        var matched = entity.Captains.FirstOrDefault(c =>
            string.Equals(NormalizeAccessCode(c.OperatorUsername), normalized, StringComparison.OrdinalIgnoreCase));
        if (matched is null)
            throw new InvalidOperationException("That code is not valid.");

        var captainPlayerId = matched.PlayerId;
        var device = GetDeviceId();
        var alreadyHere = entity.Claims.FirstOrDefault(c => DeviceEquals(c.DeviceId, device));
        if (alreadyHere is not null)
        {
            if (alreadyHere.CaptainPlayerId != captainPlayerId)
                throw new InvalidOperationException("This device already entered as another captain.");
        }
        else if (entity.Claims.Any(c => c.CaptainPlayerId == captainPlayerId))
        {
            throw new InvalidOperationException("This captain is already entered on another device.");
        }
        else
        {
            entity.Claims.Add(new AuctionCaptainClaim
            {
                CaptainPlayerId = captainPlayerId,
                DeviceId = device,
                Username = session.Username
            });
        }

        entity.UpdatedAtUtc = DateTime.Now;
        entity.Version++;
        PackNotes(entity);
        await PersistEntityAsync(entity, ct).ConfigureAwait(false);
        var names = await PlayerNamesAsync(ct).ConfigureAwait(false);
        return ToDetail(entity, names);
    }

    public async Task<AuctionDetail> OpenRoundAsync(Guid auctionId, Guid playerId, bool asBlind, CancellationToken ct = default)
    {
        var entity = await LoadEntityAsync(auctionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Auction/Drafting not found.");
        var access = AccessFor(entity);
        var turnId = AuctionRules.CurrentPickCaptainId(entity);
        var captainSelf = access.CanOperate && turnId is Guid turn && access.DeviceCaptainPlayerId == turn;
        if (!access.CanFullEdit && !captainSelf)
            throw new InvalidOperationException("Only the creator or the captain on the clock can bring a player.");
        if (!captainSelf)
            _auth.EnsureCanWrite();
        if (AuctionKindText.IsBasic(entity.Kind))
            throw new InvalidOperationException("Basic Auction has no live rounds — mark Sold on each player and Save.");
        if (AuctionKindText.IsDrafting(entity.Kind))
            throw new InvalidOperationException("Use pick (not bid rounds) for drafting.");
        if (AuctionRules.IsSettledBlindRound(entity))
            entity.CurrentRound = null;
        if (entity.CurrentRound is not null)
            throw new InvalidOperationException("Close the current round first.");
        var lot = entity.Lots.FirstOrDefault(l => l.PlayerId == playerId)
            ?? throw new InvalidOperationException("Player is not in this Auction/Drafting.");
        if (lot.IsSold)
            throw new InvalidOperationException("That player is already sold.");
        var poolError = AuctionRules.ValidatePool(entity);
        if (poolError.Length > 0)
            throw new InvalidOperationException(poolError);

        var nextRound = AuctionRules.NextRoundNumber(entity);
        var scheduledBlind = AuctionRules.IsScheduledBlindRound(entity, nextRound);
        if (scheduledBlind)
        {
            if (!access.CanFullEdit)
                throw new InvalidOperationException("Only the creator selects the player on a scheduled blind round.");
            asBlind = true;
        }
        else if (asBlind && AuctionRules.HasScheduledBlindRounds(entity))
        {
            throw new InvalidOperationException($"Round {nextRound} is not a scheduled blind bid round.");
        }

        if (asBlind)
        {
            if (!access.CanFullEdit)
                throw new InvalidOperationException("Only the creator can declare a blind bid round.");
            if (!AuctionKindText.IsAdvance(entity.Kind))
                throw new InvalidOperationException("Blind bid is only for Advance Auction.");
            if (!scheduledBlind && entity.BlindRoundsUsed >= entity.BlindBidRoundsAllowed)
                throw new InvalidOperationException("No blind bid rounds left.");
            entity.BlindRoundsUsed++;
        }

        entity.CurrentRound = new AuctionRound
        {
            PlayerId = playerId,
            IsBlind = asBlind,
            IsRevealed = !asBlind
        };
        return await PersistDetailAsync(entity, ct).ConfigureAwait(false);
    }

    public async Task<AuctionDetail> PlaceBidAsync(
        Guid auctionId, Guid captainPlayerId, int amount, bool useJoker, CancellationToken ct = default)
    {
        RequireSession();
        var entity = await LoadEntityAsync(auctionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Auction/Drafting not found.");
        if (AuctionKindText.IsBasic(entity.Kind))
            throw new InvalidOperationException("Basic Auction has no bids — mark Sold on each player and Save.");
        if (AuctionKindText.IsDrafting(entity.Kind))
            throw new InvalidOperationException("Drafting has no bids.");
        var access = AccessFor(entity);
        var round = entity.CurrentRound ?? throw new InvalidOperationException("No round is open.");
        var self = access.CanOperate && access.DeviceCaptainPlayerId == captainPlayerId;
        if (!self)
            throw new InvalidOperationException("You can only bid as the captain entered on this device.");
        if (round.IsBlind && round.IsRevealed)
            throw new InvalidOperationException("This blind round is already awarded.");
        if (round.IsBlind && !round.IsRevealed && round.Bids.Any(b => b.CaptainPlayerId == captainPlayerId))
            throw new InvalidOperationException("You already submitted a blind bid.");

        var cap = entity.Captains.FirstOrDefault(c => c.PlayerId == captainPlayerId)
            ?? throw new InvalidOperationException("Captain not found.");
        var max = AuctionRules.MaxBidThisRound(entity, captainPlayerId, round.PlayerId);
        var min = AuctionRules.MinNextBid(entity, round);
        if (amount < min)
            throw new InvalidOperationException($"Bid must be at least {min:N0}.");
        if (amount > max)
            throw new InvalidOperationException($"Max bid this round is {max:N0}.");
        if (useJoker)
        {
            if (round.IsBlind)
                throw new InvalidOperationException("Joker cannot be used on a blind bid round.");
            if (!AuctionKindText.IsAdvanceAuction(entity.Kind))
                throw new InvalidOperationException("Joker cards are only for Advance Auction.");
            if (cap.JokersLeft <= 0)
                throw new InvalidOperationException("No joker cards left.");
            cap.JokersLeft--;
        }

        round.Bids.RemoveAll(b => b.CaptainPlayerId == captainPlayerId && !round.IsBlind);
        round.Bids.Add(new AuctionBid
        {
            CaptainPlayerId = captainPlayerId,
            Amount = amount,
            IsJoker = useJoker
        });
        TryCompleteBlindRound(entity, round);
        return await PersistDetailAsync(entity, ct).ConfigureAwait(false);
    }

    public async Task<AuctionDetail> RevealBlindAsync(Guid auctionId, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var entity = await RequireCreatorAuction(auctionId, ct).ConfigureAwait(false);
        var round = entity.CurrentRound ?? throw new InvalidOperationException("No round is open.");
        if (!round.IsBlind)
            throw new InvalidOperationException("This is not a blind bid round.");
        if (round.IsRevealed)
            throw new InvalidOperationException("Blind bids are already revealed.");
        if (!AuctionRules.AllExpectedBlindBidsIn(entity, round))
        {
            var expected = AuctionRules.ExpectedBlindBidderIds(entity).Count;
            throw new InvalidOperationException($"Waiting for bids ({round.Bids.Count}/{expected}).");
        }
        TryCompleteBlindRound(entity, round);
        return await PersistDetailAsync(entity, ct).ConfigureAwait(false);
    }

    public async Task<AuctionDetail> CloseRoundAsync(
        Guid auctionId, bool unsold, Guid? simpleWinnerId, int? simplePrice, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var entity = await RequireCreatorAuction(auctionId, ct).ConfigureAwait(false);
        if (AuctionKindText.IsBasic(entity.Kind))
            throw new InvalidOperationException("Basic Auction has no live rounds — mark Sold on each player and Save.");
        if (AuctionKindText.IsDrafting(entity.Kind))
            throw new InvalidOperationException("Use pick (not bid rounds) for drafting.");
        var round = entity.CurrentRound;
        if (unsold)
        {
            if (round is not null)
            {
                var lot = entity.Lots.FirstOrDefault(l => l.PlayerId == round.PlayerId);
                if (lot is not null)
                {
                    lot.IsSold = false;
                    lot.SoldPrice = null;
                    lot.SoldToCaptainPlayerId = null;
                }
            }
            entity.CurrentRound = null;
            AdvanceCaptainTurn(entity);
            return await PersistDetailAsync(entity, ct).ConfigureAwait(false);
        }

        if (entity.Kind == AuctionKind.SimpleAuction && simpleWinnerId is Guid win && simplePrice is int price)
        {
            var playerId = round?.PlayerId
                ?? throw new InvalidOperationException("Open a round first.");
            var max = AuctionRules.MaxBidThisRound(entity, win, playerId);
            var lot = entity.Lots.First(l => l.PlayerId == playerId);
            if (price < lot.BasePrice)
                throw new InvalidOperationException($"Sold price must be at least base {lot.BasePrice:N0}.");
            if (price > max)
                throw new InvalidOperationException($"Max bid this round for that captain is {max:N0}.");
            lot.IsSold = true;
            lot.SoldPrice = price;
            lot.SoldToCaptainPlayerId = win;
            RecordAward(entity, lot.PlayerId);
            entity.CurrentRound = null;
            AdvanceCaptainTurn(entity);
            return await PersistDetailAsync(entity, ct).ConfigureAwait(false);
        }

        if (round is null)
            throw new InvalidOperationException("No round is open.");
        if (round.IsBlind && !round.IsRevealed)
            throw new InvalidOperationException("Reveal blind bids first.");
        if (round.Bids.Count == 0)
            throw new InvalidOperationException("No bids to close on. Mark unsold or wait for bids.");
        AwardHighest(entity, round);
        entity.CurrentRound = null;
        AdvanceCaptainTurn(entity);
        return await PersistDetailAsync(entity, ct).ConfigureAwait(false);
    }

    public async Task<AuctionDetail> DraftPickAsync(Guid auctionId, Guid playerId, CancellationToken ct = default)
    {
        var entity = await LoadEntityAsync(auctionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Auction/Drafting not found.");
        if (!AuctionKindText.IsDrafting(entity.Kind))
            throw new InvalidOperationException("Picking is only for drafting.");
        if (AuctionRules.IsComplete(entity))
            throw new InvalidOperationException("This Auction/Drafting is complete.");
        var access = AccessFor(entity);
        var turnId = AuctionRules.CurrentPickCaptainId(entity);
        var captainSelf = access.CanOperate && turnId is Guid turn && access.DeviceCaptainPlayerId == turn;
        if (!access.CanFullEdit && !captainSelf)
            throw new InvalidOperationException("Only the creator or the captain on the clock can pick.");
        if (!captainSelf)
            _auth.EnsureCanWrite();
        if (turnId is not Guid pickId)
        {
            var slotsLeft = entity.Captains.Any(c => AuctionRules.RemainingSlots(entity, c.PlayerId) > 0);
            var poolLeft = entity.Lots.Any(l => !l.IsSold);
            throw new InvalidOperationException(slotsLeft && poolLeft
                ? "No captain can pick from the remaining groups."
                : "Drafting is complete — every captain has a full squad.");
        }
        var lot = entity.Lots.FirstOrDefault(l => l.PlayerId == playerId)
            ?? throw new InvalidOperationException("Player is not in this Auction/Drafting.");
        if (!AuctionRules.CanPick(entity, pickId, lot))
        {
            if (lot.IsSold)
                throw new InvalidOperationException("That player is already picked.");
            if (AuctionKindText.IsAdvanceDrafting(entity.Kind))
                throw new InvalidOperationException("This captain has already taken the max from that group.");
            throw new InvalidOperationException("This captain cannot pick that player.");
        }

        lot.IsSold = true;
        lot.SoldPrice = 0;
        lot.SoldToCaptainPlayerId = pickId;
        RecordAward(entity, lot.PlayerId);
        entity.DraftTurnIndex++;
        return await PersistDetailAsync(entity, ct).ConfigureAwait(false);
    }

    public async Task<AuctionDetail> UndoLastPickAsync(Guid auctionId, CancellationToken ct = default)
    {
        var entity = await RequireCreatorAuction(auctionId, ct).ConfigureAwait(false);
        SyncAwardHistory(entity);
        if (entity.AwardedPlayerIds.Count == 0)
            throw new InvalidOperationException("Nothing to undo.");

        var playerId = entity.AwardedPlayerIds[^1];
        entity.AwardedPlayerIds.RemoveAt(entity.AwardedPlayerIds.Count - 1);
        var lot = entity.Lots.FirstOrDefault(l => l.PlayerId == playerId);
        if (lot is not null)
        {
            lot.IsSold = false;
            lot.SoldPrice = null;
            lot.SoldToCaptainPlayerId = null;
        }
        if (entity.DraftTurnIndex > 0)
            entity.DraftTurnIndex--;
        if (entity.CurrentRound?.PlayerId == playerId)
            entity.CurrentRound = null;
        return await PersistDetailAsync(entity, ct).ConfigureAwait(false);
    }

    private static void TryCompleteBlindRound(Auction entity, AuctionRound round)
    {
        if (!round.IsBlind || round.IsRevealed)
            return;
        if (!AuctionRules.AllExpectedBlindBidsIn(entity, round))
            return;
        round.IsRevealed = true;
        AwardHighest(entity, round);
        AdvanceCaptainTurn(entity);
    }

    private static void AdvanceCaptainTurn(Auction entity)
    {
        if (entity.PickOrder.Count == 0)
            entity.PickOrder.AddRange(entity.Captains.Select(c => c.PlayerId).Distinct());
        entity.DraftTurnIndex++;
    }

    private async Task<Auction> RequireCreatorAuction(Guid auctionId, CancellationToken ct)
    {
        var entity = await LoadEntityAsync(auctionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Auction/Drafting not found.");
        if (!AccessFor(entity).CanFullEdit)
            throw new InvalidOperationException("Only the creator admin can do this.");
        return entity;
    }

    private static void AwardHighest(Auction entity, AuctionRound round)
    {
        var top = round.Bids.OrderByDescending(b => b.Amount).ThenBy(b => b.CaptainPlayerId).First();
        var lot = entity.Lots.First(l => l.PlayerId == round.PlayerId);
        lot.IsSold = true;
        lot.SoldPrice = top.Amount;
        lot.SoldToCaptainPlayerId = top.CaptainPlayerId;
        RecordAward(entity, lot.PlayerId);
    }

    private static void RecordAward(Auction a, Guid playerId)
    {
        a.AwardedPlayerIds.RemoveAll(id => id == playerId);
        a.AwardedPlayerIds.Add(playerId);
    }

    private static void SyncAwardHistory(Auction a)
    {
        var sold = a.Lots.Where(l => l.IsSold).Select(l => l.PlayerId).ToHashSet();
        a.AwardedPlayerIds.RemoveAll(id => !sold.Contains(id));
        foreach (var lot in a.Lots)
        {
            if (!lot.IsSold)
                continue;
            if (!a.AwardedPlayerIds.Contains(lot.PlayerId))
                a.AwardedPlayerIds.Add(lot.PlayerId);
        }
    }

    private static string FormatAwardHist(Auction a, List<AuctionLot> lots)
    {
        SyncAwardHistory(a);
        return string.Join(".", a.AwardedPlayerIds
            .Select(id => lots.FindIndex(l => l.PlayerId == id))
            .Where(i => i >= 0));
    }

    private static void ApplyAwardHist(Auction a, List<AuctionLot> lots, string? blob)
    {
        a.AwardedPlayerIds.Clear();
        if (string.IsNullOrWhiteSpace(blob))
        {
            SyncAwardHistory(a);
            return;
        }
        foreach (var tok in blob.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(tok, out var i) || i < 0 || i >= lots.Count)
                continue;
            var id = lots[i].PlayerId;
            if (!a.AwardedPlayerIds.Contains(id))
                a.AwardedPlayerIds.Add(id);
        }
        SyncAwardHistory(a);
    }

    private async Task<AuctionDetail> PersistDetailAsync(Auction entity, CancellationToken ct)
    {
        entity.UpdatedAtUtc = DateTime.Now;
        entity.Version++;
        PackNotes(entity);
        await PersistEntityAsync(entity, ct).ConfigureAwait(false);
        var names = await PlayerNamesAsync(ct).ConfigureAwait(false);
        return ToDetail(entity, names);
    }

    public async Task<(int AuctionCount, IReadOnlyList<AuctionReportRow> Rows)> GetAllTimeReportAsync(AuctionKind kind, CancellationToken ct = default)
    {
        var auctions = await LoadVisibleEntitiesAsync(ct).ConfigureAwait(false);
        var names = await PlayerNamesAsync(ct).ConfigureAwait(false);
        var ofKind = auctions.Where(a => a.Kind == kind).ToList();
        return (ofKind.Count, BuildLocalReport(ofKind, names));
    }

    public async Task<AuctionPlayerDetail?> GetPlayerDetailAsync(Guid playerId, AuctionKind kind, CancellationToken ct = default)
    {
        var auctions = await LoadVisibleEntitiesAsync(ct).ConfigureAwait(false);
        var involved = auctions
            .Where(a => a.Kind == kind)
            .Where(a => a.Captains.Any(c => c.PlayerId == playerId) || a.Lots.Any(l => l.PlayerId == playerId))
            .ToList();
        if (involved.Count == 0)
            return null;

        var names = await PlayerNamesAsync(ct).ConfigureAwait(false);
        var report = BuildLocalReport(involved, names)
            .FirstOrDefault(r => r.PlayerId == playerId);
        if (report is null)
            return null;

        var drafting = AuctionKindText.IsDrafting(kind);
        var kindLabel = AuctionKindText.Of(kind);

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
                        ? CaptainLabel(a, cid, names)
                        : null,
                    GroupName = l.GroupName ?? string.Empty,
                    IsDrafting = drafting,
                    KindLabel = kindLabel
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
                    PlayersBought = bought.Count,
                    IsDrafting = drafting,
                    KindLabel = kindLabel
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
        var cached = _playerNameCache;
        if (cached is not null && DateTime.UtcNow - _playerNameCacheAtUtc < TimeSpan.FromMinutes(2))
            return cached;

        var players = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
        var map = players.Where(p => !p.IsDeleted).ToDictionary(p => p.Id, p => p.FullName);
        _playerNameCache = map;
        _playerNameCacheAtUtc = DateTime.UtcNow;
        return map;
    }

    private async Task<List<Auction>> LoadVisibleEntitiesAsync(CancellationToken ct)
    {
        var remote = await _api.GetAuctionsAsync(ct).ConfigureAwait(false);
        var list = remote.Where(a => !a.IsDeleted).Select(ToEntity).Where(VisibleToCurrent).ToList();
        foreach (var local in LoadLocalEntities().Where(a => !a.IsDeleted && VisibleToCurrent(a)))
        {
            if (list.All(x => x.Id != local.Id))
                list.Add(local);
        }
        return list;
    }

    private async Task<Auction?> LoadEntityAsync(Guid id, CancellationToken ct)
    {
        var local = LoadLocalEntities().FirstOrDefault(a => a.Id == id && !a.IsDeleted);
        if (local is not null)
            return local;

        var remote = (await _api.GetAuctionsAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(a => a.Id == id && !a.IsDeleted);
        return remote is null ? null : ToEntity(remote);
    }

    private async Task PersistEntityAsync(Auction entity, CancellationToken ct)
    {
        var remote = await _api.UpdateAuctionAsync(entity.Id, ToApiBody(entity), ct).ConfigureAwait(false);
        ApplyDtoScalars(entity, remote);
    }

    private AuctionListItem ToListItem(Auction a)
    {
        var access = AccessFor(a);
        return new AuctionListItem(
            a.Id,
            a.Title,
            a.AuctionDateUtc,
            a.Captains.Count,
            a.Lots.Count,
            a.Lots.Count(l => l.IsSold),
            a.Lots.Count(l => !l.IsSold),
            a.Lots.Where(l => l.IsSold).Sum(l => l.SoldPrice ?? 0),
            a.Kind,
            AuctionKindText.RunModeFor(a.Kind),
            AuctionKindText.Of(a.Kind),
            AuctionRules.IsComplete(a)
                ? "COMPLETE"
                : AuctionKindText.IsBasic(a.Kind) ? "LOCAL" : "LIVE",
            access.CanFullEdit,
            access.CanEnterAsCaptain,
            access.CanDelete);
    }

    private AuctionDetail ToDetail(Auction a, IReadOnlyDictionary<Guid, string> names)
        => new(
            a.Id,
            a.Title,
            a.AuctionDateUtc,
            a.Notes,
            a.Kind,
            AuctionKindText.RunModeFor(a.Kind),
            a.CreatedByUserId,
            a.CreatedByUsername,
            Math.Max(1, a.PlayersPerCaptain),
            a.JokersPerCaptain,
            a.BlindBidRoundsAllowed,
            a.BlindRoundsUsed,
            a.UpdateAdminUsernames,
            a.Claims,
            AccessFor(a),
            ToRoundInfo(a, names),
            AuctionRules.CurrentPickCaptainId(a),
            AuctionRules.CurrentPickCaptainId(a) is Guid tid
                ? CaptainLabel(a, tid, names)
                : string.Empty,
            a.DraftGroups.Select(g => new AuctionDraftGroup { Name = g.Name, MaxPerCaptain = g.MaxPerCaptain }).ToList(),
            a.PickOrder.ToList(),
            a.Captains
                .OrderBy(c => names.GetValueOrDefault(c.PlayerId, "?"))
                .Select(c =>
                {
                    var currentId = a.CurrentRound?.PlayerId;
                    return new AuctionCaptainRow(
                        c.PlayerId,
                        names.GetValueOrDefault(c.PlayerId, "?"),
                        c.TeamName,
                        c.WalletAmount,
                        c.OperatorUsername,
                        c.JokersLeft,
                        AuctionRules.BoughtCount(a, c.PlayerId),
                        AuctionRules.RemainingSlots(a, c.PlayerId),
                        AuctionRules.RemainingWallet(a, c.PlayerId),
                        AuctionRules.MaxBidThisRound(a, c.PlayerId, currentId));
                })
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
                        ? CaptainLabel(a, cid, names)
                        : null,
                    l.GroupName))
                .ToList(),
            a.BlindRoundNumbers.ToList(),
            a.DraftTurnIndex);

    private static AuctionRoundInfo? ToRoundInfo(
        Auction a, IReadOnlyDictionary<Guid, string> names)
    {
        var round = a.CurrentRound;
        if (round is null)
            return null;
        var lot = a.Lots.FirstOrDefault(l => l.PlayerId == round.PlayerId);
        var hide = round.IsBlind && !round.IsRevealed;
        var bids = round.Bids
            .OrderByDescending(b => hide ? 0 : b.Amount)
            .Select(b => new AuctionBidRow(
                b.CaptainPlayerId,
                CaptainLabel(a, b.CaptainPlayerId, names),
                hide ? 0 : b.Amount,
                hide ? false : b.IsJoker,
                hide))
            .ToList();
        var expected = round.IsBlind
            ? AuctionRules.ExpectedBlindBidderIds(a).Count
            : a.Captains.Count;
        return new AuctionRoundInfo(
            round.PlayerId,
            names.GetValueOrDefault(round.PlayerId, "?"),
            lot?.BasePrice ?? 0,
            round.IsBlind,
            round.IsRevealed,
            AuctionRules.MinNextBid(a, round),
            round.Bids.Count,
            expected,
            bids);
    }

    private static string CaptainLabel(Auction a, Guid captainPlayerId, IReadOnlyDictionary<Guid, string> names)
    {
        var name = names.GetValueOrDefault(captainPlayerId, "?");
        var team = a.Captains.FirstOrDefault(c => c.PlayerId == captainPlayerId)?.TeamName;
        return string.IsNullOrWhiteSpace(team) ? name : $"{name} · {team}";
    }

    private Auction ToEntity(ApiAuctionDto dto)
    {
        var row = new Auction { Id = dto.Id };
        ApplyDtoScalars(row, dto);
        UnpackNotes(row);
        RebuildChildren(row, dto);
        ApplyTeamNamesFromNotes(row);
        return row;
    }

    private AuctionAccess AccessFor(Auction a)
    {
        var session = _auth.Current;
        var device = GetDeviceId();
        var userName = session?.Username ?? string.Empty;
        var isCreator = a.CreatedByUserId is Guid cid && session is not null && session.UserId == cid
            || (!string.IsNullOrWhiteSpace(a.CreatedByUsername)
                && string.Equals(a.CreatedByUsername, userName, StringComparison.OrdinalIgnoreCase));
        var legacyOpen = a.CreatedByUserId is null && string.IsNullOrWhiteSpace(a.CreatedByUsername);
        var canFull = isCreator || (legacyOpen && _auth.CanWrite);
        var deviceCaptain = a.Claims
            .FirstOrDefault(c => DeviceEquals(c.DeviceId, device))
            ?.CaptainPlayerId;
        var isClaimedHere = deviceCaptain is Guid;
        var complete = AuctionRules.IsComplete(a);
        var canOperate = !complete && AuctionKindText.RequiresCaptainCode(a.Kind) && isClaimedHere;
        var canEnter = !complete && AuctionKindText.RequiresCaptainCode(a.Kind) && LeagueMemberMayEnter(isCreator);
        return new AuctionAccess(
            isCreator,
            isClaimedHere,
            canFull,
            canOperate,
            canFull && (isCreator || (legacyOpen && _auth.CanWrite)),
            deviceCaptain,
            canEnter);
    }

    private bool LeagueMemberMayEnter(bool isCreator)
    {
        if (isCreator)
            return false;
        var session = _auth.Current;
        if (session is null)
            return false;
        if (session.Role is not (AppRole.User or AppRole.Admin))
            return false;
        return session.IsHomeLeague(_league.CurrentLeagueId);
    }

    private static string NormalizeAccessCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return value.Trim().Replace(" ", "");
    }

    private bool VisibleToCurrent(Auction a) => !a.IsDeleted;

    private AuthSession RequireSession()
        => _auth.Current ?? throw new InvalidOperationException("Sign in to manage Auction/Drafting.");

    private static void Validate(
        AuctionDraft draft, IReadOnlyList<AuctionCaptainClaim>? existingClaims)
    {
        if (string.IsNullOrWhiteSpace(draft.Title))
            throw new ArgumentException("Auction/Drafting title is required.");
        if (draft.Title.Trim().Length > 160)
            throw new ArgumentException("Auction/Drafting title must be 160 characters or fewer.");
        if (draft.PlayersPerCaptain < 1)
            throw new ArgumentException("Each captain must select at least 1 player.");

        var isDrafting = AuctionKindText.IsDrafting(draft.Kind);
        var stage = draft.Stage;

        if (AuctionKindText.IsAdvanceAuction(draft.Kind))
        {
            if (draft.JokersPerCaptain < 0)
                throw new ArgumentException("Joker cards cannot be negative.");
            if (draft.BlindBidRoundsAllowed < 0)
                throw new ArgumentException("Blind bid rounds cannot be negative.");
            if (draft.BlindRoundNumbers is { Count: > 0 } rounds && rounds.Any(n => n < 1))
                throw new ArgumentException("Blind bid rounds must be positive round numbers, e.g. 3, 6, 9.");
        }

        if (AuctionKindText.IsAdvanceDrafting(draft.Kind))
        {
            var groups = (draft.DraftGroups ?? Array.Empty<AuctionDraftGroup>())
                .Where(g => !string.IsNullOrWhiteSpace(g.Name) && g.MaxPerCaptain > 0)
                .ToList();
            if (groups.Count == 0)
                throw new InvalidOperationException("Add at least 1 group with a max per captain.");
            if (groups.Select(g => g.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != groups.Count)
                throw new InvalidOperationException("Group names must be unique.");
            if (groups.Sum(g => g.MaxPerCaptain) < draft.PlayersPerCaptain)
                throw new InvalidOperationException($"Group maxes must cover {draft.PlayersPerCaptain} players per captain.");
        }

        if (stage >= AuctionSaveStage.Captains && draft.Captains.Count < 2)
            throw new InvalidOperationException("Select at least 2 captains.");
        if (stage >= AuctionSaveStage.Complete && draft.Lots.Count == 0)
            throw new InvalidOperationException("Select at least 1 player for this Auction/Drafting.");

        if (draft.Captains.Count == 0 && draft.Lots.Count == 0 && stage < AuctionSaveStage.Captains)
        {
            _ = existingClaims;
            return;
        }

        var captainIds = draft.Captains.Select(c => c.PlayerId).ToList();
        if (captainIds.Count != captainIds.Distinct().Count())
            throw new InvalidOperationException("A captain cannot be selected twice.");

        var teamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operatorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in draft.Captains)
        {
            var team = (c.TeamName ?? string.Empty).Trim();
            if (team.Length == 0)
                throw new ArgumentException("Set a team name for every captain.");
            if (team.Length > TeamNameMax)
                throw new ArgumentException($"Team name must be {TeamNameMax} characters or fewer.");
            if (!teamNames.Add(team))
                throw new InvalidOperationException("Each captain needs a unique team name.");
            if (c.WalletAmount < 0 && !isDrafting)
                throw new ArgumentException("Captain wallet cannot be negative.");
            if (AuctionKindText.RequiresCaptainCode(draft.Kind))
            {
                var code = NormalizeAccessCode(c.OperatorUsername);
                if (code.Length < AccessCodeMin)
                    throw new ArgumentException($"Set a captain code ({AccessCodeMin}–{AccessCodeMax} characters) for every captain.");
                if (code.Length > AccessCodeMax)
                    throw new ArgumentException($"Captain codes must be {AccessCodeMax} characters or fewer.");
                if (!operatorNames.Add(code))
                    throw new InvalidOperationException("Each captain needs a different code.");
            }
        }

        var wallets = draft.Captains
            .GroupBy(c => c.PlayerId)
            .ToDictionary(g => g.Key, g => g.Last().WalletAmount);

        var spent = wallets.Keys.ToDictionary(id => id, _ => 0);
        var seenPlayers = new HashSet<Guid>();
        foreach (var lot in draft.Lots)
        {
            if (!seenPlayers.Add(lot.PlayerId))
                throw new InvalidOperationException("Duplicate player in Auction/Drafting list.");
            if (lot.BasePrice < 0 && !isDrafting)
                throw new ArgumentException("Base price cannot be negative.");
            if (lot.IsSold)
            {
                if (!isDrafting && (lot.SoldPrice is null || lot.SoldPrice < 0))
                    throw new ArgumentException("Sold players need a valid sold price.");
                if (!isDrafting && lot.SoldPrice < lot.BasePrice)
                    throw new ArgumentException("Sold price cannot be less than base price.");
                if (lot.SoldToCaptainPlayerId is not Guid cap || !wallets.ContainsKey(cap))
                    throw new InvalidOperationException("Sold player must be assigned to a selected captain.");
                if (!isDrafting)
                    spent[cap] += lot.SoldPrice ?? 0;
            }
        }

        if (!isDrafting)
        {
            foreach (var (capId, total) in spent)
            {
                if (total > wallets[capId])
                {
                    throw new InvalidOperationException(
                        $"A captain spent {total:N0} but wallet is only {wallets[capId]:N0}.");
                }
            }
        }

        var checkPool = stage >= AuctionSaveStage.Complete
            || (draft.Captains.Count >= 2 && draft.Lots.Count > 0);
        if (!checkPool)
        {
            _ = existingClaims;
            return;
        }

        var probe = new Auction
        {
            Kind = draft.Kind,
            PlayersPerCaptain = draft.PlayersPerCaptain,
            DraftGroups = (draft.DraftGroups ?? Array.Empty<AuctionDraftGroup>())
                .Select(g => new AuctionDraftGroup { Name = g.Name.Trim(), MaxPerCaptain = g.MaxPerCaptain })
                .ToList(),
            PickOrder = (draft.PickOrder ?? Array.Empty<Guid>()).ToList(),
            Captains = draft.Captains.Select(c => new AuctionCaptain
            {
                PlayerId = c.PlayerId,
                TeamName = c.TeamName.Trim(),
                WalletAmount = isDrafting ? 0 : c.WalletAmount
            }).ToList(),
            Lots = draft.Lots.Select(l => new AuctionLot
            {
                PlayerId = l.PlayerId,
                BasePrice = isDrafting ? 0 : l.BasePrice,
                IsSold = l.IsSold,
                SoldPrice = l.SoldPrice,
                SoldToCaptainPlayerId = l.SoldToCaptainPlayerId,
                GroupName = l.GroupName ?? string.Empty
            }).ToList()
        };
        var poolError = AuctionRules.ValidatePool(probe);
        if (poolError.Length > 0)
            throw new InvalidOperationException(poolError);

        _ = existingClaims;
    }

    private static Auction BuildEntity(
        Guid id,
        AuctionDraft draft,
        DateTime created,
        DateTime updated,
        long version,
        AuthSession? sessionOverride,
        IReadOnlyList<AuctionCaptainClaim>? existingClaims)
    {
        var createdById = sessionOverride?.UserId;
        var createdByName = sessionOverride?.Username;
        var captains = draft.Captains
            .GroupBy(c => c.PlayerId)
            .Select(g => g.Last())
            .ToList();
        var entity = new Auction
        {
            Id = id,
            Title = draft.Title.Trim(),
            AuctionDateUtc = draft.AuctionDateUtc,
            Kind = draft.Kind,
            RunMode = AuctionKindText.RunModeFor(draft.Kind),
            PlayersPerCaptain = Math.Max(1, draft.PlayersPerCaptain),
            JokersPerCaptain = AuctionKindText.IsAdvanceAuction(draft.Kind) ? Math.Max(0, draft.JokersPerCaptain) : 0,
            BlindBidRoundsAllowed = 0,
            BlindRoundNumbers = new List<int>(),
            DraftTurnIndex = 0,
            PickOrder = (draft.PickOrder is { Count: > 0 } order
                ? order
                : captains.Select(c => c.PlayerId)).Distinct().ToList(),
            DraftGroups = AuctionKindText.IsAdvanceDrafting(draft.Kind)
                ? (draft.DraftGroups ?? Array.Empty<AuctionDraftGroup>())
                    .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                    .Select(g => new AuctionDraftGroup { Name = g.Name.Trim(), MaxPerCaptain = Math.Max(0, g.MaxPerCaptain) })
                    .ToList()
                : new List<AuctionDraftGroup>(),
            CreatedByUserId = createdById,
            CreatedByUsername = createdByName,
            UpdateAdminUsernames = new List<string>(),
            Claims = (existingClaims ?? Array.Empty<AuctionCaptainClaim>())
                .GroupBy(c => c.CaptainPlayerId)
                .Select(g => g.Last())
                .ToList(),
            Version = version,
            CreatedAtUtc = created,
            UpdatedAtUtc = updated,
            Captains = captains.Select(c => new AuctionCaptain
            {
                AuctionId = id,
                PlayerId = c.PlayerId,
                TeamName = c.TeamName.Trim(),
                WalletAmount = AuctionKindText.IsDrafting(draft.Kind) ? 0 : Math.Max(0, c.WalletAmount),
                OperatorUsername = AuctionKindText.RequiresCaptainCode(draft.Kind)
                    ? NormalizeAccessCode(c.OperatorUsername)
                    : string.Empty,
                JokersLeft = AuctionKindText.IsAdvanceAuction(draft.Kind) ? Math.Max(0, draft.JokersPerCaptain) : 0
            }).ToList(),
            Lots = draft.Lots.Select(l => ToLot(id, l)).ToList()
        };
        ApplyBlindSettings(entity, draft);
        return entity;
    }

    private static AuctionLot ToLot(Guid auctionId, AuctionLotDraft l)
        => new()
        {
            Id = Guid.NewGuid(),
            AuctionId = auctionId,
            PlayerId = l.PlayerId,
            BasePrice = Math.Max(0, l.BasePrice),
            IsSold = l.IsSold,
            SoldPrice = l.IsSold ? Math.Max(0, l.SoldPrice ?? 0) : null,
            SoldToCaptainPlayerId = l.IsSold ? l.SoldToCaptainPlayerId : null,
            GroupName = (l.GroupName ?? string.Empty).Trim()
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
        var teams = row.Captains.ToDictionary(c => c.PlayerId, c => c.TeamName);
        row.Captains.Clear();
        foreach (var c in dto.Captains.GroupBy(x => x.PlayerId).Select(g => g.Last()))
        {
            row.Captains.Add(new AuctionCaptain
            {
                AuctionId = row.Id,
                PlayerId = c.PlayerId,
                TeamName = teams.GetValueOrDefault(c.PlayerId) ?? string.Empty,
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

    private static void PackNotes(Auction a)
    {
        var captains = a.Captains.OrderBy(c => c.PlayerId).ToList();
        var lots = a.Lots.OrderBy(l => l.PlayerId).ToList();
        if (AuctionKindText.IsDrafting(a.Kind))
        {
            var teams = string.Join(",", captains.Select(c => San(c.TeamName)));
            var ops = string.Join(",", captains.Select(c => San(c.OperatorUsername)));
            var claims = string.Join(",", a.Claims.Select(c =>
            {
                var idx = captains.FindIndex(x => x.PlayerId == c.CaptainPlayerId);
                return idx < 0 ? "" : $"{idx}:{ShortDevice(c.DeviceId)}";
            }).Where(x => x.Length > 0));
            var order = string.Join(",", a.PickOrder.Select(id => captains.FindIndex(c => c.PlayerId == id))
                .Where(i => i >= 0));
            var groups = string.Join(",", a.DraftGroups.Select(g => $"{San(g.Name)}:{g.MaxPerCaptain}"));
            var gnames = a.DraftGroups.Select(g => (g.Name ?? string.Empty).Trim()).ToList();
            var lotg = string.Join(",", lots.Select(l =>
            {
                var want = (l.GroupName ?? string.Empty).Trim();
                var gi = gnames.FindIndex(n => string.Equals(n, want, StringComparison.OrdinalIgnoreCase));
                return gi < 0 ? "-1" : gi.ToString();
            }));
            var packed =
                $"JPL3|{(byte)a.Kind}|{a.CreatedByUserId:D}|{San(a.CreatedByUsername)}|{Math.Max(1, a.PlayersPerCaptain)}|{ops}|{teams}|{claims}|{order}|{a.DraftTurnIndex}|{groups}|{lotg}";
            var hist = FormatAwardHist(a, lots);
            if (hist.Length > 0)
                packed = $"{packed}|{hist}";
            if (packed.Length > NotesMax)
                throw new InvalidOperationException("Draft setup is too large (shorter group/team names).");
            a.Notes = packed;
            return;
        }

        var teams2 = string.Join(",", captains.Select(c => San(c.TeamName)));
        var ops2 = string.Join(",", captains.Select(c => San(c.OperatorUsername)));
        var claims2 = string.Join(",", a.Claims.Select(c =>
        {
            var idx = captains.FindIndex(x => x.PlayerId == c.CaptainPlayerId);
            return idx < 0 ? "" : $"{idx}:{ShortDevice(c.DeviceId)}";
        }).Where(x => x.Length > 0));
        var jleft = string.Join(",", captains.Select(c => c.JokersLeft.ToString()));
        var round = "-1:0:0:";
        if (a.CurrentRound is { } r)
        {
            var li = lots.FindIndex(l => l.PlayerId == r.PlayerId);
            var bids = string.Join(".", r.Bids.Select(b =>
            {
                var ci = captains.FindIndex(x => x.PlayerId == b.CaptainPlayerId);
                return ci < 0 ? "" : $"{ci}-{b.Amount}-{(b.IsJoker ? 1 : 0)}";
            }).Where(x => x.Length > 0));
            round = $"{li}:{(r.IsBlind ? 1 : 0)}:{(r.IsRevealed ? 1 : 0)}:{bids}";
        }
        var order2 = string.Join(",", a.PickOrder.Select(id => captains.FindIndex(c => c.PlayerId == id))
            .Where(i => i >= 0));

        var packed2 =
            $"JPL2|{(byte)a.Kind}|{a.CreatedByUserId:D}|{San(a.CreatedByUsername)}|{Math.Max(1, a.PlayersPerCaptain)}|{a.JokersPerCaptain}|{PackBlindToken(a)}|{a.BlindRoundsUsed}|{ops2}|{teams2}|{claims2}|{jleft}|{round}|{order2}|{a.DraftTurnIndex}";
        var hist2 = FormatAwardHist(a, lots);
        if (hist2.Length > 0)
            packed2 = $"{packed2}|{hist2}";
        if (packed2.Length > NotesMax)
        {
            throw new InvalidOperationException(
                "Auction/Drafting setup is too large to store (shorten team names).");
        }
        a.Notes = packed2;
    }

    private static void ApplyBlindSettings(Auction entity, AuctionDraft draft)
    {
        if (!AuctionKindText.IsAdvanceAuction(draft.Kind))
        {
            entity.BlindRoundNumbers = new List<int>();
            entity.BlindBidRoundsAllowed = 0;
            return;
        }

        entity.BlindRoundNumbers = NormalizeBlindRoundNumbers(draft.BlindRoundNumbers);
        entity.BlindBidRoundsAllowed = entity.BlindRoundNumbers.Count > 0
            ? entity.BlindRoundNumbers.Count
            : Math.Max(0, draft.BlindBidRoundsAllowed);
    }

    private static List<int> NormalizeBlindRoundNumbers(IReadOnlyList<int>? raw)
    {
        var list = new List<int>();
        foreach (var n in raw ?? Array.Empty<int>())
        {
            if (n > 0 && !list.Contains(n))
                list.Add(n);
        }
        list.Sort();
        return list;
    }

    private static string PackBlindToken(Auction a)
    {
        if (a.BlindRoundNumbers.Count > 0)
            return "R" + string.Join(".", a.BlindRoundNumbers);
        return Math.Max(0, a.BlindBidRoundsAllowed).ToString();
    }

    private static void UnpackBlindToken(Auction a, string? token)
    {
        a.BlindRoundNumbers = new List<int>();
        if (string.IsNullOrWhiteSpace(token))
        {
            a.BlindBidRoundsAllowed = 0;
            return;
        }

        if (token.StartsWith("R", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = new List<int>();
            foreach (var part in token[1..].Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var n) && n > 0)
                    parsed.Add(n);
            }
            a.BlindRoundNumbers = NormalizeBlindRoundNumbers(parsed);
            a.BlindBidRoundsAllowed = a.BlindRoundNumbers.Count;
            return;
        }

        if (int.TryParse(token, out var count))
            a.BlindBidRoundsAllowed = Math.Max(0, count);
    }

    private static void UnpackNotes(Auction a)
    {
        var notes = a.Notes;
        if (string.IsNullOrWhiteSpace(notes))
        {
            a.Kind = AuctionKind.SimpleAuction;
            a.RunMode = AuctionKindText.RunModeFor(a.Kind);
            if (a.PlayersPerCaptain < 1) a.PlayersPerCaptain = 11;
            return;
        }

        if (notes.StartsWith("JPL3|", StringComparison.Ordinal))
        {
            var p = notes.Split('|');
            if (p.Length < 12) return;
            if (byte.TryParse(p[1], out var k3) && k3 <= 4)
                a.Kind = (AuctionKind)k3;
            a.RunMode = AuctionKindText.RunModeFor(a.Kind);
            if (Guid.TryParse(p[2], out var c3) && c3 != Guid.Empty)
                a.CreatedByUserId = c3;
            a.CreatedByUsername = Unsan(p[3]);
            if (int.TryParse(p[4], out var ppc3)) a.PlayersPerCaptain = Math.Max(1, ppc3);
            a.UpdateAdminUsernames = p[5].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Unsan).Where(s => s.Length > 0).ToList();
            return;
        }

        if (notes.StartsWith("JPL2|", StringComparison.Ordinal))
        {
            var p = notes.Split('|');
            if (p.Length < 13) return;
            if (byte.TryParse(p[1], out var kindByte) && kindByte <= 4)
                a.Kind = (AuctionKind)kindByte;
            a.RunMode = AuctionKindText.RunModeFor(a.Kind);
            if (Guid.TryParse(p[2], out var creatorId) && creatorId != Guid.Empty)
                a.CreatedByUserId = creatorId;
            a.CreatedByUsername = Unsan(p[3]);
            if (int.TryParse(p[4], out var ppc)) a.PlayersPerCaptain = Math.Max(1, ppc);
            if (int.TryParse(p[5], out var jk)) a.JokersPerCaptain = Math.Max(0, jk);
            UnpackBlindToken(a, p[6]);
            if (int.TryParse(p[7], out var bu)) a.BlindRoundsUsed = Math.Max(0, bu);
            a.UpdateAdminUsernames = p[8].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Unsan).Where(s => s.Length > 0).ToList();
            return;
        }

        if (!notes.StartsWith("JPL1|", StringComparison.Ordinal))
        {
            a.Kind = AuctionKind.SimpleAuction;
            a.RunMode = AuctionKindText.RunModeFor(a.Kind);
            if (a.PlayersPerCaptain < 1) a.PlayersPerCaptain = 11;
            return;
        }

        var parts = notes.Split('|');
        if (parts.Length < 8)
            return;
        if (byte.TryParse(parts[1], out var k1) && k1 <= 4)
            a.Kind = (AuctionKind)k1;
        a.RunMode = AuctionKindText.RunModeFor(a.Kind);
        if (Guid.TryParse(parts[3], out var cid) && cid != Guid.Empty)
            a.CreatedByUserId = cid;
        a.CreatedByUsername = Unsan(parts[4]);
        a.UpdateAdminUsernames = NormalizeAdmins(
            parts[5].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            a.CreatedByUsername);
        if (a.PlayersPerCaptain < 1) a.PlayersPerCaptain = 11;
    }

    private static void ApplyTeamNamesFromNotes(Auction a)
    {
        var notes = a.Notes;
        if (string.IsNullOrWhiteSpace(notes))
            return;
        var list = a.Captains.OrderBy(c => c.PlayerId).ToList();
        var lots = a.Lots.OrderBy(l => l.PlayerId).ToList();

        if (notes.StartsWith("JPL3|", StringComparison.Ordinal))
        {
            var p = notes.Split('|');
            if (p.Length < 12) return;
            var ops = p[5].Split(',', StringSplitOptions.None);
            var teams = p[6].Split(',', StringSplitOptions.None);
            for (var i = 0; i < list.Count; i++)
            {
                if (i < teams.Length) list[i].TeamName = Unsan(teams[i]);
                if (i < ops.Length) list[i].OperatorUsername = Unsan(ops[i]);
            }
            a.UpdateAdminUsernames = list.Select(c => c.OperatorUsername).Where(s => s.Length > 0).ToList();

            a.Claims.Clear();
            foreach (var chunk in p[7].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var pair = chunk.Split(':');
                if (pair.Length != 2 || !int.TryParse(pair[0], out var idx) || idx < 0 || idx >= list.Count)
                    continue;
                a.Claims.Add(new AuctionCaptainClaim
                {
                    CaptainPlayerId = list[idx].PlayerId,
                    DeviceId = pair[1]
                });
            }

            a.PickOrder.Clear();
            foreach (var tok in p[8].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(tok, out var oi) && oi >= 0 && oi < list.Count)
                    a.PickOrder.Add(list[oi].PlayerId);
            }
            if (a.PickOrder.Count == 0)
                a.PickOrder.AddRange(list.Select(c => c.PlayerId));

            if (int.TryParse(p[9], out var turn))
                a.DraftTurnIndex = Math.Max(0, turn);

            a.DraftGroups.Clear();
            foreach (var g in p[10].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var colon = g.LastIndexOf(':');
                if (colon <= 0) continue;
                var name = Unsan(g[..colon]);
                if (name.Length == 0) continue;
                _ = int.TryParse(g[(colon + 1)..], out var max);
                a.DraftGroups.Add(new AuctionDraftGroup { Name = name, MaxPerCaptain = Math.Max(0, max) });
            }

            var lotIdx = p[11].Split(',', StringSplitOptions.None);
            for (var i = 0; i < lots.Count; i++)
            {
                if (i >= lotIdx.Length || !int.TryParse(lotIdx[i], out var gi)) continue;
                if (gi >= 0 && gi < a.DraftGroups.Count)
                    lots[i].GroupName = a.DraftGroups[gi].Name;
            }
            ApplyAwardHist(a, lots, p.Length >= 13 ? p[12] : null);
            return;
        }

        if (notes.StartsWith("JPL2|", StringComparison.Ordinal))
        {
            var p = notes.Split('|');
            if (p.Length < 13) return;
            var ops = p[8].Split(',', StringSplitOptions.None);
            var teams = p[9].Split(',', StringSplitOptions.None);
            for (var i = 0; i < list.Count; i++)
            {
                if (i < teams.Length) list[i].TeamName = Unsan(teams[i]);
                if (i < ops.Length) list[i].OperatorUsername = Unsan(ops[i]);
            }
            a.UpdateAdminUsernames = list.Select(c => c.OperatorUsername).Where(s => s.Length > 0).ToList();

            a.Claims.Clear();
            foreach (var chunk in p[10].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var pair = chunk.Split(':');
                if (pair.Length != 2 || !int.TryParse(pair[0], out var idx) || idx < 0 || idx >= list.Count)
                    continue;
                a.Claims.Add(new AuctionCaptainClaim
                {
                    CaptainPlayerId = list[idx].PlayerId,
                    DeviceId = pair[1],
                    Username = string.Empty
                });
            }

            var jparts = p[11].Split(',', StringSplitOptions.None);
            for (var i = 0; i < list.Count && i < jparts.Length; i++)
            {
                if (int.TryParse(jparts[i], out var left))
                    list[i].JokersLeft = Math.Max(0, left);
            }

            var rp = p[12].Split(':');
            if (rp.Length >= 3 && int.TryParse(rp[0], out var pidx) && pidx >= 0 && pidx < lots.Count)
            {
                a.CurrentRound = new AuctionRound
                {
                    PlayerId = lots[pidx].PlayerId,
                    IsBlind = rp[1] == "1",
                    IsRevealed = rp.Length > 2 && rp[2] == "1"
                };
                var bidBlob = rp.Length > 3 ? string.Join(":", rp.Skip(3)) : "";
                foreach (var chunk in bidBlob.Split('.', StringSplitOptions.RemoveEmptyEntries))
                {
                    var bits = chunk.Split('-');
                    if (bits.Length < 2 || !int.TryParse(bits[0], out var ci) || ci < 0 || ci >= list.Count)
                        continue;
                    if (!int.TryParse(bits[1], out var amt)) continue;
                    a.CurrentRound.Bids.Add(new AuctionBid
                    {
                        CaptainPlayerId = list[ci].PlayerId,
                        Amount = amt,
                        IsJoker = bits.Length > 2 && bits[2] == "1"
                    });
                }
            }

            a.PickOrder.Clear();
            if (p.Length >= 14)
            {
                foreach (var tok in p[13].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (int.TryParse(tok, out var oi) && oi >= 0 && oi < list.Count)
                        a.PickOrder.Add(list[oi].PlayerId);
                }
            }
            if (a.PickOrder.Count == 0)
                a.PickOrder.AddRange(list.Select(c => c.PlayerId));
            if (p.Length >= 15 && int.TryParse(p[14], out var turn2))
                a.DraftTurnIndex = Math.Max(0, turn2);
            ApplyAwardHist(a, lots, p.Length >= 16 ? p[15] : null);

            return;
        }

        if (!notes.StartsWith("JPL1|", StringComparison.Ordinal))
            return;
        var parts = notes.Split('|');
        if (parts.Length < 8)
            return;
        var teamParts = parts[6].Split(',', StringSplitOptions.None);
        for (var i = 0; i < list.Count && i < teamParts.Length; i++)
            list[i].TeamName = Unsan(teamParts[i]);
        a.Claims.Clear();
        if (parts[7].Length == 0)
            return;
        foreach (var chunk in parts[7].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = chunk.Split(':');
            if (pair.Length != 2 || !int.TryParse(pair[0], out var idx) || idx < 0 || idx >= list.Count)
                continue;
            a.Claims.Add(new AuctionCaptainClaim
            {
                CaptainPlayerId = list[idx].PlayerId,
                DeviceId = pair[1],
                Username = string.Empty
            });
        }
    }

    private static List<string> NormalizeAdmins(IEnumerable<string>? raw, string? createdByUsername)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var item in raw ?? Array.Empty<string>())
        {
            var name = San(item);
            if (name.Length == 0) continue;
            if (!string.IsNullOrWhiteSpace(createdByUsername)
                && string.Equals(name, createdByUsername, StringComparison.OrdinalIgnoreCase))
                continue;
            if (set.Add(name))
                list.Add(name);
        }
        return list;
    }

    private static string San(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return value.Trim().Replace("|", " ").Replace(",", " ").Replace(":", " ");
    }

    private static string Unsan(string? value) => (value ?? string.Empty).Trim();

    private static string GetDeviceId()
    {
        var existing = Preferences.Default.Get(DevicePrefKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;
        var id = Guid.NewGuid().ToString("N");
        Preferences.Default.Set(DevicePrefKey, id);
        return id;
    }

    private static string ShortDevice(string deviceId)
    {
        var t = (deviceId ?? string.Empty).Replace("-", "", StringComparison.Ordinal);
        return t.Length >= 8 ? t[..8] : t.PadRight(8, '0');
    }

    private static bool DeviceEquals(string stored, string current)
        => string.Equals(ShortDevice(stored), ShortDevice(current), StringComparison.OrdinalIgnoreCase);

    private static bool TitleMatches(string title, string? q)
        => string.IsNullOrWhiteSpace(q)
           || title.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string LocalPath()
        => Path.Combine(FileSystem.AppDataDirectory, LocalFileName);

    private bool LocalHas(Guid id) => LoadLocalEntities().Any(a => a.Id == id);

    private List<Auction> LoadLocalEntities()
    {
        lock (LocalLock)
        {
            try
            {
                var path = LocalPath();
                if (!File.Exists(path))
                    return new List<Auction>();
                var json = File.ReadAllText(path);
                var rows = JsonSerializer.Deserialize<List<LocalAuctionRow>>(json, JsonOpts) ?? [];
                return rows.Select(FromLocalRow).Where(a => !a.IsDeleted).ToList();
            }
            catch
            {
                return new List<Auction>();
            }
        }
    }

    private void SaveLocal(Auction entity)
    {
        lock (LocalLock)
        {
            var rows = LoadLocalRowsUnlocked();
            rows.RemoveAll(r => r.Id == entity.Id);
            rows.Add(ToLocalRow(entity));
            File.WriteAllText(LocalPath(), JsonSerializer.Serialize(rows, JsonOpts));
        }
    }

    private void DeleteLocal(Guid id)
    {
        lock (LocalLock)
        {
            var rows = LoadLocalRowsUnlocked();
            rows.RemoveAll(r => r.Id == id);
            File.WriteAllText(LocalPath(), JsonSerializer.Serialize(rows, JsonOpts));
        }
    }

    private List<LocalAuctionRow> LoadLocalRowsUnlocked()
    {
        try
        {
            var path = LocalPath();
            if (!File.Exists(path))
                return new List<LocalAuctionRow>();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<LocalAuctionRow>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return new List<LocalAuctionRow>();
        }
    }

    private Auction FromLocalRow(LocalAuctionRow row)
    {
        var a = new Auction
        {
            Id = row.Id,
            Title = row.Title,
            AuctionDateUtc = row.AuctionDateUtc,
            Notes = row.Notes,
            Version = row.Version,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
            IsDeleted = row.IsDeleted,
            Captains = row.Captains.Select(c => new AuctionCaptain
            {
                AuctionId = row.Id,
                PlayerId = c.PlayerId,
                WalletAmount = c.WalletAmount
            }).ToList(),
            Lots = row.Lots.Select(l => new AuctionLot
            {
                Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id,
                AuctionId = row.Id,
                PlayerId = l.PlayerId,
                BasePrice = l.BasePrice,
                IsSold = l.IsSold,
                SoldPrice = l.SoldPrice,
                SoldToCaptainPlayerId = l.SoldToCaptainPlayerId
            }).ToList()
        };
        UnpackNotes(a);
        ApplyTeamNamesFromNotes(a);
        return a;
    }

    private static LocalAuctionRow ToLocalRow(Auction a) => new()
    {
        Id = a.Id,
        Title = a.Title,
        AuctionDateUtc = a.AuctionDateUtc,
        Notes = a.Notes,
        Version = a.Version,
        CreatedAtUtc = a.CreatedAtUtc,
        UpdatedAtUtc = a.UpdatedAtUtc,
        IsDeleted = a.IsDeleted,
        Captains = a.Captains.Select(c => new LocalCaptainRow
        {
            PlayerId = c.PlayerId,
            WalletAmount = c.WalletAmount
        }).ToList(),
        Lots = a.Lots.Select(l => new LocalLotRow
        {
            Id = l.Id,
            PlayerId = l.PlayerId,
            BasePrice = l.BasePrice,
            IsSold = l.IsSold,
            SoldPrice = l.SoldPrice,
            SoldToCaptainPlayerId = l.SoldToCaptainPlayerId
        }).ToList()
    };

    private sealed class LocalAuctionRow
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime AuctionDateUtc { get; set; }
        public string? Notes { get; set; }
        public long Version { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public bool IsDeleted { get; set; }
        public List<LocalCaptainRow> Captains { get; set; } = new();
        public List<LocalLotRow> Lots { get; set; } = new();
    }

    private sealed class LocalCaptainRow
    {
        public Guid PlayerId { get; set; }
        public int WalletAmount { get; set; }
    }

    private sealed class LocalLotRow
    {
        public Guid Id { get; set; }
        public Guid PlayerId { get; set; }
        public int BasePrice { get; set; }
        public bool IsSold { get; set; }
        public int? SoldPrice { get; set; }
        public Guid? SoldToCaptainPlayerId { get; set; }
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
                var bought = a.Lots
                    .Where(l => l.IsSold && l.SoldToCaptainPlayerId == c.PlayerId)
                    .ToList();
                row.TotalSpentAsCaptain += bought.Sum(l => l.SoldPrice ?? 0);
                row.PlayersBoughtAsCaptain += bought.Count;
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
