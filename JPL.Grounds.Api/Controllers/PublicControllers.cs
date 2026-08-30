using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JPL.Grounds.Api.Contracts;
using JPL.Grounds.Api.Data;
using JPL.Grounds.Api.Models;
using JPL.Grounds.Api.Services;

namespace JPL.Grounds.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        service = "JPL.Grounds.Api",
        version = "1.0.3",
        time = DateTime.Now
    });
}

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [HttpGet("me")]
    public IActionResult Me()
    {
        var userId = GroundsCaller.UserId(HttpContext);
        var username = GroundsCaller.Username(HttpContext);
        var role = GroundsCaller.Role(HttpContext);
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(username))
            return Unauthorized();
        return Ok(new { userId, username, role });
    }
}

[ApiController]
[Route("api/grounds")]
public sealed class GroundsController : ControllerBase, IAsyncDisposable
{
    private readonly GroundsDbContext _db;
    private readonly GroundImageStore _images;
    private readonly GoogleMapsLocationResolver _maps;

    public GroundsController(IGroundsDbFactory dbs, GroundImageStore images, GoogleMapsLocationResolver maps)
    {
        _db = dbs.CreateGrounds();
        _images = images;
        _maps = maps;
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [HttpPost("resolve-maps")]
    public async Task<ActionResult<MapsLocationDto>> ResolveMaps([FromBody] MapsResolveRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _maps.ResolveAsync(request.GoogleMapsUrl, ct).ConfigureAwait(false));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("location-options")]
    public async Task<ActionResult<GroundLocationOptionsDto>> LocationOptions(
        string? country,
        string? state,
        string? city,
        CancellationToken ct = default)
    {
        var published = GroundsCaller.WhereOwnedByCaller(
            _db.Grounds.AsNoTracking()
                .Where(g => !g.IsDeleted && g.ListingStatus == GroundListingStatus.Published),
            HttpContext);

        var countries = await DistinctNamesAsync(
                published.Select(g => g.CountryName ?? "Pakistan"), ct)
            .ConfigureAwait(false);
        if (!countries.Any(c => string.Equals(c, "Pakistan", StringComparison.OrdinalIgnoreCase)))
            countries = countries.Prepend("Pakistan").ToList();

        var stateQuery = published;
        var countryName = TrimOrNull(country);
        if (countryName is not null)
            stateQuery = FilterCountry(stateQuery, countryName);
        var states = countryName is null
            ? (IReadOnlyList<string>)[]
            : await DistinctNamesAsync(stateQuery.Select(g => g.ProvinceName), ct).ConfigureAwait(false);

        var cityQuery = stateQuery;
        var stateName = TrimOrNull(state);
        if (stateName is not null)
            cityQuery = cityQuery.Where(g => g.ProvinceName.ToLower() == stateName.ToLower());
        var cities = stateName is null
            ? (IReadOnlyList<string>)[]
            : await DistinctNamesAsync(cityQuery.Select(g => g.CityName), ct).ConfigureAwait(false);

        var areaQuery = cityQuery;
        var cityName = TrimOrNull(city);
        if (cityName is not null)
            areaQuery = areaQuery.Where(g => g.CityName.ToLower() == cityName.ToLower());
        var areas = cityName is null
            ? (IReadOnlyList<string>)[]
            : await DistinctNamesAsync(areaQuery.Select(g => g.AreaName ?? ""), ct).ConfigureAwait(false);

        return Ok(new GroundLocationOptionsDto(countries, states, cities, areas));
    }

    [HttpGet("search")]
    public async Task<ActionResult<GroundSearchPage>> Search(
        string? postalCode,
        string? q,
        string? country,
        string? state,
        string? city,
        string? area,
        decimal? minRate,
        decimal? maxRate,
        string? amenities,
        string? cursor,
        int take = 5,
        CancellationToken ct = default)
    {
        take = Paging.ClampTake(take);
        var query = GroundsCaller.WhereOwnedByCaller(
            _db.Grounds.AsNoTracking()
                .Where(g => !g.IsDeleted && g.ListingStatus == GroundListingStatus.Published),
            HttpContext);

        var postal = GoogleMapsLocationResolver.NormalizePostal(postalCode);
        if (postal is not null)
        {
            query = query.Where(g => g.PostalCode == postal);
        }
        else
        {
            var countryName = TrimOrNull(country);
            if (countryName is not null)
                query = FilterCountry(query, countryName);
            var stateName = TrimOrNull(state);
            if (stateName is not null)
                query = query.Where(g => g.ProvinceName.ToLower() == stateName.ToLower());
            var cityName = TrimOrNull(city);
            if (cityName is not null)
                query = query.Where(g => g.CityName.ToLower() == cityName.ToLower());
            var areaName = TrimOrNull(area);
            if (areaName is not null)
                query = query.Where(g => (g.AreaName ?? "").ToLower() == areaName.ToLower());
        }

        if (minRate is decimal min)
            query = query.Where(g => g.HourlyRate >= min);
        if (maxRate is decimal max)
            query = query.Where(g => g.HourlyRate <= max);

        var name = q?.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            var like = $"%{name}%";
            query = query.Where(g => EF.Functions.Like(g.Name, like));
        }

        var required = ParseAmenities(amenities);
        if (required.Count > 0)
        {
            foreach (var code in required)
                query = query.Where(g => g.Amenities.Any(x => x.Code == code));
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        if (GroundsMap.TryDecodeCursor(cursor, out var created, out var id))
        {
            query = query.Where(g =>
                g.CreatedAtUtc < created ||
                (g.CreatedAtUtc == created && string.Compare(g.Id.ToString(), id.ToString()) < 0));
        }

        var rows = await query
            .AsSplitQuery()
            .Include(g => g.Amenities)
            .Include(g => g.Services)
            .Include(g => g.Offers)
            .OrderByDescending(g => g.CreatedAtUtc)
            .ThenByDescending(g => g.Id)
            .Take(take + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var hasMore = rows.Count > take;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var stats = await GroundsMap.LoadStatsAsync(_db, rows.Select(g => g.Id).ToList(), ct)
            .ConfigureAwait(false);
        var items = rows.Select(g => GroundsMap.ToCard(g, Request, stats: stats.GetValueOrDefault(g.Id))).ToList();
        string? next = null;
        if (hasMore && rows.Count > 0)
        {
            var last = rows[^1];
            next = GroundsMap.EncodeCursor(last.CreatedAtUtc, last.Id);
        }

        return Ok(new GroundSearchPage(items, next, hasMore, total));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GroundDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var g = await _db.Grounds.AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Amenities)
            .Include(x => x.Services)
            .Include(x => x.ShopItems)
            .Include(x => x.Images)
            .Include(x => x.Offers)
            .Include(x => x.Links)
            .Include(x => x.Videos)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            .ConfigureAwait(false);
        if (g is null)
            return NotFound();
        if (!GroundsCaller.CanViewGround(HttpContext, g))
            return NotFound();

        var stats = await GroundsMap.LoadStatsAsync(_db, [g.Id], ct).ConfigureAwait(false);
        return Ok(GroundsMap.ToDetail(g, Request, stats.GetValueOrDefault(g.Id)));
    }

    [HttpGet("{id:guid}/reviews")]
    public async Task<ActionResult<GroundReviewPage>> Reviews(
        Guid id, string? cursor, int take = 5, CancellationToken ct = default)
    {
        var exists = await _db.Grounds.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted, ct)
            .ConfigureAwait(false);
        if (exists is null)
            return NotFound();
        if (!GroundsCaller.CanViewGround(HttpContext, exists))
            return NotFound();

        take = Paging.ClampTake(take);
        var query = _db.Reviews.AsNoTracking()
            .Include(r => r.Booking)
            .Where(r => r.GroundId == id && !r.Booking.IsDeleted);

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        if (GroundsMap.TryDecodeCursor(cursor, out var created, out var reviewId))
        {
            query = query.Where(r =>
                r.CreatedAtUtc < created ||
                (r.CreatedAtUtc == created && string.Compare(r.Id.ToString(), reviewId.ToString()) < 0));
        }

        var rows = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(take + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var hasMore = rows.Count > take;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        string? next = null;
        if (hasMore && rows.Count > 0)
        {
            var last = rows[^1];
            next = GroundsMap.EncodeCursor(last.CreatedAtUtc, last.Id);
        }

        return Ok(new GroundReviewPage(rows.Select(GroundsMap.ToReview).ToList(), next, hasMore, total));
    }

    [HttpGet("{id:guid}/images/{fileName}")]
    public IActionResult Image(Guid id, string fileName)
    {
        if (!_images.TryGetFullPath(id, fileName, out var fullPath, out var contentType))
            return NotFound();
        return PhysicalFile(fullPath, contentType);
    }

    [HttpPost("{id:guid}/bookings")]
    public async Task<ActionResult<BookingDto>> RequestBooking(Guid id, [FromBody] BookingCreateRequest request, CancellationToken ct)
    {
        if (GroundsCaller.IsGroundOwner(HttpContext) && !GroundsCaller.IsSuperAdmin(HttpContext))
            return BadRequest("Ground owners book from the owner dashboard.");
        if (GroundsCaller.IsGuest(HttpContext))
            return StatusCode(StatusCodes.Status403Forbidden, "Sign up with your own account to book a ground.");
        var accountPhone = GroundsCaller.WhatsAppNumber(HttpContext);
        var phoneSource = !string.IsNullOrWhiteSpace(accountPhone) ? accountPhone : request.CustomerPhone;
        if (!PakistanWhatsApp.TryNormalize(phoneSource, out var customerPhone))
            return BadRequest(PakistanWhatsApp.RequiredError);

        var ground = await _db.Grounds
            .Include(g => g.Offers)
            .Include(g => g.Services)
            .Include(g => g.ShopItems)
            .FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted && g.ListingStatus == GroundListingStatus.Published, ct)
            .ConfigureAwait(false);
        if (ground is null)
            return NotFound("Ground not found.");

        var customerId = GroundsCaller.UserId(HttpContext);
        var accountName = GroundsCaller.Username(HttpContext);
        var name = !string.IsNullOrWhiteSpace(accountName)
            ? accountName
            : (request.CustomerName?.Trim() ?? string.Empty);

        var (booking, error) = await GroundBookingBuilder.TryBuildAsync(
                _db, ground, request, customerId, name, customerPhone, GroundBookingStatus.Pending, ct)
            .ConfigureAwait(false);
        if (error is { } e)
            return e.StatusCode == 409 ? Conflict(e.Message) : BadRequest(e.Message);

        _db.Bookings.Add(booking!);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(GroundsMap.ToBooking(booking!, ground));
    }

    private static List<string> ParseAmenities(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Length > 40 ? s[..40] : s)
            .ToList();
    }

    private static string? Trim(string? value, int max)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t))
            return null;
        return t.Length <= max ? t : t[..max];
    }

    private static IQueryable<Ground> FilterCountry(IQueryable<Ground> query, string countryName)
    {
        var wanted = countryName.Trim().ToLower();
        if (wanted == "pakistan")
            return query.Where(g =>
                g.CountryName == null
                || g.CountryName == ""
                || g.CountryName.ToLower() == "pakistan");
        return query.Where(g => g.CountryName.ToLower() == wanted);
    }

    private static string? TrimOrNull(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    private static async Task<IReadOnlyList<string>> DistinctNamesAsync(
        IQueryable<string> source, CancellationToken ct)
    {
        var rows = await source.ToListAsync(ct).ConfigureAwait(false);
        return rows
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
