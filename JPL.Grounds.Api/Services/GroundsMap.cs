using Microsoft.EntityFrameworkCore;
using JPL.Grounds.Api.Contracts;
using JPL.Grounds.Api.Data;
using JPL.Grounds.Api.Models;

namespace JPL.Grounds.Api.Services;

internal static class GroundsMap
{
    public const int MaxLinksPerGround = 8;

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

    public static GroundCardDto ToCard(Ground g, HttpRequest request, bool canDelete = false)
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
            canDelete);

    public static GroundDetailDto ToDetail(Ground g, HttpRequest request)
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
            GroundBookingKinds.ClampNightEndHour(g.NightEndsHour));

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
            ownerPhone);

    public static IReadOnlyList<GroundLinkDto> ToLinks(Ground g)
        => (g.Links ?? Enumerable.Empty<GroundLink>())
            .OrderBy(x => x.SortOrder)
            .Select(x => new GroundLinkDto(
                string.IsNullOrWhiteSpace(x.Title) ? string.Empty : x.Title.Trim(),
                x.Url))
            .ToList();

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
