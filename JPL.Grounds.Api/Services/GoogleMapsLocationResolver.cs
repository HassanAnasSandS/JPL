using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.OpenLocationCode;
using JPL.Grounds.Api.Contracts;

namespace JPL.Grounds.Api.Services;

public sealed class GoogleMapsLocationResolver
{
    public const string HttpClientName = "maps";

    private static readonly Regex AtCoords = new(
        @"@(-?\d{1,2}\.\d+),(-?\d{1,3}\.\d+)",
        RegexOptions.Compiled);

    private static readonly Regex BangCoords = new(
        @"!3d(-?\d{1,2}\.\d+)!4d(-?\d{1,3}\.\d+)",
        RegexOptions.Compiled);

    private static readonly Regex QueryCoords = new(
        @"[?&#](?:q|query|ll|center|destination)=(-?\d{1,2}\.\d{2,})[,+](-?\d{1,3}\.\d{2,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HtmlPostal = new(
        @"""postal_code""\s*:\s*""(\d{5})""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AddressPostal = new(
        @"(?:^|[,\s])(\d{5})(?:\s*,\s*Pakistan\b|\s+Pakistan\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MapsSearchPath = new(
        @"/maps/(?:place|search)/([^/@?]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// WhatsApp / maps.app.goo.gl expands to /maps?q=Place+Name&amp;ftid=...
    /// The name itself can contain '&amp;' (Cues &amp; Savours), so stop only at known Google params.
    /// </summary>
    private static readonly Regex MapsQValue = new(
        @"[?&]q=(.+?)(?:&(?:ftid|entry|shh|lucs|g_ep|skid|g_st|cid|hl|authuser|api|pb)=|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex PlusCodeRx = new(
        @"\b([2-9CFGHJMPQRVWX]{4,8})(?:\+|\s)([2-9CFGHJMPQRVWX]{2,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TrailingPostal = new(
        @"(?:^|[,\s])(\d{5})(?:\s*$|\s*,|\s+Pakistan\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DataCoords = new(
        @"3d(-?\d{1,2}\.\d+)!4d(-?\d{1,3}\.\d+)",
        RegexOptions.Compiled);

    private static readonly Regex EmbeddedMapsUrl = new(
        @"https://(?:www\.)?google\.[^/""'\s<>]+/maps/(?:place|search)/[^""'\s<>]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SectorOrBlock = new(
        @"^(?:sector|block|plot)\b|\s+(?:sector|block)\s+[\w-]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleMapsLocationResolver> _log;

    public GoogleMapsLocationResolver(
        IHttpClientFactory http,
        IConfiguration config,
        ILogger<GoogleMapsLocationResolver> log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

    public async Task<MapsLocationDto> ResolveAsync(string? mapsUrl, CancellationToken ct)
    {
        var url = mapsUrl?.Trim() ?? string.Empty;
        if (url.Length == 0)
            throw new InvalidOperationException("Google Maps location link is required.");
        if (url.Length > 500)
            url = url[..500];
        if (!IsGoogleMapsUrl(url))
            throw new InvalidOperationException(
                "Paste a Google Maps link (Share → Copy link). Short links like maps.app.goo.gl are fine.");

        var expanded = await ExpandAsync(url, ct).ConfigureAwait(false);
        var mapsUri = PreferMapsUri(expanded.Uri, expanded.Body);
        double lat;
        double lng;
        if (!TryReadCoordinates(mapsUri, expanded.Body, out lat, out lng))
        {
            var geo = await TryGeocodePlaceAsync(mapsUri, expanded.Body, ct).ConfigureAwait(false);
            if (geo is null)
            {
                throw new InvalidOperationException(
                    "Could not read this Maps link. Open Google Maps, share the place or drop a pin, then Share → Copy link.");
            }

            lat = geo.Value.Lat;
            lng = geo.Value.Lng;
        }

        if (!IsInPakistan(lat, lng))
            throw new InvalidOperationException("This pin is outside Pakistan. Drop the pin on the ground in Pakistan.");

        var pin = await ReverseGeocodeAsync(lat, lng, expanded.Body, ct).ConfigureAwait(false);
        var postal = NormalizePostal(pin.PostalCode)
                     ?? ExtractPostalFromHtml(expanded.Body)
                     ?? ExtractPostalFromText(mapsUri)
                     ?? ExtractPostalFromText(expanded.Body);
        if (postal is null)
            throw new InvalidOperationException(
                "Could not read a postal code from this Maps pin. Drop the pin on the ground entrance or building, then copy the link again.");

        var country = First(pin.Country, "Pakistan");
        var state = pin.State;
        var city = pin.City;
        var area = First(
            pin.Area,
            ExtractAreaFromMaps(expanded.Uri, expanded.Body, city));
        if (string.Equals(area, city, StringComparison.OrdinalIgnoreCase))
            area = ExtractAreaFromMaps(expanded.Uri, expanded.Body, city);
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(city))
            throw new InvalidOperationException(
                "Could not read country/state/city from this Maps pin. Drop the pin on the ground, then copy the link again.");

        return new MapsLocationDto(
            url,
            postal,
            lat,
            lng,
            country,
            state,
            city,
            area,
            NullIfEmpty(pin.Formatted));
    }

    public static bool IsGoogleMapsUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme is not "http" and not "https")
            return false;

        var host = uri.Host.ToLowerInvariant();
        if (host is "maps.app.goo.gl" or "goo.gl" or "maps.google.com" or "maps.google.com.pk")
            return true;
        if (host.EndsWith(".google.com", StringComparison.Ordinal) &&
            uri.AbsolutePath.Contains("/maps", StringComparison.OrdinalIgnoreCase))
            return true;
        if (host.StartsWith("www.google.", StringComparison.Ordinal) &&
            uri.AbsolutePath.Contains("/maps", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    internal static bool TryReadCoordinates(string? uri, string? body, out double lat, out double lng)
    {
        lat = 0;
        lng = 0;
        var hay = $"{uri}\n{body}";
        if (string.IsNullOrWhiteSpace(hay))
            return false;

        // Pin coords (!3d/!4d) are more precise than the map camera (@lat,lng).
        if (TryMatchPair(BangCoords, hay, out lat, out lng) && IsInPakistan(lat, lng))
            return true;
        if (TryMatchPair(DataCoords, hay, out lat, out lng) && IsInPakistan(lat, lng))
            return true;
        if (TryMatchPair(QueryCoords, hay, out lat, out lng) && IsInPakistan(lat, lng))
            return true;
        if (TryMatchPair(AtCoords, hay, out lat, out lng) && IsInPakistan(lat, lng))
            return true;
        return false;
    }

    internal static bool IsInPakistan(double lat, double lng)
        => lat is >= 23.5 and <= 37.15 && lng is >= 60.85 and <= 77.85;

    internal static string? NormalizePostal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length < 5)
            return null;
        var code = digits[..5];
        return code == "00000" ? null : code;
    }

    private async Task<(string Uri, string Body)> ExpandAsync(string url, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var final = res.RequestMessage?.RequestUri?.ToString() ?? url;
            var body = string.Empty;
            try
            {
                body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (body.Length > 400_000)
                    body = body[..400_000];
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not read Maps page body.");
            }

            return (PreferMapsUri(final, body), body);
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("Timed out reading this Maps link. Try again.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Could not open this Maps link right now. Check the link and try again.");
        }
    }

    private static string PreferMapsUri(string uri, string? body)
    {
        if (uri.Contains("/maps/place/", StringComparison.OrdinalIgnoreCase) ||
            uri.Contains("/maps/search/", StringComparison.OrdinalIgnoreCase))
            return uri;
        if (string.IsNullOrWhiteSpace(body))
            return uri;
        var hay = body.Replace("&amp;", "&", StringComparison.Ordinal);
        var m = EmbeddedMapsUrl.Match(hay);
        return m.Success ? m.Value.TrimEnd('"', '\'', '>', ')', ']') : uri;
    }

    private async Task<(double Lat, double Lng)?> TryGeocodePlaceAsync(
        string? uri, string? body, CancellationToken ct)
    {
        if (TryPlusCode(uri, body, out var plusLat, out var plusLng) && IsInPakistan(plusLat, plusLng))
            return (plusLat, plusLng);

        var googleKey = _config["GoogleMaps:ApiKey"];
        foreach (var query in PlaceQueries(uri, body))
        {
            if (!string.IsNullOrWhiteSpace(googleKey))
            {
                var g = await TryGoogleAddressAsync(query, googleKey, ct).ConfigureAwait(false);
                if (g is not null && IsInPakistan(g.Value.Lat, g.Value.Lng))
                    return g;
            }

            var n = await TryNominatimSearchAsync(query, ct).ConfigureAwait(false);
            if (n is not null && IsInPakistan(n.Value.Lat, n.Value.Lng))
                return n;

            var p = await TryPhotonSearchAsync(query, ct).ConfigureAwait(false);
            if (p is not null && IsInPakistan(p.Value.Lat, p.Value.Lng))
                return p;
        }

        var postal = ExtractPostalFromText(uri) ?? ExtractPostalFromText(body);
        if (postal is not null)
        {
            var byPost = await TryNominatimPostalAsync(postal, ct).ConfigureAwait(false);
            if (byPost is not null && IsInPakistan(byPost.Value.Lat, byPost.Value.Lng))
                return byPost;
        }

        return null;
    }

    private static bool TryPlusCode(string? uri, string? body, out double lat, out double lng)
    {
        lat = 0;
        lng = 0;
        var hay = $"{uri}\n{body}";
        if (string.IsNullOrWhiteSpace(hay))
            return false;

        var decoded = DecodeMapsText(hay);
        var m = PlusCodeRx.Match(decoded);
        if (!m.Success)
            return false;

        var code = $"{m.Groups[1].Value.ToUpperInvariant()}+{m.Groups[2].Value.ToUpperInvariant()}";
        var (refLat, refLng) = ReferenceForPlusCode(decoded);
        try
        {
            var recovered = OpenLocationCode.ShortCode.RecoverNearest(code, refLat, refLng);
            var area = recovered.Decode();
            lat = area.CenterLatitude;
            lng = area.CenterLongitude;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static (double Lat, double Lng) ReferenceForPlusCode(string text)
    {
        foreach (var city in PkCityRefs)
        {
            if (text.Contains(city.Name, StringComparison.OrdinalIgnoreCase))
                return (city.Lat, city.Lng);
        }

        return (30.3753, 69.3451);
    }

    private static readonly (string Name, double Lat, double Lng)[] PkCityRefs =
    {
        ("Rahim Yar Khan", 28.4202, 70.2952),
        ("Dera Ghazi Khan", 30.0327, 70.6403),
        ("Wah Cantonment", 33.7715, 72.7511),
        ("Mirpur Khas", 25.5270, 69.0120),
        ("Gujranwala", 32.1877, 74.1945),
        ("Faisalabad", 31.4504, 73.1350),
        ("Rawalpindi", 33.5651, 73.0169),
        ("Islamabad", 33.6844, 73.0479),
        ("Hyderabad", 25.3960, 68.3578),
        ("Nawabshah", 26.2442, 68.4100),
        ("Sheikhupura", 31.7131, 73.9783),
        ("Bahawalpur", 29.3544, 71.6911),
        ("Peshawar", 34.0151, 71.5249),
        ("Sargodha", 32.0836, 72.6711),
        ("Gujrat", 32.5740, 74.0780),
        ("Sahiwal", 30.6682, 73.1114),
        ("Karachi", 24.8607, 67.0011),
        ("Lahore", 31.5204, 74.3587),
        ("Multan", 30.1575, 71.5249),
        ("Quetta", 30.1798, 66.9750),
        ("Sialkot", 32.4945, 74.5229),
        ("Sukkur", 27.7052, 68.8574),
        ("Larkana", 27.5590, 68.2120),
        ("Mardan", 34.1989, 72.0231),
        ("Mingora", 34.7717, 72.3600),
        ("Okara", 30.8081, 73.4458)
    };

    private static IEnumerable<string> PlaceQueries(string? uri, string? body)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in EnumeratePlaceTitles(uri, body))
        {
            foreach (var q in ExpandPlaceQuery(raw))
            {
                if (seen.Add(q))
                    yield return q;
            }
        }
    }

    private static IEnumerable<string> EnumeratePlaceTitles(string? uri, string? body)
    {
        foreach (var source in new[] { uri, body })
        {
            if (string.IsNullOrWhiteSpace(source))
                continue;
            var text = DecodeMapsText(source);

            foreach (Match m in MapsQValue.Matches(text))
            {
                var decoded = DecodePlusQuery(m.Groups[1].Value);
                if (decoded.Length >= 3 && !LooksLikeCoordinates(decoded))
                    yield return decoded;
            }

            var path = MapsSearchPath.Match(text);
            if (!path.Success)
                continue;
            var fromPath = DecodePlusQuery(path.Groups[1].Value);
            if (fromPath.Length >= 3 && !LooksLikeCoordinates(fromPath))
                yield return fromPath;
        }
    }

    private static IEnumerable<string> ExpandPlaceQuery(string decoded)
    {
        var withoutPlus = PlusCodeRx.Replace(decoded, " ").Trim();
        withoutPlus = Regex.Replace(withoutPlus, @"\s{2,}", " ").Trim(' ', ',');

        var parts = decoded.Split(',').Select(p => p.Trim()).Where(p => p.Length > 1).ToList();
        var candidates = new List<string> { decoded };
        if (withoutPlus.Length >= 8 && !string.Equals(withoutPlus, decoded, StringComparison.OrdinalIgnoreCase))
            candidates.Add(withoutPlus);

        for (var i = 0; i < parts.Count; i++)
        {
            var joined = string.Join(", ", parts.Skip(i));
            if (joined.Length >= 8)
                candidates.Add(joined);
            var cleaned = PlusCodeRx.Replace(joined, " ").Trim(' ', ',');
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
            if (cleaned.Length >= 8)
                candidates.Add(cleaned);
        }

        return candidates;
    }

    private static string DecodeMapsText(string text)
        => text.Replace("&amp;", "&", StringComparison.Ordinal)
            .Replace("%21", "!", StringComparison.OrdinalIgnoreCase)
            .Replace("%2C", ",", StringComparison.OrdinalIgnoreCase)
            .Replace("%26", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("%23", "#", StringComparison.OrdinalIgnoreCase)
            .Replace("%20", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("+", " ");

    private static string DecodePlusQuery(string raw)
    {
        try
        {
            return Uri.UnescapeDataString(raw.Replace('+', ' ')).Trim();
        }
        catch
        {
            return raw.Replace('+', ' ').Trim();
        }
    }

    internal static string? ExtractPostalFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var decoded = DecodeMapsText(text);
        var m = TrailingPostal.Match(decoded);
        return m.Success ? NormalizePostal(m.Groups[1].Value) : null;
    }

    private async Task<(double Lat, double Lng)?> TryNominatimSearchAsync(string query, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(HttpClientName);
            var url =
                $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&countrycodes=pk&format=jsonv2&limit=5";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Cric360Grounds/1.0 (https://jplground.shoptick.shop)");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en");
            using var res = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return null;

            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
                .ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!TryReadJsonCoord(item, "lat", "lon", out var lat, out var lng))
                    continue;
                if (IsInPakistan(lat, lng))
                    return (lat, lng);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Nominatim place search failed.");
        }

        return null;
    }

    private async Task<(double Lat, double Lng)?> TryNominatimPostalAsync(string postal, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(HttpClientName);
            var url =
                $"https://nominatim.openstreetmap.org/search?postalcode={Uri.EscapeDataString(postal)}&country=Pakistan&format=jsonv2&limit=3";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Cric360Grounds/1.0 (https://jplground.shoptick.shop)");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en");
            using var res = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return null;

            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
                .ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!TryReadJsonCoord(item, "lat", "lon", out var lat, out var lng))
                    continue;
                if (IsInPakistan(lat, lng))
                    return (lat, lng);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Nominatim postal search failed.");
        }

        return null;
    }

    private async Task<(double Lat, double Lng)?> TryPhotonSearchAsync(string query, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(HttpClientName);
            using var res = await client.GetAsync(
                    $"https://photon.komoot.io/api/?q={Uri.EscapeDataString(query)}&limit=5", ct)
                .ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return null;

            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
                .ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("features", out var features))
                return null;
            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("geometry", out var geometry) ||
                    !geometry.TryGetProperty("coordinates", out var coords) ||
                    coords.GetArrayLength() < 2)
                    continue;
                var lng = coords[0].GetDouble();
                var lat = coords[1].GetDouble();
                if (feature.TryGetProperty("properties", out var props))
                {
                    var cc = Get(props, "countrycode");
                    if (!string.IsNullOrWhiteSpace(cc) &&
                        !string.Equals(cc, "PK", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (IsInPakistan(lat, lng))
                    return (lat, lng);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Photon place search failed.");
        }

        return null;
    }

    private async Task<(double Lat, double Lng)?> TryGoogleAddressAsync(string query, string apiKey, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(HttpClientName);
            var url =
                $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(query)}&components=country:PK&key={Uri.EscapeDataString(apiKey)}";
            var payload = await client.GetFromJsonAsync<GoogleGeocodeResponse>(url, ct).ConfigureAwait(false);
            var loc = payload?.Results?.FirstOrDefault()?.Geometry?.Location;
            if (loc is null)
                return null;
            return (loc.Lat, loc.Lng);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Google place geocode failed.");
            return null;
        }
    }

    private static bool TryReadJsonCoord(JsonElement item, string latName, string lngName, out double lat, out double lng)
    {
        lat = 0;
        lng = 0;
        if (!item.TryGetProperty(latName, out var la) || !item.TryGetProperty(lngName, out var lo))
            return false;
        return double.TryParse(la.GetString() ?? la.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
               && double.TryParse(lo.GetString() ?? lo.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out lng);
    }

    private readonly record struct PlaceFill(
        string? PostalCode = null,
        string? Country = null,
        string? State = null,
        string? City = null,
        string? Area = null,
        string? Formatted = null);

    private async Task<PlaceFill> ReverseGeocodeAsync(double lat, double lng, string? mapsHtml, CancellationToken ct)
    {
        // Nominatim first: neighbourhood/suburb at the pin. Google/postal APIs
        // often return the wider town (North Nazimabad) for the same code.
        var nominatim = await ReverseNominatimAsync(lat, lng, ct).ConfigureAwait(false);
        PlaceFill google = default;
        var googleKey = _config["GoogleMaps:ApiKey"];
        if (!string.IsNullOrWhiteSpace(googleKey))
            google = await ReverseGoogleAsync(lat, lng, googleKey, ct).ConfigureAwait(false);

        var photon = await ReversePhotonAsync(lat, lng, ct).ConfigureAwait(false);
        var fill = FillGaps(nominatim, FillGaps(google, photon));
        return fill with
        {
            PostalCode = fill.PostalCode ?? ExtractPostalFromHtml(mapsHtml)
        };
    }

    private async Task<PlaceFill> ReverseNominatimAsync(double lat, double lng, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(HttpClientName);
            var url =
                $"https://nominatim.openstreetmap.org/reverse?lat={Inv(lat)}&lon={Inv(lng)}&format=jsonv2&addressdetails=1&zoom=18";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Cric360Grounds/1.0 (https://jplground.shoptick.shop)");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en");
            using var res = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return default;

            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
                .ConfigureAwait(false);
            var root = doc.RootElement;
            var display = root.TryGetProperty("display_name", out var dn) ? dn.GetString() : null;
            if (!root.TryGetProperty("address", out var address))
                return new PlaceFill(Formatted: display);

            var countryCode = Get(address, "country_code");
            if (!string.IsNullOrWhiteSpace(countryCode) &&
                !string.Equals(countryCode, "pk", StringComparison.OrdinalIgnoreCase))
                return default;

            var named = root.TryGetProperty("name", out var nm) ? nm.GetString() : null;
            var addressType = root.TryGetProperty("addresstype", out var at) ? at.GetString()
                : root.TryGetProperty("type", out var ty) ? ty.GetString()
                : null;
            return FromNominatimAddress(address, display, named, addressType);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Nominatim reverse geocode failed.");
            return default;
        }
    }

    private async Task<PlaceFill> ReversePhotonAsync(double lat, double lng, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(HttpClientName);
            using var res = await client.GetAsync($"https://photon.komoot.io/reverse?lat={Inv(lat)}&lon={Inv(lng)}", ct)
                .ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                return default;

            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
                .ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("features", out var features) || features.GetArrayLength() == 0)
                return default;

            var props = features[0].GetProperty("properties");
            var cc = Get(props, "countrycode");
            if (!string.IsNullOrWhiteSpace(cc) &&
                !string.Equals(cc, "PK", StringComparison.OrdinalIgnoreCase))
                return default;

            return new PlaceFill(
                PostalCode: NormalizePostal(Get(props, "postcode")),
                Country: "Pakistan",
                State: NullIfEmpty(Get(props, "state")),
                City: NormalizeCityName(First(Get(props, "city"), Get(props, "town"), Get(props, "village"), Get(props, "county"))),
                Area: CleanArea(Get(props, "locality")),
                Formatted: Get(props, "name"));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Photon reverse geocode failed.");
            return default;
        }
    }

    private async Task<PlaceFill> ReverseGoogleAsync(double lat, double lng, string apiKey, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(HttpClientName);
            var url =
                $"https://maps.googleapis.com/maps/api/geocode/json?latlng={Inv(lat)},{Inv(lng)}&key={Uri.EscapeDataString(apiKey)}";
            var payload = await client.GetFromJsonAsync<GoogleGeocodeResponse>(url, ct).ConfigureAwait(false);
            return FromGoogleResult(payload?.Results?.FirstOrDefault());
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Google reverse geocode failed.");
            return default;
        }
    }

    private static PlaceFill FromNominatimAddress(
        JsonElement address,
        string? display,
        string? named = null,
        string? addressType = null)
    {
        var countryName = Get(address, "country");
        if (string.IsNullOrWhiteSpace(countryName) &&
            string.Equals(Get(address, "country_code"), "pk", StringComparison.OrdinalIgnoreCase))
            countryName = "Pakistan";

        var city = PickNominatimCity(address);
        return new PlaceFill(
            PostalCode: NormalizePostal(Get(address, "postcode")),
            Country: NullIfEmpty(countryName) ?? "Pakistan",
            State: NullIfEmpty(First(Get(address, "state"), Get(address, "region"))),
            City: city,
            Area: PickNominatimArea(address, named, addressType, city),
            Formatted: display);
    }

    private static PlaceFill FromGoogleResult(GoogleGeocodeResult? result)
    {
        if (result is null)
            return default;

        string? Comp(string type)
            => result.AddressComponents?.FirstOrDefault(c => c.Types?.Contains(type, StringComparer.OrdinalIgnoreCase) == true)?.LongName;

        var country = Comp("country");
        if (string.IsNullOrWhiteSpace(country) ||
            string.Equals(country, "PK", StringComparison.OrdinalIgnoreCase))
            country = "Pakistan";

        return new PlaceFill(
            PostalCode: NormalizePostal(Comp("postal_code")),
            Country: country,
            State: NullIfEmpty(Comp("administrative_area_level_1")),
            City: NormalizeCityName(First(Comp("locality"), Comp("administrative_area_level_2"))),
            Area: CleanArea(First(
                Comp("neighborhood"),
                Comp("sublocality_level_3"),
                Comp("sublocality_level_2"),
                Comp("sublocality"),
                Comp("sublocality_level_1"))),
            Formatted: result.FormattedAddress);
    }

    private static PlaceFill FillGaps(PlaceFill current, PlaceFill extra)
        => current with
        {
            PostalCode = current.PostalCode ?? extra.PostalCode,
            Country = NullIfEmpty(current.Country) ?? extra.Country,
            State = NullIfEmpty(current.State) ?? extra.State,
            City = NullIfEmpty(current.City) ?? extra.City,
            Area = NullIfEmpty(current.Area) ?? extra.Area,
            Formatted = NullIfEmpty(current.Formatted) ?? extra.Formatted
        };

    private static string? PickNominatimCity(JsonElement address)
    {
        foreach (var key in new[] { "city", "county", "state_district", "town", "village" })
        {
            var city = NormalizeCityName(Get(address, key));
            if (city is not null)
                return city;
        }
        return null;
    }

    private static string? PickNominatimArea(JsonElement address, string? named, string? addressType, string? city)
    {
        string? fromType = null;
        if (addressType is "suburb" or "neighbourhood" or "neighborhood" or "quarter" or "hamlet")
            fromType = CleanArea(named, city);

        var neighbourhood = Get(address, "neighbourhood");
        if (LooksLikeBlock(neighbourhood))
            neighbourhood = null;

        return First(
            CleanArea(Get(address, "suburb"), city),
            fromType,
            CleanTownArea(Get(address, "town"), city),
            CleanArea(neighbourhood, city),
            CleanArea(Get(address, "quarter"), city));
    }

    private static string? ExtractAreaFromMaps(string? uri, string? body, string? city)
    {
        foreach (var raw in EnumerateMapsPlaceTitles(uri, body))
        {
            var area = CleanArea(raw, city);
            if (area is not null && !LooksLikeCoordinates(area) && !LooksLikeBlock(area))
                return area;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateMapsPlaceTitles(string? uri, string? body)
    {
        if (!string.IsNullOrWhiteSpace(uri))
        {
            var m = MapsSearchPath.Match(uri);
            if (m.Success)
            {
                var decoded = Uri.UnescapeDataString(m.Groups[1].Value.Replace('+', ' '));
                var first = decoded.Split(',')[0].Trim();
                if (first.Length > 0)
                    yield return first;
            }
        }

        if (string.IsNullOrWhiteSpace(body))
            yield break;
        var html = MapsSearchPath.Match(body);
        if (html.Success)
        {
            var decoded = Uri.UnescapeDataString(html.Groups[1].Value.Replace('+', ' '));
            var first = decoded.Split(',')[0].Trim();
            if (first.Length > 0)
                yield return first;
        }
    }

    private static string? NormalizeCityName(string? raw)
    {
        var s = NullIfEmpty(raw);
        if (s is null)
            return null;
        if (ContainsKarachi(s))
            return "Karachi";
        if (s.EndsWith(" Division", StringComparison.OrdinalIgnoreCase))
            return NormalizeCityName(s[..^" Division".Length]);
        if (s.EndsWith(" District", StringComparison.OrdinalIgnoreCase))
            return NormalizeCityName(s[..^" District".Length]);
        if (s.EndsWith(" Town", StringComparison.OrdinalIgnoreCase))
            return null;
        return s;
    }

    private static string? CleanTownArea(string? town, string? city)
    {
        var s = NullIfEmpty(town);
        if (s is null || ContainsKarachi(s) && s.Equals("Karachi", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!s.EndsWith(" Town", StringComparison.OrdinalIgnoreCase))
            return null;
        return CleanArea(s[..^" Town".Length], city);
    }

    private static string? CleanArea(string? raw, string? city = null)
    {
        var s = NullIfEmpty(raw);
        if (s is null)
            return null;
        s = Regex.Replace(s, @"\s+(?:GPO|P\.?O\.?|Post Office)\b", "", RegexOptions.IgnoreCase).Trim();
        s = Regex.Replace(s, @"\s+Sector\s+[\w-]+$", "", RegexOptions.IgnoreCase).Trim();
        s = Regex.Replace(s, @"\s+Town$", "", RegexOptions.IgnoreCase).Trim();
        if (s.Length == 0)
            return null;
        if (string.Equals(s, city, StringComparison.OrdinalIgnoreCase))
            return null;
        if (s.EndsWith(" Division", StringComparison.OrdinalIgnoreCase) ||
            s.EndsWith(" District", StringComparison.OrdinalIgnoreCase))
            return null;
        return s;
    }

    private static bool LooksLikeBlock(string? name)
        => !string.IsNullOrWhiteSpace(name) && SectorOrBlock.IsMatch(name.Trim());

    private static bool LooksLikeCoordinates(string value)
        => Regex.IsMatch(value, @"^\d{1,2}[°\s]");

    private static bool ContainsKarachi(string value)
        => value.Contains("Karachi", StringComparison.OrdinalIgnoreCase)
           || value.Contains("Karāchi", StringComparison.OrdinalIgnoreCase)
           || value.Contains("Karācī", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractPostalFromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;
        var m = HtmlPostal.Match(html);
        if (m.Success)
            return NormalizePostal(m.Groups[1].Value);
        m = AddressPostal.Match(html);
        return m.Success ? NormalizePostal(m.Groups[1].Value) : null;
    }

    private static bool TryMatchPair(Regex regex, string text, out double lat, out double lng)
    {
        lat = 0;
        lng = 0;
        foreach (Match m in regex.Matches(text))
        {
            if (m.Groups.Count < 3)
                continue;
            if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
                continue;
            if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                continue;
            lat = a;
            lng = b;
            return true;
        }
        return false;
    }

    private static string Inv(double n) => n.ToString("0.#######", CultureInfo.InvariantCulture);

    private static string? Get(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) ? v.GetString() : null;

    private static string First(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class GoogleGeocodeResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("results")]
        public List<GoogleGeocodeResult>? Results { get; set; }
    }

    private sealed class GoogleGeocodeResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("formatted_address")]
        public string? FormattedAddress { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("address_components")]
        public List<GoogleAddressComponent>? AddressComponents { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("geometry")]
        public GoogleGeometry? Geometry { get; set; }
    }

    private sealed class GoogleGeometry
    {
        [System.Text.Json.Serialization.JsonPropertyName("location")]
        public GoogleLatLng? Location { get; set; }
    }

    private sealed class GoogleLatLng
    {
        [System.Text.Json.Serialization.JsonPropertyName("lat")]
        public double Lat { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("lng")]
        public double Lng { get; set; }
    }

    private sealed class GoogleAddressComponent
    {
        [System.Text.Json.Serialization.JsonPropertyName("long_name")]
        public string? LongName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("types")]
        public List<string>? Types { get; set; }
    }
}
