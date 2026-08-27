namespace JPL.Grounds.Api.Models;

public static class GroundBookingKinds
{
    public const int MaxPerGround = 16;

    public static int ClampNightHour(int hour)
        => Math.Clamp(hour <= 0 ? 18 : hour, 12, 23);

    public static int ClampNightEndHour(int hour)
        => Math.Clamp(hour, 0, 23);

    public static bool IsWeekend(DateTime start)
        => start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public static bool IsNight(DateTime start, int nightStartsHour, int nightEndsHour = 0)
    {
        var hour = start.Hour;
        var from = ClampNightHour(nightStartsHour);
        var to = ClampNightEndHour(nightEndsHour);
        if (from == to)
            return hour >= from;
        if (from < to)
            return hour >= from && hour <= to;
        return hour >= from || hour <= to;
    }

    public static string BandTitle(DateTime start, int nightStartsHour, int nightEndsHour = 0)
    {
        var night = IsNight(start, nightStartsHour, nightEndsHour);
        if (IsWeekend(start))
            return night ? "Weekend night" : "Weekend / day";
        return night ? "Weekday night" : "Weekday / day";
    }

    public static decimal RateFor(
        decimal price, decimal nightPrice, decimal weekendPrice, decimal weekendNightPrice,
        DateTime start, int nightStartsHour, int nightEndsHour = 0)
    {
        var day = Math.Max(0, price);
        var nightRate = nightPrice > 0 ? nightPrice : day;
        var weekendRate = weekendPrice > 0 ? weekendPrice : day;
        decimal weekendNight;
        if (weekendNightPrice > 0)
            weekendNight = weekendNightPrice;
        else if (weekendPrice > 0 && nightPrice > 0)
            weekendNight = weekendPrice + (nightPrice - day);
        else if (weekendPrice > 0)
            weekendNight = weekendRate;
        else
            weekendNight = nightRate;

        var weekend = IsWeekend(start);
        var night = IsNight(start, nightStartsHour, nightEndsHour);
        if (weekend && night)
            return Math.Max(0, weekendNight);
        if (night)
            return Math.Max(0, nightRate);
        if (weekend)
            return Math.Max(0, weekendRate);
        return day;
    }

    public static decimal AmountAcross(
        decimal price, decimal nightPrice, decimal weekendPrice, decimal weekendNightPrice,
        DateTime start, DateTime end, int nightStartsHour, int nightEndsHour = 0)
    {
        decimal total = 0;
        foreach (var slice in EnumerateSlices(price, nightPrice, weekendPrice, weekendNightPrice, start, end, nightStartsHour, nightEndsHour))
            total += slice.Rate * slice.Hours;
        return Math.Round(total, 2);
    }

    public static string DescribeBands(
        decimal price, decimal nightPrice, decimal weekendPrice, decimal weekendNightPrice,
        DateTime start, DateTime end, int nightStartsHour, int nightEndsHour = 0)
    {
        var slices = EnumerateSlices(price, nightPrice, weekendPrice, weekendNightPrice, start, end, nightStartsHour, nightEndsHour);
        if (slices.Count == 0)
            return BandTitle(start, nightStartsHour, nightEndsHour);
        if (slices.Count == 1)
        {
            var only = slices[0];
            return only.Rate > 0 ? $"{only.Band} · Rs {only.Rate:0}" : only.Band;
        }

        return string.Join(" + ", slices.Select(s =>
        {
            var hrs = s.Hours == decimal.Truncate(s.Hours) ? s.Hours.ToString("0") : s.Hours.ToString("0.#");
            return s.Rate > 0
                ? $"{hrs} hr {s.Band} (Rs {s.Rate:0}/hr)"
                : $"{hrs} hr {s.Band}";
        }));
    }

    private static List<(string Band, decimal Hours, decimal Rate)> EnumerateSlices(
        decimal price, decimal nightPrice, decimal weekendPrice, decimal weekendNightPrice,
        DateTime start, DateTime end, int nightStartsHour, int nightEndsHour)
    {
        var slices = new List<(string Band, decimal Hours, decimal Rate)>();
        if (end <= start)
            return slices;

        var cursor = start;
        while (cursor < end)
        {
            var hourStart = new DateTime(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 0, 0, cursor.Kind);
            var next = hourStart.AddHours(1);
            if (next <= cursor)
                next = cursor.AddMinutes(1);
            if (next > end)
                next = end;
            var hours = Math.Round((decimal)(next - cursor).TotalHours, 4);
            if (hours <= 0)
                break;
            var rate = RateFor(price, nightPrice, weekendPrice, weekendNightPrice, cursor, nightStartsHour, nightEndsHour);
            var band = BandTitle(cursor, nightStartsHour, nightEndsHour);
            if (slices.Count > 0 && slices[^1].Band == band && slices[^1].Rate == rate)
                slices[^1] = (band, slices[^1].Hours + hours, rate);
            else
                slices.Add((band, hours, rate));
            cursor = next;
        }

        return slices;
    }

    public static decimal RateFor(GroundBookingOffer offer, DateTime start, int nightStartsHour, int nightEndsHour = 0)
        => RateFor(offer.Price, offer.NightPrice, offer.WeekendPrice, offer.WeekendNightPrice, start, nightStartsHour, nightEndsHour);

    public static IEnumerable<decimal> BandPrices(GroundBookingOffer offer)
    {
        if (offer.Price > 0)
            yield return offer.Price;
        if (offer.NightPrice > 0)
            yield return offer.NightPrice;
        if (offer.WeekendPrice > 0)
            yield return offer.WeekendPrice;
        if (offer.WeekendNightPrice > 0)
            yield return offer.WeekendNightPrice;
    }

    public static decimal FromPrice(IEnumerable<GroundBookingOffer> offers)
    {
        var priced = offers.Where(o => o.IsEnabled).SelectMany(BandPrices).ToList();
        return priced.Count == 0 ? 0 : priced.Min();
    }

    public static (DateTime End, decimal Amount, decimal Hours) Quote(
        GroundBookingOffer offer, DateTime start, decimal requestedHours, int nightStartsHour = 18, int nightEndsHour = 6)
    {
        var rate = RateFor(offer, start, nightStartsHour, nightEndsHour);
        var perHour = offer.DurationHours <= 1;
        decimal hours;
        decimal amount;
        if (perHour)
        {
            hours = requestedHours <= 0 ? 1 : Math.Clamp(Math.Round(requestedHours, 2), 1, 12);
            var end = start.AddHours((double)hours);
            amount = AmountAcross(
                offer.Price, offer.NightPrice, offer.WeekendPrice, offer.WeekendNightPrice,
                start, end, nightStartsHour, nightEndsHour);
            return (end, amount, hours);
        }
        else
        {
            hours = offer.DurationHours > 0 ? offer.DurationHours : 1;
            amount = rate;
        }

        return (start.AddHours((double)hours), amount, hours);
    }

    public static string TitleOf(string? kind, IEnumerable<GroundBookingOffer>? offers = null)
    {
        var fromDb = offers?.FirstOrDefault(o =>
            string.Equals(o.Kind, kind, StringComparison.OrdinalIgnoreCase))?.Title;
        if (!string.IsNullOrWhiteSpace(fromDb))
            return fromDb.Trim();
        return LegacyTitle(kind);
    }

    public static string LegacyTitle(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return "Booking";
        return kind.Trim() switch
        {
            "Hourly" => "Hourly",
            "Match" => "Match",
            "HalfDay" => "Half day",
            "FullDay" => "Full day",
            "Night" => "Night / floodlights",
            "Nets" => "Practice nets",
            "Tournament" => "Tournament day",
            _ => kind.Trim()
        };
    }

    public static string NewKind(string title, HashSet<string> used)
    {
        var slug = new string((title ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Take(20)
            .ToArray());
        if (slug.Length < 2)
            slug = "type";
        string kind;
        do
        {
            kind = $"{slug}-{Guid.NewGuid().ToString("N")[..8]}";
        } while (!used.Add(kind));
        return kind.Length > 64 ? kind[..64] : kind;
    }

    public static int SuggestedStartHour(bool pricesPerHour, decimal durationHours)
        => pricesPerHour || durationHours <= 1 ? 9 : 16;
}
