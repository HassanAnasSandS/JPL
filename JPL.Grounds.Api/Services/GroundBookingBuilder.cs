using JPL.Grounds.Api.Contracts;
using JPL.Grounds.Api.Data;
using JPL.Grounds.Api.Models;

namespace JPL.Grounds.Api.Services;

internal readonly record struct BookingBuildError(int StatusCode, string Message);

internal static class GroundBookingBuilder
{
    public static async Task<(GroundBooking? Booking, BookingBuildError? Error)> TryBuildAsync(
        GroundsDbContext db,
        Ground ground,
        BookingCreateRequest request,
        Guid customerUserId,
        string customerName,
        string customerPhone,
        string status,
        CancellationToken ct)
    {
        var kind = request.BookingKind?.Trim() ?? string.Empty;
        GroundBookingOffer? offer;
        if (kind.Length == 0)
        {
            offer = ground.Offers
                .Where(o => o.IsEnabled && o.Price > 0)
                .OrderBy(o => o.SortOrder)
                .FirstOrDefault();
        }
        else
        {
            offer = ground.Offers.FirstOrDefault(o =>
                o.IsEnabled && string.Equals(o.Kind, kind, StringComparison.OrdinalIgnoreCase));
        }

        if (offer is null)
            return Fail(400, "This ground does not offer that booking type. Pick another type.");

        var start = request.StartAt;
        decimal requestedHours = request.Hours;
        if (requestedHours <= 0 && request.EndAt > request.StartAt)
            requestedHours = (decimal)(request.EndAt - request.StartAt).TotalHours;

        DateTime end;
        decimal amount;
        decimal hours;
        try
        {
            (end, amount, hours) = GroundBookingKinds.Quote(
                offer, start, requestedHours, ground.NightStartsHour, ground.NightEndsHour);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(400, ex.Message);
        }

        if (end <= start)
            return Fail(400, "End time must be after start time.");
        if (start < DateTime.Now.AddMinutes(-2))
            return Fail(400, "Choose a future date and time.");

        var clash = await GroundsMap.FindSlotClashAsync(db, ground.Id, start, end, Guid.Empty, ct)
            .ConfigureAwait(false);
        if (clash is not null)
            return Fail(409, GroundsMap.SlotConflictMessage(clash.StartAt, clash.EndAt));

        var extras = GroundsMap.PickRequested(ground, request.Services);
        amount += extras.Sum(s => s.Amount);
        var shop = GroundsMap.PickRequestedShop(ground, request.Shop);
        amount += shop.Sum(s => s.Item.Price * s.Qty);

        var booking = new GroundBooking
        {
            GroundId = ground.Id,
            CustomerUserId = customerUserId,
            CustomerName = Trim(customerName, 120) ?? string.Empty,
            CustomerPhone = customerPhone,
            StartAt = start,
            EndAt = end,
            BookingKind = offer.Kind,
            BookedHours = hours,
            Status = status,
            BookingAmount = amount,
            Notes = Trim(request.Notes, 1000)
        };
        foreach (var extra in extras)
        {
            booking.Services.Add(new GroundBookingService
            {
                BookingId = booking.Id,
                Code = extra.Code,
                Amount = extra.Amount
            });
        }
        foreach (var (item, qty) in shop)
        {
            booking.ShopItems.Add(new GroundBookingShopItem
            {
                BookingId = booking.Id,
                Code = item.Code,
                Price = item.Price,
                Kind = GroundShopKinds.Normalize(item.Kind),
                Qty = qty
            });
        }

        return (booking, null);
    }

    private static (GroundBooking?, BookingBuildError) Fail(int status, string message)
        => (null, new BookingBuildError(status, message));

    private static string? Trim(string? value, int max)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t))
            return null;
        return t.Length <= max ? t : t[..max];
    }
}
