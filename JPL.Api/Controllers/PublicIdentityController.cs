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

    [HttpGet("/p/{leagueId:guid}/payments")]
    public async Task<IActionResult> PaymentsPage(Guid leagueId, CancellationToken ct)
    {
        var leagueName = await LeagueNameAsync(leagueId, ct).ConfigureAwait(false);
        if (leagueName is null)
            return NotFound("League not found.");
        var path = Path.Combine(_env.WebRootPath, "player-payments.html");
        if (!System.IO.File.Exists(path))
            return NotFound("Player payments page is missing.");
        var html = await System.IO.File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        html = html
            .Replace("{{LEAGUE_ID}}", leagueId.ToString("D"), StringComparison.Ordinal)
            .Replace("{{LEAGUE_NAME}}", System.Net.WebUtility.HtmlEncode(leagueName), StringComparison.Ordinal);
        Response.Headers.CacheControl = "no-store";
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpGet("/p/{leagueId:guid}/expenses")]
    public async Task<IActionResult> ExpensesPage(Guid leagueId, CancellationToken ct)
    {
        var leagueName = await LeagueNameAsync(leagueId, ct).ConfigureAwait(false);
        if (leagueName is null) return NotFound("League not found.");
        var path = Path.Combine(_env.WebRootPath, "player-expenses.html");
        if (!System.IO.File.Exists(path)) return NotFound("Expenses page is missing.");
        var html = await System.IO.File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        html = html.Replace("{{LEAGUE_ID}}", leagueId.ToString("D"), StringComparison.Ordinal)
            .Replace("{{LEAGUE_NAME}}", System.Net.WebUtility.HtmlEncode(leagueName), StringComparison.Ordinal);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
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

    [HttpGet("/api/public/leagues/{leagueId:guid}/payments")]
    public async Task<ActionResult<PublicPlayerPayments>> Payments(Guid leagueId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var leagueName = await LeagueNameAsync(leagueId, ct).ConfigureAwait(false);
        if (leagueName is null)
            return NotFound();

        await using var db = _dbs.CreateForLeague(leagueId);
        var shares = await db.VenueBookingParticipants.AsNoTracking()
            .Where(p => !p.VenueBooking.IsDeleted)
            .Select(p => new
            {
                p.PlayerId, p.Amount, p.Status, p.VenueBookingId,
                p.VenueBooking.BookingDateUtc, p.VenueBooking.VenueId, p.VenueBooking.BookedByPlayerId
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var paymentSums = shares.GroupBy(p => p.PlayerId)
            .Select(g => new
            {
                PlayerId = g.Key,
                Total = g.Sum(p => p.Amount),
                Paid = g.Where(p => string.Equals(p.Status, JPL.Api.Models.VenueBookingPaymentStatus.Paid, StringComparison.OrdinalIgnoreCase))
                    .Sum(p => p.Amount)
            }).ToArray();

        var ids = paymentSums.Select(x => x.PlayerId).ToArray();
        var referencedPlayerIds = shares.Select(x => x.BookedByPlayerId).Concat(ids).Distinct().ToArray();
        var names = await db.Players.AsNoTracking()
            .Where(p => !p.IsDeleted && referencedPlayerIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName })
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
            .ConfigureAwait(false);
        var venueIds = shares.Select(x => x.VenueId).Distinct().ToArray();
        var venues = venueIds.Length == 0 ? new Dictionary<Guid, string>() : await db.Venues.AsNoTracking()
            .Where(v => !v.IsDeleted && venueIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Name, ct).ConfigureAwait(false);

        var players = paymentSums
            .Where(x => names.ContainsKey(x.PlayerId))
            .Select(x =>
            {
                var total = Math.Max(0, x.Total);
                var paid = Math.Min(total, Math.Max(0, x.Paid));
                var history = shares.Where(s => s.PlayerId == x.PlayerId)
                    .OrderByDescending(s => s.BookingDateUtc)
                    .Select(s => new PublicPlayerPaymentTransaction(
                        s.VenueBookingId,
                        s.BookingDateUtc,
                        venues.GetValueOrDefault(s.VenueId, "Venue booking"),
                        names.GetValueOrDefault(s.BookedByPlayerId, "Unknown player"),
                        Math.Max(0, s.Amount),
                        string.Equals(s.Status, JPL.Api.Models.VenueBookingPaymentStatus.Paid, StringComparison.OrdinalIgnoreCase)
                            ? JPL.Api.Models.VenueBookingPaymentStatus.Paid : JPL.Api.Models.VenueBookingPaymentStatus.Pending))
                    .ToArray();
                return new PublicPlayerPaymentRow(x.PlayerId, names[x.PlayerId], total, paid, total - paid, history);
            })
            .OrderByDescending(x => x.Remaining)
            .ThenByDescending(x => x.Total)
            .ThenBy(x => x.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(new PublicPlayerPayments(leagueId, leagueName, players));
    }

    [HttpGet("/api/public/leagues/{leagueId:guid}/expenses")]
    public async Task<IActionResult> Expenses(Guid leagueId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var leagueName = await LeagueNameAsync(leagueId, ct).ConfigureAwait(false);
        if (leagueName is null) return NotFound();
        await using var db = _dbs.CreateForLeague(leagueId);
        var rows = await db.Expenses.AsNoTracking().Where(x => !x.IsDeleted)
            .Include(x => x.Contributions).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct).ConfigureAwait(false);
        var playerIds = rows.SelectMany(x => x.Contributions.Select(c => c.PlayerId).Append(x.PurchasedByPlayerId)).Distinct().ToArray();
        var names = playerIds.Length == 0 ? new Dictionary<Guid, string>() : await db.Players.AsNoTracking()
            .Where(p => !p.IsDeleted && playerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct).ConfigureAwait(false);
        var expenses = rows.Select(x => new
        {
            x.Id, x.Name,
            x.Amount,
            PurchasedByPlayerName = names.GetValueOrDefault(x.PurchasedByPlayerId, "Unknown player"),
            ContributedAmount = x.Contributions
                .Where(c => string.Equals(c.Status, JPL.Api.Models.ExpenseContributionStatus.Paid, StringComparison.OrdinalIgnoreCase))
                .Sum(c => c.Amount),
            OutstandingAmount = Math.Max(0, x.Amount - x.Contributions
                .Where(c => string.Equals(c.Status, JPL.Api.Models.ExpenseContributionStatus.Paid, StringComparison.OrdinalIgnoreCase))
                .Sum(c => c.Amount)),
            x.CreatedAtUtc,
            Contributions = x.Contributions.Select(c => new
            {
                c.PlayerId, PlayerName = names.GetValueOrDefault(c.PlayerId, "Unknown player"), c.Amount,
                Status = JPL.Api.Models.ExpenseContributionStatus.Normalize(c.Status)
            }).OrderByDescending(c => c.Amount).ThenBy(c => c.PlayerName).ToArray()
        }).ToArray();
        return Ok(new { leagueId, leagueName, expenses });
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

public sealed record PublicPlayerPaymentRow(
    Guid PlayerId,
    string PlayerName,
    decimal Total,
    decimal Paid,
    decimal Remaining,
    IReadOnlyList<PublicPlayerPaymentTransaction> Transactions);

public sealed record PublicPlayerPaymentTransaction(
    Guid BookingId,
    DateTime Date,
    string Purpose,
    string BookedBy,
    decimal Amount,
    string Status);

public sealed record PublicPlayerPayments(
    Guid LeagueId,
    string LeagueName,
    IReadOnlyList<PublicPlayerPaymentRow> Players);
