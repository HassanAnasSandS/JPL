namespace JPL.Grounds.Api.Models;

public enum AppRole : byte
{
    User = 0,
    Admin = 1,
    SuperAdmin = 2,
    GroundOwner = 3,
    PublicUser = 4,
    Guest = 5
}

public enum GroundListingStatus : byte
{
    Draft = 0,
    Published = 1,
    Paused = 2
}

public static class GroundBookingStatus
{
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string Rejected = "Rejected";

    public static readonly string[] All =
    [
        Pending, Confirmed, Completed, Cancelled, Rejected
    ];

    public static bool IsValid(string? status)
        => All.Any(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return Pending;
        foreach (var s in All)
        {
            if (string.Equals(s, status, StringComparison.OrdinalIgnoreCase))
                return s;
        }
        return Pending;
    }

    public static bool HoldsSlot(string status)
        => string.Equals(status, Pending, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, Confirmed, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, Completed, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Read-only map onto Cric360 Master Users — login stays on JPL.Api.</summary>
public sealed class MasterUser
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public AppRole Role { get; set; } = AppRole.User;
    public Guid? LeagueId { get; set; }
    public string? WhatsAppNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
}

public sealed class Ground
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AddressLine { get; set; }
    public string? ContactPhone { get; set; }
    public string? WhatsAppNumber { get; set; }
    public Guid ProvinceId { get; set; }
    public Guid CityId { get; set; }
    public Guid? AreaId { get; set; }
    public string ProvinceName { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public string? AreaName { get; set; }
    public string? PostalCode { get; set; }
    public string CountryName { get; set; } = "Pakistan";
    public string? GoogleMapsUrl { get; set; }
    public decimal HourlyRate { get; set; }
    public int NightStartsHour { get; set; } = 18;
    public int NightEndsHour { get; set; } = 6;
    public GroundListingStatus ListingStatus { get; set; } = GroundListingStatus.Draft;
    public string? CoverImagePath { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
    public ICollection<GroundImage> Images { get; set; } = new List<GroundImage>();
    public ICollection<GroundAmenity> Amenities { get; set; } = new List<GroundAmenity>();
    public ICollection<GroundService> Services { get; set; } = new List<GroundService>();
    public ICollection<GroundShopItem> ShopItems { get; set; } = new List<GroundShopItem>();
    public ICollection<GroundBookingOffer> Offers { get; set; } = new List<GroundBookingOffer>();
    public ICollection<GroundLink> Links { get; set; } = new List<GroundLink>();
    public ICollection<GroundVideo> Videos { get; set; } = new List<GroundVideo>();
    public ICollection<GroundBooking> Bookings { get; set; } = new List<GroundBooking>();
    public ICollection<GroundReview> Reviews { get; set; } = new List<GroundReview>();
}

public sealed class GroundImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroundId { get; set; }
    public Ground Ground { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsCover { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
}

public sealed class GroundLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroundId { get; set; }
    public Ground Ground { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class GroundVideo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroundId { get; set; }
    public Ground Ground { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class GroundBookingOffer
{
    public Guid GroundId { get; set; }
    public Ground Ground { get; set; } = null!;
    public string Kind { get; set; } = "Hourly";
    public string Title { get; set; } = "Hourly";
    public string? Hint { get; set; }
    public decimal Price { get; set; }
    public decimal NightPrice { get; set; }
    public decimal WeekendPrice { get; set; }
    public decimal WeekendNightPrice { get; set; }
    public decimal DurationHours { get; set; } = 1;
    public bool PricesPerHour { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class GroundAmenity
{
    public Guid GroundId { get; set; }
    public Ground Ground { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
}

public sealed class GroundService
{
    public Guid GroundId { get; set; }
    public Ground Ground { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class GroundBookingService
{
    public Guid BookingId { get; set; }
    public GroundBooking Booking { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class GroundShopItem
{
    public Guid GroundId { get; set; }
    public Ground Ground { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Kind { get; set; } = GroundShopKinds.Rent;
}

public sealed class GroundBookingShopItem
{
    public Guid BookingId { get; set; }
    public GroundBooking Booking { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Kind { get; set; } = GroundShopKinds.Rent;
    public int Qty { get; set; }
}

public sealed class GroundBooking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroundId { get; set; }
    public Ground Ground { get; set; } = null!;
    public Guid CustomerUserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public string BookingKind { get; set; } = "Hourly";
    public decimal BookedHours { get; set; }
    public string Status { get; set; } = GroundBookingStatus.Pending;
    public decimal BookingAmount { get; set; }
    public decimal Discount { get; set; }
    public decimal Advance { get; set; }
    public string? Notes { get; set; }
    public string? PaymentNote { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
    public ICollection<GroundBookingService> Services { get; set; } = new List<GroundBookingService>();
    public ICollection<GroundBookingShopItem> ShopItems { get; set; } = new List<GroundBookingShopItem>();
    public GroundReview? Review { get; set; }

    public bool IsOwnerCreated => CustomerUserId == Guid.Empty;

    public decimal Balance => Math.Max(0, BookingAmount - Discount - Advance);

    public void ApplyMoney(decimal? discount, decimal? advance)
    {
        if (discount is decimal d)
            Discount = Math.Max(0, d);
        Discount = Math.Min(Discount, BookingAmount);
        if (advance is decimal a)
            Advance = Math.Max(0, a);
        var maxAdvance = Math.Max(0, BookingAmount - Discount);
        if (Advance > maxAdvance)
            Advance = maxAdvance;
    }
}

public sealed class GroundReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroundId { get; set; }
    public Ground Ground { get; set; } = null!;
    public Guid BookingId { get; set; }
    public GroundBooking Booking { get; set; } = null!;
    public Guid CustomerUserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
}

/// <summary>SuperAdmin catalog — owners pick these on a ground; not the per-ground join rows.</summary>
public sealed class AmenityCatalog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class ServiceCatalog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class ShopCatalog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = GroundShopKinds.Rent;
    public decimal Price { get; set; }
    public int SortOrder { get; set; }
}

public static class GroundShopKinds
{
    public const string Rent = "Rent";
    public const string Sell = "Sell";
    public const int MaxQty = 99;

    public static readonly string[] All = [Rent, Sell];

    public static bool IsValid(string? kind)
        => All.Any(s => string.Equals(s, kind, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? kind)
        => string.Equals(kind, Sell, StringComparison.OrdinalIgnoreCase) ? Sell : Rent;

    public static int ClampQty(int qty)
        => Math.Clamp(qty, 0, MaxQty);
}

public static class GroundCatalogSeed
{
    public static readonly string[] Amenities =
    [
        "Turf", "Matting", "Floodlights", "PracticeNets", "Parking",
        "Washrooms", "Pavilion", "Scoreboard", "Cafeteria"
    ];

    public static readonly string[] Services =
    [
        "Umpire", "Scorer", "Tape balls", "Cricket balls", "Generator",
        "Live streaming", "Photographer", "Refreshments", "Kit hire",
        "Extra nets", "Shamiana", "Sound system"
    ];

    public static readonly (string Name, string Kind)[] Shop =
    [
        ("Helmet", GroundShopKinds.Rent),
        ("Batting pads", GroundShopKinds.Rent),
        ("Bat", GroundShopKinds.Rent),
        ("Gloves", GroundShopKinds.Rent),
        ("Kit hire", GroundShopKinds.Rent),
        ("Soft drinks", GroundShopKinds.Sell),
        ("Mineral water", GroundShopKinds.Sell),
        ("Tape ball pack", GroundShopKinds.Sell)
    ];
}
