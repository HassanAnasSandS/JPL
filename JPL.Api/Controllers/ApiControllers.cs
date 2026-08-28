using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Contracts;
using JPL.Api.Data;
using JPL.Api.Models;
using JPL.Api.Services;

namespace JPL.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        service = "JPL.Api",
        version = "2.0",
        time = DateTime.Now
    });
}

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase, IAsyncDisposable
{
    private readonly MasterDbContext _db;
    private readonly PasswordHasher<AppUser> _hasher = new();

    public AuthController(IApiDbFactory dbs) => _db = dbs.CreateMaster();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        if (username.Length == 0 || string.IsNullOrEmpty(request.Password))
            return Unauthorized(new { code = "missing_credentials", message = "Enter your username and password." });

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, ct)
            .ConfigureAwait(false);

        if (user is null || !user.IsActive)
            return Unauthorized(new { code = "user_not_found", message = "This user does not exist." });

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            return Unauthorized(new { code = "wrong_password", message = "Password is not correct." });

        // Lightweight opaque token for mobile clients (persist on device until logout).
        // Format: {opaque}.{userId:N} — middleware re-validates user IsActive on every API call.
        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + "." + user.Id.ToString("N");
        var needsPicker = user.Role is not AppRole.GroundOwner
                          and not AppRole.PublicUser
                          and not AppRole.Guest
                          && user.LeagueId is null;
        return Ok(new LoginResponse(user.Id, user.Username, user.Role.ToString(), token, user.LeagueId, needsPicker, user.WhatsAppNumber));
    }

    [HttpPost("signup")]
    public async Task<ActionResult<LoginResponse>> Signup([FromBody] SignupRequest request, CancellationToken ct)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        if (username.Length == 0)
            return BadRequest("Name is required.");
        if (username.Length > 64)
            return BadRequest("Name must be 64 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Trim().Length < 4)
            return BadRequest("Password must be at least 4 characters.");
        var whatsApp = request.WhatsApp;
        if (!PakistanWhatsApp.TryNormalize(whatsApp, out var normalizedWhatsApp))
            return BadRequest(PakistanWhatsApp.RequiredError);

        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Username == username, ct).ConfigureAwait(false))
            return BadRequest($"Username \"{username}\" already exists.");
        if (string.Equals(username, "user", StringComparison.OrdinalIgnoreCase))
            return BadRequest("That name is reserved.");

        var user = new AppUser
        {
            Username = username,
            Role = AppRole.PublicUser,
            LeagueId = null,
            WhatsAppNumber = normalizedWhatsApp,
            IsActive = true,
            CreatedAtUtc = DateTime.Now
        };
        user.PasswordHash = _hasher.HashPassword(user, request.Password.Trim());
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + "." + user.Id.ToString("N");
        return Ok(new LoginResponse(user.Id, user.Username, user.Role.ToString(), token, null, false, user.WhatsAppNumber));
    }

    /// <summary>Current session user (requires Bearer token; inactive/deleted → 401 from middleware).</summary>
    [HttpGet("me")]
    public ActionResult<LoginResponse> Me()
    {
        var idText = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var username = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var leagueText = User.FindFirst("league_id")?.Value;
        if (!Guid.TryParse(idText, out var userId) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(role))
            return Unauthorized();

        Guid? leagueId = Guid.TryParse(leagueText, out var lid) && lid != Guid.Empty ? lid : null;
        var whatsApp = User.FindFirst("whatsapp")?.Value
                       ?? HttpContext.Items["JplWhatsApp"] as string;
        var needsPicker = !string.Equals(role, nameof(AppRole.GroundOwner), StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(role, nameof(AppRole.PublicUser), StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(role, nameof(AppRole.Guest), StringComparison.OrdinalIgnoreCase)
                          && leagueId is null;
        return Ok(new LoginResponse(userId, username, role, string.Empty, leagueId, NeedsLeaguePicker: needsPicker, WhatsAppNumber: whatsApp));
    }
}

[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase, IAsyncDisposable
{
    private readonly IApiDbFactory _dbs;
    private readonly MasterDbContext _db;
    private readonly PasswordHasher<AppUser> _hasher = new();

    public UsersController(IApiDbFactory dbs)
    {
        _dbs = dbs;
        _db = dbs.CreateMaster();
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(CancellationToken ct)
    {
        if (!CallerIsPlatformAdmin())
            return Forbid();

        var leagueNames = await _db.Leagues.AsNoTracking()
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct)
            .ConfigureAwait(false);

        var list = await _db.Users.AsNoTracking()
            .Where(u => u.Role != AppRole.SuperAdmin)
            .OrderByDescending(u => u.Role)
            .ThenBy(u => u.Username)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Ok(list.Select(u => ToDto(u, leagueNames)).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken ct)
    {
        if (!CallerIsPlatformAdmin())
            return Forbid();

        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (u is null || u.Role == AppRole.SuperAdmin)
            return NotFound();

        var leagueName = await LeagueNameAsync(u.LeagueId, ct).ConfigureAwait(false);
        return Ok(ToDto(u, leagueName));
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] UserUpsertRequest request, CancellationToken ct)
    {
        if (!CallerIsPlatformAdmin())
            return Forbid();
        if (!TryValidateUser(request, requirePassword: true, out var username, out var role, out var error))
            return BadRequest(error);
        if (role == AppRole.PublicUser)
            return BadRequest("Public users sign up from the login screen.");
        if (role == AppRole.Guest)
            return BadRequest("Guest is a built-in system account.");

        if (await UsernameTakenAsync(username, null, ct).ConfigureAwait(false))
            return BadRequest($"Username \"{username}\" already exists.");

        Guid? leagueId = null;
        if (role == AppRole.PublicUser)
            leagueId = null;
        else if (role != AppRole.GroundOwner)
        {
            leagueId = await RequireLeagueAsync(request.LeagueId, ct).ConfigureAwait(false);
            if (leagueId is null)
                return BadRequest("Select a league for this user.");
        }

        if (!PakistanWhatsApp.TryNormalizeOptional(request.WhatsAppNumber, out var whatsApp))
            return BadRequest(PakistanWhatsApp.InvalidError);

        var user = new AppUser
        {
            Id = request.Id is { } id && id != Guid.Empty ? id : Guid.NewGuid(),
            Username = username,
            Role = role,
            LeagueId = leagueId,
            WhatsAppNumber = whatsApp,
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.Now
        };
        user.PasswordHash = _hasher.HashPassword(user, request.Password!.Trim());

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(user, await LeagueNameAsync(user.LeagueId, ct).ConfigureAwait(false)));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDto>> Update(Guid id, [FromBody] UserUpsertRequest request, CancellationToken ct)
    {
        if (!CallerIsPlatformAdmin())
            return Forbid();
        if (!TryValidateUser(request, requirePassword: false, out var username, out var role, out var error))
            return BadRequest(error);

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (user is null || user.Role == AppRole.SuperAdmin)
            return NotFound();

        if (await UsernameTakenAsync(username, id, ct).ConfigureAwait(false))
            return BadRequest($"Username \"{username}\" already exists.");

        var isGuestAccount = user.Role == AppRole.Guest
                             || string.Equals(user.Username, "user", StringComparison.OrdinalIgnoreCase);
        if (isGuestAccount)
        {
            username = "user";
            role = AppRole.Guest;
        }
        else if (role == AppRole.Guest)
            return BadRequest("Guest is a built-in system account.");

        Guid? leagueId = null;
        if (role is AppRole.PublicUser or AppRole.Guest)
            leagueId = null;
        else if (role != AppRole.GroundOwner)
        {
            leagueId = await RequireLeagueAsync(request.LeagueId, ct).ConfigureAwait(false);
            if (leagueId is null)
                return BadRequest("Select a league for this user.");
        }

        user.Username = username;
        user.Role = role;
        user.LeagueId = leagueId;
        if (!PakistanWhatsApp.TryNormalizeOptional(request.WhatsAppNumber, out var whatsApp))
            return BadRequest(PakistanWhatsApp.InvalidError);
        user.WhatsAppNumber = whatsApp ?? user.WhatsAppNumber;
        user.IsActive = request.IsActive;
        if (!string.IsNullOrWhiteSpace(request.Password))
            user.PasswordHash = _hasher.HashPassword(user, request.Password.Trim());

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(user, await LeagueNameAsync(user.LeagueId, ct).ConfigureAwait(false)));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!CallerIsPlatformAdmin())
            return Forbid();

        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (user is null || user.Role == AppRole.SuperAdmin)
            return NotFound();
        if (user.Role == AppRole.Guest || string.Equals(user.Username, "user", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Cannot delete the Guest account.");

        if (user.LeagueId is Guid leagueId)
        {
            await using var leagueDb = _dbs.CreateForLeague(leagueId);
            var hasBookings = await leagueDb.VenueBookings
                .AnyAsync(b => !b.IsDeleted && b.CreatedByUserId == id, ct)
                .ConfigureAwait(false);
            if (hasBookings)
                return BadRequest("Cannot delete this user because they have venue bookings.");
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private async Task<bool> UsernameTakenAsync(string username, Guid? excludeId, CancellationToken ct)
    {
        var q = _db.Users.AsNoTracking().Where(u => u.Username == username);
        if (excludeId is Guid id)
            q = q.Where(u => u.Id != id);
        return await q.AnyAsync(ct).ConfigureAwait(false);
    }

    private static bool TryValidateUser(
        UserUpsertRequest request,
        bool requirePassword,
        out string username,
        out AppRole role,
        out string error)
    {
        username = request.Username?.Trim() ?? string.Empty;
        role = AppRole.User;
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

        var roleText = request.Role?.Trim() ?? string.Empty;
        if (!Enum.TryParse<AppRole>(roleText, ignoreCase: true, out role) ||
            (role != AppRole.Admin && role != AppRole.User && role != AppRole.GroundOwner
             && role != AppRole.PublicUser && role != AppRole.Guest))
        {
            error = "Role must be League Admin, League User, Ground Owner, Public User, or Guest.";
            return false;
        }

        if (role == AppRole.Guest && !string.Equals(username, "user", StringComparison.OrdinalIgnoreCase))
        {
            error = "Guest is only the built-in continue-without-login account.";
            return false;
        }

        return true;
    }

    private async Task<Guid?> RequireLeagueAsync(Guid? leagueId, CancellationToken ct)
    {
        if (leagueId is not Guid id || id == Guid.Empty)
            return null;
        var exists = await _db.Leagues.AsNoTracking()
            .AnyAsync(l => l.Id == id && l.IsActive, ct)
            .ConfigureAwait(false);
        return exists ? id : null;
    }

    private async Task<string?> LeagueNameAsync(Guid? leagueId, CancellationToken ct)
    {
        if (leagueId is not Guid id || id == Guid.Empty)
            return null;
        return await _db.Leagues.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => l.Name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    private bool CallerIsPlatformAdmin()
    {
        var name = HttpContext.Items.TryGetValue("JplUsername", out var nameObj) && nameObj is string n
            ? n
            : User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        return CallerIsSuperAdmin() && PlatformAdmin.Is(name);
    }

    private bool CallerIsSuperAdmin()
        => string.Equals(CallerRole(), nameof(AppRole.SuperAdmin), StringComparison.OrdinalIgnoreCase);

    private string CallerRole()
    {
        if (HttpContext.Items.TryGetValue("JplRole", out var roleObj) && roleObj is string r && !string.IsNullOrWhiteSpace(r))
            return r;
        return User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;
    }

    private static UserDto ToDto(AppUser u, IReadOnlyDictionary<Guid, string> leagueNames)
    {
        string? leagueName = null;
        if (u.LeagueId is Guid id)
            leagueNames.TryGetValue(id, out leagueName);
        return ToDto(u, leagueName);
    }

    private static UserDto ToDto(AppUser u, string? leagueName = null)
        => new(u.Id, u.Username, u.Role.ToString(), u.IsActive, u.CreatedAtUtc, u.LeagueId, leagueName, u.WhatsAppNumber);
}

[ApiController]
[Route("api/players")]
public sealed class PlayersController : ControllerBase
{
    private readonly ApiDbContext _db;
    private readonly PlayerImageStore _images;
    private readonly IApiLeagueScope _league;

    public PlayersController(ApiDbContext db, PlayerImageStore images, IApiLeagueScope league)
    {
        _db = db;
        _images = images;
        _league = league;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlayerDto>>> GetAll(CancellationToken ct)
    {
        var list = await _db.Players.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.FullName)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return Ok(list.Select(ToDto).ToList());
    }

    /// <summary>Player IDs that have a stored photo. Image bytes are not included.</summary>
    [HttpGet("photos")]
    public async Task<ActionResult<IReadOnlyList<Guid>>> PhotoIds(CancellationToken ct)
    {
        var ids = await _db.Players.AsNoTracking()
            .Where(p => !p.IsDeleted && p.ImagePath != null && p.ImagePath != "")
            .Select(p => p.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return Ok(ids);
    }

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken ct)
    {
        var path = await _db.Players.AsNoTracking()
            .Where(p => p.Id == id && !p.IsDeleted)
            .Select(p => p.ImagePath)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(path))
            return NotFound();

        if (!_images.TryGetFullPath(_league.CurrentLeagueId, path, out var fullPath, out var contentType))
            return NotFound();

        return PhysicalFile(fullPath, contentType);
    }

    [HttpPost("{id:guid}/image")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(PlayerImageStore.MaxBytes + 24_576)]
    [RequestFormLimits(MultipartBodyLengthLimit = PlayerImageStore.MaxBytes + 24_576)]
    public async Task<IActionResult> UploadImage(Guid id, [FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Choose an image file.");
        if (file.Length > PlayerImageStore.MaxBytes)
            return BadRequest("Image must be 512 KB or smaller.");

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct)
            .ConfigureAwait(false);
        if (player is null)
            return NotFound();

        try
        {
            var saved = await _images.SaveAsync(_league.CurrentLeagueId, player.Id, file, ct)
                .ConfigureAwait(false);
            if (!string.Equals(player.ImagePath, saved, StringComparison.OrdinalIgnoreCase))
                _images.Delete(_league.CurrentLeagueId, player.ImagePath);
            player.ImagePath = saved;
            player.UpdatedAtUtc = DateTime.Now;
            player.Version++;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:guid}/image")]
    public async Task<IActionResult> DeleteImage(Guid id, CancellationToken ct)
    {
        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct)
            .ConfigureAwait(false);
        if (player is null)
            return NotFound();

        _images.Delete(_league.CurrentLeagueId, player.ImagePath);
        player.ImagePath = null;
        player.UpdatedAtUtc = DateTime.Now;
        player.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlayerDto>> Get(Guid id, CancellationToken ct)
    {
        var p = await _db.Players.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            .ConfigureAwait(false);
        if (p is null)
            return NotFound();

        return Ok(ToDto(p));
    }

    [HttpPost]
    public async Task<ActionResult<PlayerDto>> Create([FromBody] PlayerUpsertRequest request, CancellationToken ct)
    {
        if (!TryValidatePlayerName(request.FullName, out var name, out var error))
            return BadRequest(error);

        if (await PlayerNameTakenAsync(name, excludeId: null, ct).ConfigureAwait(false))
            return BadRequest($"Player name \"{name}\" already exists.");

        var now = DateTime.Now;
        var player = new Player
        {
            Id = request.Id is { } id && id != Guid.Empty ? id : Guid.NewGuid(),
            FullName = name,
            IsActive = request.IsActive,
            NickName = TrimTo(request.NickName, 40),
            PlayingRole = TrimTo(request.PlayingRole, 40),
            BattingStyle = TrimTo(request.BattingStyle, 40),
            BowlingStyle = TrimTo(request.BowlingStyle, 60),
            Bio = TrimTo(request.Bio, 500),
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _db.Players.Add(player);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(player));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PlayerDto>> Update(Guid id, [FromBody] PlayerUpsertRequest request, CancellationToken ct)
    {
        if (!TryValidatePlayerName(request.FullName, out var name, out var error))
            return BadRequest(error);

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct).ConfigureAwait(false);
        if (player is null)
            return NotFound();

        if (await PlayerNameTakenAsync(name, excludeId: id, ct).ConfigureAwait(false))
            return BadRequest($"Player name \"{name}\" already exists.");

        player.FullName = name;
        player.IsActive = request.IsActive;
        player.NickName = TrimTo(request.NickName, 40);
        player.PlayingRole = TrimTo(request.PlayingRole, 40);
        player.BattingStyle = TrimTo(request.BattingStyle, 40);
        player.BowlingStyle = TrimTo(request.BowlingStyle, 60);
        player.Bio = TrimTo(request.Bio, 500);
        player.UpdatedAtUtc = DateTime.Now;
        player.Version++;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(player));
    }

    private async Task<bool> PlayerNameTakenAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var key = name.ToLowerInvariant();
        return await _db.Players.AsNoTracking()
            .AnyAsync(
                p => !p.IsDeleted
                     && (excludeId == null || p.Id != excludeId)
                     && p.FullName.ToLower() == key,
                ct)
            .ConfigureAwait(false);
    }

    private static bool TryValidatePlayerName(string? raw, out string name, out string error)
    {
        name = (raw ?? string.Empty).Trim();
        error = string.Empty;
        if (name.Length == 0)
        {
            error = "Player name is required.";
            return false;
        }

        if (name.Length > 120)
        {
            error = "Player name must be 120 characters or fewer.";
            return false;
        }

        return true;
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == id, ct).ConfigureAwait(false);
        if (player is null || player.IsDeleted)
            return NoContent();

        var block = await DeleteConstraints.PlayerAsync(_db, id, ct).ConfigureAwait(false);
        if (block is not null)
            return BadRequest(block);

        player.IsDeleted = true;
        player.UpdatedAtUtc = DateTime.Now;
        player.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private static PlayerDto ToDto(Player p) => new(
        p.Id, p.FullName,
        p.Version, p.UpdatedAtUtc, p.IsDeleted, p.IsActive,
        p.NickName, p.PlayingRole, p.BattingStyle, p.BowlingStyle, p.Bio);

    private static string? TrimTo(string? raw, int max)
    {
        var s = raw?.Trim();
        if (string.IsNullOrWhiteSpace(s))
            return null;
        return s.Length <= max ? s : s[..max];
    }
}

[ApiController]
[Route("api/teams")]
public sealed class TeamsController : ControllerBase
{
    private readonly ApiDbContext _db;
    private readonly TeamImageStore _images;
    private readonly IApiLeagueScope _league;

    public TeamsController(ApiDbContext db, TeamImageStore images, IApiLeagueScope league)
    {
        _db = db;
        _images = images;
        _league = league;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TeamDto>>> GetAll(CancellationToken ct)
    {
        var teams = await _db.Teams.AsNoTracking()
            .Include(t => t.TeamPlayers)
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dto = teams.Select(ToDto).ToList();
        return Ok(dto);
    }

    /// <summary>Team IDs that have a stored logo. Image bytes are not included.</summary>
    [HttpGet("photos")]
    public async Task<ActionResult<IReadOnlyList<Guid>>> PhotoIds(CancellationToken ct)
    {
        var ids = await _db.Teams.AsNoTracking()
            .Where(t => !t.IsDeleted && t.ImagePath != null && t.ImagePath != "")
            .Select(t => t.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return Ok(ids);
    }

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken ct)
    {
        var path = await _db.Teams.AsNoTracking()
            .Where(t => t.Id == id && !t.IsDeleted)
            .Select(t => t.ImagePath)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(path))
            return NotFound();

        if (!_images.TryGetFullPath(_league.CurrentLeagueId, path, out var fullPath, out var contentType))
            return NotFound();

        return PhysicalFile(fullPath, contentType);
    }

    [HttpPost("{id:guid}/image")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(TeamImageStore.MaxBytes + 24_576)]
    [RequestFormLimits(MultipartBodyLengthLimit = TeamImageStore.MaxBytes + 24_576)]
    public async Task<IActionResult> UploadImage(Guid id, [FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("Choose an image file.");
        if (file.Length > TeamImageStore.MaxBytes)
            return BadRequest("Image must be 512 KB or smaller.");

        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
            .ConfigureAwait(false);
        if (team is null)
            return NotFound();

        try
        {
            var saved = await _images.SaveAsync(_league.CurrentLeagueId, team.Id, file, ct)
                .ConfigureAwait(false);
            if (!string.Equals(team.ImagePath, saved, StringComparison.OrdinalIgnoreCase))
                _images.Delete(_league.CurrentLeagueId, team.ImagePath);
            team.ImagePath = saved;
            team.UpdatedAtUtc = DateTime.Now;
            team.Version++;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:guid}/image")]
    public async Task<IActionResult> DeleteImage(Guid id, CancellationToken ct)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
            .ConfigureAwait(false);
        if (team is null)
            return NotFound();

        _images.Delete(_league.CurrentLeagueId, team.ImagePath);
        team.ImagePath = null;
        team.UpdatedAtUtc = DateTime.Now;
        team.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TeamDto>> Get(Guid id, CancellationToken ct)
    {
        var team = await _db.Teams.AsNoTracking()
            .Include(t => t.TeamPlayers)
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
            .ConfigureAwait(false);
        if (team is null)
            return NotFound();

        return Ok(ToDto(team));
    }

    [HttpPost]
    public async Task<ActionResult<TeamDto>> Create([FromBody] TeamUpsertRequest request, CancellationToken ct)
    {
        if (!TryValidateTeamName(request.Name, out var name, out var nameError))
            return BadRequest(nameError);

        if (!TryValidateRoster(request, out var playerIds, out var playingSet, out var rosterError))
            return BadRequest(rosterError);

        if (await TeamNameTakenAsync(name, excludeId: null, ct).ConfigureAwait(false))
            return BadRequest($"Team name \"{name}\" already exists.");

        try
        {
            await EnsurePlayersExistAsync(playerIds, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var now = DateTime.Now;
        var team = new Team
        {
            Id = request.Id is { } id && id != Guid.Empty ? id : Guid.NewGuid(),
            Name = name,
            CaptainPlayerId = request.CaptainPlayerId,
            IsActive = request.IsActive,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _db.Teams.Add(team);
        foreach (var pid in playerIds)
            _db.TeamPlayers.Add(new TeamPlayer
            {
                TeamId = team.Id,
                PlayerId = pid,
                IsPlaying = playingSet.Contains(pid)
            });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var saved = await _db.Teams.AsNoTracking()
            .Include(t => t.TeamPlayers)
            .FirstAsync(t => t.Id == team.Id, ct)
            .ConfigureAwait(false);
        return Ok(ToDto(saved));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TeamDto>> Update(Guid id, [FromBody] TeamUpsertRequest request, CancellationToken ct)
    {
        if (!TryValidateTeamName(request.Name, out var name, out var nameError))
            return BadRequest(nameError);

        if (!TryValidateRoster(request, out var playerIds, out var playingSet, out var rosterError))
            return BadRequest(rosterError);

        if (await TeamNameTakenAsync(name, excludeId: id, ct).ConfigureAwait(false))
            return BadRequest($"Team name \"{name}\" already exists.");

        try
        {
            await EnsurePlayersExistAsync(playerIds, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var team = await _db.Teams.Include(t => t.TeamPlayers)
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
            .ConfigureAwait(false);
        if (team is null)
            return NotFound();

        team.Name = name;
        team.CaptainPlayerId = request.CaptainPlayerId;
        team.IsActive = request.IsActive;
        team.UpdatedAtUtc = DateTime.Now;
        team.Version++;

        ApplySquad(team, id, playerIds, playingSet);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var saved = await _db.Teams.AsNoTracking()
            .Include(t => t.TeamPlayers)
            .FirstAsync(t => t.Id == id, ct)
            .ConfigureAwait(false);
        return Ok(ToDto(saved));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
        if (team is null || team.IsDeleted)
            return NoContent();

        var block = await DeleteConstraints.TeamAsync(_db, id, ct).ConfigureAwait(false);
        if (block is not null)
            return BadRequest(block);

        team.IsDeleted = true;
        team.IsActive = false;
        team.UpdatedAtUtc = DateTime.Now;
        team.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private static bool TryValidateRoster(
        TeamUpsertRequest request,
        out List<Guid> playerIds,
        out HashSet<Guid> playingSet,
        out string error)
    {
        playerIds = TeamRoster.Distinct(request.PlayerIds);
        playingSet = new HashSet<Guid>();
        error = string.Empty;

        if (playerIds.Count == 0)
        {
            error = "Add at least one player to the squad.";
            return false;
        }

        if (request.CaptainPlayerId is not Guid cid || cid == Guid.Empty)
        {
            error = "Captain is required.";
            return false;
        }

        if (!playerIds.Contains(cid))
        {
            error = "Captain must be one of the team players.";
            return false;
        }

        if (!TeamRoster.TryResolvePlaying(playerIds, request.PlayingPlayerIds, out playingSet, out var playingError))
        {
            error = playingError ?? "Playing list is invalid.";
            return false;
        }

        return true;
    }

    private void ApplySquad(Team team, Guid teamId, IReadOnlyList<Guid> playerIds, HashSet<Guid> playingSet)
    {
        var desired = playerIds.ToHashSet();
        foreach (var remove in team.TeamPlayers.Where(tp => !desired.Contains(tp.PlayerId)).ToList())
            _db.TeamPlayers.Remove(remove);

        foreach (var tp in team.TeamPlayers)
            tp.IsPlaying = playingSet.Contains(tp.PlayerId);

        var have = team.TeamPlayers.Select(tp => tp.PlayerId).ToHashSet();
        foreach (var add in desired.Where(pid => !have.Contains(pid)))
        {
            _db.TeamPlayers.Add(new TeamPlayer
            {
                TeamId = teamId,
                PlayerId = add,
                IsPlaying = playingSet.Contains(add)
            });
        }
    }

    private async Task EnsurePlayersExistAsync(IReadOnlyList<Guid> playerIds, CancellationToken ct)
    {
        if (playerIds.Count == 0)
            return;
        var count = await _db.Players.CountAsync(p => playerIds.Contains(p.Id) && !p.IsDeleted, ct).ConfigureAwait(false);
        if (count != playerIds.Count)
            throw new InvalidOperationException("One or more players were not found on server.");
    }

    private async Task<bool> TeamNameTakenAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var key = name.ToLowerInvariant();
        return await _db.Teams.AsNoTracking()
            .AnyAsync(
                t => !t.IsDeleted
                     && (excludeId == null || t.Id != excludeId)
                     && t.Name.ToLower() == key,
                ct)
            .ConfigureAwait(false);
    }

    private static bool TryValidateTeamName(string? raw, out string name, out string error)
    {
        name = (raw ?? string.Empty).Trim();
        error = string.Empty;
        if (name.Length == 0)
        {
            error = "Team name is required.";
            return false;
        }

        if (name.Length > 120)
        {
            error = "Team name must be 120 characters or fewer.";
            return false;
        }

        return true;
    }

    private static TeamDto ToDto(Team t) => new(
        t.Id, t.Name, t.CaptainPlayerId,
        t.TeamPlayers.Select(tp => tp.PlayerId).ToList(),
        t.Version, t.UpdatedAtUtc, t.IsDeleted, t.IsActive,
        t.TeamPlayers.Where(tp => tp.IsPlaying).Select(tp => tp.PlayerId).ToList());
}

[ApiController]
[Route("api/matches")]
public sealed class MatchesController : ControllerBase
{
    private readonly ApiDbContext _db;

    public MatchesController(ApiDbContext db) => _db = db;

    /// <summary>Completed matches only — ongoing scoring stays on devices.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MatchSummaryDto>>> GetCompleted(CancellationToken ct)
    {
        var matches = await _db.Matches.AsNoTracking()
            .Where(m => !m.IsDeleted && m.Status == MatchStatus.Completed && m.TournamentId == null && m.SeriesId == null)
            .OrderByDescending(m => m.MatchDateUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (matches.Count == 0)
            return Ok(Array.Empty<MatchSummaryDto>());

        var matchIds = matches.Select(m => m.Id).ToList();
        var teamIds = matches.SelectMany(m => new[] { m.TeamAId, m.TeamBId }).Distinct().ToList();

        var teams = await _db.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct)
            .ConfigureAwait(false);

        var innings = await _db.Innings.AsNoTracking()
            .Where(i => matchIds.Contains(i.MatchId))
            .Include(i => i.Deliveries)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var innsByMatch = innings.GroupBy(i => i.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        var squadCounts = await _db.TeamPlayers.AsNoTracking()
            .Where(tp => teamIds.Contains(tp.TeamId))
            .GroupBy(tp => tp.TeamId)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct)
            .ConfigureAwait(false);

        var potmIds = matches
            .Where(m => m.PlayerOfTheMatchPlayerId is Guid)
            .Select(m => m.PlayerOfTheMatchPlayerId!.Value)
            .Distinct()
            .ToList();
        var potmNames = potmIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Players.AsNoTracking()
                .Where(p => potmIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
                .ConfigureAwait(false);

        var list = matches.Select(m =>
        {
            innsByMatch.TryGetValue(m.Id, out var matchInns);
            matchInns ??= new List<Innings>();
            var teamA = teams.GetValueOrDefault(m.TeamAId, "?");
            var teamB = teams.GetValueOrDefault(m.TeamBId, "?");
            var (aRuns, aWkts) = TeamTotals(matchInns, m.TeamAId);
            var (bRuns, bWkts) = TeamTotals(matchInns, m.TeamBId);
            var firstBat = matchInns.OrderBy(i => i.InningsNumber).FirstOrDefault()?.BattingTeamId;
            var (result, winnerId) = BuildResult(
                m, teamA, teamB, aRuns, aWkts, bRuns, bWkts, firstBat,
                MatchSquadSnapshot.SizeFor(m.SquadRosterJson, m.TeamAId, true, squadCounts.GetValueOrDefault(m.TeamAId)),
                MatchSquadSnapshot.SizeFor(m.SquadRosterJson, m.TeamBId, false, squadCounts.GetValueOrDefault(m.TeamBId)));
            string? potmName = null;
            if (m.PlayerOfTheMatchPlayerId is Guid potmId)
                potmName = potmNames.GetValueOrDefault(potmId);

            return new MatchSummaryDto(
                m.Id,
                m.Title,
                m.TeamAId,
                m.TeamBId,
                teamA,
                teamB,
                (byte)m.Status,
                (byte)m.Format,
                m.MatchDateUtc,
                m.OversLimit,
                m.Venue,
                aRuns,
                aWkts,
                bRuns,
                bWkts,
                result,
                winnerId,
                m.PlayerOfTheMatchPlayerId,
                potmName,
                (byte)m.OversMode,
                m.TournamentId,
                m.SeriesId,
                m.LiveStreamUrl);
        }).ToList();

        return Ok(list);
    }

    [HttpGet("{id:guid}/analytics")]
    public async Task<ActionResult<MatchAnalyticsReport>> GetAnalytics(Guid id, CancellationToken ct)
    {
        var match = await _db.Matches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted, ct)
            .ConfigureAwait(false);
        if (match is null)
            return NotFound();

        var innings = await _db.Innings.AsNoTracking()
            .Where(i => i.MatchId == id)
            .Include(i => i.Deliveries)
            .OrderBy(i => i.InningsNumber)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (innings.Count == 0)
            return NotFound();

        var teamIds = new[] { match.TeamAId, match.TeamBId };
        var teams = await _db.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct)
            .ConfigureAwait(false);
        var playerIds = innings.SelectMany(i => i.Deliveries)
            .SelectMany(d => new[] { d.BatterId, d.NonStrikerId })
            .Distinct()
            .ToList();
        var names = await _db.Players.AsNoTracking()
            .Where(p => playerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
            .ConfigureAwait(false);

        var teamA = teams.GetValueOrDefault(match.TeamAId, "Team A");
        var teamB = teams.GetValueOrDefault(match.TeamBId, "Team B");
        var rows = innings.Select(inn => (
            inn.InningsNumber,
            inn.BattingTeamId == match.TeamAId ? teamA : teamB,
            (IReadOnlyList<Delivery>)inn.Deliveries.OrderBy(d => d.Sequence).ToList()
        )).ToList();
        var result = match.Status == MatchStatus.InProgress ? "LIVE"
            : match.Status == MatchStatus.Completed ? "Completed" : "";
        return Ok(MatchAnalyticsBuilder.Build(match.Title, teamA, teamB, result, Math.Max(match.OversLimit, 1), rows, names));
    }

    /// <summary>Full batting/bowling scorecard for every innings of a completed match.</summary>
    [HttpGet("{id:guid}/scorecard")]
    public async Task<ActionResult<MatchScorecardDto>> GetScorecard(Guid id, CancellationToken ct)
    {
        var match = await _db.Matches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted, ct)
            .ConfigureAwait(false);
        if (match is null)
            return NotFound();

        var innings = await _db.Innings.AsNoTracking()
            .Where(i => i.MatchId == id)
            .Include(i => i.Deliveries)
            .OrderBy(i => i.InningsNumber)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var teamIds = new[] { match.TeamAId, match.TeamBId };
        var teams = await _db.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct)
            .ConfigureAwait(false);

        var squadRows = await _db.TeamPlayers.AsNoTracking()
            .Where(tp => tp.TeamId == match.TeamAId || tp.TeamId == match.TeamBId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        MatchSquadSnapshot? squadSnap = MatchSquadSnapshot.TryParse(match.SquadRosterJson);

        var playerIds = innings
            .SelectMany(i => i.Deliveries)
            .SelectMany(d => new[] { d.BatterId, d.BowlerId, d.DismissedPlayerId ?? Guid.Empty, d.FielderId ?? Guid.Empty })
            .Concat(squadRows.Select(tp => tp.PlayerId))
            .Concat(squadSnap?.PlayersOfTeamA() ?? [])
            .Concat(squadSnap?.PlayersOfTeamB() ?? [])
            .Where(pid => pid != Guid.Empty)
            .Distinct()
            .ToList();
        if (match.PlayerOfTheMatchPlayerId is Guid potmInclude)
            playerIds.Add(potmInclude);
        if (match.TeamACaptainId is Guid capA)
            playerIds.Add(capA);
        if (match.TeamBCaptainId is Guid capB)
            playerIds.Add(capB);

        var names = await _db.Players.AsNoTracking()
            .Where(p => playerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
            .ConfigureAwait(false);

        string NameOf(Guid pid) => names.GetValueOrDefault(pid, "?");

        Guid? CaptainOf(Guid teamId)
        {
            if (teamId == match.TeamAId)
                return match.TeamACaptainId;
            if (teamId == match.TeamBId)
                return match.TeamBCaptainId;
            return null;
        }

        IReadOnlyList<(Guid Id, string Name, bool IsCaptain)> SquadOf(Guid teamId)
        {
            var captainId = CaptainOf(teamId);
            IEnumerable<Guid> ids;
            if (squadSnap is not null)
            {
                ids = teamId == match.TeamAId ? squadSnap.PlayersOfTeamA()
                    : teamId == match.TeamBId ? squadSnap.PlayersOfTeamB()
                    : [];
                if (!ids.Any())
                    ids = squadRows.Where(tp => tp.TeamId == teamId && tp.IsPlaying).Select(tp => tp.PlayerId);
                if (!ids.Any())
                    ids = squadRows.Where(tp => tp.TeamId == teamId).Select(tp => tp.PlayerId);
            }
            else
            {
                ids = squadRows.Where(tp => tp.TeamId == teamId && tp.IsPlaying).Select(tp => tp.PlayerId);
                if (!ids.Any())
                    ids = squadRows.Where(tp => tp.TeamId == teamId).Select(tp => tp.PlayerId);
            }

            return ids
                .Select(id => (id, NameOf(id), captainId == id))
                .OrderBy(p => p.Item2, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var sections = new List<InningsScorecardDto>();
        foreach (var inn in innings)
        {
            var balls = inn.Deliveries.OrderBy(d => d.Sequence).ToList();
            var section = BuildInningsDto(
                inn, balls, teams, NameOf,
                CaptainOf(inn.BattingTeamId),
                CaptainOf(inn.BowlingTeamId),
                SquadOf(inn.BattingTeamId),
                SquadOf(inn.BowlingTeamId));
            sections.Add(section);
        }

        var teamA = teams.GetValueOrDefault(match.TeamAId, "?");
        var teamB = teams.GetValueOrDefault(match.TeamBId, "?");
        var (aRuns, aWkts) = TeamTotals(innings, match.TeamAId);
        var (bRuns, bWkts) = TeamTotals(innings, match.TeamBId);
        var firstBat = innings.OrderBy(i => i.InningsNumber).FirstOrDefault()?.BattingTeamId;
        string result;
        Guid? winnerId;
        if (match.Status == MatchStatus.Completed)
        {
            (result, winnerId) = BuildResult(
                match, teamA, teamB, aRuns, aWkts, bRuns, bWkts, firstBat,
                MatchSquadSnapshot.SizeFor(match.SquadRosterJson, match.TeamAId, true, squadRows.Count(tp => tp.TeamId == match.TeamAId && tp.IsPlaying)),
                MatchSquadSnapshot.SizeFor(match.SquadRosterJson, match.TeamBId, false, squadRows.Count(tp => tp.TeamId == match.TeamBId && tp.IsPlaying)));
        }
        else
        {
            result = match.Status == MatchStatus.InProgress ? "LIVE" : "Upcoming";
            winnerId = null;
        }

        var formatText = match.Format == MatchFormat.DoubleInnings ? "Double innings" : "Single inning";
        var meta = $"{formatText} · {OversRules.ShortLabel(match.OversMode, match.OversLimit)} · {match.MatchDateUtc:dd MMM yyyy}";
        if (!string.IsNullOrWhiteSpace(match.Venue))
            meta += $" · {match.Venue}";

        var tossText = string.Empty;
        if (match.TossWinnerTeamId is Guid tossId)
        {
            var tossName = teams.GetValueOrDefault(tossId, "?");
            tossText = match.TossWinnerBatsFirst
                ? $"Toss: {tossName} elected to bat"
                : $"Toss: {tossName} elected to bowl";
        }

        string? potmName = null;
        if (match.PlayerOfTheMatchPlayerId is Guid potmId)
            potmName = names.GetValueOrDefault(potmId);

        var liveInn = innings.LastOrDefault(i => !i.IsCompleted) ?? innings.LastOrDefault();
        var liveSection = liveInn is null
            ? null
            : sections.FirstOrDefault(s => s.InningsNumber == liveInn.InningsNumber);
        var liveBalls = liveInn?.Deliveries.OrderBy(d => d.Sequence).ToList() ?? new List<Delivery>();
        var lastBall = liveBalls.LastOrDefault();
        var legal = liveBalls.Count(d => d.IsLegalDelivery);

        string BatterLine()
        {
            if (lastBall is null || liveSection is null)
                return "—";
            var row = liveSection.Batting.FirstOrDefault(b => b.PlayerId == lastBall.BatterId);
            return row is null ? "—" : $"{row.Name}  {row.Runs}({row.Balls})";
        }

        string BowlerLine()
        {
            if (lastBall is null || liveSection is null)
                return "—";
            var row = liveSection.Bowling.FirstOrDefault(b => b.PlayerId == lastBall.BowlerId);
            if (row is null)
                return "—";
            var o = row.BallsBowled / 6;
            var b = row.BallsBowled % 6;
            return $"{row.Name}  {o}.{b}-{row.Maidens}-{row.RunsConceded}-{row.Wickets}";
        }

        string NonStrikerLine()
        {
            if (lastBall is null || liveSection is null)
                return "—";
            var row = liveSection.Batting.FirstOrDefault(b =>
                b.PlayerId == lastBall.NonStrikerId && b.PlayerId != lastBall.BatterId);
            return row is null ? "—" : $"{row.Name}  {row.Runs}({row.Balls})";
        }

        var overNo = lastBall?.OverNumber;
        var thisOver = overNo is int on
            ? liveBalls.Where(d => d.OverNumber == on).Select(ToOverBallDto).ToList()
            : new List<OverBallMarkDto>();

        var (isChase, chaseNeed, rrr) = ComputeChase(match, innings);

        return Ok(new MatchScorecardDto
        {
            MatchId = match.Id,
            Title = match.Title,
            TeamAId = match.TeamAId,
            TeamBId = match.TeamBId,
            TeamAName = teamA,
            TeamBName = teamB,
            TeamARuns = aRuns,
            TeamAWickets = aWkts,
            TeamBRuns = bRuns,
            TeamBWickets = bWkts,
            ResultText = result,
            MetaLine = meta,
            TossText = tossText,
            WinnerTeamId = winnerId,
            PlayerOfTheMatchPlayerId = match.PlayerOfTheMatchPlayerId,
            PlayerOfTheMatchName = potmName,
            LiveStreamUrl = match.LiveStreamUrl,
            CurrentBatter = BatterLine(),
            CurrentBowler = BowlerLine(),
            CurrentOver = $"{legal / 6}.{legal % 6}",
            TotalOvers = match.OversMode == Models.OversMode.Unlimited
                ? "Unlimited"
                : match.OversLimit.ToString(),
            CurrentNonStriker = NonStrikerLine(),
            BugTeamScore = liveSection is null ? "—" : $"{liveSection.BattingTeamName} {liveSection.TotalRuns}/{liveSection.Wickets}",
            CurrentRunRate = legal == 0 || liveSection is null
                ? "-"
                : Math.Round(liveSection.TotalRuns * 6.0 / legal, 2).ToString("0.00"),
            ThisOver = thisOver,
            Status = (byte)match.Status,
            IsChase = isChase,
            ChaseNeedText = chaseNeed,
            RequiredRunRate = rrr,
            TeamACaptainName = match.TeamACaptainId is Guid ac ? NameOf(ac) : null,
            TeamBCaptainName = match.TeamBCaptainId is Guid bc ? NameOf(bc) : null,
            Innings = sections
        });
    }

    private static (bool IsChase, string ChaseNeed, string RequiredRunRate) ComputeChase(
        Match match, IReadOnlyList<Innings> innings)
    {
        if (match.Status != MatchStatus.InProgress)
            return (false, string.Empty, "-");

        var liveInn = innings.LastOrDefault(i => !i.IsCompleted);
        if (liveInn is null)
            return (false, string.Empty, "-");

        var currentRuns = liveInn.Deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns);
        var legal = liveInn.Deliveries.Count(d => d.IsLegalDelivery);
        var matchLegal = innings.Sum(i => i.Deliveries.Count(d => d.IsLegalDelivery));
        var remaining = OversRules.BallsRemaining(match.OversMode, match.OversLimit, legal, matchLegal);

        int InnRuns(Innings inn) => inn.Deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns);
        int CompletedTeamRuns(Guid teamId) => innings
            .Where(i => i.BattingTeamId == teamId && i.Id != liveInn.Id)
            .Sum(InnRuns);

        static string NeedText(int target, int need, int? ballsRemaining, Models.OversMode mode)
        {
            if (need == 0)
                return $"Target {target} achieved";
            if (mode == Models.OversMode.Unlimited || ballsRemaining is null)
                return $"Need {need} runs  ·  Target {target}";
            var oversLeft = $"{ballsRemaining.Value / 6}.{ballsRemaining.Value % 6}";
            return $"Need {need} from {ballsRemaining} balls ({oversLeft} ov)  ·  Target {target}";
        }

        static string Rate(int need, int? ballsRemaining)
        {
            if (need == 0)
                return "0.00";
            if (ballsRemaining is not int left || left <= 0)
                return "-";
            return Math.Round(need * 6.0 / left, 2).ToString("0.00");
        }

        if (match.Format == MatchFormat.SingleInning && liveInn.InningsNumber == 2)
        {
            var first = innings.FirstOrDefault(i => i.InningsNumber == 1);
            var target = (first is null ? 0 : InnRuns(first)) + 1;
            var need = Math.Max(0, target - currentRuns);
            return (true, NeedText(target, need, remaining, match.OversMode), Rate(need, remaining));
        }

        if (match.Format == MatchFormat.DoubleInnings && liveInn.InningsNumber >= 4)
        {
            var inningsTarget = CompletedTeamRuns(liveInn.BowlingTeamId) - CompletedTeamRuns(liveInn.BattingTeamId) + 1;
            if (inningsTarget < 1)
                inningsTarget = 1;
            var need = Math.Max(0, inningsTarget - currentRuns);
            return (true, NeedText(inningsTarget, need, remaining, match.OversMode), Rate(need, remaining));
        }

        return (false, string.Empty, "-");
    }

    private static OverBallMarkDto ToOverBallDto(Delivery d)
    {
        if (d.IsWicket)
            return new OverBallMarkDto { Text = "W", Kind = "wicket" };
        if (d.ExtraType == ExtraType.Wide)
            return new OverBallMarkDto { Text = "wd", Kind = "extra" };
        if (d.ExtraType == ExtraType.NoBall)
            return new OverBallMarkDto { Text = "nb", Kind = "extra" };
        var runs = d.RunsOffBat + (d.ExtraType is ExtraType.Bye or ExtraType.LegBye ? d.ExtraRuns : 0);
        if (runs >= 6)
            return new OverBallMarkDto { Text = "6", Kind = "six" };
        if (runs == 4)
            return new OverBallMarkDto { Text = "4", Kind = "four" };
        if (runs == 0)
            return new OverBallMarkDto { Text = "•", Kind = "dot" };
        return new OverBallMarkDto { Text = runs.ToString(), Kind = "run" };
    }

    private static string FormatExtrasBreakdown(IReadOnlyList<Delivery> balls)
    {
        var total = balls.Sum(d => d.ExtraRuns);
        if (total == 0)
            return "Extras 0";
        var wd = balls.Where(d => d.ExtraType == ExtraType.Wide).Sum(d => d.ExtraRuns);
        var nb = balls.Where(d => d.ExtraType == ExtraType.NoBall).Sum(d => d.ExtraRuns);
        var b = balls.Where(d => d.ExtraType == ExtraType.Bye).Sum(d => d.ExtraRuns);
        var lb = balls.Where(d => d.ExtraType == ExtraType.LegBye).Sum(d => d.ExtraRuns);
        var bits = new List<string>();
        if (wd > 0) bits.Add($"wd {wd}");
        if (nb > 0) bits.Add($"nb {nb}");
        if (b > 0) bits.Add($"b {b}");
        if (lb > 0) bits.Add($"lb {lb}");
        return bits.Count == 0 ? $"Extras {total}" : $"Extras {total} ({string.Join(", ", bits)})";
    }

    private static string FormatFallOfWickets(IReadOnlyList<Delivery> balls, Func<Guid, string> nameOf)
    {
        var runs = 0;
        var wkts = 0;
        var legal = 0;
        var parts = new List<string>();
        foreach (var d in balls)
        {
            runs += d.RunsOffBat + d.ExtraRuns;
            if (d.IsLegalDelivery)
                legal++;
            if (!d.IsWicket)
                continue;
            wkts++;
            var whoId = d.DismissedPlayerId is Guid id && id != Guid.Empty ? id : d.BatterId;
            parts.Add($"{runs}/{wkts} ({nameOf(whoId)}, {legal / 6}.{legal % 6})");
        }

        return parts.Count == 0 ? string.Empty : $"FOW: {string.Join(" · ", parts)}";
    }

    private static InningsScorecardDto BuildInningsDto(
        Innings inn,
        List<Delivery> deliveries,
        Dictionary<Guid, string> teams,
        Func<Guid, string> rawNameOf,
        Guid? battingCaptainId = null,
        Guid? bowlingCaptainId = null,
        IReadOnlyList<(Guid Id, string Name, bool IsCaptain)>? battingSquad = null,
        IReadOnlyList<(Guid Id, string Name, bool IsCaptain)>? bowlingSquad = null)
    {
        string NameOf(Guid id)
        {
            var n = rawNameOf(id);
            if (battingCaptainId == id || bowlingCaptainId == id)
                return $"{n} (C)";
            return n;
        }

        var battingOrder = new List<Guid>();
        foreach (var d in deliveries)
        {
            if (!battingOrder.Contains(d.BatterId))
                battingOrder.Add(d.BatterId);
        }

        var batting = new List<BattingRowDto>();
        foreach (var pid in battingOrder)
        {
            var faced = deliveries.Where(d => d.BatterId == pid).ToList();
            var outDelivery = deliveries.LastOrDefault(d => d.IsWicket && d.DismissedPlayerId == pid);
            batting.Add(new BattingRowDto
            {
                PlayerId = pid,
                Name = rawNameOf(pid),
                Runs = faced.Sum(d => d.RunsOffBat),
                Balls = faced.Count(d => d.IsLegalDelivery),
                Fours = faced.Count(d => d.RunsOffBat == 4),
                Sixes = faced.Count(d => d.RunsOffBat == 6),
                IsOut = outDelivery is not null,
                IsCaptain = battingCaptainId == pid,
                Dismissal = outDelivery is null ? string.Empty : FormatDismissal(outDelivery, NameOf)
            });
        }

        var bowlingOrder = new List<Guid>();
        foreach (var d in deliveries)
        {
            if (!bowlingOrder.Contains(d.BowlerId))
                bowlingOrder.Add(d.BowlerId);
        }

        var bowling = new List<BowlingRowDto>();
        foreach (var pid in bowlingOrder)
        {
            var bowled = deliveries.Where(d => d.BowlerId == pid).ToList();
            bowling.Add(new BowlingRowDto
            {
                PlayerId = pid,
                Name = rawNameOf(pid),
                BallsBowled = bowled.Count(d => d.IsLegalDelivery),
                RunsConceded = bowled.Sum(d => d.RunsOffBat + d.ExtraRuns),
                Wickets = bowled.Count(d =>
                    d.IsWicket && d.WicketType is not WicketType.RunOut and not WicketType.Retired and not WicketType.None),
                Maidens = CountMaidens(bowled),
                IsCaptain = bowlingCaptainId == pid
            });
        }

        return new InningsScorecardDto
        {
            InningsNumber = inn.InningsNumber,
            BattingTeamId = inn.BattingTeamId,
            BowlingTeamId = inn.BowlingTeamId,
            BattingTeamName = teams.GetValueOrDefault(inn.BattingTeamId, "?"),
            BowlingTeamName = teams.GetValueOrDefault(inn.BowlingTeamId, "?"),
            IsFollowOn = inn.IsFollowOn,
            Extras = deliveries.Sum(d => d.ExtraRuns),
            TotalRuns = deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns),
            Wickets = deliveries.Count(d => d.IsWicket),
            Batting = batting,
            Bowling = bowling,
            DidNotBatText = FormatDidNotLine("Did not bat", battingSquad, battingOrder),
            DidNotBowlText = FormatDidNotLine("Did not bowl", bowlingSquad, bowlingOrder),
            OversFaced = $"{deliveries.Count(d => d.IsLegalDelivery) / 6}.{deliveries.Count(d => d.IsLegalDelivery) % 6}",
            ExtrasText = FormatExtrasBreakdown(deliveries),
            FallOfWicketsText = FormatFallOfWickets(deliveries, rawNameOf)
        };
    }

    private static string FormatDidNotLine(
        string label,
        IReadOnlyList<(Guid Id, string Name, bool IsCaptain)>? squad,
        IEnumerable<Guid> usedIds)
    {
        if (squad is null || squad.Count == 0)
            return string.Empty;

        var used = usedIds as HashSet<Guid> ?? usedIds.ToHashSet();
        var leftover = squad
            .Where(p => !used.Contains(p.Id) && !string.IsNullOrWhiteSpace(p.Name) && p.Name != "?")
            .Select(p => p.IsCaptain ? $"{p.Name} (C)" : p.Name)
            .ToList();
        return leftover.Count == 0
            ? string.Empty
            : $"{label}: {string.Join(", ", leftover)}";
    }

    private static int CountMaidens(List<Delivery> bowled)
    {
        var maidens = 0;
        var legalInOver = 0;
        var runsInOver = 0;
        foreach (var d in bowled.OrderBy(x => x.Sequence))
        {
            runsInOver += d.RunsOffBat + d.ExtraRuns;
            if (!d.IsLegalDelivery)
                continue;
            legalInOver++;
            if (legalInOver < 6)
                continue;
            if (runsInOver == 0)
                maidens++;
            legalInOver = 0;
            runsInOver = 0;
        }
        return maidens;
    }

    private static string FormatDismissal(Delivery d, Func<Guid, string> nameOf)
    {
        var how = d.WicketType switch
        {
            WicketType.Bowled => "b",
            WicketType.Caught => d.FielderId is Guid f ? $"c {nameOf(f)} b" : "c b",
            WicketType.Lbw => "lbw b",
            WicketType.RunOut => d.FielderId is Guid f ? $"run out ({nameOf(f)})" : "run out",
            WicketType.Stumped => d.FielderId is Guid f ? $"st {nameOf(f)} b" : "st b",
            WicketType.HitWicket => "hit wicket b",
            WicketType.Retired => "retired",
            _ => "out"
        };

        if (d.WicketType is WicketType.RunOut or WicketType.Retired)
            return how;

        return $"{how} {nameOf(d.BowlerId)}";
    }

    private static (int Runs, int Wickets) TeamTotals(IReadOnlyList<Innings> inns, Guid teamId)
    {
        var runs = 0;
        var wickets = 0;
        foreach (var inn in inns.Where(i => i.BattingTeamId == teamId))
        {
            foreach (var d in inn.Deliveries)
            {
                runs += d.RunsOffBat + d.ExtraRuns;
                if (d.IsWicket) wickets++;
            }
        }

        return (runs, wickets);
    }

    /// <summary>
    /// Batting-first winner → "won by X runs". Chasing winner → "won by X wickets"
    /// (remaining wickets = squad size, minus 1 if last man cannot bat alone).
    /// </summary>
    private static (string Text, Guid? WinnerId) BuildResult(
        Match match,
        string teamA,
        string teamB,
        int aRuns,
        int aWkts,
        int bRuns,
        int bWkts,
        Guid? firstBattingTeamId,
        int teamASquadSize,
        int teamBSquadSize)
    {
        if (aRuns == bRuns)
            return ("Match tied — both teams level", null);

        Guid winnerId;
        string winName;
        int winRuns, winWkts, loseRuns;
        if (aRuns > bRuns)
        {
            winnerId = match.TeamAId;
            winName = teamA;
            winRuns = aRuns;
            winWkts = aWkts;
            loseRuns = bRuns;
        }
        else
        {
            winnerId = match.TeamBId;
            winName = teamB;
            winRuns = bRuns;
            winWkts = bWkts;
            loseRuns = aRuns;
        }

        var chased = firstBattingTeamId is Guid first && first != winnerId;
        if (chased)
        {
            var squadSize = winnerId == match.TeamAId ? teamASquadSize : teamBSquadSize;
            var remaining = MatchResultRules.WicketsRemaining(
                squadSize, winWkts, match.LastBatsmanCanBatAsSingleWicket);
            var unit = remaining == 1 ? "wicket" : "wickets";
            return ($"{winName} won by {remaining} {unit}", winnerId);
        }

        var margin = winRuns - loseRuns;
        var runUnit = margin == 1 ? "run" : "runs";
        return ($"{winName} won by {margin} {runUnit}", winnerId);
    }
}

[ApiController]
[Route("api/netsessions")]
public sealed class NetSessionsController : ControllerBase
{
    private readonly ApiDbContext _db;

    public NetSessionsController(ApiDbContext db) => _db = db;

    /// <summary>Completed nets sessions only — setup/live scoring stays on devices and arrives via sync.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NetSessionSummaryDto>>> GetCompleted(CancellationToken ct)
    {
        var sessions = await _db.NetSessions.AsNoTracking()
            .Where(s => !s.IsDeleted && s.Status == NetSessionStatus.Completed)
            .OrderByDescending(s => s.SessionDateUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (sessions.Count == 0)
            return Ok(Array.Empty<NetSessionSummaryDto>());

        var ids = sessions.Select(s => s.Id).ToList();
        var balls = await _db.NetDeliveries.AsNoTracking()
            .Where(d => ids.Contains(d.SessionId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var bySession = balls.GroupBy(d => d.SessionId).ToDictionary(g => g.Key, g => g.ToList());

        var list = sessions.Select(s =>
        {
            bySession.TryGetValue(s.Id, out var d);
            d ??= new List<NetDelivery>();
            var legal = d.Count(x => x.IsLegalDelivery);
            return new NetSessionSummaryDto(
                s.Id,
                s.Name,
                s.SessionDateUtc,
                (byte)s.Status,
                CountRosterPlayers(s.PlayerRosterJson),
                d.Sum(x => x.RunsOffBat + x.ExtraRuns),
                d.Count(x => x.IsWicket),
                legal / 6,
                legal % 6);
        }).ToList();

        return Ok(list);
    }

    [HttpGet("{id:guid}/scorecard")]
    public async Task<ActionResult<NetSessionScorecardDto>> GetScorecard(Guid id, CancellationToken ct)
    {
        var session = await _db.NetSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted, ct)
            .ConfigureAwait(false);
        if (session is null)
            return NotFound();

        var deliveries = await _db.NetDeliveries.AsNoTracking()
            .Where(d => d.SessionId == id)
            .OrderBy(d => d.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var names = await LoadNetPlayerNamesAsync(session, deliveries, ct).ConfigureAwait(false);
        string NameOf(Guid pid) => names.GetValueOrDefault(pid, "?");

        var battingOrder = new List<Guid>();
        var bowlingOrder = new List<Guid>();
        foreach (var d in deliveries)
        {
            if (d.BatterId != Guid.Empty && !battingOrder.Contains(d.BatterId))
                battingOrder.Add(d.BatterId);
            if (d.BowlerId != Guid.Empty && !bowlingOrder.Contains(d.BowlerId))
                bowlingOrder.Add(d.BowlerId);
        }

        var batting = new List<NetScorecardBattingDto>();
        foreach (var pid in battingOrder)
        {
            var spells = SplitNetBatterSpells(deliveries, pid);
            foreach (var faced in spells)
            {
                var isOut = faced.Count > 0
                            && faced[^1].IsWicket
                            && faced[^1].DismissedPlayerId == pid;
                var outBall = isOut ? faced[^1] : null;
                batting.Add(new NetScorecardBattingDto
                {
                    PlayerId = pid,
                    Name = NameOf(pid),
                    Runs = faced.Sum(d => d.RunsOffBat),
                    Balls = faced.Count(d => d.IsLegalDelivery),
                    Fours = faced.Count(d => d.RunsOffBat == 4),
                    Sixes = faced.Count(d => d.RunsOffBat == 6),
                    IsOut = isOut,
                    Outs = isOut ? 1 : 0,
                    Dismissal = outBall is null ? string.Empty : FormatNetDismissal(outBall, NameOf)
                });
            }
        }

        var bowling = bowlingOrder.Select(pid =>
        {
            var bowled = deliveries.Where(d => d.BowlerId == pid).ToList();
            var legal = bowled.Count(d => d.IsLegalDelivery);
            return new NetScorecardBowlingDto
            {
                PlayerId = pid,
                Name = NameOf(pid),
                BallsBowled = legal,
                RunsConceded = bowled.Sum(d => d.RunsOffBat + d.ExtraRuns),
                Wickets = bowled.Count(d => d.IsWicket && d.WicketType is not WicketType.RunOut and not WicketType.Retired),
                Maidens = CountNetMaidens(bowled)
            };
        }).ToList();

        var legalAll = deliveries.Count(d => d.IsLegalDelivery);
        return Ok(new NetSessionScorecardDto
        {
            SessionId = session.Id,
            Name = session.Name,
            MetaLine = session.SessionDateUtc.ToString("dd MMM yyyy"),
            TotalRuns = deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns),
            Wickets = deliveries.Count(d => d.IsWicket),
            Extras = deliveries.Sum(d => d.ExtraRuns),
            OversText = $"{legalAll / 6}.{legalAll % 6}",
            Batting = batting,
            Bowling = bowling
        });
    }

    private async Task<Dictionary<Guid, string>> LoadNetPlayerNamesAsync(
        NetSession session, List<NetDelivery> deliveries, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        try
        {
            if (!string.IsNullOrWhiteSpace(session.PlayerRosterJson))
            {
                using var doc = JsonDocument.Parse(session.PlayerRosterJson);
                if (doc.RootElement.TryGetProperty("players", out var players)
                    || doc.RootElement.TryGetProperty("Players", out players))
                {
                    foreach (var p in players.EnumerateArray())
                    {
                        Guid id = Guid.Empty;
                        if (p.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                            Guid.TryParse(idEl.GetString(), out id);
                        else if (p.TryGetProperty("Id", out idEl) && idEl.ValueKind == JsonValueKind.String)
                            Guid.TryParse(idEl.GetString(), out id);
                        var name = p.TryGetProperty("fullName", out var n) ? n.GetString()
                            : p.TryGetProperty("FullName", out n) ? n.GetString() : null;
                        if (id != Guid.Empty && !string.IsNullOrWhiteSpace(name))
                            names[id] = name;
                    }
                }
            }
        }
        catch
        {
            /* roster optional */
        }

        var ids = names.Keys
            .Concat(deliveries.SelectMany(d => new[]
            {
                d.BatterId, d.BowlerId, d.DismissedPlayerId ?? Guid.Empty, d.FielderId ?? Guid.Empty
            }))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
            return names;

        var fromDb = await _db.Players.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
            .ConfigureAwait(false);
        foreach (var kv in fromDb)
            names[kv.Key] = kv.Value;
        return names;
    }

    private static int CountRosterPlayers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("players", out var players)
                || doc.RootElement.TryGetProperty("Players", out players))
                return players.GetArrayLength();
        }
        catch
        {
            /* ignore */
        }
        return 0;
    }

    private static int CountNetMaidens(List<NetDelivery> bowled)
    {
        var maidens = 0;
        var legalInOver = 0;
        var runsInOver = 0;
        foreach (var d in bowled.OrderBy(x => x.Sequence))
        {
            runsInOver += d.RunsOffBat + d.ExtraRuns;
            if (!d.IsLegalDelivery)
                continue;
            legalInOver++;
            if (legalInOver < 6)
                continue;
            if (runsInOver == 0)
                maidens++;
            legalInOver = 0;
            runsInOver = 0;
        }
        return maidens;
    }

    private static List<List<NetDelivery>> SplitNetBatterSpells(IReadOnlyList<NetDelivery> deliveries, Guid pid)
    {
        var spells = new List<List<NetDelivery>>();
        List<NetDelivery>? current = null;
        foreach (var d in deliveries)
        {
            if (d.BatterId != pid)
                continue;
            current ??= new List<NetDelivery>();
            current.Add(d);
            if (d.IsWicket && d.DismissedPlayerId == pid)
            {
                spells.Add(current);
                current = null;
            }
        }

        if (current is { Count: > 0 })
            spells.Add(current);
        return spells;
    }

    private static string FormatNetDismissal(NetDelivery d, Func<Guid, string> nameOf)
    {
        var how = d.WicketType switch
        {
            WicketType.Bowled => "b",
            WicketType.Caught => d.FielderId is Guid f ? $"c {nameOf(f)} b" : "c b",
            WicketType.Lbw => "lbw b",
            WicketType.RunOut => d.FielderId is Guid f ? $"run out ({nameOf(f)})" : "run out",
            WicketType.Stumped => d.FielderId is Guid f ? $"st {nameOf(f)} b" : "st b",
            WicketType.HitWicket => "hit wicket b",
            WicketType.Retired => "retired",
            _ => "out"
        };
        if (d.WicketType is WicketType.RunOut or WicketType.Retired)
            return how;
        return $"{how} {nameOf(d.BowlerId)}";
    }
}

[ApiController]
[Route("api/sync")]
public sealed class SyncController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ApiDbContext _db;
    private readonly ILiveFeed _live;

    public SyncController(ApiDbContext db, ILiveFeed live)
    {
        _db = db;
        _live = live;
    }

    [HttpPost]
    public async Task<ActionResult<SyncPushResponse>> Push([FromBody] SyncPushRequest request, CancellationToken ct)
    {
        if (request.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(request.EntityType))
            return BadRequest(new SyncPushResponse(false, "Invalid sync payload."));

        var isLive = string.Equals(Request.Headers["X-Jpl-Live"].ToString(), "1", StringComparison.Ordinal);

        // Idempotent: same key = already applied.
        var exists = await _db.SyncReceipts.AnyAsync(x => x.IdempotencyKey == request.IdempotencyKey, ct)
            .ConfigureAwait(false);
        if (exists)
        {
            if (isLive)
                await _live.NotifyAfterApplyAsync(request, ct).ConfigureAwait(false);
            return Ok(new SyncPushResponse(true, "Duplicate ignored."));
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        switch (request.EntityType)
        {
            case "Player":
                await ApplyPlayerAsync(request, ct).ConfigureAwait(false);
                break;
            case "Team":
                await ApplyTeamAsync(request, ct).ConfigureAwait(false);
                break;
            case "Match":
                await ApplyMatchAsync(request, ct).ConfigureAwait(false);
                break;
            case "Innings":
                await ApplyInningsAsync(request, ct).ConfigureAwait(false);
                break;
            case "Delivery":
                await ApplyDeliveryAsync(request, ct).ConfigureAwait(false);
                break;
            case "NetSession":
                await ApplyNetSessionAsync(request, ct).ConfigureAwait(false);
                break;
            case "NetDelivery":
                await ApplyNetDeliveryAsync(request, ct).ConfigureAwait(false);
                break;
            default:
                return BadRequest(new SyncPushResponse(false, $"Unknown entity type: {request.EntityType}"));
        }

        _db.SyncReceipts.Add(new SyncReceipt
        {
            IdempotencyKey = request.IdempotencyKey,
            EntityType = request.EntityType,
            EntityId = request.EntityId
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        if (isLive)
            await _live.NotifyAfterApplyAsync(request, ct).ConfigureAwait(false);

        return Ok(new SyncPushResponse(true, "Applied."));
    }

    private async Task ApplyPlayerAsync(SyncPushRequest request, CancellationToken ct)
    {
        if (string.Equals(request.Operation, "Delete", StringComparison.OrdinalIgnoreCase))
        {
            var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == request.EntityId, ct).ConfigureAwait(false);
            if (player is not null)
            {
                player.IsDeleted = true;
                player.UpdatedAtUtc = DateTime.Now;
                player.Version++;
            }
            return;
        }

        var dto = JsonSerializer.Deserialize<PlayerDto>(request.PayloadJson, JsonOptions)
                  ?? throw new InvalidOperationException("Invalid player payload.");

        var existing = await _db.Players.FirstOrDefaultAsync(p => p.Id == dto.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            _db.Players.Add(new Player
            {
                Id = dto.Id,
                FullName = dto.FullName,
                IsActive = dto.IsActive,
                NickName = dto.NickName,
                PlayingRole = dto.PlayingRole,
                BattingStyle = dto.BattingStyle,
                BowlingStyle = dto.BowlingStyle,
                Bio = dto.Bio,
                Version = dto.Version,
                UpdatedAtUtc = dto.UpdatedAtUtc,
                IsDeleted = dto.IsDeleted,
                CreatedAtUtc = DateTime.Now
            });
            return;
        }

        if (dto.Version < existing.Version)
            return; // older write ignored — no lock conflict

        existing.FullName = dto.FullName;
        existing.IsActive = dto.IsActive;
        existing.NickName = dto.NickName;
        existing.PlayingRole = dto.PlayingRole;
        existing.BattingStyle = dto.BattingStyle;
        existing.BowlingStyle = dto.BowlingStyle;
        existing.Bio = dto.Bio;
        existing.Version = dto.Version;
        existing.UpdatedAtUtc = dto.UpdatedAtUtc;
        existing.IsDeleted = dto.IsDeleted;
    }

    private async Task ApplyTeamAsync(SyncPushRequest request, CancellationToken ct)
    {
        if (string.Equals(request.Operation, "Delete", StringComparison.OrdinalIgnoreCase))
        {
            var team = await _db.Teams.Include(t => t.TeamPlayers)
                .FirstOrDefaultAsync(t => t.Id == request.EntityId, ct)
                .ConfigureAwait(false);
            if (team is not null)
            {
                team.IsDeleted = true;
                team.CaptainPlayerId = null;
                team.UpdatedAtUtc = DateTime.Now;
                team.Version++;
            }
            return;
        }

        using var doc = JsonDocument.Parse(request.PayloadJson);
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetGuid();
        var name = root.GetProperty("name").GetString() ?? string.Empty;
        Guid? captainId = root.TryGetProperty("captainPlayerId", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetGuid() : null;
        var version = root.TryGetProperty("version", out var v) ? v.GetInt64() : 1;
        var updated = root.TryGetProperty("updatedAtUtc", out var u) ? u.GetDateTime() : DateTime.Now;
        var isDeleted = root.TryGetProperty("isDeleted", out var d) && d.GetBoolean();
        var isActive = !root.TryGetProperty("isActive", out var ia) || ia.ValueKind == JsonValueKind.Null || ia.GetBoolean();
        var playerIds = new List<Guid>();
        if (root.TryGetProperty("playerIds", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
                playerIds.Add(item.GetGuid());
        }

        List<Guid>? playingIds = null;
        if (root.TryGetProperty("playingPlayerIds", out var playingArr) && playingArr.ValueKind == JsonValueKind.Array)
        {
            playingIds = new List<Guid>();
            foreach (var item in playingArr.EnumerateArray())
                playingIds.Add(item.GetGuid());
        }

        playerIds = TeamRoster.Distinct(playerIds);
        if (!TeamRoster.TryResolvePlaying(playerIds, playingIds, out var playingSet, out _))
            playingSet = playerIds.ToHashSet();

        var existing = await _db.Teams.Include(t => t.TeamPlayers)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var team = new Team
            {
                Id = id,
                Name = name,
                CaptainPlayerId = captainId,
                IsActive = isActive,
                Version = version,
                UpdatedAtUtc = updated,
                IsDeleted = isDeleted,
                CreatedAtUtc = DateTime.Now
            };
            _db.Teams.Add(team);
            foreach (var pid in playerIds)
            {
                _db.TeamPlayers.Add(new TeamPlayer
                {
                    TeamId = id,
                    PlayerId = pid,
                    IsPlaying = playingSet.Contains(pid)
                });
            }
            return;
        }

        if (version < existing.Version)
            return;

        existing.Name = name;
        existing.CaptainPlayerId = captainId;
        existing.IsActive = isActive;
        existing.Version = version;
        existing.UpdatedAtUtc = updated;
        existing.IsDeleted = isDeleted;

        var desired = playerIds.ToHashSet();
        foreach (var remove in existing.TeamPlayers.Where(tp => !desired.Contains(tp.PlayerId)).ToList())
            _db.TeamPlayers.Remove(remove);

        foreach (var tp in existing.TeamPlayers)
            tp.IsPlaying = playingSet.Contains(tp.PlayerId);

        var have = existing.TeamPlayers.Select(tp => tp.PlayerId).ToHashSet();
        foreach (var add in desired.Where(pid => !have.Contains(pid)))
            _db.TeamPlayers.Add(new TeamPlayer
            {
                TeamId = id,
                PlayerId = add,
                IsPlaying = playingSet.Contains(add)
            });
    }

    private async Task ApplyMatchAsync(SyncPushRequest request, CancellationToken ct)
    {
        if (string.Equals(request.Operation, "Delete", StringComparison.OrdinalIgnoreCase))
        {
            var match = await _db.Matches.FirstOrDefaultAsync(m => m.Id == request.EntityId, ct).ConfigureAwait(false);
            if (match is not null)
            {
                if (match.StartedByUserId is Guid deleteOwner
                    && TryGetSyncCaller(out var deleteCaller)
                    && deleteOwner != deleteCaller)
                    throw new InvalidOperationException("Only the admin who started this match can delete it.");
                match.IsDeleted = true;
                match.UpdatedAtUtc = DateTime.Now;
                match.Version++;
            }
            return;
        }

        var dto = JsonSerializer.Deserialize<Match>(request.PayloadJson, JsonOptions)
                  ?? throw new InvalidOperationException("Invalid match payload.");

        // Match Ids are client-generated Guids — phone A and phone B never collide.
        var existing = await _db.Matches.FirstOrDefaultAsync(m => m.Id == dto.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            dto.Innings = new List<Innings>();
            if (TryGetSyncCaller(out var creatorId))
            {
                dto.StartedByUserId ??= creatorId;
                if (dto.StartedByUserId != creatorId)
                    throw new InvalidOperationException("Only the signed-in admin can publish a new match.");
            }
            await EnsureMatchFormatAsync(dto, ct).ConfigureAwait(false);
            _db.Matches.Add(dto);
            if (dto.TournamentFixtureId is Guid newFixtureId)
            {
                var fixture = await _db.TournamentFixtures.FirstOrDefaultAsync(f => f.Id == newFixtureId, ct)
                    .ConfigureAwait(false);
                if (fixture is not null)
                    fixture.MatchId = dto.Id;
            }
            if (dto.SeriesFixtureId is Guid newSeriesFixtureId)
            {
                var seriesFixture = await _db.SeriesFixtures.FirstOrDefaultAsync(f => f.Id == newSeriesFixtureId, ct)
                    .ConfigureAwait(false);
                if (seriesFixture is not null)
                    seriesFixture.MatchId = dto.Id;
            }
            return;
        }

        if (dto.Version < existing.Version)
            return;

        if (existing.StartedByUserId is Guid ownerId
            && TryGetSyncCaller(out var callerId)
            && ownerId != callerId)
            throw new InvalidOperationException("Only the admin who started this match can update it.");

        if (existing.StartedOnDeviceId is Guid deviceId
            && dto.StartedOnDeviceId is Guid incomingDevice
            && incomingDevice != deviceId)
            throw new InvalidOperationException("This match can only be updated from the device that started it.");

        existing.Title = dto.Title;
        existing.TeamAId = dto.TeamAId;
        existing.TeamBId = dto.TeamBId;
        existing.TeamACaptainId = dto.TeamACaptainId;
        existing.TeamBCaptainId = dto.TeamBCaptainId;
        existing.OversLimit = dto.OversLimit;
        existing.OversMode = dto.OversMode;
        existing.Format = dto.Format;
        existing.FormatId = dto.FormatId;
        await EnsureMatchFormatAsync(existing, ct).ConfigureAwait(false);
        existing.MatchDateUtc = dto.MatchDateUtc;
        existing.Venue = dto.Venue;
        existing.Status = dto.Status;
        existing.TossWinnerTeamId = dto.TossWinnerTeamId;
        existing.TossWinnerBatsFirst = dto.TossWinnerBatsFirst;
        existing.LastBatsmanCanBatAsSingleWicket = dto.LastBatsmanCanBatAsSingleWicket;
        existing.BowlerMaxOvers = dto.BowlerMaxOvers;
        existing.PlayerOfTheMatchPlayerId = dto.PlayerOfTheMatchPlayerId;
        existing.TournamentId = dto.TournamentId;
        existing.TournamentFixtureId = dto.TournamentFixtureId;
        existing.SeriesId = dto.SeriesId;
        existing.SeriesFixtureId = dto.SeriesFixtureId;
        existing.Version = dto.Version;
        existing.UpdatedAtUtc = dto.UpdatedAtUtc;
        existing.IsDeleted = dto.IsDeleted;
        if (!string.IsNullOrWhiteSpace(dto.SquadRosterJson))
            existing.SquadRosterJson = dto.SquadRosterJson;
        if (HasJsonProperty(request.PayloadJson, "liveStreamUrl"))
            existing.LiveStreamUrl = dto.LiveStreamUrl;
        if (HasJsonProperty(request.PayloadJson, "isLiveTelecast"))
            existing.IsLiveTelecast = dto.IsLiveTelecast;
        existing.StartedByUserId ??= dto.StartedByUserId;
        existing.StartedOnDeviceId ??= dto.StartedOnDeviceId;

        if (dto.TournamentFixtureId is Guid fixtureId && dto.Status == MatchStatus.Completed)
        {
            var fixture = await _db.TournamentFixtures.FirstOrDefaultAsync(f => f.Id == fixtureId, ct)
                .ConfigureAwait(false);
            if (fixture is not null && fixture.MatchId != dto.Id)
                fixture.MatchId = dto.Id;
        }

        if (dto.SeriesFixtureId is Guid seriesFixtureId && dto.Status == MatchStatus.Completed)
        {
            var seriesFixture = await _db.SeriesFixtures.FirstOrDefaultAsync(f => f.Id == seriesFixtureId, ct)
                .ConfigureAwait(false);
            if (seriesFixture is not null && seriesFixture.MatchId != dto.Id)
                seriesFixture.MatchId = dto.Id;
        }
    }

    private static bool HasJsonProperty(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            /* ignore */
        }

        return false;
    }

    private async Task EnsureMatchFormatAsync(Match match, CancellationToken ct)
    {
        if (match.FormatId is Guid id && id != Guid.Empty
            && await _db.Formats.AnyAsync(f => f.Id == id, ct).ConfigureAwait(false))
            return;

        match.FormatId = await FormatCatalog.EnsureAsync(
            _db, match.Format, match.OversMode, match.OversLimit, ct).ConfigureAwait(false);
    }

    private bool TryGetSyncCaller(out Guid userId)
    {
        userId = Guid.Empty;
        if (HttpContext.Items.TryGetValue("JplUserId", out var idObj) && idObj is Guid g && g != Guid.Empty)
        {
            userId = g;
            return true;
        }

        var idText = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(idText, out userId) && userId != Guid.Empty;
    }

    private async Task EnsureMatchScoringCallerAsync(Guid matchId, CancellationToken ct)
    {
        if (!TryGetSyncCaller(out var callerId))
            return;
        var match = await _db.Matches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId && !m.IsDeleted, ct)
            .ConfigureAwait(false);
        if (match?.StartedByUserId is Guid ownerId && ownerId != callerId)
            throw new InvalidOperationException("Only the admin who started this match can update it.");
    }

    private async Task ApplyInningsAsync(SyncPushRequest request, CancellationToken ct)
    {
        var dto = JsonSerializer.Deserialize<Innings>(request.PayloadJson, JsonOptions)
                  ?? throw new InvalidOperationException("Invalid innings payload.");

        await EnsureMatchScoringCallerAsync(dto.MatchId, ct).ConfigureAwait(false);
        await EnsureMatchPlaceholderAsync(dto.MatchId, ct).ConfigureAwait(false);

        var existing = await _db.Innings.FirstOrDefaultAsync(i => i.Id == dto.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            dto.Match = null!;
            dto.Deliveries = new List<Delivery>();
            _db.Innings.Add(dto);
            return;
        }

        existing.MatchId = dto.MatchId;
        existing.InningsNumber = dto.InningsNumber;
        existing.BattingTeamId = dto.BattingTeamId;
        existing.BowlingTeamId = dto.BowlingTeamId;
        existing.IsCompleted = dto.IsCompleted;
        existing.IsFollowOn = dto.IsFollowOn;
        existing.StartedAtUtc = dto.StartedAtUtc;
    }

    private async Task ApplyDeliveryAsync(SyncPushRequest request, CancellationToken ct)
    {
        if (string.Equals(request.Operation, "Delete", StringComparison.OrdinalIgnoreCase))
        {
            var row = await _db.Deliveries.FirstOrDefaultAsync(d => d.Id == request.EntityId, ct).ConfigureAwait(false);
            if (row is not null)
            {
                await EnsureMatchScoringCallerAsync(row.MatchId, ct).ConfigureAwait(false);
                _db.Deliveries.Remove(row);
            }
            return;
        }

        var dto = JsonSerializer.Deserialize<Delivery>(request.PayloadJson, JsonOptions)
                  ?? throw new InvalidOperationException("Invalid delivery payload.");

        await EnsureMatchScoringCallerAsync(dto.MatchId, ct).ConfigureAwait(false);
        // Isolate by MatchId + Delivery.Id — concurrent matches on other phones are untouched.
        await EnsureMatchPlaceholderAsync(dto.MatchId, ct).ConfigureAwait(false);
        await EnsureInningsPlaceholderAsync(dto.MatchId, dto.InningsId, ct).ConfigureAwait(false);

        var existing = await _db.Deliveries.FirstOrDefaultAsync(d => d.Id == dto.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            // Undo then a new ball reuses the same innings sequence with a new Id.
            // If the delete is still in flight, drop the old row so unique(sequence) cannot 500.
            var clash = await _db.Deliveries.FirstOrDefaultAsync(
                    d => d.InningsId == dto.InningsId && d.Sequence == dto.Sequence && d.Id != dto.Id,
                    ct)
                .ConfigureAwait(false);
            if (clash is not null)
                _db.Deliveries.Remove(clash);

            dto.Innings = null!;
            _db.Deliveries.Add(dto);
            return;
        }

        if (dto.Version < existing.Version)
            return;

        existing.MatchId = dto.MatchId;
        existing.InningsId = dto.InningsId;
        existing.Sequence = dto.Sequence;
        existing.OverNumber = dto.OverNumber;
        existing.BallInOver = dto.BallInOver;
        existing.BatterId = dto.BatterId;
        existing.NonStrikerId = dto.NonStrikerId;
        existing.BowlerId = dto.BowlerId;
        existing.RunsOffBat = dto.RunsOffBat;
        existing.ExtraRuns = dto.ExtraRuns;
        existing.ExtraType = dto.ExtraType;
        existing.IsWicket = dto.IsWicket;
        existing.WicketType = dto.WicketType;
        existing.DismissedPlayerId = dto.DismissedPlayerId;
        existing.FielderId = dto.FielderId;
        existing.IsLegalDelivery = dto.IsLegalDelivery;
        existing.ShotZone = dto.ShotZone;
        existing.Version = dto.Version;
    }

    private async Task ApplyNetSessionAsync(SyncPushRequest request, CancellationToken ct)
    {
        if (string.Equals(request.Operation, "Delete", StringComparison.OrdinalIgnoreCase))
        {
            var session = await _db.NetSessions.FirstOrDefaultAsync(s => s.Id == request.EntityId, ct)
                .ConfigureAwait(false);
            if (session is not null)
            {
                session.IsDeleted = true;
                session.UpdatedAtUtc = DateTime.Now;
                session.Version++;
            }
            return;
        }

        var dto = JsonSerializer.Deserialize<NetSession>(request.PayloadJson, JsonOptions)
                  ?? throw new InvalidOperationException("Invalid nets session payload.");

        var existing = await _db.NetSessions.FirstOrDefaultAsync(s => s.Id == dto.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            dto.Deliveries = new List<NetDelivery>();
            _db.NetSessions.Add(dto);
            return;
        }

        if (dto.Version < existing.Version)
            return;

        existing.Name = dto.Name;
        existing.SessionDateUtc = dto.SessionDateUtc;
        existing.Status = dto.Status;
        existing.PlayerRosterJson = dto.PlayerRosterJson;
        existing.Version = dto.Version;
        existing.UpdatedAtUtc = dto.UpdatedAtUtc;
        existing.IsDeleted = dto.IsDeleted;
    }

    private async Task ApplyNetDeliveryAsync(SyncPushRequest request, CancellationToken ct)
    {
        if (string.Equals(request.Operation, "Delete", StringComparison.OrdinalIgnoreCase))
        {
            var row = await _db.NetDeliveries.FirstOrDefaultAsync(d => d.Id == request.EntityId, ct)
                .ConfigureAwait(false);
            if (row is not null)
                _db.NetDeliveries.Remove(row);
            return;
        }

        var dto = JsonSerializer.Deserialize<NetDelivery>(request.PayloadJson, JsonOptions)
                  ?? throw new InvalidOperationException("Invalid nets delivery payload.");

        await EnsureNetSessionPlaceholderAsync(dto.SessionId, ct).ConfigureAwait(false);

        var existing = await _db.NetDeliveries.FirstOrDefaultAsync(d => d.Id == dto.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            var clash = await _db.NetDeliveries.FirstOrDefaultAsync(
                    d => d.SessionId == dto.SessionId && d.Sequence == dto.Sequence && d.Id != dto.Id,
                    ct)
                .ConfigureAwait(false);
            if (clash is not null)
                _db.NetDeliveries.Remove(clash);

            dto.Session = null!;
            _db.NetDeliveries.Add(dto);
            return;
        }

        if (dto.Version < existing.Version)
            return;

        existing.SessionId = dto.SessionId;
        existing.Sequence = dto.Sequence;
        existing.OverNumber = dto.OverNumber;
        existing.BallInOver = dto.BallInOver;
        existing.BatterId = dto.BatterId;
        existing.NonStrikerId = dto.NonStrikerId;
        existing.BowlerId = dto.BowlerId;
        existing.RunsOffBat = dto.RunsOffBat;
        existing.ExtraRuns = dto.ExtraRuns;
        existing.ExtraType = dto.ExtraType;
        existing.IsWicket = dto.IsWicket;
        existing.WicketType = dto.WicketType;
        existing.DismissedPlayerId = dto.DismissedPlayerId;
        existing.FielderId = dto.FielderId;
        existing.IsLegalDelivery = dto.IsLegalDelivery;
        existing.Version = dto.Version;
    }

    private async Task EnsureNetSessionPlaceholderAsync(Guid sessionId, CancellationToken ct)
    {
        if (await _db.NetSessions.AnyAsync(s => s.Id == sessionId, ct).ConfigureAwait(false))
            return;

        _db.NetSessions.Add(new NetSession
        {
            Id = sessionId,
            Name = "Pending sync session",
            SessionDateUtc = DateTime.Now,
            Status = NetSessionStatus.InProgress
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureMatchPlaceholderAsync(Guid matchId, CancellationToken ct)
    {
        if (await _db.Matches.AnyAsync(m => m.Id == matchId, ct).ConfigureAwait(false))
            return;

        _db.Matches.Add(new Match
        {
            Id = matchId,
            Title = "Pending sync match",
            TeamAId = matchId, // placeholder unique per match — never shared across phones
            TeamBId = Guid.NewGuid(),
            OversLimit = 20,
            Status = MatchStatus.InProgress
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureInningsPlaceholderAsync(Guid matchId, Guid inningsId, CancellationToken ct)
    {
        if (await _db.Innings.AnyAsync(i => i.Id == inningsId, ct).ConfigureAwait(false))
            return;

        _db.Innings.Add(new Innings
        {
            Id = inningsId,
            MatchId = matchId,
            InningsNumber = 1,
            BattingTeamId = matchId,
            BowlingTeamId = Guid.NewGuid()
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

[ApiController]
[Route("api/stats")]
public sealed class StatsController : ControllerBase
{
    private readonly ApiDbContext _db;

    public StatsController(ApiDbContext db) => _db = db;

    /// <summary>Full player leaderboard from server SQL database (synced match deliveries).</summary>
    [HttpGet("leaderboard")]
    public async Task<ActionResult<IReadOnlyList<PlayerCareerDto>>> Leaderboard(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toExclusiveUtc,
        [FromQuery] Guid? venueId,
        [FromQuery] string? source,
        [FromQuery] Guid[]? formatIds,
        CancellationToken ct)
    {
        if (fromUtc is DateTime from && toExclusiveUtc is DateTime to && from >= to)
            return BadRequest("From date must be before or equal to To date.");

        if (IsNetsSource(source))
        {
            return Ok(await StatsCalculator
                .BuildNetsLeaderboardAsync(_db, fromUtc, toExclusiveUtc, ct)
                .ConfigureAwait(false));
        }

        return Ok(await StatsCalculator
            .BuildLeaderboardAsync(_db, fromUtc, toExclusiveUtc, venueId, formatIds, ct)
            .ConfigureAwait(false));
    }

    /// <summary>Batting / Bowling / All-rounder / Fielder / Captain / Team rankings from server DB.</summary>
    [HttpGet("rankings")]
    public async Task<ActionResult<RankingsDto>> Rankings(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toExclusiveUtc,
        [FromQuery] Guid[]? formatIds,
        CancellationToken ct)
    {
        if (fromUtc is DateTime from && toExclusiveUtc is DateTime to && from >= to)
            return BadRequest("From date must be before or equal to To date.");

        return Ok(await StatsCalculator.BuildRankingsAsync(_db, fromUtc, toExclusiveUtc, formatIds, ct).ConfigureAwait(false));
    }

    /// <summary>Team table from all completed matches. Win 2 · Tie 1 · then NRR.</summary>
    [HttpGet("team-rankings")]
    public async Task<ActionResult<IReadOnlyList<RankingRowDto>>> TeamRankings(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toExclusiveUtc,
        [FromQuery] Guid[]? formatIds,
        CancellationToken ct)
    {
        if (fromUtc is DateTime from && toExclusiveUtc is DateTime to && from >= to)
            return BadRequest("From date must be before or equal to To date.");

        return Ok(await StatsCalculator.BuildTeamRankingsAsync(_db, fromUtc, toExclusiveUtc, formatIds, ct).ConfigureAwait(false));
    }

    /// <summary>Team leaderboard for Statistics · Teams (matches, NRR, tournaments, series).</summary>
    [HttpGet("teams")]
    public async Task<ActionResult<IReadOnlyList<TeamCareerDto>>> Teams(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toExclusiveUtc,
        [FromQuery] Guid? venueId,
        [FromQuery] Guid[]? formatIds,
        CancellationToken ct)
    {
        if (fromUtc is DateTime from && toExclusiveUtc is DateTime to && from >= to)
            return BadRequest("From date must be before or equal to To date.");

        return Ok(await StatsCalculator
            .BuildTeamLeaderboardAsync(_db, fromUtc, toExclusiveUtc, venueId, formatIds, ct)
            .ConfigureAwait(false));
    }

    [HttpGet("players/{playerId:guid}")]
    public async Task<ActionResult<PlayerCareerDto>> Player(
        Guid playerId,
        [FromQuery] string? source,
        CancellationToken ct)
    {
        var board = IsNetsSource(source)
            ? await StatsCalculator.BuildNetsLeaderboardAsync(_db, ct: ct).ConfigureAwait(false)
            : await StatsCalculator.BuildLeaderboardAsync(_db, ct: ct).ConfigureAwait(false);
        var row = board.FirstOrDefault(p => p.PlayerId == playerId);
        if (row is null)
        {
            var player = await _db.Players.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == playerId && !p.IsDeleted, ct)
                .ConfigureAwait(false);
            if (player is null)
                return NotFound();
            return Ok(new PlayerCareerDto { PlayerId = player.Id, PlayerName = player.FullName });
        }
        return Ok(row);
    }

    [HttpGet("confrontations/{playerId:guid}/vs-bowlers")]
    public async Task<ActionResult<IReadOnlyList<ConfrontationDto>>> BattingVsBowlers(
        Guid playerId,
        [FromQuery] string? source,
        CancellationToken ct)
        => Ok(IsNetsSource(source)
            ? await StatsCalculator.BuildNetsConfrontationsAsync(_db, playerId, asBatterPerspective: true, ct)
                .ConfigureAwait(false)
            : await StatsCalculator.BuildConfrontationsAsync(_db, playerId, asBatterPerspective: true, ct)
                .ConfigureAwait(false));

    [HttpGet("confrontations/{playerId:guid}/vs-batters")]
    public async Task<ActionResult<IReadOnlyList<ConfrontationDto>>> BowlingVsBatters(
        Guid playerId,
        [FromQuery] string? source,
        CancellationToken ct)
        => Ok(IsNetsSource(source)
            ? await StatsCalculator.BuildNetsConfrontationsAsync(_db, playerId, asBatterPerspective: false, ct)
                .ConfigureAwait(false)
            : await StatsCalculator.BuildConfrontationsAsync(_db, playerId, asBatterPerspective: false, ct)
                .ConfigureAwait(false));

    private static bool IsNetsSource(string? source)
        => string.Equals(source, "nets", StringComparison.OrdinalIgnoreCase);
}

[ApiController]
[Route("api/hall-of-fame")]
public sealed class HallOfFameController : ControllerBase
{
    private readonly ApiDbContext _db;

    public HallOfFameController(ApiDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<HallOfFameDto>> Get(
        [FromQuery] Guid[]? formatIds,
        CancellationToken ct)
    {
        var formatIdList = StatsCalculator.NormalizeFormatIds(formatIds);

        var matchQuery = _db.Matches.AsNoTracking()
            .Where(m => !m.IsDeleted && m.Status == MatchStatus.Completed);
        if (formatIdList is not null)
            matchQuery = matchQuery.Where(m => formatIdList.Contains(m.FormatId ?? Guid.Empty));
        var matches = await matchQuery.ToListAsync(ct).ConfigureAwait(false);

        var tournamentQuery = _db.Tournaments.AsNoTracking()
            .Where(t => !t.IsDeleted
                        && t.Status == TournamentStatus.Completed
                        && t.WinnerTeamId != null);
        if (formatIdList is not null)
            tournamentQuery = tournamentQuery.Where(t => formatIdList.Contains(t.FormatId ?? Guid.Empty));
        var tournaments = await tournamentQuery.ToListAsync(ct).ConfigureAwait(false);

        var potmCounts = new Dictionary<Guid, int>();
        var captainCounts = new Dictionary<Guid, int>();
        var tourChampCounts = new Dictionary<Guid, int>();
        var seriesChampCounts = new Dictionary<Guid, int>();
        var playerIds = new HashSet<Guid>();

        if (matches.Count > 0)
        {
            var matchIds = matches.Select(m => m.Id).ToList();
            var innings = await _db.Innings.AsNoTracking()
                .Where(i => matchIds.Contains(i.MatchId))
                .Include(i => i.Deliveries)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var innsByMatch = innings.GroupBy(i => i.MatchId).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var m in matches)
            {
                if (m.PlayerOfTheMatchPlayerId is Guid potm)
                    playerIds.Add(potm);
                if (m.TeamACaptainId is Guid ca)
                    playerIds.Add(ca);
                if (m.TeamBCaptainId is Guid cb)
                    playerIds.Add(cb);
            }

            foreach (var m in matches)
            {
                if (m.PlayerOfTheMatchPlayerId is Guid potmId)
                    potmCounts[potmId] = potmCounts.GetValueOrDefault(potmId) + 1;

                innsByMatch.TryGetValue(m.Id, out var matchInns);
                matchInns ??= new List<Innings>();
                var (aRuns, _) = TeamTotalsLocal(matchInns, m.TeamAId);
                var (bRuns, _) = TeamTotalsLocal(matchInns, m.TeamBId);
                if (aRuns == bRuns)
                    continue;
                var winnerTeamId = aRuns > bRuns ? m.TeamAId : m.TeamBId;
                var captainId = winnerTeamId == m.TeamAId ? m.TeamACaptainId : m.TeamBCaptainId;
                if (captainId is Guid capId)
                    captainCounts[capId] = captainCounts.GetValueOrDefault(capId) + 1;
            }
        }

        if (tournaments.Count > 0)
        {
            var winnerTeamIds = tournaments
                .Select(t => t.WinnerTeamId!.Value)
                .Distinct()
                .ToList();
            var teams = await _db.Teams.AsNoTracking()
                .Where(t => winnerTeamIds.Contains(t.Id) && !t.IsDeleted)
                .ToDictionaryAsync(t => t.Id, t => t, ct)
                .ConfigureAwait(false);

            foreach (var tour in tournaments)
            {
                if (!teams.TryGetValue(tour.WinnerTeamId!.Value, out var team)
                    || team.CaptainPlayerId is not Guid capId)
                    continue;
                playerIds.Add(capId);
                tourChampCounts[capId] = tourChampCounts.GetValueOrDefault(capId) + 1;
            }
        }

        var seriesQuery = _db.Series.AsNoTracking()
            .Where(s => !s.IsDeleted
                        && s.Status == SeriesStatus.Completed
                        && s.WinnerTeamId != null
                        && !s.IsTied);
        if (formatIdList is not null)
            seriesQuery = seriesQuery.Where(s => formatIdList.Contains(s.FormatId ?? Guid.Empty));
        var seriesList = await seriesQuery.ToListAsync(ct).ConfigureAwait(false);

        if (seriesList.Count > 0)
        {
            var winnerTeamIds = seriesList
                .Select(s => s.WinnerTeamId!.Value)
                .Distinct()
                .ToList();
            var teams = await _db.Teams.AsNoTracking()
                .Where(t => winnerTeamIds.Contains(t.Id) && !t.IsDeleted)
                .ToDictionaryAsync(t => t.Id, t => t, ct)
                .ConfigureAwait(false);

            foreach (var series in seriesList)
            {
                if (!teams.TryGetValue(series.WinnerTeamId!.Value, out var team)
                    || team.CaptainPlayerId is not Guid capId)
                    continue;
                playerIds.Add(capId);
                seriesChampCounts[capId] = seriesChampCounts.GetValueOrDefault(capId) + 1;
            }
        }

        var names = playerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Players.AsNoTracking()
                .Where(p => playerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
                .ConfigureAwait(false);

        static List<TrophyRowDto> ToRows(Dictionary<Guid, int> counts, Dictionary<Guid, string> names, string label)
            => counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => names.GetValueOrDefault(kv.Key, "?"), StringComparer.OrdinalIgnoreCase)
                .Select((kv, i) => new TrophyRowDto
                {
                    Rank = i + 1,
                    PlayerId = kv.Key,
                    PlayerName = names.GetValueOrDefault(kv.Key, "?"),
                    Trophies = kv.Value,
                    Detail = $"{kv.Value}× {label}"
                })
                .ToList();

        return Ok(new HallOfFameDto
        {
            PlayerOfTheMatch = ToRows(potmCounts, names, "Player of the Match"),
            CaptainTrophies = ToRows(captainCounts, names, "Captain win trophy"),
            TournamentChampionTrophies = ToRows(tourChampCounts, names, "Tournament champion (captain)"),
            SeriesChampionTrophies = ToRows(seriesChampCounts, names, "Series champion (captain)")
        });
    }

    private static (int Runs, int Wickets) TeamTotalsLocal(IReadOnlyList<Innings> inns, Guid teamId)
    {
        var runs = 0;
        var wickets = 0;
        foreach (var inn in inns.Where(i => i.BattingTeamId == teamId))
        {
            foreach (var d in inn.Deliveries)
            {
                runs += d.RunsOffBat + d.ExtraRuns;
                if (d.IsWicket) wickets++;
            }
        }

        return (runs, wickets);
    }
}

[ApiController]
[Route("api/auctions")]
public sealed class AuctionsController : ControllerBase
{
    private readonly ApiDbContext _db;

    public AuctionsController(ApiDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuctionDto>>> List(CancellationToken ct)
    {
        var list = await _db.Auctions.AsNoTracking()
            .Where(a => !a.IsDeleted)
            .Include(a => a.Captains)
            .Include(a => a.Lots)
            .OrderByDescending(a => a.AuctionDateUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return Ok(list.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AuctionDto>> Get(Guid id, CancellationToken ct)
    {
        var a = await _db.Auctions.AsNoTracking()
            .Include(x => x.Captains)
            .Include(x => x.Lots)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            .ConfigureAwait(false);
        return a is null ? NotFound() : Ok(ToDto(a));
    }

    [HttpGet("report")]
    public async Task<ActionResult<IReadOnlyList<AuctionReportRowDto>>> Report(CancellationToken ct)
    {
        var auctions = await _db.Auctions.AsNoTracking()
            .Where(a => !a.IsDeleted)
            .Include(a => a.Captains)
            .Include(a => a.Lots)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var names = await _db.Players.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
            .ConfigureAwait(false);

        return Ok(BuildAuctionReport(auctions, names));
    }

    [HttpPost]
    public async Task<ActionResult<AuctionDto>> Create([FromBody] AuctionUpsertRequest request, CancellationToken ct)
    {
        var err = Validate(request);
        if (err is not null) return BadRequest(err);

        var id = request.Id is Guid g && g != Guid.Empty ? g : Guid.NewGuid();
        var now = DateTime.Now;
        var entity = new Auction
        {
            Id = id,
            Title = request.Title.Trim(),
            AuctionDateUtc = request.AuctionDateUtc,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Captains = DedupCaptains(request.Captains)
                .Select(c => new AuctionCaptain
                {
                    AuctionId = id,
                    PlayerId = c.PlayerId,
                    WalletAmount = Math.Max(0, c.WalletAmount)
                }).ToList(),
            Lots = request.Lots.Select(l => MapLot(id, l)).ToList()
        };

        _db.Auctions.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AuctionDto>> Update(Guid id, [FromBody] AuctionUpsertRequest request, CancellationToken ct)
    {
        var err = Validate(request);
        if (err is not null) return BadRequest(err);

        var entity = await _db.Auctions
            .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted, ct)
            .ConfigureAwait(false);
        if (entity is null) return NotFound();

        entity.Title = request.Title.Trim();
        entity.AuctionDateUtc = request.AuctionDateUtc;
        entity.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        entity.UpdatedAtUtc = DateTime.Now;
        entity.Version++;

        await _db.AuctionCaptains.Where(c => c.AuctionId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await _db.AuctionLots.Where(l => l.AuctionId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        foreach (var c in DedupCaptains(request.Captains))
        {
            _db.AuctionCaptains.Add(new AuctionCaptain
            {
                AuctionId = id,
                PlayerId = c.PlayerId,
                WalletAmount = Math.Max(0, c.WalletAmount)
            });
        }
        foreach (var l in request.Lots)
            _db.AuctionLots.Add(MapLot(id, l));

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _db.Entry(entity).Collection(a => a.Captains).LoadAsync(ct).ConfigureAwait(false);
        await _db.Entry(entity).Collection(a => a.Lots).LoadAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await _db.Auctions.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted, ct)
            .ConfigureAwait(false);
        if (entity is null) return NotFound();

        entity.IsDeleted = true;
        entity.UpdatedAtUtc = DateTime.Now;
        entity.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private static IEnumerable<AuctionCaptainRequest> DedupCaptains(IEnumerable<AuctionCaptainRequest> captains)
        => captains
            .GroupBy(c => c.PlayerId)
            .Select(g => g.Last());

    private static string? Validate(AuctionUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return "Auction title is required.";
        var captains = DedupCaptains(request.Captains).ToList();
        if (captains.Count < 2)
            return "Select at least 2 captains.";
        if (request.Lots.Count == 0)
            return "Select at least 1 auction player.";

        var wallets = new Dictionary<Guid, int>();
        foreach (var c in captains)
        {
            if (c.WalletAmount < 0)
                return "Captain wallet cannot be negative.";
            wallets[c.PlayerId] = c.WalletAmount;
        }

        var spent = wallets.Keys.ToDictionary(id => id, _ => 0);
        var seen = new HashSet<Guid>();
        foreach (var lot in request.Lots)
        {
            if (!seen.Add(lot.PlayerId))
                return "Duplicate player in auction list.";
            if (lot.BasePrice < 0)
                return "Base price cannot be negative.";
            if (lot.IsSold)
            {
                if (lot.SoldPrice is null || lot.SoldPrice < 0)
                    return "Sold players need a valid sold price.";
                if (lot.SoldToCaptainPlayerId is not Guid cap || !wallets.ContainsKey(cap))
                    return "Sold player must be assigned to a selected captain.";
                spent[cap] += lot.SoldPrice.Value;
            }
        }

        foreach (var (capId, total) in spent)
        {
            if (total > wallets[capId])
                return $"Captain spend {total} exceeds wallet {wallets[capId]}.";
        }

        return null;
    }

    private static AuctionLot MapLot(Guid auctionId, AuctionLotRequest l)
        => new()
        {
            Id = l.Id == Guid.Empty ? Guid.NewGuid() : l.Id,
            AuctionId = auctionId,
            PlayerId = l.PlayerId,
            BasePrice = Math.Max(0, l.BasePrice),
            IsSold = l.IsSold,
            SoldPrice = l.IsSold ? Math.Max(0, l.SoldPrice ?? 0) : null,
            SoldToCaptainPlayerId = l.IsSold ? l.SoldToCaptainPlayerId : null
        };

    private static AuctionDto ToDto(Auction a) => new()
    {
        Id = a.Id,
        Title = a.Title,
        AuctionDateUtc = a.AuctionDateUtc,
        Notes = a.Notes,
        Version = a.Version,
        CreatedAtUtc = a.CreatedAtUtc,
        UpdatedAtUtc = a.UpdatedAtUtc,
        IsDeleted = a.IsDeleted,
        Captains = a.Captains.Select(c => new AuctionCaptainDto
        {
            PlayerId = c.PlayerId,
            WalletAmount = c.WalletAmount
        }).ToList(),
        Lots = a.Lots.Select(l => new AuctionLotDto
        {
            Id = l.Id,
            PlayerId = l.PlayerId,
            BasePrice = l.BasePrice,
            IsSold = l.IsSold,
            SoldPrice = l.SoldPrice,
            SoldToCaptainPlayerId = l.SoldToCaptainPlayerId
        }).ToList()
    };

    internal static List<AuctionReportRowDto> BuildAuctionReport(
        IReadOnlyList<Auction> auctions,
        IReadOnlyDictionary<Guid, string> names)
    {
        var map = new Dictionary<Guid, AuctionReportRowDto>();

        AuctionReportRowDto Row(Guid id)
        {
            if (!map.TryGetValue(id, out var row))
            {
                row = new AuctionReportRowDto
                {
                    PlayerId = id,
                    PlayerName = names.GetValueOrDefault(id, "?")
                };
                map[id] = row;
            }
            return row;
        }

        foreach (var a in auctions)
        {
            foreach (var c in a.Captains)
            {
                var row = Row(c.PlayerId);
                row.TimesAsCaptain++;
                row.TotalWalletAssigned += c.WalletAmount;
                var spent = a.Lots
                    .Where(l => l.IsSold && l.SoldToCaptainPlayerId == c.PlayerId)
                    .Sum(l => l.SoldPrice ?? 0);
                row.TotalSpentAsCaptain += spent;
            }

            foreach (var g in a.Lots.GroupBy(l => l.PlayerId))
            {
                var row = Row(g.Key);
                row.AuctionsParticipated++;
                row.TotalBasePrice += g.Sum(x => x.BasePrice);
                var sold = g.Where(x => x.IsSold && x.SoldPrice is int).Select(x => x.SoldPrice!.Value).ToList();
                row.TimesSold += sold.Count;
                row.TimesUnsold += g.Count(x => !x.IsSold);
                row.TotalSoldAmount += sold.Sum();
                if (sold.Count > 0)
                    row.HighestSoldPrice = Math.Max(row.HighestSoldPrice, sold.Max());
            }
        }

        foreach (var row in map.Values)
        {
            row.WalletRemaining = row.TotalWalletAssigned - row.TotalSpentAsCaptain;
            row.AverageSoldPrice = row.TimesSold == 0
                ? 0
                : Math.Round(row.TotalSoldAmount * 1.0 / row.TimesSold, 1);
        }

        return map.Values
            .OrderByDescending(r => r.TotalSoldAmount)
            .ThenByDescending(r => r.TotalWalletAssigned)
            .ThenBy(r => r.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

[ApiController]
[Route("api/venues")]
public sealed class VenuesController : ControllerBase
{
    private readonly ApiDbContext _db;

    public VenuesController(ApiDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VenueDto>>> List(CancellationToken ct)
    {
        var list = await _db.Venues.AsNoTracking()
            .Where(v => !v.IsDeleted)
            .OrderBy(v => v.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return Ok(list.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VenueDto>> Get(Guid id, CancellationToken ct)
    {
        var entity = await _db.Venues.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted, ct)
            .ConfigureAwait(false);
        return entity is null ? NotFound() : Ok(ToDto(entity));
    }

    [HttpPost]
    public async Task<ActionResult<VenueDto>> Create([FromBody] VenueUpsertRequest request, CancellationToken ct)
    {
        var name = RequireName(request.Name);
        var dup = await _db.Venues.AsNoTracking()
            .AnyAsync(v => !v.IsDeleted && v.Name.ToLower() == name.ToLower(), ct)
            .ConfigureAwait(false);
        if (dup) return BadRequest(new { message = $"Venue \"{name}\" already exists." });

        var now = DateTime.Now;
        var entity = new Venue
        {
            Id = request.Id is Guid g && g != Guid.Empty ? g : Guid.NewGuid(),
            Name = name,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _db.Venues.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<VenueDto>> Update(Guid id, [FromBody] VenueUpsertRequest request, CancellationToken ct)
    {
        var entity = await _db.Venues.FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted, ct)
            .ConfigureAwait(false);
        if (entity is null) return NotFound();

        var name = RequireName(request.Name);
        var dup = await _db.Venues.AsNoTracking()
            .AnyAsync(v => !v.IsDeleted && v.Id != id && v.Name.ToLower() == name.ToLower(), ct)
            .ConfigureAwait(false);
        if (dup) return BadRequest(new { message = $"Venue \"{name}\" already exists." });

        entity.Name = name;
        entity.UpdatedAtUtc = DateTime.Now;
        entity.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await _db.Venues.FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted, ct)
            .ConfigureAwait(false);
        if (entity is null) return NotFound();

        var block = await DeleteConstraints.VenueAsync(_db, id, ct).ConfigureAwait(false);
        if (block is not null)
            return BadRequest(block);

        entity.IsDeleted = true;
        entity.UpdatedAtUtc = DateTime.Now;
        entity.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private static string RequireName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) throw new InvalidOperationException("Venue name is required.");
        if (n.Length > 160) throw new InvalidOperationException("Venue name must be 160 characters or fewer.");
        return n;
    }

    private static VenueDto ToDto(Venue v) => new()
    {
        Id = v.Id,
        Name = v.Name,
        Version = v.Version,
        CreatedAtUtc = v.CreatedAtUtc,
        UpdatedAtUtc = v.UpdatedAtUtc,
        IsDeleted = v.IsDeleted
    };
}

[ApiController]
[Route("api/formats")]
public sealed class FormatsController : ControllerBase
{
    private readonly ApiDbContext _db;

    public FormatsController(ApiDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FormatDto>>> List(CancellationToken ct)
    {
        var list = await _db.Formats.AsNoTracking()
            .Where(f => !f.IsDeleted)
            .OrderBy(f => f.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return Ok(list.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FormatDto>> Get(Guid id, CancellationToken ct)
    {
        var entity = await _db.Formats.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct)
            .ConfigureAwait(false);
        return entity is null ? NotFound() : Ok(ToDto(entity));
    }

    [HttpPost]
    public async Task<ActionResult<FormatDto>> Create([FromBody] FormatUpsertRequest request, CancellationToken ct)
    {
        var name = RequireName(request.Name);
        var (kind, mode, overs) = ValidateSettings(request);
        var dup = await _db.Formats.AsNoTracking()
            .AnyAsync(f => !f.IsDeleted && f.Name.ToLower() == name.ToLower(), ct)
            .ConfigureAwait(false);
        if (dup) return BadRequest(new { message = $"Format \"{name}\" already exists." });

        var now = DateTime.Now;
        var entity = new Format
        {
            Id = request.Id is Guid g && g != Guid.Empty ? g : Guid.NewGuid(),
            Name = name,
            Kind = kind,
            OversLimit = overs,
            OversMode = mode,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _db.Formats.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<FormatDto>> Update(Guid id, [FromBody] FormatUpsertRequest request, CancellationToken ct)
    {
        var entity = await _db.Formats.FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct)
            .ConfigureAwait(false);
        if (entity is null) return NotFound();

        var name = RequireName(request.Name);
        var (kind, mode, overs) = ValidateSettings(request);
        var dup = await _db.Formats.AsNoTracking()
            .AnyAsync(f => !f.IsDeleted && f.Id != id && f.Name.ToLower() == name.ToLower(), ct)
            .ConfigureAwait(false);
        if (dup) return BadRequest(new { message = $"Format \"{name}\" already exists." });

        entity.Name = name;
        entity.Kind = kind;
        entity.OversLimit = overs;
        entity.OversMode = mode;
        entity.UpdatedAtUtc = DateTime.Now;
        entity.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await _db.Formats.FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct)
            .ConfigureAwait(false);
        if (entity is null) return NotFound();

        var block = await DeleteConstraints.FormatAsync(_db, id, ct).ConfigureAwait(false);
        if (block is not null)
            return BadRequest(block);

        entity.IsDeleted = true;
        entity.UpdatedAtUtc = DateTime.Now;
        entity.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private static string RequireName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) throw new InvalidOperationException("Format name is required.");
        if (n.Length > 160) throw new InvalidOperationException("Format name must be 160 characters or fewer.");
        return n;
    }

    private static (MatchFormat Format, OversMode Mode, int OversLimit) ValidateSettings(FormatUpsertRequest request)
    {
        if (request.Format is not (0 or 1))
            throw new InvalidOperationException("Format must be Single inning or Double innings.");
        var kind = (MatchFormat)request.Format;
        var mode = (OversMode)request.OversMode;
        OversRules.Validate(mode, request.OversLimit, 0);
        return (kind, mode, OversRules.StoredOversLimit(mode, request.OversLimit));
    }

    private static FormatDto ToDto(Format f) => new()
    {
        Id = f.Id,
        Name = f.Name,
        Format = (byte)f.Kind,
        OversLimit = f.OversLimit,
        OversMode = (byte)f.OversMode,
        DetailsLine = FormatCatalog.DetailsLine(f.Kind, f.OversMode, f.OversLimit),
        Hint = FormatCatalog.Hint(f.Kind, f.OversMode, f.OversLimit),
        Version = f.Version,
        CreatedAtUtc = f.CreatedAtUtc,
        UpdatedAtUtc = f.UpdatedAtUtc,
        IsDeleted = f.IsDeleted
    };
}

[ApiController]
[Route("api/venuebookings")]
public sealed class VenueBookingsController : ControllerBase
{
    private readonly ApiDbContext _db;
    private readonly IApiDbFactory _dbs;

    public VenueBookingsController(ApiDbContext db, IApiDbFactory dbs)
    {
        _db = db;
        _dbs = dbs;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VenueBookingDto>>> List(CancellationToken ct)
    {
        var list = await _db.VenueBookings.AsNoTracking()
            .Where(b => !b.IsDeleted)
            .Include(b => b.Participants)
            .OrderByDescending(b => b.BookingDateUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var names = await LoadNamesAsync(list, ct).ConfigureAwait(false);
        return Ok(list.Select(b => ToDto(b, names)).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VenueBookingDto>> Get(Guid id, CancellationToken ct)
    {
        var entity = await _db.VenueBookings.AsNoTracking()
            .Include(b => b.Participants)
            .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct)
            .ConfigureAwait(false);
        if (entity is null) return NotFound();

        var names = await LoadNamesAsync(new[] { entity }, ct).ConfigureAwait(false);
        return Ok(ToDto(entity, names));
    }

    [HttpPost]
    public async Task<ActionResult<VenueBookingDto>> Create([FromBody] VenueBookingUpsertRequest request, CancellationToken ct)
    {
        if (!TryGetCaller(out var userId, out var role))
            return Unauthorized();
        if (!IsLeagueWriter(role))
            return StatusCode(StatusCodes.Status403Forbidden, "Only admins can create venue bookings.");

        var err = await ValidateAsync(request, ct).ConfigureAwait(false);
        if (err is not null) return BadRequest(err);

        var id = request.Id is Guid g && g != Guid.Empty ? g : Guid.NewGuid();
        var now = DateTime.Now;
        var entity = new VenueBooking
        {
            Id = id,
            CreatedByUserId = userId,
            VenueId = request.VenueId,
            BookingDateUtc = request.BookingDateUtc,
            BookedByPlayerId = request.BookedByPlayerId,
            BookingCost = Math.Max(0, request.BookingCost),
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Participants = DedupParticipants(request.Participants)
                .Select(p => new VenueBookingParticipant
                {
                    VenueBookingId = id,
                    PlayerId = p.PlayerId,
                    Status = VenueBookingPaymentStatus.Normalize(p.Status)
                }).ToList()
        };

        _db.VenueBookings.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var names = await LoadNamesAsync(new[] { entity }, ct).ConfigureAwait(false);
        return Ok(ToDto(entity, names));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<VenueBookingDto>> Update(Guid id, [FromBody] VenueBookingUpsertRequest request, CancellationToken ct)
    {
        if (!TryGetCaller(out var userId, out var role))
            return Unauthorized();
        if (!IsLeagueWriter(role))
            return StatusCode(StatusCodes.Status403Forbidden, "Only admins can edit venue bookings.");

        var err = await ValidateAsync(request, ct).ConfigureAwait(false);
        if (err is not null) return BadRequest(err);

        var entity = await _db.VenueBookings
            .FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct)
            .ConfigureAwait(false);
        if (entity is null) return NotFound();

        if (entity.CreatedByUserId != userId && !IsSuperAdminRole(role))
            return StatusCode(StatusCodes.Status403Forbidden, "Only the admin who created this booking can edit it.");

        entity.VenueId = request.VenueId;
        entity.BookingDateUtc = request.BookingDateUtc;
        entity.BookedByPlayerId = request.BookedByPlayerId;
        entity.BookingCost = Math.Max(0, request.BookingCost);
        entity.UpdatedAtUtc = DateTime.Now;
        entity.Version++;

        await _db.VenueBookingParticipants.Where(p => p.VenueBookingId == id)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);

        foreach (var p in DedupParticipants(request.Participants))
        {
            _db.VenueBookingParticipants.Add(new VenueBookingParticipant
            {
                VenueBookingId = id,
                PlayerId = p.PlayerId,
                Status = VenueBookingPaymentStatus.Normalize(p.Status)
            });
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _db.Entry(entity).Collection(b => b.Participants).LoadAsync(ct).ConfigureAwait(false);

        var names = await LoadNamesAsync(new[] { entity }, ct).ConfigureAwait(false);
        return Ok(ToDto(entity, names));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!TryGetCaller(out var userId, out var role))
            return Unauthorized();
        if (!IsLeagueWriter(role))
            return StatusCode(StatusCodes.Status403Forbidden, "Only admins can delete venue bookings.");

        var entity = await _db.VenueBookings.FirstOrDefaultAsync(b => b.Id == id && !b.IsDeleted, ct)
            .ConfigureAwait(false);
        if (entity is null) return NotFound();

        if (entity.CreatedByUserId != userId && !IsSuperAdminRole(role))
            return StatusCode(StatusCodes.Status403Forbidden, "Only the admin who created this booking can delete it.");

        entity.IsDeleted = true;
        entity.UpdatedAtUtc = DateTime.Now;
        entity.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private bool TryGetCaller(out Guid userId, out string role)
    {
        userId = Guid.Empty;
        role = string.Empty;

        if (HttpContext.Items.TryGetValue("JplUserId", out var idObj) && idObj is Guid g)
            userId = g;
        else
        {
            var idText = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(idText, out userId))
                return false;
        }

        if (HttpContext.Items.TryGetValue("JplRole", out var roleObj) && roleObj is string r && !string.IsNullOrWhiteSpace(r))
            role = r;
        else
            role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;

        return userId != Guid.Empty && role.Length > 0;
    }

    private bool IsLeagueWriter(string role)
    {
        if (IsSuperAdminRole(role))
            return true;
        if (!string.Equals(role, nameof(AppRole.Admin), StringComparison.OrdinalIgnoreCase))
            return false;

        if (!HttpContext.Items.TryGetValue("JplHomeLeagueId", out var homeObj)
            || homeObj is not Guid home
            || home == Guid.Empty)
            return false;

        if (HttpContext.Request.Headers.TryGetValue("X-League-Id", out var header)
            && Guid.TryParse(header.ToString(), out var selected)
            && selected != Guid.Empty)
            return selected == home;

        return true;
    }

    private static bool IsSuperAdminRole(string role)
        => string.Equals(role, nameof(AppRole.SuperAdmin), StringComparison.OrdinalIgnoreCase);

    private async Task<(Dictionary<Guid, string> Players, Dictionary<Guid, string> Users, Dictionary<Guid, string> Venues)> LoadNamesAsync(
        IReadOnlyList<VenueBooking> bookings, CancellationToken ct)
    {
        var playerIds = bookings
            .SelectMany(b => b.Participants.Select(p => p.PlayerId).Append(b.BookedByPlayerId))
            .Distinct()
            .ToList();
        var userIds = bookings.Select(b => b.CreatedByUserId).Distinct().ToList();
        var venueIds = bookings.Select(b => b.VenueId).Distinct().ToList();

        var players = playerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Players.AsNoTracking()
                .Where(p => playerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
                .ConfigureAwait(false);

        var users = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await LoadUserNamesAsync(userIds, ct).ConfigureAwait(false);

        var venues = venueIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Venues.AsNoTracking()
                .Where(v => venueIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.Name, ct)
                .ConfigureAwait(false);

        return (players, users, venues);
    }

    private async Task<Dictionary<Guid, string>> LoadUserNamesAsync(IReadOnlyList<Guid> userIds, CancellationToken ct)
    {
        await using var master = _dbs.CreateMaster();
        return await master.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct)
            .ConfigureAwait(false);
    }

    private static IEnumerable<VenueBookingParticipantRequest> DedupParticipants(
        IEnumerable<VenueBookingParticipantRequest> participants)
        => participants.GroupBy(p => p.PlayerId).Select(g => g.Last());

    private async Task<string?> ValidateAsync(VenueBookingUpsertRequest request, CancellationToken ct)
    {
        if (request.VenueId == Guid.Empty)
            return "Venue is required.";
        var venueOk = await _db.Venues.AsNoTracking()
            .AnyAsync(v => v.Id == request.VenueId && !v.IsDeleted, ct)
            .ConfigureAwait(false);
        if (!venueOk)
            return "Selected venue was not found.";
        if (request.BookedByPlayerId == Guid.Empty)
            return "Booked By player is required.";
        if (request.BookingCost < 0)
            return "Booking cost cannot be negative.";

        var seen = new HashSet<Guid>();
        foreach (var p in request.Participants)
        {
            if (p.PlayerId == Guid.Empty)
                return "Participant player is required.";
            if (!seen.Add(p.PlayerId))
                return "Duplicate participant player.";
            if (!VenueBookingPaymentStatus.IsValid(p.Status))
                return "Participant status must be Pending or Paid.";
        }

        return null;
    }

    private static VenueBookingDto ToDto(
        VenueBooking b,
        (Dictionary<Guid, string> Players, Dictionary<Guid, string> Users, Dictionary<Guid, string> Venues) names)
        => new()
        {
            Id = b.Id,
            CreatedByUserId = b.CreatedByUserId,
            CreatedByUsername = names.Users.GetValueOrDefault(b.CreatedByUserId, "?"),
            VenueId = b.VenueId,
            VenueName = names.Venues.GetValueOrDefault(b.VenueId, "?"),
            BookingDateUtc = b.BookingDateUtc,
            BookedByPlayerId = b.BookedByPlayerId,
            BookedByPlayerName = names.Players.GetValueOrDefault(b.BookedByPlayerId, "?"),
            BookingCost = b.BookingCost,
            Version = b.Version,
            CreatedAtUtc = b.CreatedAtUtc,
            UpdatedAtUtc = b.UpdatedAtUtc,
            IsDeleted = b.IsDeleted,
            Participants = b.Participants.Select(p => new VenueBookingParticipantDto
            {
                PlayerId = p.PlayerId,
                PlayerName = names.Players.GetValueOrDefault(p.PlayerId, "?"),
                Status = p.Status
            }).ToList()
        };
}

[ApiController]
[Route("api/tournaments")]
public sealed class TournamentsController : ControllerBase
{
    private readonly ApiDbContext _db;

    public TournamentsController(ApiDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TournamentListDto>>> List(CancellationToken ct)
    {
        var list = await _db.Tournaments.AsNoTracking()
            .Where(t => !t.IsDeleted)
            .Include(t => t.Teams)
            .Include(t => t.Fixtures)
            .OrderByDescending(t => t.UpdatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var winnerIds = list.Where(t => t.WinnerTeamId is Guid).Select(t => t.WinnerTeamId!.Value).Distinct().ToList();
        var names = winnerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Teams.AsNoTracking()
                .Where(t => winnerIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct)
                .ConfigureAwait(false);

        return Ok(list.Select(t => new TournamentListDto
        {
            Id = t.Id,
            Name = t.Name,
            GroupCount = t.GroupCount,
            QualifyPerGroup = t.QualifyPerGroup,
            Status = (byte)t.Status,
            StatusText = StatusText(t.Status),
            TeamCount = t.Teams.Count,
            FixtureCount = t.Fixtures.Count,
            WinnerTeamId = t.WinnerTeamId,
            WinnerTeamName = t.WinnerTeamId is Guid w ? names.GetValueOrDefault(w) : null,
            UpdatedAtUtc = t.UpdatedAtUtc
        }).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TournamentDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var t = await LoadTrackedAsync(id, ct).ConfigureAwait(false);
        if (t is null) return NotFound();

        var (standings, changed) = await TournamentEngine.ResolveProgressAsync(_db, t, ct).ConfigureAwait(false);
        if (changed)
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Ok(await ToDetailAsync(t, standings, ct).ConfigureAwait(false));
    }

    [HttpPost]
    public async Task<ActionResult<TournamentDetailDto>> Create([FromBody] TournamentUpsertRequest body, CancellationToken ct)
    {
        var name = RequireName(body.Name);
        var teams = NormalizeTeams(body);
        TournamentEngine.ValidateSetup(body.GroupCount, body.QualifyPerGroup, teams);
        await ValidateMatchDefaultsAsync(body, ct).ConfigureAwait(false);

        var entity = new Tournament
        {
            Id = Guid.NewGuid(),
            Name = name,
            GroupCount = body.GroupCount,
            QualifyPerGroup = body.QualifyPerGroup,
            VenueId = body.VenueId,
            FormatId = body.FormatId,
            Format = body.Format,
            OversLimit = OversRules.StoredOversLimit((OversMode)body.OversMode, body.OversLimit),
            OversMode = (OversMode)body.OversMode,
            LastBatsmanCanBatAsSingleWicket = body.LastBatsmanCanBatAsSingleWicket,
            BowlerMaxOvers = body.BowlerMaxOvers,
            Status = TournamentStatus.Draft,
            Teams = teams
        };
        _db.Tournaments.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(await ToDetailAsync(entity, new List<TournamentStandingRow>(), ct).ConfigureAwait(false));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TournamentDetailDto>> Update(Guid id, [FromBody] TournamentUpsertRequest body, CancellationToken ct)
    {
        var t = await LoadTrackedAsync(id, ct).ConfigureAwait(false);
        if (t is null) return NotFound();
        if (t.Status != TournamentStatus.Draft)
            return BadRequest(new { message = "Only draft tournaments can be edited. Saved tournaments are locked." });

        var name = RequireName(body.Name);
        var teams = NormalizeTeams(body);
        TournamentEngine.ValidateSetup(body.GroupCount, body.QualifyPerGroup, teams);
        await ValidateMatchDefaultsAsync(body, ct).ConfigureAwait(false);

        t.Name = name;
        t.GroupCount = body.GroupCount;
        t.QualifyPerGroup = body.QualifyPerGroup;
        t.VenueId = body.VenueId;
        t.FormatId = body.FormatId;
        t.Format = body.Format;
        t.OversLimit = OversRules.StoredOversLimit((OversMode)body.OversMode, body.OversLimit);
        t.OversMode = (OversMode)body.OversMode;
        t.LastBatsmanCanBatAsSingleWicket = body.LastBatsmanCanBatAsSingleWicket;
        t.BowlerMaxOvers = body.BowlerMaxOvers;
        t.UpdatedAtUtc = DateTime.Now;
        t.Version++;
        _db.TournamentTeams.RemoveRange(t.Teams);
        t.Teams = teams;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(await ToDetailAsync(t, new List<TournamentStandingRow>(), ct).ConfigureAwait(false));
    }

    [HttpPost("{id:guid}/generate")]
    public async Task<ActionResult<TournamentDetailDto>> Generate(Guid id, CancellationToken ct)
    {
        var t = await LoadTrackedAsync(id, ct).ConfigureAwait(false);
        if (t is null) return NotFound();
        if (t.Status != TournamentStatus.Draft)
            return BadRequest(new { message = "Tournament already saved. Edit / delete is only allowed while Draft." });

        TournamentEngine.ValidateSetup(t.GroupCount, t.QualifyPerGroup, t.Teams.ToList());
        if (t.Fixtures.Count > 0)
            _db.TournamentFixtures.RemoveRange(t.Fixtures);

        var fixtures = TournamentEngine.BuildFixtures(t);
        foreach (var f in fixtures)
            _db.TournamentFixtures.Add(f);

        t.Status = TournamentStatus.Scheduled;
        t.WinnerTeamId = null;
        t.UpdatedAtUtc = DateTime.Now;
        t.Version++;

        // 1 group / 1 qualify with a single team and no RR games → champion immediately
        if (t.GroupCount == 1 && t.QualifyPerGroup == 1 && fixtures.Count == 0)
        {
            var only = t.Teams.FirstOrDefault(x => x.GroupNumber == 1);
            if (only is not null)
            {
                t.WinnerTeamId = only.TeamId;
                t.Status = TournamentStatus.Completed;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        var (standings, _) = await TournamentEngine.ResolveProgressAsync(_db, t, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(await ToDetailAsync(t, standings, ct).ConfigureAwait(false));
    }

    [HttpPost("{id:guid}/fixtures/{fixtureId:guid}/link-match")]
    public async Task<ActionResult<TournamentDetailDto>> LinkMatch(
        Guid id, Guid fixtureId, [FromBody] TournamentLinkMatchRequest body, CancellationToken ct)
    {
        var t = await LoadTrackedAsync(id, ct).ConfigureAwait(false);
        if (t is null) return NotFound();
        if (t.Status == TournamentStatus.Draft)
            return BadRequest(new { message = "Save the tournament first before starting matches." });
        var fixture = t.Fixtures.FirstOrDefault(f => f.Id == fixtureId);
        if (fixture is null) return NotFound();
        if (fixture.TeamAId is null || fixture.TeamBId is null)
            return BadRequest(new { message = "Fixture teams are not decided yet." });

        fixture.MatchId = body.MatchId;
        if (t.Status == TournamentStatus.Scheduled)
            t.Status = TournamentStatus.InProgress;
        t.UpdatedAtUtc = DateTime.Now;
        t.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var (standings, changed) = await TournamentEngine.ResolveProgressAsync(_db, t, ct).ConfigureAwait(false);
        if (changed) await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(await ToDetailAsync(t, standings, ct).ConfigureAwait(false));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var t = await _db.Tournaments.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct).ConfigureAwait(false);
        if (t is null) return NotFound();
        if (t.Status != TournamentStatus.Draft)
            return BadRequest(new { message = "Only draft tournaments can be deleted. Saved tournaments are locked." });
        t.IsDeleted = true;
        t.UpdatedAtUtc = DateTime.Now;
        t.Version++;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    private async Task<Tournament?> LoadTrackedAsync(Guid id, CancellationToken ct)
        => await _db.Tournaments
            .Include(t => t.Teams)
            .Include(t => t.Fixtures)
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct)
            .ConfigureAwait(false);

    private async Task ValidateMatchDefaultsAsync(TournamentUpsertRequest body, CancellationToken ct)
    {
        if (body.VenueId == Guid.Empty)
            throw new InvalidOperationException("Venue is required.");
        var venueOk = await _db.Venues.AsNoTracking()
            .AnyAsync(v => v.Id == body.VenueId && !v.IsDeleted, ct)
            .ConfigureAwait(false);
        if (!venueOk)
            throw new InvalidOperationException("Selected venue was not found.");
        var resolved = await FormatCatalog.ResolveAsync(
                _db,
                body.FormatId ?? Guid.Empty,
                (MatchFormat)body.Format,
                (OversMode)body.OversMode,
                body.OversLimit,
                ct)
            .ConfigureAwait(false);
        body.FormatId = resolved.Id;
        body.Format = (byte)resolved.Format;
        body.OversMode = (byte)resolved.Mode;
        body.OversLimit = resolved.OversLimit;
        OversRules.Validate(resolved.Mode, resolved.OversLimit, body.BowlerMaxOvers);
    }

    private static string RequireName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) throw new InvalidOperationException("Tournament name is required.");
        if (n.Length > 160) throw new InvalidOperationException("Tournament name is too long.");
        return n;
    }

    private static List<TournamentTeam> NormalizeTeams(TournamentUpsertRequest body)
        => body.Teams
            .GroupBy(t => t.TeamId)
            .Select(g => g.First())
            .Select(t => new TournamentTeam
            {
                TeamId = t.TeamId,
                GroupNumber = t.GroupNumber
            })
            .ToList();

    private static string StatusText(TournamentStatus s) => s switch
    {
        TournamentStatus.Draft => "Draft",
        TournamentStatus.Scheduled => "Saved",
        TournamentStatus.InProgress => "In progress",
        TournamentStatus.Completed => "Completed",
        _ => s.ToString()
    };

    private static string StageText(TournamentFixtureStage s) => s switch
    {
        TournamentFixtureStage.Group => "Group",
        TournamentFixtureStage.SemiFinal => "Semi-final",
        TournamentFixtureStage.Final => "Final",
        _ => s.ToString()
    };

    private async Task<TournamentDetailDto> ToDetailAsync(
        Tournament t, List<TournamentStandingRow> standings, CancellationToken ct)
    {
        var teamIds = t.Teams.Select(x => x.TeamId)
            .Concat(t.Fixtures.Where(f => f.TeamAId is Guid).Select(f => f.TeamAId!.Value))
            .Concat(t.Fixtures.Where(f => f.TeamBId is Guid).Select(f => f.TeamBId!.Value))
            .Concat(t.WinnerTeamId is Guid w ? new[] { w } : Array.Empty<Guid>())
            .Distinct()
            .ToList();
        var names = teamIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Teams.AsNoTracking()
                .Where(x => teamIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, ct)
                .ConfigureAwait(false);

        var venueName = await _db.Venues.AsNoTracking()
            .Where(v => v.Id == t.VenueId)
            .Select(v => v.Name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? "?";

        string? formatName = null;
        if (t.FormatId is Guid formatId)
        {
            formatName = await _db.Formats.AsNoTracking()
                .Where(f => f.Id == formatId)
                .Select(f => f.Name)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        var matchIds = t.Fixtures.Where(f => f.MatchId is Guid).Select(f => f.MatchId!.Value).ToList();
        var matches = matchIds.Count == 0
            ? new List<Match>()
            : await _db.Matches.AsNoTracking()
                .Where(m => matchIds.Contains(m.Id) && !m.IsDeleted)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        var matchById = matches.ToDictionary(m => m.Id);

        var scoredIds = matches
            .Where(m => m.Status is MatchStatus.Completed or MatchStatus.InProgress)
            .Select(m => m.Id)
            .ToList();
        var innsByMatch = scoredIds.Count == 0
            ? new Dictionary<Guid, List<Innings>>()
            : (await _db.Innings.AsNoTracking()
                    .Where(i => scoredIds.Contains(i.MatchId))
                    .Include(i => i.Deliveries)
                    .ToListAsync(ct)
                    .ConfigureAwait(false))
                .GroupBy(i => i.MatchId)
                .ToDictionary(g => g.Key, g => g.ToList());

        var squadCounts = teamIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await _db.TeamPlayers.AsNoTracking()
                .Where(tp => teamIds.Contains(tp.TeamId))
                .GroupBy(tp => tp.TeamId)
                .ToDictionaryAsync(g => g.Key, g => g.Count(), ct)
                .ConfigureAwait(false);

        foreach (var s in standings)
            s.TeamName = names.GetValueOrDefault(s.TeamId, "?");

        var standingDtos = new List<TournamentStandingDto>();
        foreach (var group in standings.GroupBy(s => s.GroupNumber).OrderBy(g => g.Key))
        {
            var rank = 1;
            foreach (var s in group)
            {
                standingDtos.Add(new TournamentStandingDto
                {
                    GroupNumber = s.GroupNumber,
                    TeamId = s.TeamId,
                    TeamName = s.TeamName,
                    Played = s.Played,
                    Won = s.Won,
                    Lost = s.Lost,
                    Tied = s.Tied,
                    Points = s.Points,
                    NetRunRate = s.NetRunRate,
                    Rank = rank++
                });
            }
        }

        return new TournamentDetailDto
        {
            Id = t.Id,
            Name = t.Name,
            GroupCount = t.GroupCount,
            QualifyPerGroup = t.QualifyPerGroup,
            VenueId = t.VenueId,
            VenueName = venueName,
            FormatId = t.FormatId,
            Format = t.Format,
            FormatText = formatName ?? (t.Format == (byte)MatchFormat.DoubleInnings ? "Double innings" : "Single inning"),
            OversLimit = t.OversLimit,
            OversMode = (byte)t.OversMode,
            LastBatsmanCanBatAsSingleWicket = t.LastBatsmanCanBatAsSingleWicket,
            BowlerMaxOvers = t.BowlerMaxOvers,
            Status = (byte)t.Status,
            StatusText = StatusText(t.Status),
            WinnerTeamId = t.WinnerTeamId,
            WinnerTeamName = t.WinnerTeamId is Guid w2 ? names.GetValueOrDefault(w2) : null,
            Version = t.Version,
            CreatedAtUtc = t.CreatedAtUtc,
            UpdatedAtUtc = t.UpdatedAtUtc,
            Teams = t.Teams.OrderBy(x => x.GroupNumber).ThenBy(x => names.GetValueOrDefault(x.TeamId, ""))
                .Select(x => new TournamentTeamDto
                {
                    TeamId = x.TeamId,
                    TeamName = names.GetValueOrDefault(x.TeamId, "?"),
                    GroupNumber = x.GroupNumber
                }).ToList(),
            Fixtures = t.Fixtures.OrderBy(f => f.Sequence).Select(f =>
            {
                Match? match = f.MatchId is Guid mid && matchById.TryGetValue(mid, out var m) ? m : null;
                MatchStatus? ms = match?.Status;
                var teamAName = f.TeamAId is Guid a ? names.GetValueOrDefault(a, f.TeamASlot ?? "TBD") : (f.TeamASlot ?? "TBD");
                var teamBName = f.TeamBId is Guid b ? names.GetValueOrDefault(b, f.TeamBSlot ?? "TBD") : (f.TeamBSlot ?? "TBD");
                var resultText = string.Empty;
                if (match is { Status: MatchStatus.Completed }
                    && innsByMatch.TryGetValue(match.Id, out var matchInns))
                {
                    var (aRuns, aWkts) = FixtureTeamTotals(matchInns, match.TeamAId);
                    var (bRuns, bWkts) = FixtureTeamTotals(matchInns, match.TeamBId);
                    var firstBat = matchInns.OrderBy(i => i.InningsNumber).FirstOrDefault()?.BattingTeamId;
                    var aName = names.GetValueOrDefault(match.TeamAId, teamAName);
                    var bName = names.GetValueOrDefault(match.TeamBId, teamBName);
                    (resultText, _) = FixtureBuildResult(
                        match, aName, bName, aRuns, aWkts, bRuns, bWkts, firstBat,
                        MatchSquadSnapshot.SizeFor(match.SquadRosterJson, match.TeamAId, true, squadCounts.GetValueOrDefault(match.TeamAId)),
                        MatchSquadSnapshot.SizeFor(match.SquadRosterJson, match.TeamBId, false, squadCounts.GetValueOrDefault(match.TeamBId)));
                    if (string.Equals(resultText, "Match tied — both teams level", StringComparison.Ordinal)
                        && f.Stage != TournamentFixtureStage.Group)
                    {
                        var advancer = TournamentEngine.BetterOnGroupTable(
                            match.TeamAId, match.TeamBId, standings);
                        if (advancer is Guid aid)
                        {
                            var advName = names.GetValueOrDefault(aid, "?");
                            resultText = $"Match tied — {advName} advances (group Pts/NRR)";
                        }
                    }
                }
                else if (match is { Status: MatchStatus.InProgress }
                         && innsByMatch.TryGetValue(match.Id, out var liveInns))
                {
                    var (aRuns, aWkts) = FixtureTeamTotals(liveInns, match.TeamAId);
                    var (bRuns, bWkts) = FixtureTeamTotals(liveInns, match.TeamBId);
                    resultText = $"LIVE · {aRuns}/{aWkts} vs {bRuns}/{bWkts}";
                }

                var teamsReady = f.TeamAId is Guid && f.TeamBId is Guid;
                return new TournamentFixtureDto
                {
                    Id = f.Id,
                    Stage = (byte)f.Stage,
                    StageText = StageText(f.Stage),
                    GroupNumber = f.GroupNumber,
                    Sequence = f.Sequence,
                    Label = f.Label,
                    TeamAId = f.TeamAId,
                    TeamBId = f.TeamBId,
                    TeamAName = teamAName,
                    TeamBName = teamBName,
                    TeamASlot = f.TeamASlot,
                    TeamBSlot = f.TeamBSlot,
                    MatchId = f.MatchId,
                    MatchStatus = ms is null ? null : (byte)ms.Value,
                    MatchStatusText = ms switch
                    {
                        MatchStatus.Setup => "Setup",
                        MatchStatus.InProgress => "Live",
                        MatchStatus.Completed => "Completed",
                        MatchStatus.Abandoned => "Abandoned",
                        _ => ""
                    },
                    ResultText = resultText,
                    CanStart = teamsReady
                               && f.MatchId is null
                               && (t.Status is TournamentStatus.Scheduled or TournamentStatus.InProgress),
                    CanResume = false,
                    CanOpenScorecard = ms is MatchStatus.Completed or MatchStatus.InProgress or MatchStatus.Setup
                };
            }).ToList(),
            Standings = standingDtos
        };
    }

    private static (int Runs, int Wickets) FixtureTeamTotals(IReadOnlyList<Innings> inns, Guid teamId)
    {
        var runs = 0;
        var wickets = 0;
        foreach (var inn in inns.Where(i => i.BattingTeamId == teamId))
        {
            foreach (var d in inn.Deliveries)
            {
                runs += d.RunsOffBat + d.ExtraRuns;
                if (d.IsWicket) wickets++;
            }
        }
        return (runs, wickets);
    }

    private static (string Text, Guid? WinnerId) FixtureBuildResult(
        Match match,
        string teamA,
        string teamB,
        int aRuns,
        int aWkts,
        int bRuns,
        int bWkts,
        Guid? firstBattingTeamId,
        int teamASquadSize,
        int teamBSquadSize)
    {
        if (aRuns == bRuns)
            return ("Match tied — both teams level", null);

        Guid winnerId;
        string winName;
        int winRuns, winWkts, loseRuns;
        if (aRuns > bRuns)
        {
            winnerId = match.TeamAId;
            winName = teamA;
            winRuns = aRuns;
            winWkts = aWkts;
            loseRuns = bRuns;
        }
        else
        {
            winnerId = match.TeamBId;
            winName = teamB;
            winRuns = bRuns;
            winWkts = bWkts;
            loseRuns = aRuns;
        }

        var chased = firstBattingTeamId is Guid first && first != winnerId;
        if (chased)
        {
            var squadSize = winnerId == match.TeamAId ? teamASquadSize : teamBSquadSize;
            var remaining = MatchResultRules.WicketsRemaining(
                squadSize, winWkts, match.LastBatsmanCanBatAsSingleWicket);
            var unit = remaining == 1 ? "wicket" : "wickets";
            return ($"{winName} won by {remaining} {unit}", winnerId);
        }

        var margin = winRuns - loseRuns;
        var runUnit = margin == 1 ? "run" : "runs";
        return ($"{winName} won by {margin} {runUnit}", winnerId);
    }
}
