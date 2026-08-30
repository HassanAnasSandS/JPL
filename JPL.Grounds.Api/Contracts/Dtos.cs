namespace JPL.Grounds.Api.Contracts;

public sealed record GroundCardDto(
    Guid Id,
    string Name,
    string ProvinceName,
    string CityName,
    string? AreaName,
    decimal HourlyRate,
    string? CoverImageUrl,
    IReadOnlyList<string> Amenities,
    IReadOnlyList<GroundServiceDto> Services,
    string? ContactPhone,
    string? WhatsAppNumber,
    string? PostalCode,
    string? GoogleMapsUrl,
    string? CountryName,
    IReadOnlyList<GroundBookingOfferDto> Offers,
    bool CanDelete = false,
    double AverageRating = 0,
    int ReviewCount = 0,
    int CompletedBookingCount = 0);

public sealed record GroundDetailDto(
    Guid Id,
    Guid OwnerUserId,
    string Name,
    string? Description,
    string? AddressLine,
    string? ContactPhone,
    string? WhatsAppNumber,
    Guid ProvinceId,
    Guid CityId,
    Guid? AreaId,
    string ProvinceName,
    string CityName,
    string? AreaName,
    decimal HourlyRate,
    string ListingStatus,
    IReadOnlyList<string> Amenities,
    IReadOnlyList<GroundServiceDto> Services,
    IReadOnlyList<GroundImageDto> Images,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? PostalCode,
    string? GoogleMapsUrl,
    double? Latitude,
    double? Longitude,
    string? CountryName,
    IReadOnlyList<GroundBookingOfferDto> Offers,
    int NightStartsHour,
    IReadOnlyList<GroundLinkDto> Links,
    int NightEndsHour,
    IReadOnlyList<GroundShopItemDto> Shop,
    IReadOnlyList<GroundVideoDto> Videos,
    double AverageRating = 0,
    int ReviewCount = 0,
    int CompletedBookingCount = 0);

public sealed record GroundImageDto(Guid Id, string FileName, string Url, bool IsCover, int SortOrder);

public sealed record GroundServiceDto(string Name, decimal Amount);

public sealed record GroundServiceUpsert(string Name, decimal Amount);

public sealed record GroundShopItemDto(string Name, decimal Price, string Kind, int Qty = 0);

public sealed record GroundShopItemUpsert(string Name, decimal Price, string Kind);

public sealed record GroundShopQtyUpsert(string Name, int Qty);

public sealed record GroundBookingOfferDto(
    string Kind,
    string Title,
    string Hint,
    decimal Price,
    decimal DurationHours,
    bool IsEnabled,
    bool PricesPerHour,
    int SuggestedStartHour,
    decimal NightPrice,
    decimal WeekendPrice,
    decimal WeekendNightPrice);

public sealed record GroundBookingOfferUpsert(
    string? Kind,
    string? Title,
    string? Hint,
    decimal Price,
    decimal DurationHours,
    bool IsEnabled,
    bool PricesPerHour,
    decimal NightPrice,
    decimal WeekendPrice,
    decimal WeekendNightPrice);

public sealed record GroundUpsertRequest(
    string Name,
    string? Description,
    string? AddressLine,
    string? ContactPhone,
    string? WhatsAppNumber,
    string? GoogleMapsUrl,
    decimal HourlyRate,
    string? ListingStatus,
    IReadOnlyList<string>? Amenities,
    IReadOnlyList<GroundServiceUpsert>? Services,
    IReadOnlyList<GroundShopItemUpsert>? Shop,
    string? PostalCode,
    string? CountryName,
    string? ProvinceName,
    string? CityName,
    string? AreaName,
    IReadOnlyList<GroundBookingOfferUpsert>? Offers,
    int NightStartsHour,
    IReadOnlyList<GroundLinkUpsert>? Links,
    int NightEndsHour,
    IReadOnlyList<GroundVideoUpsert>? Videos);

public sealed record GroundLinkDto(string Title, string Url);

public sealed record GroundLinkUpsert(string? Title, string Url);

public sealed record GroundVideoDto(string Title, string Url);

public sealed record GroundVideoUpsert(string? Title, string Url);

public sealed record MapsResolveRequest(string GoogleMapsUrl);

public sealed record MapsLocationDto(
    string GoogleMapsUrl,
    string PostalCode,
    double Latitude,
    double Longitude,
    string CountryName,
    string ProvinceName,
    string CityName,
    string? AreaName,
    string? FormattedAddress);

public sealed record GroundSearchPage(
    IReadOnlyList<GroundCardDto> Items,
    string? NextCursor,
    bool HasMore,
    int TotalCount = 0);

public sealed record GroundLocationOptionsDto(
    IReadOnlyList<string> Countries,
    IReadOnlyList<string> States,
    IReadOnlyList<string> Cities,
    IReadOnlyList<string> Areas);

public sealed record BookingDto(
    Guid Id,
    Guid GroundId,
    string GroundName,
    Guid CustomerUserId,
    string CustomerName,
    string? CustomerPhone,
    DateTime StartAt,
    DateTime EndAt,
    string Status,
    decimal BookingAmount,
    decimal Discount,
    decimal Advance,
    decimal Balance,
    string? Notes,
    string? PaymentNote,
    DateTime CreatedAtUtc,
    string BookingKind,
    string BookingTitle,
    decimal BookedHours,
    IReadOnlyList<GroundServiceDto> Services,
    string? OwnerWhatsAppNumber,
    string? OwnerContactPhone,
    IReadOnlyList<GroundShopItemDto> Shop,
    bool HasReview = false,
    int? MyRating = null,
    Guid? ReviewId = null);

public sealed record BookingCreateRequest(
    DateTime StartAt,
    DateTime EndAt,
    string? CustomerName,
    string? CustomerPhone,
    string? Notes,
    string? BookingKind,
    decimal Hours,
    IReadOnlyList<string>? Services,
    IReadOnlyList<GroundShopQtyUpsert>? Shop,
    decimal? BookingAmount = null,
    decimal? Discount = null,
    decimal? Advance = null);

public sealed record BookingUpdateRequest(
    string? Status,
    decimal? BookingAmount,
    decimal? Discount,
    decimal? Advance,
    string? PaymentNote,
    string? Notes);

public sealed record CatalogItemDto(Guid Id, string Name, int SortOrder);

public sealed record CatalogItemUpsert(string Name);

public sealed record ShopCatalogItemDto(Guid Id, string Name, string Kind, decimal Price, int SortOrder);

public sealed record ShopCatalogItemUpsert(string Name);

public sealed record GroundReviewDto(
    Guid Id,
    Guid GroundId,
    Guid BookingId,
    Guid CustomerUserId,
    string CustomerName,
    int Rating,
    string? Comment,
    DateTime CreatedAtUtc,
    DateTime BookingStartAt);

public sealed record GroundReviewUpsert(int Rating, string? Comment);

public sealed record GroundReviewPage(
    IReadOnlyList<GroundReviewDto> Items,
    string? NextCursor,
    bool HasMore,
    int TotalCount = 0);

public sealed record BookingPage(
    IReadOnlyList<BookingDto> Items,
    string? NextCursor,
    bool HasMore,
    int TotalCount = 0);
