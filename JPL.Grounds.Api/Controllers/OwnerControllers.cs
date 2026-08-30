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
    public async Task<ActionResult<GroundSearchPage>> List(string? cursor, int take = 5, CancellationToken ct = default)
    {
        if (!GroundsCaller.IsGroundOwner(HttpContext) && !GroundsCaller.IsSuperAdmin(HttpContext))
            return Forbid();

        take = Paging.ClampTake(take);
        var ownerId = GroundsCaller.UserId(HttpContext);
        var super = GroundsCaller.IsSuperAdmin(HttpContext);
        var query = _db.Grounds.AsNoTracking().Where(g => !g.IsDeleted);
        if (!super)
        {
            if (ownerId == Guid.Empty)
                return Ok(new GroundSearchPage([], null, false, 0));
            query = query.Where(g => g.OwnerUserId == ownerId);
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

        var stats = await GroundsMap.LoadStatsAsync(_db, ids, ct).ConfigureAwait(false);
        return Ok(new GroundSearchPage(
            rows.Select(g => GroundsMap.ToCard(
                g, Request, canDelete: !booked.Contains(g.Id), stats: stats.GetValueOrDefault(g.Id))).ToList(),
            next,
            hasMore,
            total));
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
        var ownerId = GroundsCaller.UserId(HttpContext);
        if (ownerId == Guid.Empty)
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Ground name is required.");
        if (TryPakistanPhones(request.ContactPhone, request.WhatsAppNumber, out var contactPhone, out var whatsApp)
            is { } phoneError)
            return phoneError;

        var ground = new Ground
        {
            OwnerUserId = ownerId,
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
        if (request.Shop is not null)
            ApplyShop(ground, request.Shop);
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
        if (request.Videos is not null)
        {
            try
            {
                ApplyVideos(ground, request.Videos);
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
        var ground = await LoadOwnedForUpdateAsync(id, request, ct).ConfigureAwait(false);
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
        if (request.Shop is not null)
            ApplyShop(ground, request.Shop);
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
        try
        {
            if (request.Links is not null)
                await ReplaceLinksAsync(ground.Id, request.Links, ct).ConfigureAwait(false);
            if (request.Videos is not null)
                await ReplaceVideosAsync(ground.Id, request.Videos, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        var mapped = await LoadDetailAsync(id, ct).ConfigureAwait(false);
        return Ok(GroundsMap.ToDetail(mapped, Request));
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

    [HttpPost("{id:guid}/bookings")]
    public async Task<ActionResult<BookingDto>> CreateConfirmedBooking(
        Guid id, [FromBody] BookingCreateRequest request, CancellationToken ct)
    {
        var ground = await LoadOwnedAsync(id, ct).ConfigureAwait(false);
        if (ground is null)
            return NotFound("Ground not found.");
        if (!PakistanWhatsApp.TryNormalize(request.CustomerPhone, out var customerPhone))
            return BadRequest(PakistanWhatsApp.RequiredError);
        var name = request.CustomerName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Customer name is required.");

        var (booking, error) = await GroundBookingBuilder.TryBuildAsync(
                _db, ground, request, Guid.Empty, name, customerPhone, GroundBookingStatus.Confirmed, ct)
            .ConfigureAwait(false);
        if (error is { } e)
            return e.StatusCode == 409 ? Conflict(e.Message) : BadRequest(e.Message);

        booking!.ApplyMoney(request.Discount, request.Advance);
        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(GroundsMap.ToBooking(booking, ground));
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
            .AsSplitQuery()
            .Include(g => g.Amenities)
            .Include(g => g.Services)
            .Include(g => g.ShopItems)
            .Include(g => g.Images)
            .Include(g => g.Offers)
            .Include(g => g.Links)
            .Include(g => g.Videos)
            .FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted, ct)
            .ConfigureAwait(false);
        if (ground is null)
            return null;
        if (!GroundsCaller.CanManageGround(HttpContext, ground.OwnerUserId))
            return null;
        return ground;
    }

    private async Task<Ground?> LoadOwnedForUpdateAsync(Guid id, GroundUpsertRequest request, CancellationToken ct)
    {
        if (!GroundsCaller.IsGroundOwner(HttpContext) && !GroundsCaller.IsSuperAdmin(HttpContext))
            return null;

        var query = _db.Grounds.Where(g => g.Id == id && !g.IsDeleted);
        var includeChildren = request.Amenities is not null
            || request.Services is not null
            || request.Shop is not null
            || request.Offers is not null
            || request.ListingStatus is not null;
        if (includeChildren)
            query = query.AsSplitQuery();
        if (request.Amenities is not null)
            query = query.Include(g => g.Amenities);
        if (request.Services is not null)
            query = query.Include(g => g.Services);
        if (request.Shop is not null)
            query = query.Include(g => g.ShopItems);
        if (request.Offers is not null || request.ListingStatus is not null)
            query = query.Include(g => g.Offers);

        var ground = await query.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (ground is null)
            return null;
        if (!GroundsCaller.CanManageGround(HttpContext, ground.OwnerUserId))
            return null;
        return ground;
    }

    private Task<Ground> LoadDetailAsync(Guid id, CancellationToken ct)
        => _db.Grounds.AsNoTracking()
            .AsSplitQuery()
            .Include(g => g.Amenities)
            .Include(g => g.Services)
            .Include(g => g.ShopItems)
            .Include(g => g.Images)
            .Include(g => g.Offers)
            .Include(g => g.Links)
            .Include(g => g.Videos)
            .FirstAsync(g => g.Id == id, ct);

    private static void ApplyAmenities(Ground ground, IReadOnlyList<string>? codes)
    {
        var wanted = (codes ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c =>
            {
                var code = c.Trim();
                return code.Length > 40 ? code[..40] : code;
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();
        var wantedSet = wanted.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var extra in ground.Amenities.Where(a => !wantedSet.Contains(a.Code)).ToList())
            ground.Amenities.Remove(extra);

        var have = ground.Amenities.Select(a => a.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var code in wanted)
        {
            if (have.Add(code))
                ground.Amenities.Add(new GroundAmenity { GroundId = ground.Id, Code = code });
        }
    }

    private static void ApplyServices(Ground ground, IReadOnlyList<GroundServiceUpsert>? incoming)
    {
        var wanted = (incoming ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Name) && s.Amount >= 0)
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(16)
            .Select(row =>
            {
                var code = row.Name.Trim();
                if (code.Length > 40)
                    code = code[..40];
                return (Code: code, Amount: Math.Max(0, Math.Round(row.Amount, 2)));
            })
            .ToList();
        var map = wanted.ToDictionary(x => x.Code, x => x.Amount, StringComparer.OrdinalIgnoreCase);

        foreach (var extra in ground.Services.Where(s => !map.ContainsKey(s.Code)).ToList())
            ground.Services.Remove(extra);

        foreach (var existing in ground.Services)
            existing.Amount = map[existing.Code];

        var have = ground.Services.Select(s => s.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, amount) in wanted)
        {
            if (have.Add(code))
            {
                ground.Services.Add(new GroundService
                {
                    GroundId = ground.Id,
                    Code = code,
                    Amount = amount
                });
            }
        }
    }

    private static void ApplyShop(Ground ground, IReadOnlyList<GroundShopItemUpsert>? incoming)
    {
        var wanted = (incoming ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Name) && s.Price >= 0)
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(16)
            .Select(row =>
            {
                var code = row.Name.Trim();
                if (code.Length > 40)
                    code = code[..40];
                return (
                    Code: code,
                    Price: Math.Max(0, Math.Round(row.Price, 2)),
                    Kind: GroundShopKinds.Normalize(row.Kind));
            })
            .ToList();
        var map = wanted.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var extra in ground.ShopItems.Where(s => !map.ContainsKey(s.Code)).ToList())
            ground.ShopItems.Remove(extra);

        foreach (var existing in ground.ShopItems)
        {
            var row = map[existing.Code];
            existing.Price = row.Price;
            existing.Kind = row.Kind;
        }

        var have = ground.ShopItems.Select(s => s.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in wanted)
        {
            if (have.Add(row.Code))
            {
                ground.ShopItems.Add(new GroundShopItem
                {
                    GroundId = ground.Id,
                    Code = row.Code,
                    Price = row.Price,
                    Kind = row.Kind
                });
            }
        }
    }

    private static void ApplyOffers(Ground ground, IReadOnlyList<GroundBookingOfferUpsert>? incoming)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var next = new List<GroundBookingOffer>();
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

            next.Add(new GroundBookingOffer
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

        if (next.Count == 0)
            throw new InvalidOperationException("Add at least one booking type (name, duration, and weekday rate).");
        if (!next.Any(o => o.IsEnabled && o.Price > 0))
            throw new InvalidOperationException("Enable at least one booking type and set its weekday / day rate.");

        var wantedKinds = next.Select(o => o.Kind).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var extra in ground.Offers.Where(o => !wantedKinds.Contains(o.Kind)).ToList())
            ground.Offers.Remove(extra);

        foreach (var row in next)
        {
            var existing = ground.Offers.FirstOrDefault(o =>
                string.Equals(o.Kind, row.Kind, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                ground.Offers.Add(row);
                continue;
            }

            existing.Title = row.Title;
            existing.Hint = row.Hint;
            existing.Price = row.Price;
            existing.NightPrice = row.NightPrice;
            existing.WeekendPrice = row.WeekendPrice;
            existing.WeekendNightPrice = row.WeekendNightPrice;
            existing.DurationHours = row.DurationHours;
            existing.PricesPerHour = row.PricesPerHour;
            existing.SortOrder = row.SortOrder;
            existing.IsEnabled = row.IsEnabled;
        }

        ground.HourlyRate = GroundBookingKinds.FromPrice(ground.Offers);
    }

    private static void ApplyLinks(Ground ground, IReadOnlyList<GroundLinkUpsert>? incoming)
        => AttachUrlRows(ground.Links, ground.Id, NormalizeLinks(incoming),
            (id, title, url, sort) => new GroundLink { GroundId = id, Title = title, Url = url, SortOrder = sort });

    private static void ApplyVideos(Ground ground, IReadOnlyList<GroundVideoUpsert>? incoming)
        => AttachUrlRows(ground.Videos, ground.Id, NormalizeVideos(incoming),
            (id, title, url, sort) => new GroundVideo { GroundId = id, Title = title, Url = url, SortOrder = sort });

    private Task ReplaceLinksAsync(Guid groundId, IReadOnlyList<GroundLinkUpsert> incoming, CancellationToken ct)
        => ReplaceUrlChildrenAsync(
            _db.GroundLinks, groundId, NormalizeLinks(incoming), ct,
            (id, title, url, sort) => new GroundLink { GroundId = id, Title = title, Url = url, SortOrder = sort });

    private Task ReplaceVideosAsync(Guid groundId, IReadOnlyList<GroundVideoUpsert> incoming, CancellationToken ct)
        => ReplaceUrlChildrenAsync(
            _db.GroundVideos, groundId, NormalizeVideos(incoming), ct,
            (id, title, url, sort) => new GroundVideo { GroundId = id, Title = title, Url = url, SortOrder = sort });

    private async Task ReplaceUrlChildrenAsync<T>(
        DbSet<T> set, Guid groundId, List<(string Title, string Url)> next, CancellationToken ct,
        Func<Guid, string, string, int, T> create)
        where T : class
    {
        await set.Where(x => EF.Property<Guid>(x, "GroundId") == groundId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
        var sort = 0;
        foreach (var row in next)
            set.Add(create(groundId, row.Title, row.Url, sort++));
    }

    private static List<(string Title, string Url)> NormalizeLinks(IReadOnlyList<GroundLinkUpsert>? incoming)
        => NormalizeUrlRows(incoming?.Select(r => (r.Title, r.Url)), GroundsMap.MaxLinksPerGround, GroundsMap.NormalizeHttpUrl);

    private static List<(string Title, string Url)> NormalizeVideos(IReadOnlyList<GroundVideoUpsert>? incoming)
        => NormalizeUrlRows(incoming?.Select(r => (r.Title, r.Url)), GroundsMap.MaxVideosPerGround, GroundsMap.NormalizeVideoUrl);

    private static List<(string Title, string Url)> NormalizeUrlRows(
        IEnumerable<(string? Title, string Url)>? incoming, int max, Func<string, string> normalizeUrl)
    {
        var next = new List<(string Title, string Url)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in incoming ?? [])
        {
            if (next.Count >= max)
                break;
            var raw = (row.Url ?? string.Empty).Trim();
            if (raw.Length == 0)
                continue;
            var url = normalizeUrl(raw);
            if (!seen.Add(url))
                continue;
            var title = (row.Title ?? string.Empty).Trim();
            if (title.Length > 80)
                title = title[..80];
            next.Add((title, url));
        }
        return next;
    }

    private static void AttachUrlRows<T>(
        ICollection<T> existing, Guid groundId, List<(string Title, string Url)> next,
        Func<Guid, string, string, int, T> create)
        where T : class
    {
        foreach (var extra in existing.ToList())
            existing.Remove(extra);

        var sort = 0;
        foreach (var row in next)
            existing.Add(create(groundId, row.Title, row.Url, sort++));
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
    public async Task<ActionResult<BookingPage>> Mine(string? cursor, int take = 5, CancellationToken ct = default)
    {
        take = Paging.ClampTake(take);
        var userId = GroundsCaller.UserId(HttpContext);
        var query = _db.Bookings.AsNoTracking()
            .Include(b => b.Ground)
            .Include(b => b.Services)
            .Include(b => b.ShopItems)
            .Include(b => b.Review)
            .Where(b => !b.IsDeleted && b.CustomerUserId == userId);

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        if (GroundsMap.TryDecodeCursor(cursor, out var created, out var id))
        {
            query = query.Where(b =>
                b.CreatedAtUtc < created ||
                (b.CreatedAtUtc == created && string.Compare(b.Id.ToString(), id.ToString()) < 0));
        }

        var rows = await query
            .OrderByDescending(b => b.CreatedAtUtc)
            .ThenByDescending(b => b.Id)
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

        return Ok(new BookingPage(rows.Select(b => GroundsMap.ToBooking(b, b.Ground)).ToList(), next, hasMore, total));
    }

    [HttpGet("owner")]
    public async Task<ActionResult<BookingPage>> Owner(
        Guid? groundId, string? cursor, int take = 5,
        DateTime? from = null, DateTime? to = null,
        CancellationToken ct = default)
    {
        if (!GroundsCaller.IsGroundOwner(HttpContext) && !GroundsCaller.IsSuperAdmin(HttpContext))
            return Forbid();

        var ranged = from is DateTime start && to is DateTime end && end > start;
        take = Math.Clamp(take <= 0 ? Paging.DefaultTake : take, 1, ranged ? 500 : Paging.MaxTake);
        var ownerId = GroundsCaller.UserId(HttpContext);
        var super = GroundsCaller.IsSuperAdmin(HttpContext);
        var query = _db.Bookings.AsNoTracking()
            .Include(b => b.Ground)
            .Include(b => b.Services)
            .Include(b => b.ShopItems)
            .Include(b => b.Review)
            .Where(b => !b.IsDeleted && !b.Ground.IsDeleted);
        if (!super)
        {
            if (ownerId == Guid.Empty)
                return Ok(new BookingPage([], null, false, 0));
            query = query.Where(b => b.Ground.OwnerUserId == ownerId);
        }
        if (groundId is Guid gid && gid != Guid.Empty)
            query = query.Where(b => b.GroundId == gid);

        if (ranged)
            query = query.Where(b => b.StartAt < to && b.EndAt > from);

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        List<GroundBooking> rows;
        string? next = null;
        var hasMore = false;
        if (ranged)
        {
            rows = await query
                .OrderBy(b => b.StartAt)
                .ThenBy(b => b.Id)
                .Take(take)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            hasMore = rows.Count >= take && total > rows.Count;
        }
        else
        {
            if (GroundsMap.TryDecodeCursor(cursor, out var created, out var id))
            {
                query = query.Where(b =>
                    b.CreatedAtUtc < created ||
                    (b.CreatedAtUtc == created && string.Compare(b.Id.ToString(), id.ToString()) < 0));
            }

            rows = await query
                .OrderByDescending(b => b.CreatedAtUtc)
                .ThenByDescending(b => b.Id)
                .Take(take + 1)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            hasMore = rows.Count > take;
            if (hasMore)
                rows.RemoveAt(rows.Count - 1);
            if (hasMore && rows.Count > 0)
            {
                var last = rows[^1];
                next = GroundsMap.EncodeCursor(last.CreatedAtUtc, last.Id);
            }
        }

        return Ok(new BookingPage(rows.Select(b => GroundsMap.ToBooking(b, b.Ground)).ToList(), next, hasMore, total));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingDto>> Get(Guid id, CancellationToken ct)
    {
        var b = await _db.Bookings.AsNoTracking()
            .Include(x => x.Ground)
            .Include(x => x.Services)
            .Include(x => x.ShopItems)
            .Include(x => x.Review)
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
            .Include(x => x.ShopItems)
            .Include(x => x.Review)
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
            b.ApplyMoney(request.Discount, request.Advance);
            if (request.PaymentNote is not null)
                b.PaymentNote = Trim(request.PaymentNote, 400);
        }

        if (request.Notes is not null)
            b.Notes = Trim(request.Notes, 1000);

        b.UpdatedAtUtc = DateTime.Now;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(GroundsMap.ToBooking(b, b.Ground));
    }

    [HttpGet("{id:guid}/review")]
    public async Task<ActionResult<GroundReviewDto>> GetReview(Guid id, CancellationToken ct)
    {
        var booking = await LoadBookingForReviewAsync(id, tracking: false, ct).ConfigureAwait(false);
        if (booking is null)
            return NotFound();
        if (!CanSeeBooking(booking))
            return Forbid();
        if (booking.Review is null)
            return NotFound();
        booking.Review.Booking = booking;
        return Ok(GroundsMap.ToReview(booking.Review));
    }

    [HttpPost("{id:guid}/review")]
    public async Task<ActionResult<GroundReviewDto>> CreateReview(
        Guid id, [FromBody] GroundReviewUpsert request, CancellationToken ct)
    {
        if (GroundsCaller.IsGuest(HttpContext))
            return StatusCode(StatusCodes.Status403Forbidden, "Sign up to review a ground.");

        var booking = await LoadBookingForReviewAsync(id, tracking: true, ct).ConfigureAwait(false);
        if (booking is null)
            return NotFound("Booking not found.");

        var userId = GroundsCaller.UserId(HttpContext);
        var blocked = GroundsMap.ReviewBlockReason(booking, userId);
        if (blocked is not null)
            return BadRequest(blocked);
        if (booking.Review is not null)
            return Conflict("You already reviewed this booking. Only one review is allowed per completed booking.");
        if (!GroundsMap.TryNormalizeReview(request, out var rating, out var comment, out var error))
            return BadRequest(error);

        var review = new GroundReview
        {
            GroundId = booking.GroundId,
            BookingId = booking.Id,
            CustomerUserId = userId,
            CustomerName = string.IsNullOrWhiteSpace(booking.CustomerName)
                ? GroundsCaller.Username(HttpContext)
                : booking.CustomerName,
            Rating = rating,
            Comment = comment,
            CreatedAtUtc = DateTime.Now,
            UpdatedAtUtc = DateTime.Now
        };
        _db.Reviews.Add(review);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            return Conflict("You already reviewed this booking. Only one review is allowed per completed booking.");
        }

        review.Booking = booking;
        return Ok(GroundsMap.ToReview(review));
    }

    [HttpPut("{id:guid}/review")]
    public async Task<ActionResult<GroundReviewDto>> UpdateReview(
        Guid id, [FromBody] GroundReviewUpsert request, CancellationToken ct)
    {
        var booking = await LoadBookingForReviewAsync(id, tracking: true, ct).ConfigureAwait(false);
        if (booking is null)
            return NotFound("Booking not found.");

        var userId = GroundsCaller.UserId(HttpContext);
        var blocked = GroundsMap.ReviewBlockReason(booking, userId);
        if (blocked is not null)
            return BadRequest(blocked);
        if (booking.Review is null)
            return NotFound("No review yet for this booking.");
        if (!GroundsMap.TryNormalizeReview(request, out var rating, out var comment, out var error))
            return BadRequest(error);

        booking.Review.Rating = rating;
        booking.Review.Comment = comment;
        booking.Review.UpdatedAtUtc = DateTime.Now;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        booking.Review.Booking = booking;
        return Ok(GroundsMap.ToReview(booking.Review));
    }

    private async Task<GroundBooking?> LoadBookingForReviewAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = tracking
            ? _db.Bookings.AsQueryable()
            : _db.Bookings.AsNoTracking();
        return await query
            .Include(x => x.Ground)
            .Include(x => x.Review)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted && !x.Ground.IsDeleted, ct)
            .ConfigureAwait(false);
    }

    private bool CanSeeBooking(GroundBooking booking)
    {
        var userId = GroundsCaller.UserId(HttpContext);
        return booking.CustomerUserId == userId
               || GroundsCaller.CanManageGround(HttpContext, booking.Ground.OwnerUserId);
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
