using Microsoft.EntityFrameworkCore;
using JPL.Grounds.Api.Contracts;
using JPL.Grounds.Api.Data;
using JPL.Grounds.Api.Models;

namespace JPL.Grounds.Api.Services;

internal readonly record struct GroundPublicStats(double AverageRating, int ReviewCount, int CompletedBookingCount);

internal static class GroundsMap
{
    public const int MaxLinksPerGround = 8;
    public const int MaxVideosPerGround = 8;
    public const int MaxVideoUrlLength = 1000;
    public const int MinRating = 1;
    public const int MaxRating = 5;

    public static string ImageUrl(HttpRequest request, Guid groundId, string fileName)
        => $"{request.Scheme}://{request.Host.Value}/api/grounds/{groundId:D}/images/{fileName}";

    public static IReadOnlyList<GroundBookingOfferDto> ToOffers(Ground g, bool enabledOnly = false)
    {
        var rows = (g.Offers ?? Enumerable.Empty<GroundBookingOffer>())
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.Title, StringComparer.OrdinalIgnoreCase)
            .AsEnumerable();
        if (enabledOnly)
            rows = rows.Where(o => o.IsEnabled && o.Price > 0);

        return rows.Select(row =>
        {
            var title = string.IsNullOrWhiteSpace(row.Title)
                ? GroundBookingKinds.LegacyTitle(row.Kind)
                : row.Title.Trim();
            var hours = row.DurationHours > 0 ? row.DurationHours : 1;
            var perHour = hours <= 1;
            return new GroundBookingOfferDto(
                row.Kind,
                title,
                string.Empty,
                row.Price,
                hours,
                row.IsEnabled,
                perHour,
                GroundBookingKinds.SuggestedStartHour(perHour, hours),
                row.NightPrice,
                row.WeekendPrice,
                row.WeekendNightPrice);
        }).ToList();
    }

    public static GroundCardDto ToCard(
        Ground g, HttpRequest request, bool canDelete = false, GroundPublicStats stats = default)
        => new(
            g.Id,
            g.Name,
            g.ProvinceName,
            g.CityName,
            g.AreaName,
            g.HourlyRate,
            string.IsNullOrWhiteSpace(g.CoverImagePath) ? null : ImageUrl(request, g.Id, g.CoverImagePath),
            g.Amenities.Select(a => a.Code).OrderBy(c => c).ToList(),
            ToServices(g.Services),
            g.ContactPhone,
            g.WhatsAppNumber,
            g.PostalCode,
            g.GoogleMapsUrl,
            g.CountryName,
            ToOffers(g, enabledOnly: true),
            canDelete,
            stats.AverageRating,
            stats.ReviewCount,
            stats.CompletedBookingCount);

    public static GroundDetailDto ToDetail(Ground g, HttpRequest request, GroundPublicStats stats = default)
        => new(
            g.Id,
            g.OwnerUserId,
            g.Name,
            g.Description,
            g.AddressLine,
            g.ContactPhone,
            g.WhatsAppNumber,
            g.ProvinceId,
            g.CityId,
            g.AreaId,
            g.ProvinceName,
            g.CityName,
            g.AreaName,
            g.HourlyRate,
            g.ListingStatus.ToString(),
            g.Amenities.Select(a => a.Code).OrderBy(c => c).ToList(),
            ToServices(g.Services),
            g.Images.OrderBy(i => i.SortOrder).Select(i => new GroundImageDto(
                i.Id,
                i.FileName,
                ImageUrl(request, g.Id, i.FileName),
                i.IsCover,
                i.SortOrder)).ToList(),
            g.CreatedAtUtc,
            g.UpdatedAtUtc,
            g.PostalCode,
            g.GoogleMapsUrl,
            g.Latitude,
            g.Longitude,
            g.CountryName,
            ToOffers(g),
            GroundBookingKinds.ClampNightHour(g.NightStartsHour),
            ToLinks(g),
            GroundBookingKinds.ClampNightEndHour(g.NightEndsHour),
            ToShop(g.ShopItems),
            ToVideos(g),
            stats.AverageRating,
            stats.ReviewCount,
            stats.CompletedBookingCount);

    public static BookingDto ToBooking(GroundBooking b, Ground ground)
        => ToBooking(b, ground.Name, ground.WhatsAppNumber, ground.ContactPhone, ground.Offers);

    public static BookingDto ToBooking(
        GroundBooking b,
        string groundName,
        string? ownerWhatsApp = null,
        string? ownerPhone = null,
        IEnumerable<GroundBookingOffer>? offers = null)
        => new(
            b.Id,
            b.GroundId,
            groundName,
            b.CustomerUserId,
            b.CustomerName,
            b.CustomerPhone,
            b.StartAt,
            b.EndAt,
            b.Status,
            b.BookingAmount,
            b.Discount,
            b.Advance,
            b.Balance,
            b.Notes,
            b.PaymentNote,
            b.CreatedAtUtc,
            b.BookingKind,
            GroundBookingKinds.TitleOf(b.BookingKind, offers),
            b.BookedHours,
            ToBookingServices(b.Services),
            ownerWhatsApp,
            ownerPhone,
            ToBookingShop(b.ShopItems),
            b.Review is not null,
            b.Review?.Rating,
            b.Review?.Id);

    public static GroundReviewDto ToReview(GroundReview r)
        => new(
            r.Id,
            r.GroundId,
            r.BookingId,
            r.CustomerUserId,
            r.CustomerName,
            r.Rating,
            r.Comment,
            r.CreatedAtUtc,
            r.Booking?.StartAt ?? r.CreatedAtUtc);

    public static async Task<Dictionary<Guid, GroundPublicStats>> LoadStatsAsync(
        GroundsDbContext db, IReadOnlyList<Guid> groundIds, CancellationToken ct)
    {
        if (groundIds.Count == 0)
            return new Dictionary<Guid, GroundPublicStats>();

        var completed = await db.Bookings.AsNoTracking()
            .Where(b => !b.IsDeleted
                        && groundIds.Contains(b.GroundId)
                        && b.Status == GroundBookingStatus.Completed)
            .GroupBy(b => b.GroundId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var ratings = await db.Reviews.AsNoTracking()
            .Where(r => groundIds.Contains(r.GroundId) && !r.Booking.IsDeleted)
            .GroupBy(r => r.GroundId)
            .Select(g => new { g.Key, Count = g.Count(), Sum = g.Sum(x => x.Rating) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var completedMap = completed.ToDictionary(x => x.Key, x => x.Count);
        var result = new Dictionary<Guid, GroundPublicStats>(groundIds.Count);
        foreach (var id in groundIds)
        {
            var completedCount = completedMap.GetValueOrDefault(id);
            var rating = ratings.FirstOrDefault(x => x.Key == id);
            var avg = rating is null || rating.Count == 0
                ? 0
                : Math.Round(rating.Sum / (double)rating.Count, 1);
            result[id] = new GroundPublicStats(avg, rating?.Count ?? 0, completedCount);
        }

        return result;
    }

    public static string? ReviewBlockReason(GroundBooking booking, Guid userId)
    {
        if (booking.IsDeleted)
            return "Booking not found.";
        if (!string.Equals(booking.Status, GroundBookingStatus.Completed, StringComparison.OrdinalIgnoreCase))
            return "You can review only after the booking is marked Completed.";
        if (booking.CustomerUserId == Guid.Empty || booking.CustomerUserId != userId)
            return "Only the customer who completed this booking can review.";
        return null;
    }

    public static bool TryNormalizeReview(GroundReviewUpsert request, out int rating, out string? comment, out string? error)
    {
        rating = request.Rating;
        comment = Trim(request.Comment, 1000);
        error = null;
        if (rating < MinRating || rating > MaxRating)
        {
            error = "Rate this ground from 1 to 5 stars.";
            return false;
        }

        return true;
    }

    private static string? Trim(string? value, int max)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t))
            return null;
        return t.Length <= max ? t : t[..max];
    }

    public static IReadOnlyList<GroundLinkDto> ToLinks(Ground g)
        => (g.Links ?? Enumerable.Empty<GroundLink>())
            .OrderBy(x => x.SortOrder)
            .Select(x => new GroundLinkDto(
                string.IsNullOrWhiteSpace(x.Title) ? string.Empty : x.Title.Trim(),
                x.Url))
            .ToList();

    public static IReadOnlyList<GroundVideoDto> ToVideos(Ground g)
        => (g.Videos ?? Enumerable.Empty<GroundVideo>())
            .OrderBy(x => x.SortOrder)
            .Select(x => new GroundVideoDto(
                string.IsNullOrWhiteSpace(x.Title) ? string.Empty : x.Title.Trim(),
                x.Url))
            .ToList();

    public static string NormalizeVideoUrl(string raw)
    {
        var text = (raw ?? string.Empty).Trim();
        var iframe = System.Text.RegularExpressions.Regex.Match(
            text,
            """src\s*=\s*["'](?<url>https?://[^"']+)["']""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (iframe.Success)
            text = iframe.Groups["url"].Value.Trim();
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new InvalidOperationException("Each video needs a URL.");
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
            throw new InvalidOperationException($"Not a valid video link: {value}");
        var normalized = uri.AbsoluteUri;
        if (normalized.Length > MaxVideoUrlLength)
            throw new InvalidOperationException("Video link is too long.");
        return normalized;
    }

    public static string NormalizeHttpUrl(string raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new InvalidOperationException("Each link needs a URL.");
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
            throw new InvalidOperationException($"Not a valid web link: {value}");
        var normalized = uri.AbsoluteUri;
        if (normalized.Length > 500)
            throw new InvalidOperationException("Link is too long.");
        return normalized;
    }

    public static IReadOnlyList<GroundServiceDto> ToServices(IEnumerable<GroundService>? rows)
        => (rows ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Code) && s.Amount >= 0)
            .OrderBy(s => s.Code)
            .Select(s => new GroundServiceDto(s.Code, s.Amount))
            .ToList();

    public static IReadOnlyList<GroundServiceDto> ToBookingServices(IEnumerable<GroundBookingService>? rows)
        => (rows ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Code))
            .OrderBy(s => s.Code)
            .Select(s => new GroundServiceDto(s.Code, s.Amount))
            .ToList();

    public static IReadOnlyList<GroundShopItemDto> ToShop(IEnumerable<GroundShopItem>? rows)
        => (rows ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Code) && s.Price >= 0)
            .OrderBy(s => s.Code)
            .Select(s => new GroundShopItemDto(s.Code, s.Price, GroundShopKinds.Normalize(s.Kind)))
            .ToList();

    public static IReadOnlyList<GroundShopItemDto> ToBookingShop(IEnumerable<GroundBookingShopItem>? rows)
        => (rows ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Code) && s.Qty > 0)
            .OrderBy(s => s.Code)
            .Select(s => new GroundShopItemDto(s.Code, s.Price, GroundShopKinds.Normalize(s.Kind), s.Qty))
            .ToList();

    public static List<GroundService> PickRequested(Ground ground, IReadOnlyList<string>? requested)
    {
        var names = (requested ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();
        if (names.Count == 0)
            return [];

        return (ground.Services ?? [])
            .Where(s => s.Amount >= 0 && names.Any(n => string.Equals(n, s.Code, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public static List<(GroundShopItem Item, int Qty)> PickRequestedShop(Ground ground, IReadOnlyList<GroundShopQtyUpsert>? requested)
    {
        var wanted = (requested ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Name) && s.Qty > 0)
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.Key, Qty: GroundShopKinds.ClampQty(g.Sum(x => x.Qty))))
            .Where(x => x.Qty > 0)
            .Take(16)
            .ToList();
        if (wanted.Count == 0)
            return [];

        var result = new List<(GroundShopItem Item, int Qty)>();
        foreach (var (name, qty) in wanted)
        {
            var item = (ground.ShopItems ?? [])
                .FirstOrDefault(s => s.Price >= 0 && string.Equals(name, s.Code, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
                result.Add((item, qty));
        }
        return result;
    }

    public static string EncodeCursor(DateTime createdAt, Guid id)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{createdAt.Ticks:D20}|{id:N}"));

    public static bool TryDecodeCursor(string? cursor, out DateTime createdAt, out Guid id)
    {
        createdAt = default;
        id = default;
        if (string.IsNullOrWhiteSpace(cursor))
            return false;
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = text.Split('|');
            if (parts.Length != 2)
                return false;
            if (!long.TryParse(parts[0], out var ticks) || !Guid.TryParseExact(parts[1], "N", out id))
                return false;
            createdAt = new DateTime(ticks, DateTimeKind.Unspecified);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public const string SlotConflictTitle = "Time not available";

    public static string SlotConflictMessage(DateTime? start = null, DateTime? end = null)
    {
        if (start is DateTime s && end is DateTime e)
            return $"This time overlaps another booking ({s:dd MMM yyyy HH:mm} – {e:HH:mm}). Please choose a different date or time.";
        return "This time overlaps another booking on this ground. Please choose a different date or time.";
    }

    public static Task<GroundBooking?> FindSlotClashAsync(
        GroundsDbContext db,
        Guid groundId,
        DateTime start,
        DateTime end,
        Guid exceptId,
        CancellationToken ct)
        => db.Bookings.AsNoTracking()
            .Where(x =>
                x.GroundId == groundId &&
                x.Id != exceptId &&
                !x.IsDeleted &&
                (x.Status == GroundBookingStatus.Pending
                 || x.Status == GroundBookingStatus.Confirmed
                 || x.Status == GroundBookingStatus.Completed) &&
                x.StartAt < end &&
                x.EndAt > start)
            .OrderBy(x => x.StartAt)
            .FirstOrDefaultAsync(ct);
}
