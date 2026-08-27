using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JPL.Services;

public sealed class BookingConflictException : Exception
{
    public BookingConflictException(string message) : base(message) { }
}

public sealed record GroundCardDto(
    Guid Id,
    string Name,
    string ProvinceName,
    string CityName,
    string? AreaName,
    decimal HourlyRate,
    string? CoverImageUrl,
    IReadOnlyList<string>? Amenities,
    string? ContactPhone,
    string? WhatsAppNumber,
    string? PostalCode = null,
    string? GoogleMapsUrl = null,
    string? CountryName = null,
    IReadOnlyList<GroundBookingOfferDto>? Offers = null,
    IReadOnlyList<GroundServiceDto>? Services = null,
    bool CanDelete = false)
{
    public string LocationLine
    {
        get
        {
            var bits = new[] { PostalCode, AreaName, CityName, ProvinceName, CountryName }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            return string.Join(" · ", bits);
        }
    }

    public string RateLabel => GroundBookingOfferDto.Summarize(Offers, HourlyRate);

    public string? ChatPhone => WhatsAppNumber ?? ContactPhone;

    public bool HasWhatsApp => PakistanWhatsApp.TryNormalize(ChatPhone, out _);
}

public sealed record GroundImageDto(Guid Id, string FileName, string Url, bool IsCover, int SortOrder);

public sealed record GroundLinkDto(string Title, string Url)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Url : Title.Trim();
}

public sealed record GroundServiceDto(string Name, decimal Amount)
{
    public string Display => Amount > 0 ? $"{Name} · Rs {Amount:0}" : $"{Name} · Free";
    public string AmountLabel => Amount > 0 ? $"Rs {Amount:0}" : "Free";

    public static string Summarize(IReadOnlyList<GroundServiceDto>? services)
    {
        if (services is not { Count: > 0 })
            return string.Empty;
        return string.Join(" · ", services.Select(s => s.Display));
    }
}

public sealed record CatalogItemDto(Guid Id, string Name, int SortOrder);

public sealed record GroundBookingOfferDto(
    string Kind,
    string Title,
    string Hint,
    decimal Price,
    decimal DurationHours,
    bool IsEnabled,
    bool PricesPerHour,
    int SuggestedStartHour,
    decimal NightPrice = 0,
    decimal WeekendPrice = 0,
    decimal WeekendNightPrice = 0)
{
    public bool IsHourly => PricesPerHour || DurationHours <= 1;

    public IReadOnlyList<decimal> PricedBands
    {
        get
        {
            var list = new List<decimal>();
            if (Price > 0)
                list.Add(Price);
            if (NightPrice > 0)
                list.Add(NightPrice);
            if (WeekendPrice > 0)
                list.Add(WeekendPrice);
            if (WeekendNightPrice > 0)
                list.Add(WeekendNightPrice);
            return list;
        }
    }

    public string PriceLabel
    {
        get
        {
            var bands = PricedBands;
            if (bands.Count == 0)
                return "Rate on confirm";
            var min = bands.Min();
            var max = bands.Max();
            var unit = IsHourly ? " / hr" : $" · {DurationHours:0} hr";
            return min == max ? $"Rs {min:0}{unit}" : $"Rs {min:0}–{max:0}{unit}";
        }
    }

    public string BandsLine
    {
        get
        {
            var bits = new List<string>();
            if (Price > 0)
                bits.Add($"Weekday Rs {Price:0}");
            if (NightPrice > 0)
                bits.Add($"Night Rs {NightPrice:0}");
            if (WeekendPrice > 0)
                bits.Add($"Weekend Rs {WeekendPrice:0}");
            if (WeekendNightPrice > 0)
                bits.Add($"Weekend night Rs {WeekendNightPrice:0}");
            return string.Join(" · ", bits);
        }
    }

    public string Display => $"{Title} — {PriceLabel}";

    public decimal RateFor(DateTime start, int nightStartsHour, int nightEndsHour = 0)
        => GroundBookingKinds.RateFor(Price, NightPrice, WeekendPrice, WeekendNightPrice, start, nightStartsHour, nightEndsHour);

    public static string Summarize(IReadOnlyList<GroundBookingOfferDto>? offers, decimal hourlyRate)
    {
        var offered = offers?.Where(o => o.IsEnabled).ToList() ?? [];
        if (offered.Count == 0)
            return hourlyRate <= 0 ? "Rate on request" : $"From Rs {hourlyRate:0}";
        var priced = offered.SelectMany(o => o.PricedBands).ToList();
        var names = string.Join(", ", offered.Select(o => o.Title));
        if (priced.Count == 0)
            return $"{names} · rate on confirm";
        var min = priced.Min();
        var max = priced.Max();
        return min == max
            ? $"From Rs {min:0} · {names}"
            : $"From Rs {min:0}–{max:0} · {names}";
    }
}

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
    IReadOnlyList<string>? Amenities,
    IReadOnlyList<GroundServiceDto>? Services,
    IReadOnlyList<GroundImageDto>? Images,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? PostalCode = null,
    string? GoogleMapsUrl = null,
    double? Latitude = null,
    double? Longitude = null,
    string? CountryName = null,
    IReadOnlyList<GroundBookingOfferDto>? Offers = null,
    int NightStartsHour = 18,
    IReadOnlyList<GroundLinkDto>? Links = null,
    int NightEndsHour = 6);

public sealed record MapsLocationDto(
    string GoogleMapsUrl,
    string PostalCode,
    double Latitude,
    double Longitude,
    string CountryName,
    string ProvinceName,
    string CityName,
    string? AreaName,
    string? FormattedAddress)
{
    public string LocationLine
    {
        get
        {
            var bits = new[] { PostalCode, AreaName, CityName, ProvinceName, CountryName }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            return string.Join(" · ", bits);
        }
    }
}

public sealed record GroundSearchPageDto(
    IReadOnlyList<GroundCardDto> Items,
    string? NextCursor,
    bool HasMore);

public sealed record GroundLocationOptionsDto(
    IReadOnlyList<string> Countries,
    IReadOnlyList<string> States,
    IReadOnlyList<string> Cities,
    IReadOnlyList<string> Areas);

public sealed record GroundBookingDto(
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
    decimal Advance,
    decimal Balance,
    string? Notes,
    string? PaymentNote,
    DateTime CreatedAtUtc,
    string? BookingKind = null,
    string? BookingTitle = null,
    decimal BookedHours = 0,
    IReadOnlyList<GroundServiceDto>? Services = null,
    string? OwnerWhatsAppNumber = null,
    string? OwnerContactPhone = null)
{
    public string? OwnerChatPhone => OwnerWhatsAppNumber ?? OwnerContactPhone;

    public bool HasOwnerWhatsApp => PakistanWhatsApp.TryNormalize(OwnerChatPhone, out _);

    public bool HasCustomerWhatsApp => PakistanWhatsApp.TryNormalize(CustomerPhone, out _);

    public string WhenLabel
    {
        get
        {
            var kind = string.IsNullOrWhiteSpace(BookingTitle)
                ? GroundBookingKinds.TitleOf(BookingKind)
                : BookingTitle;
            return $"{kind} · {StartAt:dd MMM yyyy HH:mm} – {EndAt:HH:mm}";
        }
    }
    public string MoneyLabel => $"Amount {BookingAmount:0} · Adv {Advance:0} · Bal {Balance:0}";
    public string ServicesLine => GroundServiceDto.Summarize(Services);
}

/// <summary>
/// JPL.Grounds.Api only (jplground.shoptick.shop → :5026).
/// Grounds, images, marketplace bookings, GroundOwner account create/update.
/// Never login, never Admin/User accounts, never touch Venue/VenueBooking.
/// Auth: same Bearer token issued by JPL.Api.
/// </summary>
public interface IGroundsApiClient
{
    Task<MapsLocationDto> ResolveMapsAsync(string googleMapsUrl, CancellationToken ct = default);
    Task<GroundSearchPageDto> SearchAsync(
        string? postalCode, string? q,
        string? country, string? state, string? city, string? area,
        decimal? minRate, decimal? maxRate, string? cursor, int take = 20,
        CancellationToken ct = default);
    Task<GroundLocationOptionsDto> GetLocationOptionsAsync(
        string? country, string? state, string? city, CancellationToken ct = default);
    Task<GroundSearchPageDto> GetOwnerGroundsAsync(string? cursor, int take = 20, CancellationToken ct = default);
    Task<GroundDetailDto?> GetGroundAsync(Guid id, CancellationToken ct = default);
    Task<GroundDetailDto> CreateGroundAsync(object body, CancellationToken ct = default);
    Task<GroundDetailDto> UpdateGroundAsync(Guid id, object body, CancellationToken ct = default);
    Task DeleteGroundAsync(Guid id, CancellationToken ct = default);
    Task<GroundImageDto> UploadGroundImageAsync(Guid groundId, Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task DeleteGroundImageAsync(Guid groundId, Guid imageId, CancellationToken ct = default);
    Task<GroundBookingDto> RequestBookingAsync(Guid groundId, object body, CancellationToken ct = default);
    Task<IReadOnlyList<GroundBookingDto>> GetMyBookingsAsync(string? cursor, int take = 20, CancellationToken ct = default);
    Task<IReadOnlyList<GroundBookingDto>> GetOwnerBookingsAsync(Guid? groundId, string? cursor, int take = 20, CancellationToken ct = default);
    Task<GroundBookingDto?> GetBookingAsync(Guid id, CancellationToken ct = default);
    Task<GroundBookingDto> UpdateBookingAsync(Guid id, object body, CancellationToken ct = default);
    Task<ApiUserDto> CreateGroundOwnerAsync(object body, CancellationToken ct = default);
    Task<ApiUserDto> UpdateGroundOwnerAsync(Guid id, object body, CancellationToken ct = default);
    Task DeleteGroundOwnerAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CatalogItemDto>> GetAmenityCatalogAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CatalogItemDto>> GetServiceCatalogAsync(CancellationToken ct = default);
    Task<CatalogItemDto> CreateAmenityCatalogAsync(string name, CancellationToken ct = default);
    Task<CatalogItemDto> UpdateAmenityCatalogAsync(Guid id, string name, CancellationToken ct = default);
    Task DeleteAmenityCatalogAsync(Guid id, CancellationToken ct = default);
    Task<CatalogItemDto> CreateServiceCatalogAsync(string name, CancellationToken ct = default);
    Task<CatalogItemDto> UpdateServiceCatalogAsync(Guid id, string name, CancellationToken ct = default);
    Task DeleteServiceCatalogAsync(Guid id, CancellationToken ct = default);
}

public sealed class GroundsApiClient : IGroundsApiClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _http;

    public GroundsApiClient(IHttpClientFactory http) => _http = http;

    private HttpClient Client => _http.CreateClient(GroundsApiOptions.HttpClientName);

    public Task<MapsLocationDto> ResolveMapsAsync(string googleMapsUrl, CancellationToken ct = default)
        => PostAsync<MapsLocationDto>("api/grounds/resolve-maps", new { googleMapsUrl }, ct);

    public async Task<GroundSearchPageDto> SearchAsync(
        string? postalCode, string? q,
        string? country, string? state, string? city, string? area,
        decimal? minRate, decimal? maxRate, string? cursor, int take = 20,
        CancellationToken ct = default)
    {
        var qs = Query(
            ("postalCode", postalCode),
            ("q", q),
            ("country", country),
            ("state", state),
            ("city", city),
            ("area", area),
            ("minRate", minRate?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("maxRate", maxRate?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("cursor", cursor),
            ("take", take.ToString()));
        return await GetAsync<GroundSearchPageDto>($"api/grounds/search{qs}", ct).ConfigureAwait(false)
               ?? new GroundSearchPageDto([], null, false);
    }

    public async Task<GroundLocationOptionsDto> GetLocationOptionsAsync(
        string? country, string? state, string? city, CancellationToken ct = default)
    {
        var qs = Query(
            ("country", country),
            ("state", state),
            ("city", city));
        return await GetAsync<GroundLocationOptionsDto>($"api/grounds/location-options{qs}", ct).ConfigureAwait(false)
               ?? new GroundLocationOptionsDto([], [], [], []);
    }

    public async Task<GroundSearchPageDto> GetOwnerGroundsAsync(string? cursor, int take = 20, CancellationToken ct = default)
    {
        var qs = Query(("cursor", cursor), ("take", take.ToString()));
        return await GetAsync<GroundSearchPageDto>($"api/owner/grounds{qs}", ct).ConfigureAwait(false)
               ?? new GroundSearchPageDto([], null, false);
    }

    public Task<GroundDetailDto?> GetGroundAsync(Guid id, CancellationToken ct = default)
        => GetAsync<GroundDetailDto>($"api/grounds/{id:D}", ct);

    public Task<GroundDetailDto> CreateGroundAsync(object body, CancellationToken ct = default)
        => PostAsync<GroundDetailDto>("api/owner/grounds", body, ct);

    public Task<GroundDetailDto> UpdateGroundAsync(Guid id, object body, CancellationToken ct = default)
        => PutAsync<GroundDetailDto>($"api/owner/grounds/{id:D}", body, ct);

    public async Task DeleteGroundAsync(Guid id, CancellationToken ct = default)
    {
        using var res = await Client.DeleteAsync($"api/owner/grounds/{id:D}", ct).ConfigureAwait(false);
        await EnsureSuccess(res).ConfigureAwait(false);
    }

    public async Task<GroundImageDto> UploadGroundImageAsync(
        Guid groundId, Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var part = new StreamContent(content);
        part.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(part, "file", fileName);
        using var res = await Client.PostAsync($"api/owner/grounds/{groundId:D}/images", form, ct).ConfigureAwait(false);
        await EnsureSuccess(res).ConfigureAwait(false);
        var dto = await res.Content.ReadFromJsonAsync<GroundImageDto>(Json, ct).ConfigureAwait(false);
        return dto ?? throw new InvalidOperationException("Upload failed.");
    }

    public async Task DeleteGroundImageAsync(Guid groundId, Guid imageId, CancellationToken ct = default)
    {
        using var res = await Client.DeleteAsync($"api/owner/grounds/{groundId:D}/images/{imageId:D}", ct)
            .ConfigureAwait(false);
        await EnsureSuccess(res).ConfigureAwait(false);
    }

    public Task<GroundBookingDto> RequestBookingAsync(Guid groundId, object body, CancellationToken ct = default)
        => PostAsync<GroundBookingDto>($"api/grounds/{groundId:D}/bookings", body, ct);

    public Task<IReadOnlyList<GroundBookingDto>> GetMyBookingsAsync(string? cursor, int take = 20, CancellationToken ct = default)
    {
        var qs = Query(("cursor", cursor), ("take", take.ToString()));
        return GetListAsync<GroundBookingDto>($"api/bookings/mine{qs}", ct);
    }

    public Task<IReadOnlyList<GroundBookingDto>> GetOwnerBookingsAsync(
        Guid? groundId, string? cursor, int take = 20, CancellationToken ct = default)
    {
        var qs = Query(
            ("groundId", groundId?.ToString("D")),
            ("cursor", cursor),
            ("take", take.ToString()));
        return GetListAsync<GroundBookingDto>($"api/bookings/owner{qs}", ct);
    }

    public Task<GroundBookingDto?> GetBookingAsync(Guid id, CancellationToken ct = default)
        => GetAsync<GroundBookingDto>($"api/bookings/{id:D}", ct);

    public Task<GroundBookingDto> UpdateBookingAsync(Guid id, object body, CancellationToken ct = default)
        => PutAsync<GroundBookingDto>($"api/bookings/{id:D}", body, ct);

    public Task<ApiUserDto> CreateGroundOwnerAsync(object body, CancellationToken ct = default)
        => PostAsync<ApiUserDto>("api/platform/ground-owners", body, ct);

    public Task<ApiUserDto> UpdateGroundOwnerAsync(Guid id, object body, CancellationToken ct = default)
        => PutAsync<ApiUserDto>($"api/platform/ground-owners/{id:D}", body, ct);

    public async Task DeleteGroundOwnerAsync(Guid id, CancellationToken ct = default)
    {
        using var res = await Client.DeleteAsync($"api/platform/ground-owners/{id:D}", ct).ConfigureAwait(false);
        await EnsureSuccess(res).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<CatalogItemDto>> GetAmenityCatalogAsync(CancellationToken ct = default)
        => GetListAsync<CatalogItemDto>("api/catalog/amenities", ct);

    public Task<IReadOnlyList<CatalogItemDto>> GetServiceCatalogAsync(CancellationToken ct = default)
        => GetListAsync<CatalogItemDto>("api/catalog/services", ct);

    public Task<CatalogItemDto> CreateAmenityCatalogAsync(string name, CancellationToken ct = default)
        => PostAsync<CatalogItemDto>("api/platform/amenities", new { name }, ct);

    public Task<CatalogItemDto> UpdateAmenityCatalogAsync(Guid id, string name, CancellationToken ct = default)
        => PutAsync<CatalogItemDto>($"api/platform/amenities/{id:D}", new { name }, ct);

    public Task DeleteAmenityCatalogAsync(Guid id, CancellationToken ct = default)
        => DeleteAsync($"api/platform/amenities/{id:D}", ct);

    public Task<CatalogItemDto> CreateServiceCatalogAsync(string name, CancellationToken ct = default)
        => PostAsync<CatalogItemDto>("api/platform/services", new { name }, ct);

    public Task<CatalogItemDto> UpdateServiceCatalogAsync(Guid id, string name, CancellationToken ct = default)
        => PutAsync<CatalogItemDto>($"api/platform/services/{id:D}", new { name }, ct);

    public Task DeleteServiceCatalogAsync(Guid id, CancellationToken ct = default)
        => DeleteAsync($"api/platform/services/{id:D}", ct);

    private async Task DeleteAsync(string url, CancellationToken ct)
    {
        using var res = await Client.DeleteAsync(url, ct).ConfigureAwait(false);
        await EnsureSuccess(res).ConfigureAwait(false);
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct = default) where T : class
    {
        using var res = await Client.GetAsync(url, ct).ConfigureAwait(false);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccess(res).ConfigureAwait(false);
        return await res.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string url, CancellationToken ct)
    {
        var list = await GetAsync<List<T>>(url, ct).ConfigureAwait(false);
        return list ?? [];
    }

    private async Task<T> PostAsync<T>(string url, object body, CancellationToken ct)
    {
        using var res = await Client.PostAsJsonAsync(url, body, Json, ct).ConfigureAwait(false);
        await EnsureSuccess(res).ConfigureAwait(false);
        var dto = await res.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
        return dto ?? throw new InvalidOperationException("Empty response.");
    }

    private async Task<T> PutAsync<T>(string url, object body, CancellationToken ct)
    {
        using var res = await Client.PutAsJsonAsync(url, body, Json, ct).ConfigureAwait(false);
        await EnsureSuccess(res).ConfigureAwait(false);
        var dto = await res.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
        return dto ?? throw new InvalidOperationException("Empty response.");
    }

    private static string Query(params (string Key, string? Value)[] pairs)
    {
        var bits = pairs
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}")
            .ToList();
        return bits.Count == 0 ? string.Empty : "?" + string.Join("&", bits);
    }

    private static async Task EnsureSuccess(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode)
            return;
        var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        var message = CleanApiError(text, res.ReasonPhrase);
        if ((int)res.StatusCode == 409)
            throw new BookingConflictException(message);
        throw new InvalidOperationException(message);
    }

    private static string CleanApiError(string? text, string? reasonPhrase)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.IsNullOrWhiteSpace(reasonPhrase) ? "Request failed." : reasonPhrase!;
        text = text.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(text) ?? text.Trim('"');
            }
            catch
            {
                return text.Trim('"');
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                    return msg.GetString() ?? text;
                if (doc.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                    return title.GetString() ?? text;
            }
        }
        catch
        {
            // body is not JSON
        }

        return text;
    }
}
