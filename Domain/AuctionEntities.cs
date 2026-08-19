namespace JPL.Domain;

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

    public ICollection<AuctionCaptain> Captains { get; set; } = new List<AuctionCaptain>();
    public ICollection<AuctionLot> Lots { get; set; } = new List<AuctionLot>();
}

public sealed class AuctionCaptain
{
    public Guid AuctionId { get; set; }
    public Auction Auction { get; set; } = null!;
    public Guid PlayerId { get; set; }
    /// <summary>Budget this captain may spend in the auction.</summary>
    public int WalletAmount { get; set; }
}

public sealed class AuctionLot
{
    public Guid Id { get; set; }
    public Guid AuctionId { get; set; }
    public Auction Auction { get; set; } = null!;
    public Guid PlayerId { get; set; }
    /// <summary>Opening / reserve price.</summary>
    public int BasePrice { get; set; }
    /// <summary>Final sold amount; null when unsold.</summary>
    public int? SoldPrice { get; set; }
    /// <summary>Buying captain (player id); null when unsold.</summary>
    public Guid? SoldToCaptainPlayerId { get; set; }
    public bool IsSold { get; set; }
}
