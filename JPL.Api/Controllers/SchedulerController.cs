using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Contracts;
using JPL.Api.Data;
using JPL.Api.Models;
using JPL.Api.Services;

namespace JPL.Api.Controllers;

[ApiController]
[Route("api/scheduler")]
public sealed class SchedulerController : ControllerBase
{
    private readonly IApiDbFactory _dbs;

    public SchedulerController(IApiDbFactory dbs) => _dbs = dbs;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SchedulerListDto>>> List(CancellationToken ct)
    {
        if (!TryGetOwner(out var userId, out var isPlatformAdmin, out var error))
            return error;

        await using var db = _dbs.CreateMaster();
        var query = db.SchedulerTournaments.AsNoTracking()
            .Include(t => t.Teams)
            .Include(t => t.Matches)
            .AsQueryable();
        if (!isPlatformAdmin)
            query = query.Where(t => t.CreatedByUserId == userId);

        var rows = await query
            .OrderByDescending(t => t.UpdatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var names = await LoadUserNamesAsync(db, rows.Select(t => t.CreatedByUserId), ct)
            .ConfigureAwait(false);
        return Ok(rows.Select(t => ToListDto(t, names)).ToList());
    }

    [HttpGet("page")]
    public async Task<ActionResult<PagedResult<SchedulerListDto>>> GetPage(
        int skip = 0, int take = 15, string? q = null, Guid? createdBy = null, CancellationToken ct = default)
    {
        if (!TryGetOwner(out var userId, out var isPlatformAdmin, out var error))
            return error;

        (skip, take) = Paging.Clamp(skip, take);
        await using var db = _dbs.CreateMaster();
        var query = db.SchedulerTournaments.AsNoTracking().AsQueryable();
        if (!isPlatformAdmin)
            query = query.Where(t => t.CreatedByUserId == userId);
        else if (createdBy is Guid ownerId && ownerId != Guid.Empty)
            query = query.Where(t => t.CreatedByUserId == ownerId);
        var like = Paging.LikePattern(q);
        if (like is not null)
            query = query.Where(t => EF.Functions.Like(t.Name, like));

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query
            .Include(t => t.Teams)
            .Include(t => t.Matches)
            .OrderByDescending(t => t.UpdatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var names = await LoadUserNamesAsync(db, rows.Select(t => t.CreatedByUserId), ct)
            .ConfigureAwait(false);
        return Ok(new PagedResult<SchedulerListDto>(rows.Select(t => ToListDto(t, names)).ToList(), total, skip, take));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SchedulerDetailDto>> Get(Guid id, CancellationToken ct)
    {
        if (!TryGetOwner(out var userId, out var isPlatformAdmin, out var error))
            return error;

        await using var db = _dbs.CreateMaster();
        var row = await LoadAsync(db, id, userId, isPlatformAdmin, tracked: false, ct).ConfigureAwait(false);
        if (row is null)
            return NotFound();
        return Ok(ToDetailDto(row));
    }

    [HttpPost]
    public async Task<ActionResult<SchedulerDetailDto>> Create(
        [FromBody] SchedulerUpsertRequest request, CancellationToken ct)
    {
        if (!TryGetOwner(out var userId, out _, out var error))
            return error;
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Enter a tournament name.");

        await using var db = _dbs.CreateMaster();
        var entity = new SchedulerTournament
        {
            Id = request.Id is Guid id && id != Guid.Empty ? id : Guid.NewGuid(),
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };

        if (await db.SchedulerTournaments.IgnoreQueryFilters()
                .AnyAsync(t => t.Id == entity.Id, ct).ConfigureAwait(false))
            return Conflict("A scheduler with this id already exists.");

        Apply(entity, request);
        db.SchedulerTournaments.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDetailDto(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SchedulerDetailDto>> Update(
        Guid id, [FromBody] SchedulerUpsertRequest request, CancellationToken ct)
    {
        if (!TryGetOwner(out var userId, out var isPlatformAdmin, out var error))
            return error;
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Enter a tournament name.");

        await using var db = _dbs.CreateMaster();
        var current = await db.SchedulerTournaments.AsNoTracking()
            .Where(t => t.Id == id && (isPlatformAdmin || t.CreatedByUserId == userId))
            .Select(t => new { t.Id, t.Status, t.WinnerTeamId, t.KnockoutFormat })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (current is null)
            return NotFound();
        // Completed but missing WinnerTeamId (e.g. Knockout=No before champion logic) — allow champion backfill.
        var allowMissingChampionFix = current.Status == 3 && current.WinnerTeamId is null;
        if (current.Status == 3 && !allowMissingChampionFix)
            return BadRequest("Cannot update a completed scheduler.");

        await db.SchedulerMatches.Where(m => m.TournamentId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.SchedulerTeams.Where(t => t.TournamentId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var name = request.Name.Trim();
        var updatedAt = DateTime.Now;
        var isCustom = request.RoundRobinType == 2;
        var groupCount = isCustom ? 1 : request.GroupCount;
        var affected = await db.SchedulerTournaments.IgnoreQueryFilters()
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Name, name)
                .SetProperty(t => t.GroupCount, groupCount)
                .SetProperty(t => t.RoundRobinType, request.RoundRobinType)
                .SetProperty(t => t.QualifyPerGroup, request.QualifyPerGroup)
                .SetProperty(t => t.KnockoutFormat, request.KnockoutFormat)
                .SetProperty(t => t.Status, request.Status)
                .SetProperty(t => t.WinnerTeamId, request.WinnerTeamId)
                .SetProperty(t => t.QualificationPicksJson, request.QualificationPicksJson)
                .SetProperty(t => t.UpdatedAtUtc, updatedAt)
                .SetProperty(t => t.IsDeleted, false), ct)
            .ConfigureAwait(false);
        if (affected == 0)
            return NotFound();

        foreach (var team in request.Teams ?? [])
        {
            db.SchedulerTeams.Add(new SchedulerTeam
            {
                Id = team.Id == Guid.Empty ? Guid.NewGuid() : team.Id,
                TournamentId = id,
                Name = (team.Name ?? string.Empty).Trim(),
                GroupNumber = isCustom ? 1 : team.GroupNumber,
                SortOrder = team.SortOrder
            });
        }

        foreach (var match in request.Matches ?? [])
        {
            db.SchedulerMatches.Add(new SchedulerMatch
            {
                Id = match.Id == Guid.Empty ? Guid.NewGuid() : match.Id,
                TournamentId = id,
                Stage = match.Stage,
                GroupNumber = match.GroupNumber,
                Sequence = match.Sequence,
                Label = match.Label ?? string.Empty,
                TeamAId = match.TeamAId,
                TeamBId = match.TeamBId,
                TeamASlot = match.TeamASlot,
                TeamBSlot = match.TeamBSlot,
                Result = match.Result,
                WinnerFeedsMatchId = match.WinnerFeedsMatchId,
                WinnerFeedsIsTeamA = match.WinnerFeedsIsTeamA,
                LoserFeedsMatchId = match.LoserFeedsMatchId,
                LoserFeedsIsTeamA = match.LoserFeedsIsTeamA
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var saved = await LoadAsync(db, id, userId, isPlatformAdmin, tracked: false, ct).ConfigureAwait(false);
        return Ok(ToDetailDto(saved!));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!TryGetOwner(out var userId, out var isPlatformAdmin, out var error))
            return error;

        await using var db = _dbs.CreateMaster();
        var row = await db.SchedulerTournaments.AsNoTracking()
            .Where(t => t.Id == id && (isPlatformAdmin || t.CreatedByUserId == userId))
            .Select(t => new { t.Id, t.Status, t.WinnerTeamId })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (row is null)
            return NotFound();
        if (row.Status == 3 || row.WinnerTeamId is not null)
            return BadRequest("Cannot delete a completed scheduler.");

        var affected = await db.SchedulerTournaments.IgnoreQueryFilters()
            .Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsDeleted, true)
                .SetProperty(t => t.UpdatedAtUtc, DateTime.Now), ct)
            .ConfigureAwait(false);
        if (affected == 0)
            return NotFound();
        return NoContent();
    }

    private bool TryGetOwner(out Guid userId, out bool isPlatformAdmin, out ActionResult error)
    {
        userId = Guid.Empty;
        isPlatformAdmin = false;
        error = Unauthorized();

        if (HttpContext.Items.TryGetValue("JplUserId", out var idObj) && idObj is Guid g)
            userId = g;
        else
        {
            var idText = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(idText, out userId))
                return false;
        }

        var role = HttpContext.Items.TryGetValue("JplRole", out var roleObj) && roleObj is string r
            ? r
            : User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;
        var username = HttpContext.Items.TryGetValue("JplUsername", out var nameObj) && nameObj is string n
            ? n
            : User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

        if (userId == Guid.Empty)
            return false;
        if (string.Equals(role, nameof(AppRole.Guest), StringComparison.OrdinalIgnoreCase))
        {
            error = StatusCode(StatusCodes.Status403Forbidden, "Login required to use Tournament Scheduler.");
            return false;
        }

        isPlatformAdmin = string.Equals(role, nameof(AppRole.SuperAdmin), StringComparison.OrdinalIgnoreCase)
                          && PlatformAdmin.Is(username);
        error = null!;
        return true;
    }

    private static async Task<SchedulerTournament?> LoadAsync(
        MasterDbContext db, Guid id, Guid userId, bool isPlatformAdmin, bool tracked, CancellationToken ct)
    {
        var query = tracked
            ? db.SchedulerTournaments.Include(t => t.Teams).Include(t => t.Matches)
            : db.SchedulerTournaments.AsNoTracking().Include(t => t.Teams).Include(t => t.Matches);
        return await query
            .FirstOrDefaultAsync(
                t => t.Id == id && (isPlatformAdmin || t.CreatedByUserId == userId),
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<Dictionary<Guid, string>> LoadUserNamesAsync(
        MasterDbContext db, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct)
            .ConfigureAwait(false);
    }

    private static void Apply(SchedulerTournament entity, SchedulerUpsertRequest request)
    {
        entity.Name = request.Name.Trim();
        // Custom (RoundRobinType = 2) is always a single group.
        var isCustom = request.RoundRobinType == 2;
        entity.GroupCount = isCustom ? 1 : request.GroupCount;
        entity.RoundRobinType = request.RoundRobinType;
        entity.QualifyPerGroup = request.QualifyPerGroup;
        entity.KnockoutFormat = request.KnockoutFormat;
        entity.Status = request.Status;
        entity.WinnerTeamId = request.WinnerTeamId;
        entity.QualificationPicksJson = request.QualificationPicksJson;
        entity.UpdatedAtUtc = DateTime.Now;
        entity.IsDeleted = false;
        AddChildren(entity, request, isCustom);
    }

    private static void AddChildren(SchedulerTournament entity, SchedulerUpsertRequest request, bool isCustom)
    {
        foreach (var team in request.Teams ?? [])
        {
            entity.Teams.Add(new SchedulerTeam
            {
                Id = team.Id == Guid.Empty ? Guid.NewGuid() : team.Id,
                TournamentId = entity.Id,
                Name = (team.Name ?? string.Empty).Trim(),
                GroupNumber = isCustom ? 1 : team.GroupNumber,
                SortOrder = team.SortOrder
            });
        }

        foreach (var match in request.Matches ?? [])
        {
            entity.Matches.Add(new SchedulerMatch
            {
                Id = match.Id == Guid.Empty ? Guid.NewGuid() : match.Id,
                TournamentId = entity.Id,
                Stage = match.Stage,
                GroupNumber = match.GroupNumber,
                Sequence = match.Sequence,
                Label = match.Label ?? string.Empty,
                TeamAId = match.TeamAId,
                TeamBId = match.TeamBId,
                TeamASlot = match.TeamASlot,
                TeamBSlot = match.TeamBSlot,
                Result = match.Result,
                WinnerFeedsMatchId = match.WinnerFeedsMatchId,
                WinnerFeedsIsTeamA = match.WinnerFeedsIsTeamA,
                LoserFeedsMatchId = match.LoserFeedsMatchId,
                LoserFeedsIsTeamA = match.LoserFeedsIsTeamA
            });
        }
    }

    private static SchedulerListDto ToListDto(SchedulerTournament t, IReadOnlyDictionary<Guid, string> userNames)
    {
        var names = t.Teams.ToDictionary(x => x.Id, x => x.Name);
        var winner = t.WinnerTeamId is Guid wid && names.TryGetValue(wid, out var n) ? n : null;
        return new SchedulerListDto
        {
            Id = t.Id,
            Name = t.Name,
            GroupCount = t.GroupCount,
            TeamCount = t.Teams?.Count ?? 0,
            RoundRobinType = t.RoundRobinType,
            QualifyPerGroup = t.QualifyPerGroup,
            KnockoutFormat = t.KnockoutFormat,
            Status = t.Status,
            StatusText = StatusText(t.Status),
            MetaLine = SetupHint(t),
            WinnerLine = winner is null ? string.Empty : $"Champion: {winner}",
            CanEdit = t.Status != 3,
            CreatedByUserId = t.CreatedByUserId,
            CreatedByUsername = userNames.GetValueOrDefault(t.CreatedByUserId, "Unknown user"),
            UpdatedAtUtc = t.UpdatedAtUtc
        };
    }

    private static SchedulerDetailDto ToDetailDto(SchedulerTournament t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        GroupCount = t.GroupCount,
        RoundRobinType = t.RoundRobinType,
        QualifyPerGroup = t.QualifyPerGroup,
        KnockoutFormat = t.KnockoutFormat,
        Status = t.Status,
        WinnerTeamId = t.WinnerTeamId,
        QualificationPicksJson = t.QualificationPicksJson,
        CreatedAtUtc = t.CreatedAtUtc,
        UpdatedAtUtc = t.UpdatedAtUtc,
        Teams = t.Teams.OrderBy(x => x.SortOrder).Select(x => new SchedulerTeamDto
        {
            Id = x.Id,
            Name = x.Name,
            GroupNumber = x.GroupNumber,
            SortOrder = x.SortOrder
        }).ToList(),
        Matches = t.Matches.OrderBy(x => x.Sequence).Select(x => new SchedulerMatchDto
        {
            Id = x.Id,
            Stage = x.Stage,
            GroupNumber = x.GroupNumber,
            Sequence = x.Sequence,
            Label = x.Label,
            TeamAId = x.TeamAId,
            TeamBId = x.TeamBId,
            TeamASlot = x.TeamASlot,
            TeamBSlot = x.TeamBSlot,
            Result = x.Result,
            WinnerFeedsMatchId = x.WinnerFeedsMatchId,
            WinnerFeedsIsTeamA = x.WinnerFeedsIsTeamA,
            LoserFeedsMatchId = x.LoserFeedsMatchId,
            LoserFeedsIsTeamA = x.LoserFeedsIsTeamA
        }).ToList()
    };

    private static string StatusText(byte status) => status switch
    {
        1 => "Group stage",
        2 => "Knockouts",
        3 => "Completed",
        _ => "Draft"
    };

    private static string SetupHint(SchedulerTournament t)
    {
        var rr = t.RoundRobinType switch
        {
            1 => "Double Round Robin",
            2 => "Custom",
            _ => "Single Round Robin"
        };
        var ko = t.KnockoutFormat switch
        {
            0 => "Quarter Finals → Semi Finals → Final",
            1 => "Playoff → Semi Final → Final",
            2 => "Semi Finals → Final",
            4 => "No (round matches only)",
            _ => "Final"
        };
        var teams = t.Teams?.Count ?? 0;
        var groups = t.GroupCount == 1 ? "1 group" : $"{t.GroupCount} groups";
        var teamLabel = teams == 1 ? "1 team" : $"{teams} teams";
        return t.KnockoutFormat == 4
            ? $"{rr} · {groups} {teamLabel} · round matches only — no knockout"
            : $"{rr} · {groups} {teamLabel} · top {t.QualifyPerGroup}/group · {ko}";
    }
}
