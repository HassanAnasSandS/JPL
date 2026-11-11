using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Services;

namespace JPL.Api.Controllers;

[ApiController]
public sealed class PublicIdentityController : ControllerBase
{
    private readonly IApiDbFactory _dbs;
    private readonly IWebHostEnvironment _env;
    private readonly PlayerImageStore _images;

    public PublicIdentityController(IApiDbFactory dbs, IWebHostEnvironment env, PlayerImageStore images)
    {
        _dbs = dbs;
        _env = env;
        _images = images;
    }

    [HttpGet("/p/{leagueId:guid}")]
    public async Task<IActionResult> DirectoryPage(Guid leagueId, CancellationToken ct)
    {
        var leagueName = await LeagueNameAsync(leagueId, ct).ConfigureAwait(false);
        if (leagueName is null)
            return NotFound("League not found.");
        var path = Path.Combine(_env.WebRootPath, "player-directory.html");
        if (!System.IO.File.Exists(path))
            return NotFound("Directory page is missing.");
        var html = await System.IO.File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        html = html
            .Replace("{{LEAGUE_ID}}", leagueId.ToString("D"), StringComparison.Ordinal)
            .Replace("{{LEAGUE_NAME}}", System.Net.WebUtility.HtmlEncode(leagueName), StringComparison.Ordinal);
        Response.Headers.CacheControl = "no-store";
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpGet("/p/{leagueId:guid}/{playerId:guid}")]
    public async Task<IActionResult> ProfilePage(Guid leagueId, Guid playerId, CancellationToken ct)
    {
        var leagueName = await LeagueNameAsync(leagueId, ct).ConfigureAwait(false);
        if (leagueName is null)
            return NotFound("League not found.");
        await using var db = _dbs.CreateForLeague(leagueId);
        var name = await db.Players.AsNoTracking()
            .Where(p => p.Id == playerId && !p.IsDeleted)
            .Select(p => p.FullName)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(name))
            return NotFound("Player not found.");

        var path = Path.Combine(_env.WebRootPath, "player-profile.html");
        if (!System.IO.File.Exists(path))
            return NotFound("Profile page is missing.");
        var html = await System.IO.File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var photoAbs = $"{Request.Scheme}://{Request.Host}/api/public/leagues/{leagueId:D}/players/{playerId:D}/image";
        html = html
            .Replace("{{LEAGUE_ID}}", leagueId.ToString("D"), StringComparison.Ordinal)
            .Replace("{{PLAYER_ID}}", playerId.ToString("D"), StringComparison.Ordinal)
            .Replace("{{LEAGUE_NAME}}", System.Net.WebUtility.HtmlEncode(leagueName), StringComparison.Ordinal)
            .Replace("{{PLAYER_NAME}}", System.Net.WebUtility.HtmlEncode(name), StringComparison.Ordinal)
            .Replace("{{PHOTO_URL}}", photoAbs, StringComparison.Ordinal);
        Response.Headers.CacheControl = "no-store";
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpGet("/api/public/leagues/{leagueId:guid}/players")]
    public async Task<ActionResult<PublicPlayerDirectory>> Directory(Guid leagueId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var leagueName = await LeagueNameAsync(leagueId, ct).ConfigureAwait(false);
        if (leagueName is null)
            return NotFound();
        await using var db = _dbs.CreateForLeague(leagueId);
        var dir = await PublicPlayerIdentity.DirectoryAsync(db, leagueId, leagueName, ct).ConfigureAwait(false);
        return Ok(dir);
    }

    [HttpGet("/api/public/leagues/{leagueId:guid}/players/{playerId:guid}")]
    public async Task<ActionResult<PublicPlayerProfile>> Profile(Guid leagueId, Guid playerId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var leagueName = await LeagueNameAsync(leagueId, ct).ConfigureAwait(false);
        if (leagueName is null)
            return NotFound();
        await using var db = _dbs.CreateForLeague(leagueId);
        var profile = await PublicPlayerIdentity.ProfileAsync(db, leagueId, leagueName, playerId, ct)
            .ConfigureAwait(false);
        if (profile is null)
            return NotFound();
        return Ok(profile);
    }

    [HttpGet("/api/public/leagues/{leagueId:guid}/players/{playerId:guid}/image")]
    public async Task<IActionResult> Image(Guid leagueId, Guid playerId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=300";
        await using var master = _dbs.CreateMaster();
        var leagueOk = await master.Leagues.AsNoTracking()
            .AnyAsync(l => l.Id == leagueId && l.IsActive, ct)
            .ConfigureAwait(false);
        if (!leagueOk)
            return NotFound();

        await using var db = _dbs.CreateForLeague(leagueId);
        var path = await db.Players.AsNoTracking()
            .Where(p => p.Id == playerId && !p.IsDeleted)
            .Select(p => p.ImagePath)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(path))
            return NotFound();
        if (!_images.TryGetFullPath(leagueId, path, out var fullPath, out var contentType))
            return NotFound();
        return PhysicalFile(fullPath, contentType);
    }

    private async Task<string?> LeagueNameAsync(Guid leagueId, CancellationToken ct)
    {
        await using var master = _dbs.CreateMaster();
        return await master.Leagues.AsNoTracking()
            .Where(l => l.Id == leagueId && l.IsActive)
            .Select(l => l.Name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }
}
