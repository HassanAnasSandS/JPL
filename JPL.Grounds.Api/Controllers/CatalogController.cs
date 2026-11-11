using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JPL.Grounds.Api.Contracts;
using JPL.Grounds.Api.Data;
using JPL.Grounds.Api.Models;
using JPL.Grounds.Api.Services;

namespace JPL.Grounds.Api.Controllers;

[ApiController]
[Route("api/catalog")]
public sealed class CatalogController : ControllerBase, IAsyncDisposable
{
    private readonly GroundsDbContext _db;

    public CatalogController(IGroundsDbFactory dbs) => _db = dbs.CreateGrounds();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [HttpGet("amenities")]
    public async Task<ActionResult<IReadOnlyList<CatalogItemDto>>> Amenities(CancellationToken ct)
        => Ok(await _db.AmenityCatalog.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new CatalogItemDto(x.Id, x.Name, x.SortOrder))
            .ToListAsync(ct)
            .ConfigureAwait(false));

    [HttpGet("amenities/page")]
    public async Task<ActionResult<PagedResult<CatalogItemDto>>> AmenitiesPage(
        int skip = 0, int take = 15, string? q = null, CancellationToken ct = default)
    {
        skip = Paging.ClampSkip(skip);
        take = Paging.ClampTake(take);
        var query = _db.AmenityCatalog.AsNoTracking().AsQueryable();
        var like = Paging.LikePattern(q);
        if (like is not null)
            query = query.Where(x => EF.Functions.Like(x.Name, like));
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Skip(skip).Take(take)
            .Select(x => new CatalogItemDto(x.Id, x.Name, x.SortOrder))
            .ToListAsync(ct).ConfigureAwait(false);
        return Ok(new PagedResult<CatalogItemDto>(items, total, skip, take));
    }

    [HttpGet("services")]
    public async Task<ActionResult<IReadOnlyList<CatalogItemDto>>> Services(CancellationToken ct)
        => Ok(await _db.ServiceCatalog.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new CatalogItemDto(x.Id, x.Name, x.SortOrder))
            .ToListAsync(ct)
            .ConfigureAwait(false));

    [HttpGet("services/page")]
    public async Task<ActionResult<PagedResult<CatalogItemDto>>> ServicesPage(
        int skip = 0, int take = 15, string? q = null, CancellationToken ct = default)
    {
        skip = Paging.ClampSkip(skip);
        take = Paging.ClampTake(take);
        var query = _db.ServiceCatalog.AsNoTracking().AsQueryable();
        var like = Paging.LikePattern(q);
        if (like is not null)
            query = query.Where(x => EF.Functions.Like(x.Name, like));
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Skip(skip).Take(take)
            .Select(x => new CatalogItemDto(x.Id, x.Name, x.SortOrder))
            .ToListAsync(ct).ConfigureAwait(false);
        return Ok(new PagedResult<CatalogItemDto>(items, total, skip, take));
    }

    [HttpGet("shop")]
    public async Task<ActionResult<IReadOnlyList<ShopCatalogItemDto>>> Shop(CancellationToken ct)
        => Ok(await _db.ShopCatalog.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new ShopCatalogItemDto(x.Id, x.Name, x.Kind, x.Price, x.SortOrder))
            .ToListAsync(ct)
            .ConfigureAwait(false));

    [HttpGet("shop/page")]
    public async Task<ActionResult<PagedResult<ShopCatalogItemDto>>> ShopPage(
        int skip = 0, int take = 15, string? q = null, CancellationToken ct = default)
    {
        skip = Paging.ClampSkip(skip);
        take = Paging.ClampTake(take);
        var query = _db.ShopCatalog.AsNoTracking().AsQueryable();
        var like = Paging.LikePattern(q);
        if (like is not null)
            query = query.Where(x => EF.Functions.Like(x.Name, like));
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Skip(skip).Take(take)
            .Select(x => new ShopCatalogItemDto(x.Id, x.Name, x.Kind, x.Price, x.SortOrder))
            .ToListAsync(ct).ConfigureAwait(false);
        return Ok(new PagedResult<ShopCatalogItemDto>(items, total, skip, take));
    }
}

[ApiController]
[Route("api/platform/amenities")]
public sealed class AmenityCatalogController : ControllerBase, IAsyncDisposable
{
    private readonly GroundsDbContext _db;

    public AmenityCatalogController(IGroundsDbFactory dbs) => _db = dbs.CreateGrounds();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [HttpPost]
    public async Task<ActionResult<CatalogItemDto>> Create([FromBody] CatalogItemUpsert request, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();
        if (!CatalogNames.TryNormalize(request.Name, out var name, out var error))
            return BadRequest(error);
        if (await _db.AmenityCatalog.AnyAsync(x => x.Name == name, ct).ConfigureAwait(false))
            return BadRequest($"Amenity \"{name}\" already exists.");

        var max = await _db.AmenityCatalog.Select(x => (int?)x.SortOrder).MaxAsync(ct).ConfigureAwait(false) ?? -1;
        var row = new AmenityCatalog { Name = name, SortOrder = max + 1 };
        _db.AmenityCatalog.Add(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(new CatalogItemDto(row.Id, row.Name, row.SortOrder));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CatalogItemDto>> Update(Guid id, [FromBody] CatalogItemUpsert request, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();
        if (!CatalogNames.TryNormalize(request.Name, out var name, out var error))
            return BadRequest(error);

        var row = await _db.AmenityCatalog.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (row is null)
            return NotFound();
        if (await _db.AmenityCatalog.AnyAsync(x => x.Name == name && x.Id != id, ct).ConfigureAwait(false))
            return BadRequest($"Amenity \"{name}\" already exists.");

        row.Name = name;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(new CatalogItemDto(row.Id, row.Name, row.SortOrder));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();
        var row = await _db.AmenityCatalog.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (row is null)
            return NotFound();
        _db.AmenityCatalog.Remove(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private bool IsPlatformAdmin()
        => GroundsCaller.IsSuperAdmin(HttpContext) && PlatformAdmin.Is(GroundsCaller.Username(HttpContext));
}

[ApiController]
[Route("api/platform/services")]
public sealed class ServiceCatalogController : ControllerBase, IAsyncDisposable
{
    private readonly GroundsDbContext _db;

    public ServiceCatalogController(IGroundsDbFactory dbs) => _db = dbs.CreateGrounds();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [HttpPost]
    public async Task<ActionResult<CatalogItemDto>> Create([FromBody] CatalogItemUpsert request, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();
        if (!CatalogNames.TryNormalize(request.Name, out var name, out var error))
            return BadRequest(error);
        if (await _db.ServiceCatalog.AnyAsync(x => x.Name == name, ct).ConfigureAwait(false))
            return BadRequest($"Service \"{name}\" already exists.");

        var max = await _db.ServiceCatalog.Select(x => (int?)x.SortOrder).MaxAsync(ct).ConfigureAwait(false) ?? -1;
        var row = new ServiceCatalog { Name = name, SortOrder = max + 1 };
        _db.ServiceCatalog.Add(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(new CatalogItemDto(row.Id, row.Name, row.SortOrder));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CatalogItemDto>> Update(Guid id, [FromBody] CatalogItemUpsert request, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();
        if (!CatalogNames.TryNormalize(request.Name, out var name, out var error))
            return BadRequest(error);

        var row = await _db.ServiceCatalog.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (row is null)
            return NotFound();
        if (await _db.ServiceCatalog.AnyAsync(x => x.Name == name && x.Id != id, ct).ConfigureAwait(false))
            return BadRequest($"Service \"{name}\" already exists.");

        row.Name = name;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(new CatalogItemDto(row.Id, row.Name, row.SortOrder));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();
        var row = await _db.ServiceCatalog.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (row is null)
            return NotFound();
        _db.ServiceCatalog.Remove(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private bool IsPlatformAdmin()
        => GroundsCaller.IsSuperAdmin(HttpContext) && PlatformAdmin.Is(GroundsCaller.Username(HttpContext));
}

[ApiController]
[Route("api/platform/shop")]
public sealed class ShopCatalogController : ControllerBase, IAsyncDisposable
{
    private readonly GroundsDbContext _db;

    public ShopCatalogController(IGroundsDbFactory dbs) => _db = dbs.CreateGrounds();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [HttpPost]
    public async Task<ActionResult<ShopCatalogItemDto>> Create([FromBody] ShopCatalogItemUpsert request, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();
        if (!CatalogNames.TryNormalize(request.Name, out var name, out var error))
            return BadRequest(error);
        if (await _db.ShopCatalog.AnyAsync(x => x.Name == name, ct).ConfigureAwait(false))
            return BadRequest($"Shop item \"{name}\" already exists.");

        var max = await _db.ShopCatalog.Select(x => (int?)x.SortOrder).MaxAsync(ct).ConfigureAwait(false) ?? -1;
        var row = new ShopCatalog { Name = name, SortOrder = max + 1 };
        _db.ShopCatalog.Add(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(new ShopCatalogItemDto(row.Id, row.Name, row.Kind, row.Price, row.SortOrder));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ShopCatalogItemDto>> Update(Guid id, [FromBody] ShopCatalogItemUpsert request, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();
        if (!CatalogNames.TryNormalize(request.Name, out var name, out var error))
            return BadRequest(error);

        var row = await _db.ShopCatalog.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (row is null)
            return NotFound();
        if (await _db.ShopCatalog.AnyAsync(x => x.Name == name && x.Id != id, ct).ConfigureAwait(false))
            return BadRequest($"Shop item \"{name}\" already exists.");

        row.Name = name;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(new ShopCatalogItemDto(row.Id, row.Name, row.Kind, row.Price, row.SortOrder));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();
        var row = await _db.ShopCatalog.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (row is null)
            return NotFound();
        _db.ShopCatalog.Remove(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private bool IsPlatformAdmin()
        => GroundsCaller.IsSuperAdmin(HttpContext) && PlatformAdmin.Is(GroundsCaller.Username(HttpContext));
}

internal static class CatalogNames
{
    public static bool TryNormalize(string? raw, out string name, out string error)
    {
        name = (raw ?? string.Empty).Trim();
        error = string.Empty;
        if (name.Length == 0)
        {
            error = "Name is required.";
            return false;
        }
        if (name.Length > 40)
        {
            error = "Name must be 40 characters or fewer.";
            return false;
        }
        return true;
    }
}
