using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JPL.Grounds.Api.Contracts;
using JPL.Grounds.Api.Data;
using JPL.Grounds.Api.Models;
using JPL.Grounds.Api.Services;

namespace JPL.Grounds.Api.Controllers;

[ApiController]
[Route("api/owner/grounds")]
public sealed class OwnerGroundsController : ControllerBase, IAsyncDisposable
{
    private readonly GroundsDbContext _db;
    private readonly GroundImageStore _images;
    private readonly GoogleMapsLocationResolver _maps;

    public OwnerGroundsController(IGroundsDbFactory dbs, GroundImageStore images, GoogleMapsLocationResolver maps)
    {
        _db = dbs.CreateGrounds();
        _images = images;
        _maps = maps;
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [HttpGet]
    public async Task<ActionResult<GroundSearchPage>> List(string? cursor, int take = 20, CancellationToken ct = default)
    {
        if (!GroundsCaller.IsGroundOwner(HttpContext) && !GroundsCaller.IsSuperAdmin(HttpContext))
            return Forbid();

        take = Math.Clamp(take, 1, 50);
        var ownerId = GroundsCaller.UserId(HttpContext);
        var super = GroundsCaller.IsSuperAdmin(HttpContext);
        var query = _db.Grounds.AsNoTracking().Where(g => !g.IsDeleted);
        if (!super)
            query = query.Where(g => g.OwnerUserId == ownerId);

        if (GroundsMap.TryDecodeCursor(cursor, out var created, out var id))
        {
            query = query.Where(g =>
                g.CreatedAtUtc < created ||
                (g.CreatedAtUtc == created && string.Compare(g.Id.ToString(), id.ToString()) < 0));
        }

        var rows = await query
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

        string? next = null;
        if (hasMore && rows.Count > 0)
        {
            var last = rows[^1];
            next = GroundsMap.EncodeCursor(last.CreatedAtUtc, last.Id);
        }

        var ids = rows.Select(g => g.Id).ToList();
        var booked = ids.Count == 0
            ? new HashSet<Guid>()
            : (await _db.Bookings.AsNoTracking()
                .Where(b => !b.IsDeleted && ids.Contains(b.GroundId))
                .Select(b => b.GroundId)
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false)).ToHashSet();

        return Ok(new GroundSearchPage(
            rows.Select(g => GroundsMap.ToCard(g, Request, canDelete: !booked.Contains(g.Id))).ToList(),
            next,
            hasMore));
    }

    [HttpPost("resolve-maps")]
    public async Task<ActionResult<MapsLocationDto>> ResolveMaps([FromBody] MapsResolveRequest request, CancellationToken ct)
    {
        if (!GroundsCaller.IsGroundOwner(HttpContext) && !GroundsCaller.IsSuperAdmin(HttpContext))
            return Forbid();
        try
        {
            return Ok(await _maps.ResolveAsync(request.GoogleMapsUrl, ct).ConfigureAwait(false));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost]
    public async Task<ActionResult<GroundDetailDto>> Create([FromBody] GroundUpsertRequest request, CancellationToken ct)
    {
        if (!GroundsCaller.IsGroundOwner(HttpContext) && !GroundsCaller.IsSuperAdmin(HttpContext))
            return Forbid();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Ground name is required.");
        if (TryPakistanPhones(request.ContactPhone, request.WhatsAppNumber, out var contactPhone, out var whatsApp)
            is { } phoneError)
            return phoneError;

        var ground = new Ground
        {
            OwnerUserId = GroundsCaller.UserId(HttpContext),
            Name = RequireName(request.Name),
            Description = Trim(request.Description, 2000),
            AddressLine = Trim(request.AddressLine, 240),
            ContactPhone = contactPhone,
            WhatsAppNumber = whatsApp,
            ProvinceId = Guid.Empty,
            CityId = Guid.Empty,
            AreaId = null,
            CountryName = "Pakistan",
            ProvinceName = string.Empty,
            CityName = string.Empty,
            HourlyRate = Math.Max(0, request.HourlyRate),
            NightStartsHour = GroundBookingKinds.ClampNightHour(request.NightStartsHour),
            NightEndsHour = GroundBookingKinds.ClampNightEndHour(request.NightEndsHour),
            ListingStatus = GroundListingStatus.Draft
        };
        if (await TryApplyMapsAsync(ground, request, ct).ConfigureAwait(false) is { } mapsError)
            return mapsError;
        if (request.Amenities is not null)
            ApplyAmenities(ground, request.Amenities);
        if (request.Services is not null)
            ApplyServices(ground, request.Services);
        if (request.Offers is not null)
        {
            try
            {
                ApplyOffers(ground, request.Offers);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
        if (request.Links is not null)
        {
            try
            {
                ApplyLinks(ground, request.Links);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
        _db.Grounds.Add(ground);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(GroundsMap.ToDetail(ground, Request));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GroundDetailDto>> Update(Guid id, [FromBody] GroundUpsertRequest request, CancellationToken ct)
    {
        var ground = await LoadOwnedAsync(id, ct).ConfigureAwait(false);
        if (ground is null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Ground name is required.");
        if (TryPakistanPhones(request.ContactPhone, request.WhatsAppNumber, out var contactPhone, out var whatsApp)
            is { } phoneError)
            return phoneError;

        ground.Name = RequireName(request.Name);
        ground.Description = Trim(request.Description, 2000);
        ground.AddressLine = Trim(request.AddressLine, 240);
        ground.ContactPhone = contactPhone;
        ground.WhatsAppNumber = whatsApp;
        ground.ProvinceId = Guid.Empty;
        ground.CityId = Guid.Empty;
        ground.AreaId = null;
        if (await TryApplyMapsAsync(ground, request, ct).ConfigureAwait(false) is { } mapsError)
            return mapsError;
        ground.HourlyRate = request.Offers is not null ? Math.Max(0, request.HourlyRate) : ground.HourlyRate;
        if (request.Offers is not null)
        {
            ground.NightStartsHour = GroundBookingKinds.ClampNightHour(request.NightStartsHour);
            ground.NightEndsHour = GroundBookingKinds.ClampNightEndHour(request.NightEndsHour);
        }
        if (request.ListingStatus is not null)
        {
            var next = ParseListing(request.ListingStatus);
            if (next == GroundListingStatus.Published && !CanPublish(ground))
                return BadRequest("Save Location and Booking before publishing.");
            ground.ListingStatus = next;
        }
        ground.UpdatedAtUtc = DateTime.Now;
        if (request.Amenities is not null)
            ApplyAmenities(ground, request.Amenities);
        if (request.Services is not null)
            ApplyServices(ground, request.Services);
        if (request.Offers is not null)
        {
            try
            {
                ApplyOffers(ground, request.Offers);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
        if (request.Links is not null)
        {
            try
            {
                ApplyLinks(ground, request.Links);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(GroundsMap.ToDetail(ground, Request));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ground = await LoadOwnedAsync(id, ct).ConfigureAwait(false);
        if (ground is null)
            return NotFound();
        var hasBooking = await _db.Bookings.AsNoTracking()
            .AnyAsync(b => b.GroundId == id && !b.IsDeleted, ct)
            .ConfigureAwait(false);
        if (hasBooking)
            return BadRequest("This ground has bookings, so it cannot be deleted.");
        ground.IsDeleted = true;
        ground.UpdatedAtUtc = DateTime.Now;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPost("{id:guid}/images")]
    [RequestSizeLimit(GroundImageStore.MaxBytes + 64 * 1024)]
    public async Task<ActionResult<GroundImageDto>> UploadImage(Guid id, IFormFile file, CancellationToken ct)
    {
        var ground = await LoadOwnedAsync(id, ct).ConfigureAwait(false);
        if (ground is null)
            return NotFound();
        if (file is null || file.Length == 0)
            return BadRequest("Select an image.");

        var count = ground.Images.Count;
        if (count >= GroundImageStore.MaxImagesPerGround)
            return BadRequest($"Maximum {GroundImageStore.MaxImagesPerGround} images per ground.");

        string fileName;
        try
        {
            fileName = await _images.SaveAsync(id, file, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var image = new GroundImage
        {
            GroundId = id,
            FileName = fileName,
            SortOrder = count,
            IsCover = count == 0
        };
        _db.GroundImages.Add(image);
        if (image.IsCover)
            ground.CoverImagePath = fileName;
        ground.UpdatedAtUtc = DateTime.Now;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(new GroundImageDto(image.Id, image.FileName, GroundsMap.ImageUrl(Request, id, fileName), image.IsCover, image.SortOrder));
    }

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    public async Task<IActionResult> DeleteImage(Guid id, Guid imageId, CancellationToken ct)
    {
        var ground = await LoadOwnedAsync(id, ct).ConfigureAwait(false);
        if (ground is null)
            return NotFound();
        var image = ground.Images.FirstOrDefault(i => i.Id == imageId);
        if (image is null)
            return NotFound();

        _images.Delete(id, image.FileName);
        _db.GroundImages.Remove(image);
        if (image.IsCover)
        {
            var next = ground.Images.Where(i => i.Id != imageId).OrderBy(i => i.SortOrder).FirstOrDefault();
            if (next is not null)
            {
                next.IsCover = true;
                ground.CoverImagePath = next.FileName;
            }
            else
                ground.CoverImagePath = null;
        }

        ground.UpdatedAtUtc = DateTime.Now;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private async Task<Ground?> LoadOwnedAsync(Guid id, CancellationToken ct)
    {
        if (!GroundsCaller.IsGroundOwner(HttpContext) && !GroundsCaller.IsSuperAdmin(HttpContext))
            return null;
        var ground = await _db.Grounds
            .Include(g => g.Amenities)
            .Include(g => g.Services)
            .Include(g => g.Images)
            .Include(g => g.Offers)
            .Include(g => g.Links)
            .FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted, ct)
            .ConfigureAwait(false);
        if (ground is null)
            return null;
        if (!GroundsCaller.CanManageGround(HttpContext, ground.OwnerUserId))
            return null;
        return ground;
    }

    private static void ApplyAmenities(Ground ground, IReadOnlyList<string>? codes)
    {
        ground.Amenities.Clear();
        foreach (var code in (codes ?? []).Where(c => !string.IsNullOrWhiteSpace(c))
                     .Select(c => c.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(16))
        {
            ground.Amenities.Add(new GroundAmenity { GroundId = ground.Id, Code = code.Length > 40 ? code[..40] : code });
        }
    }

    private static void ApplyServices(Ground ground, IReadOnlyList<GroundServiceUpsert>? incoming)
    {
        ground.Services.Clear();
        foreach (var row in (incoming ?? [])
                     .Where(s => !string.IsNullOrWhiteSpace(s.Name) && s.Amount >= 0)
                     .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.First())
                     .Take(16))
        {
            var code = row.Name.Trim();
            if (code.Length > 40)
                code = code[..40];
            ground.Services.Add(new GroundService
            {
                GroundId = ground.Id,
                Code = code,
                Amount = Math.Max(0, Math.Round(row.Amount, 2))
            });
        }
    }

    private static void ApplyOffers(Ground ground, IReadOnlyList<GroundBookingOfferUpsert>? incoming)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ground.Offers.Clear();
        var sort = 0;
        foreach (var row in (incoming ?? []).Take(GroundBookingKinds.MaxPerGround))
        {
            var title = (row.Title ?? string.Empty).Trim();
            if (title.Length == 0)
                continue;
            if (title.Length > 80)
                title = title[..80];

            var kind = (row.Kind ?? string.Empty).Trim();
            if (kind.Length == 0 || !used.Add(kind))
                kind = GroundBookingKinds.NewKind(title, used);
            if (kind.Length > 64)
                kind = kind[..64];

            var hours = row.DurationHours > 0 ? Math.Clamp(row.DurationHours, 0.5m, 16m) : 1;
            var perHour = hours <= 1;
            if (perHour)
                hours = hours <= 0 ? 1 : Math.Clamp(hours, 0.5m, 12m);

            ground.Offers.Add(new GroundBookingOffer
            {
                GroundId = ground.Id,
                Kind = kind,
                Title = title,
                Hint = null,
                Price = Math.Max(0, row.Price),
                NightPrice = Math.Max(0, row.NightPrice),
                WeekendPrice = Math.Max(0, row.WeekendPrice),
                WeekendNightPrice = Math.Max(0, row.WeekendNightPrice),
                DurationHours = hours,
                PricesPerHour = perHour,
                SortOrder = sort++,
                IsEnabled = row.IsEnabled
            });
        }

        if (ground.Offers.Count == 0)
            throw new InvalidOperationException("Add at least one booking type (name, duration, and weekday rate).");
        if (!ground.Offers.Any(o => o.IsEnabled && o.Price > 0))
            throw new InvalidOperationException("Enable at least one booking type and set its weekday / day rate.");

        ground.HourlyRate = GroundBookingKinds.FromPrice(ground.Offers);
    }

    private static void ApplyLinks(Ground ground, IReadOnlyList<GroundLinkUpsert>? incoming)
    {
        ground.Links.Clear();
        var sort = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in incoming ?? [])
        {
            if (sort >= GroundsMap.MaxLinksPerGround)
                break;
            var raw = (row.Url ?? string.Empty).Trim();
            if (raw.Length == 0)
                continue;
            var url = GroundsMap.NormalizeHttpUrl(raw);
            if (!seen.Add(url))
                continue;
            var title = (row.Title ?? string.Empty).Trim();
            if (title.Length > 80)
                title = title[..80];
            ground.Links.Add(new GroundLink
            {
                GroundId = ground.Id,
                Title = title,
                Url = url,
                SortOrder = sort++
            });
        }
    }

    private static bool CanPublish(Ground ground)
        => !string.IsNullOrWhiteSpace(ground.GoogleMapsUrl)
           && !string.IsNullOrWhiteSpace(ground.CityName)
           && ground.Offers.Any(o => o.IsEnabled && o.Price > 0);

    private async Task<ActionResult?> TryApplyMapsAsync(Ground ground, GroundUpsertRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.GoogleMapsUrl))
            return null;
        MapsLocationDto pin;
        try
        {
            pin = await _maps.ResolveAsync(request.GoogleMapsUrl, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        ground.CountryName = Overlay(request.CountryName, pin.CountryName, 80) ?? "Pakistan";
        ground.ProvinceName = Overlay(request.ProvinceName, pin.ProvinceName, 80) ?? pin.ProvinceName ?? string.Empty;
        ground.CityName = Overlay(request.CityName, pin.CityName, 80) ?? pin.CityName ?? string.Empty;
        ground.AreaName = Overlay(request.AreaName, pin.AreaName, 80);
        ground.PostalCode = OverlayPostal(request.PostalCode, pin.PostalCode);
        ground.GoogleMapsUrl = pin.GoogleMapsUrl;
        ground.Latitude = pin.Latitude;
        ground.Longitude = pin.Longitude;
        ground.AddressLine = Trim(request.AddressLine, 240) ?? ground.AddressLine;
        if (string.IsNullOrWhiteSpace(ground.PostalCode)
            || string.IsNullOrWhiteSpace(ground.CountryName)
            || string.IsNullOrWhiteSpace(ground.ProvinceName)
            || string.IsNullOrWhiteSpace(ground.CityName)
            || string.IsNullOrWhiteSpace(ground.AreaName)
            || string.IsNullOrWhiteSpace(ground.AddressLine))
            return BadRequest("Complete all location fields.");
        return null;
    }

    private static GroundListingStatus ParseListing(string? text)
    {
        if (Enum.TryParse<GroundListingStatus>(text, ignoreCase: true, out var status))
            return status;
        return GroundListingStatus.Draft;
    }

    private static string? Overlay(string? userValue, string? fallback, int max)
        => Trim(userValue, max) ?? Trim(fallback, max);

    private ActionResult? TryPakistanPhones(
        string? phone,
        string? whatsApp,
        out string? contactPhone,
        out string? whatsAppNumber)
    {
        contactPhone = null;
        whatsAppNumber = null;
        if (!PakistanWhatsApp.TryNormalize(phone, out var phoneNorm))
            return BadRequest(PakistanWhatsApp.PhoneInvalidError);
        if (string.IsNullOrWhiteSpace(whatsApp))
            return BadRequest(PakistanWhatsApp.RequiredError);
        if (!PakistanWhatsApp.TryNormalize(whatsApp, out var waNorm))
            return BadRequest(PakistanWhatsApp.InvalidError);
        contactPhone = phoneNorm;
        whatsAppNumber = waNorm;
        return null;
    }

    private static string? OverlayPostal(string? userValue, string? fallback)
        => GoogleMapsLocationResolver.NormalizePostal(userValue)
           ?? GoogleMapsLocationResolver.NormalizePostal(fallback);

    private static string RequireName(string? name)
    {
        var t = name?.Trim() ?? string.Empty;
        if (t.Length == 0)
            throw new InvalidOperationException("Ground name is required.");
        return t.Length <= 160 ? t : t[..160];
    }

    private static string? Trim(string? value, int max)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t))
            return null;
        return t.Length <= max ? t : t[..max];
    }
}

[ApiController]
[Route("api/bookings")]
public sealed class BookingsController : ControllerBase, IAsyncDisposable
{
    private readonly GroundsDbContext _db;

    public BookingsController(IGroundsDbFactory dbs) => _db = dbs.CreateGrounds();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<BookingDto>>> Mine(string? cursor, int take = 20, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 50);
        var userId = GroundsCaller.UserId(HttpContext);
        var query = _db.Bookings.AsNoTracking()
            .Include(b => b.Ground)
            .Include(b => b.Services)
            .Where(b => !b.IsDeleted && b.CustomerUserId == userId);

        if (GroundsMap.TryDecodeCursor(cursor, out var created, out var id))
        {
            query = query.Where(b =>
                b.CreatedAtUtc < created ||
                (b.CreatedAtUtc == created && string.Compare(b.Id.ToString(), id.ToString()) < 0));
        }

        var rows = await query
            .OrderByDescending(b => b.CreatedAtUtc)
            .ThenByDescending(b => b.Id)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Ok(rows.Select(b => GroundsMap.ToBooking(b, b.Ground)).ToList());
    }

    [HttpGet("owner")]
    public async Task<ActionResult<IReadOnlyList<BookingDto>>> Owner(Guid? groundId, string? cursor, int take = 20, CancellationToken ct = default)
    {
        if (!GroundsCaller.IsGroundOwner(HttpContext) && !GroundsCaller.IsSuperAdmin(HttpContext))
            return Forbid();

        take = Math.Clamp(take, 1, 50);
        var ownerId = GroundsCaller.UserId(HttpContext);
        var super = GroundsCaller.IsSuperAdmin(HttpContext);
        var query = _db.Bookings.AsNoTracking()
            .Include(b => b.Ground)
            .Include(b => b.Services)
            .Where(b => !b.IsDeleted && !b.Ground.IsDeleted);
        if (!super)
            query = query.Where(b => b.Ground.OwnerUserId == ownerId);
        if (groundId is Guid gid && gid != Guid.Empty)
            query = query.Where(b => b.GroundId == gid);

        if (GroundsMap.TryDecodeCursor(cursor, out var created, out var id))
        {
            query = query.Where(b =>
                b.CreatedAtUtc < created ||
                (b.CreatedAtUtc == created && string.Compare(b.Id.ToString(), id.ToString()) < 0));
        }

        var rows = await query
            .OrderByDescending(b => b.CreatedAtUtc)
            .ThenByDescending(b => b.Id)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Ok(rows.Select(b => GroundsMap.ToBooking(b, b.Ground)).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingDto>> Get(Guid id, CancellationToken ct)
    {
        var b = await _db.Bookings.AsNoTracking()
            .Include(x => x.Ground)
            .Include(x => x.Services)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            .ConfigureAwait(false);
        if (b is null)
            return NotFound();

        var userId = GroundsCaller.UserId(HttpContext);
        var allowed = b.CustomerUserId == userId
                      || GroundsCaller.CanManageGround(HttpContext, b.Ground.OwnerUserId);
        if (!allowed)
            return Forbid();

        return Ok(GroundsMap.ToBooking(b, b.Ground));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BookingDto>> Update(Guid id, [FromBody] BookingUpdateRequest request, CancellationToken ct)
    {
        var b = await _db.Bookings
            .Include(x => x.Ground)
            .Include(x => x.Services)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            .ConfigureAwait(false);
        if (b is null)
            return NotFound();

        var userId = GroundsCaller.UserId(HttpContext);
        var isOwner = GroundsCaller.CanManageGround(HttpContext, b.Ground.OwnerUserId);
        var isCustomer = b.CustomerUserId == userId;

        if (!isOwner && !isCustomer)
            return Forbid();

        if (request.Status is not null)
        {
            var next = GroundBookingStatus.Normalize(request.Status);
            if (!CanChangeStatus(b.Status, next, isOwner, isCustomer))
                return BadRequest("That status change is not allowed.");

            if (GroundBookingStatus.HoldsSlot(next))
            {
                var clash = await GroundsMap.FindSlotClashAsync(_db, b.GroundId, b.StartAt, b.EndAt, b.Id, ct)
                    .ConfigureAwait(false);
                if (clash is not null)
                    return Conflict(GroundsMap.SlotConflictMessage(clash.StartAt, clash.EndAt));
            }

            b.Status = next;
        }

        if (isOwner)
        {
            if (request.BookingAmount is decimal amount)
                b.BookingAmount = Math.Max(0, amount);
            if (request.Advance is decimal advance)
                b.Advance = Math.Clamp(advance, 0, b.BookingAmount);
            if (request.PaymentNote is not null)
                b.PaymentNote = Trim(request.PaymentNote, 400);
        }

        if (request.Notes is not null)
            b.Notes = Trim(request.Notes, 1000);

        b.UpdatedAtUtc = DateTime.Now;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(GroundsMap.ToBooking(b, b.Ground));
    }

    private static bool CanChangeStatus(string current, string next, bool isOwner, bool isCustomer)
    {
        if (string.Equals(current, next, StringComparison.OrdinalIgnoreCase))
            return true;

        if (isOwner)
        {
            return current switch
            {
                GroundBookingStatus.Pending when next is GroundBookingStatus.Confirmed
                    or GroundBookingStatus.Rejected
                    or GroundBookingStatus.Cancelled => true,
                GroundBookingStatus.Confirmed when next is GroundBookingStatus.Completed
                    or GroundBookingStatus.Cancelled => true,
                _ => false
            };
        }

        if (isCustomer)
        {
            return current is GroundBookingStatus.Pending or GroundBookingStatus.Confirmed
                   && next == GroundBookingStatus.Cancelled;
        }

        return false;
    }

    private static string? Trim(string? value, int max)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t))
            return null;
        return t.Length <= max ? t : t[..max];
    }
}
