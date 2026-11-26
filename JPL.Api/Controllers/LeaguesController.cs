using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Contracts;
using JPL.Api.Data;
using JPL.Api.Models;
using JPL.Api.Services;

namespace JPL.Api.Controllers;

[ApiController]
[Route("api/leagues")]
public sealed class LeaguesController : ControllerBase
{
    private const int MaxNameLength = 160;
    private const int MaxImageBytes = 2_000_000;

    private readonly IApiDbFactory _dbs;
    private readonly LeagueDatabaseManager _manager;
    private readonly ILogger<LeaguesController> _log;

    public LeaguesController(IApiDbFactory dbs, LeagueDatabaseManager manager, ILogger<LeaguesController> log)
    {
        _dbs = dbs;
        _manager = manager;
        _log = log;
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

    // ───────────────────────── SuperAdmin League management ─────────────────────────
    // League *records* only (name, image, active, delete). League sports data is untouched.

    /// <summary>All non-deleted leagues incl. inactive, with user counts. PlatformAdmin only.</summary>
    [HttpGet("manage/page")]
    public async Task<ActionResult<PagedResult<LeagueAdminDto>>> GetManagePage(
        int skip = 0, int take = 15, string? q = null, string? status = null, CancellationToken ct = default)
    {
        if (!CallerIsPlatformAdmin())
            return PlatformOnly();

        (skip, take) = Paging.Clamp(skip, take);
        await using var db = _dbs.CreateMaster();
        var all = await db.Leagues.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var activeCount = all.Count(l => l.IsActive);
        var inactiveCount = all.Count - activeCount;

        IEnumerable<League> rows = all;
        var like = q?.Trim();
        if (!string.IsNullOrEmpty(like))
            rows = rows.Where(l => l.Name.Contains(like, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            rows = rows.Where(l => l.IsActive);
        else if (string.Equals(status, "inactive", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(status, "deactive", StringComparison.OrdinalIgnoreCase))
            rows = rows.Where(l => !l.IsActive);

        var filtered = rows
            .OrderByDescending(l => l.IsActive)
            .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.CreatedAtUtc)
            .ToList();
        var total = filtered.Count;
        var page = filtered.Skip(skip).Take(take).ToList();

        var counts = await LoadRoleCountsAsync(db, page.Select(l => l.Id).ToList(), ct).ConfigureAwait(false);
        var live = await db.LeagueLiveIndexes.AsNoTracking()
            .ToDictionaryAsync(x => x.LeagueId, ct)
            .ConfigureAwait(false);

        return Ok(new PagedResult<LeagueAdminDto>(
            page.Select(l => ToAdminDto(l, counts, live.GetValueOrDefault(l.Id))).ToList(),
            total, skip, take, activeCount, inactiveCount));
    }

    /// <summary>One league for the edit form (incl. inactive). PlatformAdmin only.</summary>
    [HttpGet("manage/{id:guid}")]
    public async Task<ActionResult<LeagueAdminDto>> GetManage(Guid id, CancellationToken ct)
    {
        if (!CallerIsPlatformAdmin())
            return PlatformOnly();

        await using var db = _dbs.CreateMaster();
        var league = await db.Leagues.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (league is null)
            return NotFound("League not found.");
        return Ok(await AdminDtoAsync(db, league, ct).ConfigureAwait(false));
    }

    [HttpPost]
    public async Task<ActionResult<LeagueAdminDto>> Create([FromBody] LeagueUpsertRequest request, CancellationToken ct)
    {
        if (!CallerIsPlatformAdmin())
            return PlatformOnly();
        if (request is null)
            return BadRequest("League details are required.");
        if (!TryValidateName(request.Name, out var name, out var error))
            return BadRequest(error);
        if (!TryDecodeImage(request.ImageBase64, out var image, out error))
            return BadRequest(error);

        await using var db = _dbs.CreateMaster();
        var everyone = await db.Leagues.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var clash = everyone.FirstOrDefault(l => !l.IsDeleted && SameName(l.Name, name));
        if (clash is not null)
            return Conflict(clash.IsActive
                ? $"A league named \"{clash.Name}\" already exists."
                : $"An inactive league named \"{clash.Name}\" already exists. Activate or delete it instead.");

        var league = new League
        {
            Id = Guid.NewGuid(),
            Name = name,
            ImageBytes = image,
            IsActive = request.IsActive ?? true,
            CreatedAtUtc = DateTime.Now,
            UpdatedAtUtc = DateTime.Now
        };

        // Provision first (existing per-league provisioning path) so a failed CREATE DATABASE
        // never leaves a league row pointing nowhere. Name is guaranteed unused.
        try
        {
            var reserved = everyone.Select(l => l.DatabaseName)
                .Concat(everyone.Select(l => (string?)LeagueDatabaseManager.NameFor(l.Id, l.Name)));
            league.DatabaseName = await _manager.ProvisionNewAsync(league.Id, league.Name, reserved, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "League provisioning failed for {LeagueName}", name);
            return StatusCode(StatusCodes.Status500InternalServerError,
                "Could not create the league database: " + ex.Message);
        }

        db.Leagues.Add(league);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _log.LogInformation("SuperAdmin created league {LeagueId} {LeagueName} db={Db}", league.Id, league.Name, league.DatabaseName);
        return Ok(await AdminDtoAsync(db, league, ct).ConfigureAwait(false));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LeagueAdminDto>> Update(Guid id, [FromBody] LeagueUpsertRequest request, CancellationToken ct)
    {
        if (!CallerIsPlatformAdmin())
            return PlatformOnly();
        if (request is null)
            return BadRequest("League details are required.");
        if (!TryValidateName(request.Name, out var name, out var error))
            return BadRequest(error);
        if (!TryDecodeImage(request.ImageBase64, out var image, out error))
            return BadRequest(error);

        await using var db = _dbs.CreateMaster();
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (league is null)
            return NotFound("League not found.");

        var others = await db.Leagues.AsNoTracking().Where(l => l.Id != id).ToListAsync(ct).ConfigureAwait(false);
        var clash = others.FirstOrDefault(l => SameName(l.Name, name));
        if (clash is not null)
            return Conflict($"Another league named \"{clash.Name}\" already exists.");

        // Display name only: the league keeps its existing database (see EnsureLeagueCatalogAsync).
        league.Name = name;
        if (request.RemoveImage)
            league.ImageBytes = null;
        else if (image is not null)
            league.ImageBytes = image;

        if (request.IsActive is bool wantActive && wantActive != league.IsActive)
        {
            var failure = await ApplyActiveAsync(db, league, wantActive, ct).ConfigureAwait(false);
            if (failure is not null)
                return failure;
        }

        league.UpdatedAtUtc = DateTime.Now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(await AdminDtoAsync(db, league, ct).ConfigureAwait(false));
    }

    [HttpPost("{id:guid}/activate")]
    public Task<ActionResult<LeagueAdminDto>> Activate(Guid id, CancellationToken ct)
        => SetActiveAsync(id, true, ct);

    [HttpPost("{id:guid}/deactivate")]
    public Task<ActionResult<LeagueAdminDto>> Deactivate(Guid id, CancellationToken ct)
        => SetActiveAsync(id, false, ct);

    /// <summary>
    /// Soft delete only — the league database and all its data are kept (never dropped).
    /// Must be deactivated first so a live league cannot vanish by one mis-click.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!CallerIsPlatformAdmin())
            return PlatformOnly();

        await using var db = _dbs.CreateMaster();
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (league is null)
            return NotFound("League not found.");
        if (league.IsActive)
            return Conflict("Deactivate the league before deleting it.");

        league.IsDeleted = true;
        league.IsActive = false;
        league.DeletedAtUtc = DateTime.Now;
        league.UpdatedAtUtc = DateTime.Now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _log.LogInformation("SuperAdmin soft-deleted league {LeagueId} {LeagueName} (db {Db} kept)", league.Id, league.Name, league.DatabaseName);
        return NoContent();
    }

    private async Task<ActionResult<LeagueAdminDto>> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        if (!CallerIsPlatformAdmin())
            return PlatformOnly();

        await using var db = _dbs.CreateMaster();
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (league is null)
            return NotFound("League not found.");

        if (league.IsActive != active)
        {
            var failure = await ApplyActiveAsync(db, league, active, ct).ConfigureAwait(false);
            if (failure is not null)
                return failure;
            league.UpdatedAtUtc = DateTime.Now;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            _log.LogInformation("SuperAdmin set league {LeagueId} active={Active}", league.Id, active);
        }

        return Ok(await AdminDtoAsync(db, league, ct).ConfigureAwait(false));
    }

    /// <summary>Returns an error result, or null when the change was applied to <paramref name="league"/>.</summary>
    private async Task<ActionResult?> ApplyActiveAsync(MasterDbContext db, League league, bool active, CancellationToken ct)
    {
        if (!active)
        {
            league.IsActive = false;
            return null;
        }

        var clash = await db.Leagues.AsNoTracking()
            .Where(l => l.Id != league.Id && l.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var same = clash.FirstOrDefault(l => SameName(l.Name, league.Name));
        if (same is not null)
            return Conflict($"An active league named \"{same.Name}\" already exists. Rename this league first.");

        try
        {
            // Inactive leagues are skipped by the startup schema upgrade — bring it up to date now.
            league.DatabaseName = await _manager.EnsureLeagueReadyAsync(league, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "League database not ready for {LeagueId}", league.Id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                "League database could not be prepared: " + ex.Message);
        }

        league.IsActive = true;
        return null;
    }

    private async Task<LeagueAdminDto> AdminDtoAsync(MasterDbContext db, League league, CancellationToken ct)
    {
        var counts = await LoadRoleCountsAsync(db, new List<Guid> { league.Id }, ct).ConfigureAwait(false);
        var live = await db.LeagueLiveIndexes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LeagueId == league.Id, ct)
            .ConfigureAwait(false);
        return ToAdminDto(league, counts, live);
    }

    private static async Task<Dictionary<Guid, (int Admins, int Users)>> LoadRoleCountsAsync(
        MasterDbContext db, List<Guid> leagueIds, CancellationToken ct)
    {
        if (leagueIds.Count == 0)
            return new Dictionary<Guid, (int, int)>();
        var rows = await db.Users.AsNoTracking()
            .Where(u => u.LeagueId != null && leagueIds.Contains(u.LeagueId!.Value))
            .GroupBy(u => new { LeagueId = u.LeagueId!.Value, u.Role })
            .Select(g => new { g.Key.LeagueId, g.Key.Role, N = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows
            .GroupBy(r => r.LeagueId)
            .ToDictionary(
                g => g.Key,
                g => (g.Where(r => r.Role == AppRole.Admin).Sum(r => r.N),
                      g.Where(r => r.Role == AppRole.User).Sum(r => r.N)));
    }

    private static LeagueAdminDto ToAdminDto(
        League l, IReadOnlyDictionary<Guid, (int Admins, int Users)> counts, LeagueLiveIndex? live)
    {
        var c = counts.TryGetValue(l.Id, out var v) ? v : (0, 0);
        return new LeagueAdminDto(
            l.Id,
            l.Name,
            l.ImageBytes is { Length: > 0 } ? Convert.ToBase64String(l.ImageBytes) : null,
            l.IsActive,
            l.CreatedAtUtc,
            l.UpdatedAtUtc,
            c.Item1,
            c.Item2,
            live?.LiveMatchCount ?? 0,
            live?.LiveNetsCount ?? 0);
    }

    private static bool SameName(string? a, string? b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool TryValidateName(string? raw, out string name, out string error)
    {
        name = string.Join(' ', (raw ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        error = string.Empty;
        if (name.Length == 0)
        {
            error = "League name is required.";
            return false;
        }

        if (name.Length < 2)
        {
            error = "League name must be at least 2 characters.";
            return false;
        }

        if (name.Length > MaxNameLength)
        {
            error = $"League name must be {MaxNameLength} characters or fewer.";
            return false;
        }

        if (!name.Any(char.IsLetterOrDigit))
        {
            error = "League name must contain letters or numbers.";
            return false;
        }

        return true;
    }

    private static bool TryDecodeImage(string? base64, out byte[]? bytes, out string error)
    {
        bytes = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(base64))
            return true;
        try
        {
            bytes = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException)
        {
            error = "League image is not valid.";
            return false;
        }

        if (bytes.Length > MaxImageBytes)
        {
            error = "League image must be under 2 MB.";
            bytes = null;
            return false;
        }

        if (bytes.Length == 0)
            bytes = null;
        return true;
    }

    /// <summary>Explicit 403 (Forbid() needs an auth scheme; this API uses SessionAuthMiddleware).</summary>
    private ObjectResult PlatformOnly()
        => StatusCode(StatusCodes.Status403Forbidden, "Only the platform admin can manage leagues.");

    private bool CallerIsPlatformAdmin()
    {
        var role = HttpContext.Items.TryGetValue("JplRole", out var roleObj) && roleObj is string r && !string.IsNullOrWhiteSpace(r)
            ? r
            : User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;
        var name = HttpContext.Items.TryGetValue("JplUsername", out var nameObj) && nameObj is string n
            ? n
            : User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        return string.Equals(role, nameof(AppRole.SuperAdmin), StringComparison.OrdinalIgnoreCase)
               && PlatformAdmin.Is(name);
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
