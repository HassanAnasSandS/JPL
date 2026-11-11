using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Contracts;
using JPL.Api.Models;
using JPL.Api.Services;

namespace JPL.Api.Controllers;

[ApiController]
[Route("api/leagues")]
public sealed class LeaguesController : ControllerBase
{
    private readonly IApiDbFactory _dbs;
    private readonly LeagueDatabaseManager _databases;

    public LeaguesController(IApiDbFactory dbs, LeagueDatabaseManager databases)
    {
        _dbs = dbs;
        _databases = databases;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LeagueDto>>> GetAll(CancellationToken ct)
    {
        await using var db = _dbs.CreateMaster();
        var list = await db.Leagues.AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var userCounts = await LeagueNameDeduper.LoadUserCountsAsync(db, ct).ConfigureAwait(false);
        list = LeagueNameDeduper.CollapseActiveByName(list, userCounts).ToList();
        var live = await db.LeagueLiveIndexes.AsNoTracking()
            .ToDictionaryAsync(x => x.LeagueId, ct)
            .ConfigureAwait(false);
        return Ok(list.Select(l => ToDto(l, live.GetValueOrDefault(l.Id))).ToList());
    }

    [HttpGet("page")]
    public async Task<ActionResult<PagedResult<LeagueDto>>> GetPage(
        int skip = 0, int take = 15, string? q = null, CancellationToken ct = default)
    {
        (skip, take) = Paging.Clamp(skip, take);
        await using var db = _dbs.CreateMaster();
        var list = await db.Leagues.AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var userCounts = await LeagueNameDeduper.LoadUserCountsAsync(db, ct).ConfigureAwait(false);
        list = LeagueNameDeduper.CollapseActiveByName(list, userCounts).ToList();
        var like = q?.Trim();
        if (!string.IsNullOrEmpty(like))
            list = list.Where(l => l.Name.Contains(like, StringComparison.OrdinalIgnoreCase)).ToList();
        var total = list.Count;
        list = list.Skip(skip).Take(take).ToList();
        var live = await db.LeagueLiveIndexes.AsNoTracking()
            .ToDictionaryAsync(x => x.LeagueId, ct)
            .ConfigureAwait(false);
        return Ok(new PagedResult<LeagueDto>(
            list.Select(l => ToDto(l, live.GetValueOrDefault(l.Id))).ToList(), total, skip, take));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LeagueDto>> Get(Guid id, CancellationToken ct)
    {
        await using var db = _dbs.CreateMaster();
        var league = await db.Leagues.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, ct)
            .ConfigureAwait(false);
        if (league is null)
            return NotFound();
        var live = await db.LeagueLiveIndexes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LeagueId == id, ct)
            .ConfigureAwait(false);
        return Ok(ToDto(league, live));
    }

    [HttpPost]
    public async Task<ActionResult<LeagueDto>> Create([FromBody] LeagueUpsertRequest request, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();
        if (!TryValidate(request, out var name, out var image, out var error))
            return BadRequest(error);

        await using var check = _dbs.CreateMaster();
        if (await LeagueNameDeduper.NameTakenAsync(check, name, exceptId: null, ct).ConfigureAwait(false))
            return BadRequest("A league with this name already exists.");

        var league = new League
        {
            Id = Guid.NewGuid(),
            Name = name,
            ImageBytes = image,
            IsActive = true,
            CreatedAtUtc = DateTime.Now
        };

        string databaseName;
        try
        {
            databaseName = await _databases.ProvisionAsync(league.Id, league.Name, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }

        league.DatabaseName = databaseName;

        await using var db = _dbs.CreateMaster();
        db.Leagues.Add(league);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(league, live: null));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LeagueDto>> Update(Guid id, [FromBody] LeagueUpsertRequest request, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();
        if (!TryValidate(request, out var name, out var image, out var error))
            return BadRequest(error);

        await using var db = _dbs.CreateMaster();
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (league is null)
            return NotFound();

        if (await LeagueNameDeduper.NameTakenAsync(db, name, exceptId: id, ct).ConfigureAwait(false))
            return BadRequest("A league with this name already exists.");

        league.Name = name;
        if (image is { Length: > 0 })
            league.ImageBytes = image;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(league, live: null));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();

        await using var db = _dbs.CreateMaster();
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (league is null)
            return NotFound();

        var remaining = await db.Leagues.CountAsync(l => l.IsActive && l.Id != id, ct).ConfigureAwait(false);
        if (remaining == 0)
            return BadRequest("Cannot delete the last league.");

        var hasUsers = await db.Users.AnyAsync(u => u.LeagueId == id, ct).ConfigureAwait(false);
        if (hasUsers)
            return BadRequest("Cannot delete this league because users are assigned to it.");

        league.IsActive = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private bool IsPlatformAdmin()
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        var name = User.FindFirstValue(ClaimTypes.Name);
        return string.Equals(role, nameof(AppRole.SuperAdmin), StringComparison.OrdinalIgnoreCase)
               && PlatformAdmin.Is(name);
    }

    private static bool TryValidate(LeagueUpsertRequest request, out string name, out byte[]? image, out string error)
    {
        name = request.Name?.Trim() ?? string.Empty;
        image = null;
        error = string.Empty;
        if (name.Length == 0)
        {
            error = "League name is required.";
            return false;
        }

        if (name.Length > 160)
        {
            error = "League name must be 160 characters or fewer.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            try
            {
                image = Convert.FromBase64String(request.ImageBase64);
            }
            catch
            {
                error = "Image is not valid.";
                return false;
            }

            if (image.Length > 2_000_000)
            {
                error = "Image must be under 2 MB.";
                return false;
            }
        }

        return true;
    }

    private static LeagueDto ToDto(League l, LeagueLiveIndex? live)
        => new(
            l.Id,
            l.Name,
            l.ImageBytes is { Length: > 0 } ? Convert.ToBase64String(l.ImageBytes) : null,
            l.IsActive,
            l.CreatedAtUtc,
            live?.LiveMatchCount ?? 0,
            live?.LiveNetsCount ?? 0);
}
