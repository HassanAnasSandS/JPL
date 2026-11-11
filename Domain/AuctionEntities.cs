using System.ComponentModel.DataAnnotations.Schema;

namespace JPL.Domain;

public enum AuctionKind : byte
{
    SimpleAuction = 0,
    AdvanceAuction = 1,
    SimpleDrafting = 2,
    AdvanceDrafting = 3,
    /// <summary>Original manual entry — no live rounds / turns; creator sets sold price + captain per lot.</summary>
    BasicAuction = 4
}

public enum AuctionRunMode : byte
{
    /// <summary>Device-only (Basic Auction). Persisted locally on Save — no cloud round-trips.</summary>
    Local = 0,
    Live = 1
}

public static class AuctionKindText
{
    public static readonly string[] Labels =
    [
        "Simple Auction",
        "Advance Auction",
        "Simple Drafting",
        "Advance Drafting",
        "Basic Auction"
    ];

    /// <summary>Create picker order — Basic first (original manual auction).</summary>
    public static readonly string[] CreateLabels =
    [
        "Basic Auction",
        "Simple Auction",
        "Advance Auction",
        "Simple Drafting",
        "Advance Drafting"
    ];

    public static string Of(AuctionKind kind) => kind switch
    {
        AuctionKind.SimpleAuction => Labels[0],
        AuctionKind.AdvanceAuction => Labels[1],
        AuctionKind.SimpleDrafting => Labels[2],
        AuctionKind.AdvanceDrafting => Labels[3],
        AuctionKind.BasicAuction => Labels[4],
        _ => Labels[0]
    };

    public static bool TryParse(string? label, out AuctionKind kind)
    {
        kind = AuctionKind.SimpleAuction;
        if (string.IsNullOrWhiteSpace(label))
            return false;
        for (var i = 0; i < Labels.Length; i++)
        {
            if (!string.Equals(Labels[i], label.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            kind = (AuctionKind)i;
            return true;
        }
        return false;
    }

    public static AuctionKind Parse(string? label)
        => TryParse(label, out var kind) ? kind : AuctionKind.SimpleAuction;

    public static bool IsBasic(AuctionKind kind) => kind == AuctionKind.BasicAuction;
    /// <summary>Basic Auction always runs on-device; other kinds are cloud live.</summary>
    public static AuctionRunMode RunModeFor(AuctionKind kind)
        => IsBasic(kind) ? AuctionRunMode.Local : AuctionRunMode.Live;
    public static bool IsAdvanceAuction(AuctionKind kind) => kind == AuctionKind.AdvanceAuction;
    public static bool IsAdvance(AuctionKind kind) => IsAdvanceAuction(kind);
    public static bool IsDrafting(AuctionKind kind)
        => kind is AuctionKind.SimpleDrafting or AuctionKind.AdvanceDrafting;
    public static bool IsAdvanceDrafting(AuctionKind kind) => kind == AuctionKind.AdvanceDrafting;
    /// <summary>Live round / turn based (not Basic manual entry).</summary>
    public static bool IsRoundBased(AuctionKind kind)
        => kind is AuctionKind.SimpleAuction or AuctionKind.AdvanceAuction
            or AuctionKind.SimpleDrafting or AuctionKind.AdvanceDrafting;
    public static bool RequiresCaptainCode(AuctionKind kind)
        => kind is AuctionKind.AdvanceAuction or AuctionKind.SimpleDrafting or AuctionKind.AdvanceDrafting;
}

public sealed class AuctionCaptainClaim
{
    public Guid CaptainPlayerId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

public sealed class AuctionBid
{
    public Guid CaptainPlayerId { get; set; }
    public int Amount { get; set; }
    public bool IsJoker { get; set; }
}

public sealed class AuctionRound
{
    public Guid PlayerId { get; set; }
    public bool IsBlind { get; set; }
    public bool IsRevealed { get; set; }
    public List<AuctionBid> Bids { get; set; } = new();
}

public sealed class AuctionDraftGroup
{
    public string Name { get; set; } = string.Empty;
    public int MaxPerCaptain { get; set; }
}

public sealed class Auction
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime AuctionDateUtc { get; set; } = DateTime.Now;
    public string? Notes { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }

    /// <summary>Packed in Notes. Not SQL columns.</summary>
    [NotMapped]
    public AuctionKind Kind { get; set; } = AuctionKind.SimpleAuction;
    [NotMapped]
    public AuctionRunMode RunMode { get; set; } = AuctionRunMode.Live;
    [NotMapped]
    public Guid? CreatedByUserId { get; set; }
    [NotMapped]
    public string? CreatedByUsername { get; set; }
    [NotMapped]
    public int PlayersPerCaptain { get; set; } = 11;
    [NotMapped]
    public int JokersPerCaptain { get; set; }
    [NotMapped]
    public int BlindBidRoundsAllowed { get; set; }
    /// <summary>Round numbers that open as Blind (e.g. 3, 6, 9). Packed in Notes. Empty = legacy count + checkbox.</summary>
    [NotMapped]
    public List<int> BlindRoundNumbers { get; set; } = new();
    [NotMapped]
    public int BlindRoundsUsed { get; set; }
    [NotMapped]
    public int DraftTurnIndex { get; set; }
    [NotMapped]
    public List<Guid> PickOrder { get; set; } = new();
    [NotMapped]
    public List<Guid> AwardedPlayerIds { get; set; } = new();
    [NotMapped]
    public List<AuctionDraftGroup> DraftGroups { get; set; } = new();
    [NotMapped]
    public AuctionRound? CurrentRound { get; set; }
    [NotMapped]
    public List<string> UpdateAdminUsernames { get; set; } = new();
    [NotMapped]
    public List<AuctionCaptainClaim> Claims { get; set; } = new();

    public ICollection<AuctionCaptain> Captains { get; set; } = new List<AuctionCaptain>();
    public ICollection<AuctionLot> Lots { get; set; } = new List<AuctionLot>();
}

public sealed class AuctionCaptain
{
    public Guid AuctionId { get; set; }
    public Auction Auction { get; set; } = null!;
    public Guid PlayerId { get; set; }
    [NotMapped]
    public string TeamName { get; set; } = string.Empty;
    public int WalletAmount { get; set; }
    [NotMapped]
    public string OperatorUsername { get; set; } = string.Empty;
    [NotMapped]
    public int JokersLeft { get; set; }
}

public sealed class AuctionLot
{
    public Guid Id { get; set; }
    public Guid AuctionId { get; set; }
    public Auction Auction { get; set; } = null!;
    public Guid PlayerId { get; set; }
    public int BasePrice { get; set; }
    public int? SoldPrice { get; set; }
    public Guid? SoldToCaptainPlayerId { get; set; }
    public bool IsSold { get; set; }
    [NotMapped]
    public string GroupName { get; set; } = string.Empty;
}

public static class AuctionRules
{
    public static int BoughtCount(Auction a, Guid captainId)
        => a.Lots.Count(l => l.IsSold && l.SoldToCaptainPlayerId == captainId);

    public static int RemainingSlots(Auction a, Guid captainId)
        => Math.Max(0, Math.Max(1, a.PlayersPerCaptain) - BoughtCount(a, captainId));

    public static int RemainingWallet(Auction a, Guid captainId)
    {
        var cap = a.Captains.FirstOrDefault(c => c.PlayerId == captainId);
        if (cap is null) return 0;
        var spent = a.Lots
            .Where(l => l.IsSold && l.SoldToCaptainPlayerId == captainId)
            .Sum(l => l.SoldPrice ?? 0);
        return cap.WalletAmount - spent;
    }

    public static int MaxBidThisRound(Auction a, Guid captainId, Guid? currentPlayerId)
    {
        var slots = RemainingSlots(a, captainId);
        if (slots <= 0) return 0;
        var wallet = RemainingWallet(a, captainId);
        var keep = MinReserveAfterWin(a, captainId, currentPlayerId);
        return Math.Max(0, wallet - keep);
    }

    public static int MinNextBid(Auction a, AuctionRound? round)
    {
        var lot = round is null
            ? null
            : a.Lots.FirstOrDefault(l => l.PlayerId == round.PlayerId);
        var basePrice = lot?.BasePrice ?? 0;
        if (round is null || round.Bids.Count == 0 || round.IsBlind)
            return basePrice;
        var top = round.Bids.OrderByDescending(b => b.Amount).First();
        return top.IsJoker ? checkedSafeDouble(top.Amount) : top.Amount + 1;
    }

    public static string ValidatePool(Auction a)
    {
        var captains = a.Captains.Count;
        var need = captains * Math.Max(1, a.PlayersPerCaptain);
        if (a.Lots.Count < need)
            return $"Need at least {need} players ({captains} captains × {a.PlayersPerCaptain} each). Added {a.Lots.Count}.";

        if (AuctionKindText.IsDrafting(a.Kind))
        {
            if (AuctionKindText.IsAdvanceDrafting(a.Kind))
            {
                var groups = a.DraftGroups.Where(g => !string.IsNullOrWhiteSpace(g.Name) && g.MaxPerCaptain > 0).ToList();
                if (groups.Count == 0)
                    return "Add at least 1 group (e.g. Platinum) with a max per captain.";
                if (groups.Sum(g => g.MaxPerCaptain) < a.PlayersPerCaptain)
                    return $"Group maxes ({groups.Sum(g => g.MaxPerCaptain)}) must cover {a.PlayersPerCaptain} players per captain.";
                if (groups.Select(g => g.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != groups.Count)
                    return "Group names must be unique.";
                var names = groups.Select(g => g.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (a.Lots.Any(l => string.IsNullOrWhiteSpace(l.GroupName) || !names.Contains(l.GroupName.Trim())))
                    return "Set a group for every player.";
            }
            return string.Empty;
        }

        var minBase = a.Lots.Count == 0 ? 0 : a.Lots.Min(l => l.BasePrice);
        foreach (var c in a.Captains)
        {
            var minWallet = a.PlayersPerCaptain * minBase;
            if (c.WalletAmount < minWallet)
                return $"{c.TeamName}: wallet {c.WalletAmount:N0} is not enough to buy {a.PlayersPerCaptain} players (min {minWallet:N0}).";
            var remainSlots = RemainingSlots(a, c.PlayerId);
            var remainWallet = RemainingWallet(a, c.PlayerId);
            var minRemain = remainSlots * minBase;
            if (remainWallet < minRemain)
                return $"{c.TeamName}: remaining wallet {remainWallet:N0} cannot cover {remainSlots} remaining players (min {minRemain:N0}).";
        }
        return string.Empty;
    }

    public static int NextRoundNumber(Auction a) => Math.Max(0, a.DraftTurnIndex) + 1;

    public static bool HasScheduledBlindRounds(Auction a)
        => a.BlindRoundNumbers.Count > 0;

    public static bool IsScheduledBlindRound(Auction a, int roundNumber)
        => AuctionKindText.IsAdvanceAuction(a.Kind) && a.BlindRoundNumbers.Contains(roundNumber);

    public static bool NextRoundIsBlind(Auction a)
        => IsScheduledBlindRound(a, NextRoundNumber(a));

    public static bool IsSettledBlindRound(Auction a)
        => a.CurrentRound is { IsBlind: true, IsRevealed: true };

    public static IReadOnlyList<Guid> ExpectedBlindBidderIds(Auction a)
        => a.Captains.Where(c => RemainingSlots(a, c.PlayerId) > 0).Select(c => c.PlayerId).ToList();

    public static bool AllExpectedBlindBidsIn(Auction a, AuctionRound round)
    {
        var ids = ExpectedBlindBidderIds(a);
        return ids.Count > 0 && ids.All(id => round.Bids.Any(b => b.CaptainPlayerId == id));
    }

    public static bool IsComplete(Auction a)
        => a.Captains.Count >= 2
            && a.Lots.Count > 0
            && (a.CurrentRound is null || IsSettledBlindRound(a))
            && CurrentPickCaptainId(a) is null;

    public static Guid? CurrentPickCaptainId(Auction a)
    {
        var order = a.PickOrder.Count > 0
            ? a.PickOrder.Where(id => a.Captains.Any(c => c.PlayerId == id)).ToList()
            : a.Captains.Select(c => c.PlayerId).ToList();
        if (order.Count == 0) return null;
        for (var n = 0; n < order.Count; n++)
        {
            var id = order[(a.DraftTurnIndex + n) % order.Count];
            if (RemainingSlots(a, id) > 0 && a.Lots.Any(l => CanPick(a, id, l)))
                return id;
        }
        return null;
    }

    public static int TakenInGroup(Auction a, Guid captainId, string group)
        => a.Lots.Count(l => l.IsSold
            && l.SoldToCaptainPlayerId == captainId
            && string.Equals(l.GroupName, group, StringComparison.OrdinalIgnoreCase));

    public static int MaxInGroup(Auction a, string group)
        => a.DraftGroups.FirstOrDefault(g =>
            string.Equals(g.Name, group, StringComparison.OrdinalIgnoreCase))?.MaxPerCaptain ?? 0;

    public static bool CanPick(Auction a, Guid captainId, AuctionLot lot)
    {
        if (lot.IsSold) return false;
        if (RemainingSlots(a, captainId) <= 0) return false;
        if (!AuctionKindText.IsAdvanceDrafting(a.Kind)) return true;
        var max = MaxInGroup(a, lot.GroupName);
        if (max <= 0) return false;
        return TakenInGroup(a, captainId, lot.GroupName) < max;
    }

    private static int MinReserveAfterWin(Auction a, Guid captainId, Guid? currentPlayerId)
    {
        var after = RemainingSlots(a, captainId) - 1;
        if (after <= 0) return 0;
        var cheapest = a.Lots
            .Where(l => !l.IsSold && l.PlayerId != currentPlayerId)
            .Select(l => l.BasePrice)
            .OrderBy(x => x)
            .Take(after)
            .ToList();
        if (cheapest.Count >= after)
            return cheapest.Sum();
        var min = a.Lots.Where(l => !l.IsSold).Select(l => l.BasePrice).DefaultIfEmpty(0).Min();
        return after * min;
    }

    private static int checkedSafeDouble(int amount)
    {
        if (amount > int.MaxValue / 2) return int.MaxValue;
        return amount * 2;
    }
}
