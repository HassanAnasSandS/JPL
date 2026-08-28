using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JPL.Grounds.Api.Data;
using JPL.Grounds.Api.Models;
using JPL.Grounds.Api.Services;

namespace JPL.Grounds.Api.Controllers;

public sealed record GroundOwnerUserDto(
    Guid Id,
    string Username,
    string Role,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid? LeagueId = null,
    string? LeagueName = null,
    string? WhatsAppNumber = null);

public sealed record GroundOwnerUpsertRequest(
    string Username,
    string? Password,
    bool IsActive = true,
    string? WhatsAppNumber = null);

/// <summary>
/// GroundOwner accounts live in Master Users (same table as JPL.Api).
/// Create/update here so live JPL.Api can stay on the old role list until it is republished.
/// </summary>
[ApiController]
[Route("api/platform/ground-owners")]
public sealed class GroundOwnersController : ControllerBase, IAsyncDisposable
{
    private readonly MasterUsersDbContext _db;
    private readonly PasswordHasher<MasterUser> _hasher = new();

    public GroundOwnersController(IGroundsDbFactory dbs) => _db = dbs.CreateMasterUsers();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [HttpPost]
    public async Task<ActionResult<GroundOwnerUserDto>> Create([FromBody] GroundOwnerUpsertRequest request, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();
        if (!TryValidate(request, requirePassword: true, out var username, out var error))
            return BadRequest(error);

        if (await _db.Users.AnyAsync(u => u.Username == username, ct).ConfigureAwait(false))
            return BadRequest($"Username \"{username}\" already exists.");
        if (!PakistanWhatsApp.TryNormalize(request.WhatsAppNumber, out var whatsApp))
            return BadRequest(PakistanWhatsApp.RequiredError);

        var user = new MasterUser
        {
            Username = username,
            Role = AppRole.GroundOwner,
            LeagueId = null,
            WhatsAppNumber = whatsApp,
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.Now
        };
        user.PasswordHash = _hasher.HashPassword(user, request.Password!.Trim());
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(user));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GroundOwnerUserDto>> Update(Guid id, [FromBody] GroundOwnerUpsertRequest request, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();
        if (!TryValidate(request, requirePassword: false, out var username, out var error))
            return BadRequest(error);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct).ConfigureAwait(false);
        if (user is null || user.Role == AppRole.SuperAdmin)
            return NotFound();

        if (await _db.Users.AnyAsync(u => u.Username == username && u.Id != id, ct).ConfigureAwait(false))
            return BadRequest($"Username \"{username}\" already exists.");
        if (!string.IsNullOrWhiteSpace(request.WhatsAppNumber))
        {
            if (!PakistanWhatsApp.TryNormalize(request.WhatsAppNumber, out var whatsApp))
                return BadRequest(PakistanWhatsApp.InvalidError);
            user.WhatsAppNumber = whatsApp;
        }

        user.Username = username;
        user.Role = AppRole.GroundOwner;
        user.LeagueId = null;
        user.IsActive = request.IsActive;
        if (!string.IsNullOrWhiteSpace(request.Password))
            user.PasswordHash = _hasher.HashPassword(user, request.Password.Trim());

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(user));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!IsPlatformAdmin())
            return Forbid();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct).ConfigureAwait(false);
        if (user is null || user.Role == AppRole.SuperAdmin)
            return NotFound();
        if (user.Role != AppRole.GroundOwner)
            return BadRequest("This endpoint only deletes GroundOwner accounts.");

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private bool IsPlatformAdmin()
        => GroundsCaller.IsSuperAdmin(HttpContext) && PlatformAdmin.Is(GroundsCaller.Username(HttpContext));

    private static bool TryValidate(GroundOwnerUpsertRequest request, bool requirePassword, out string username, out string error)
    {
        username = request.Username?.Trim() ?? string.Empty;
        error = string.Empty;
        if (username.Length == 0)
        {
            error = "Username is required.";
            return false;
        }
        if (username.Length > 80)
        {
            error = "Username must be 80 characters or fewer.";
            return false;
        }
        if (requirePassword && string.IsNullOrWhiteSpace(request.Password))
        {
            error = "Password is required.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(request.Password) && request.Password.Trim().Length < 4)
        {
            error = "Password must be at least 4 characters.";
            return false;
        }
        return true;
    }

    private static GroundOwnerUserDto ToDto(MasterUser u)
        => new(u.Id, u.Username, u.Role.ToString(), u.IsActive, u.CreatedAtUtc, u.LeagueId, null, u.WhatsAppNumber);
}
