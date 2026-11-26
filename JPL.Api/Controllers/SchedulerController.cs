using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JPL.Api.Contracts;
using JPL.Api.Data;
using JPL.Api.Models;
using JPL.Api.Services;

namespace JPL.Api.Controllers;

[ApiController]
[Route("api/scheduler")]
public sealed class SchedulerController : ControllerBase
{
    private static readonly JsonSerializerOptions SchedulerPayloadJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IApiDbFactory _dbs;

    public SchedulerController(IApiDbFactory dbs) => _dbs = dbs;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SchedulerListDto>>> List(CancellationToken ct)
    {
        if (!TryGetCaller(out var userId, out var isPlatformAdmin, out var canWrite, out var homeLeagueId, out var error))
            return error;

        await using var db = _dbs.CreateMaster();
        var visibleCreators = await VisibleCreatorIdsAsync(db, userId, isPlatformAdmin, homeLeagueId, ct)
            .ConfigureAwait(false);
        var query = db.SchedulerTournaments.AsNoTracking()
            .Include(t => t.Teams)
            .Include(t => t.Matches)
            .AsQueryable();
        if (visibleCreators is not null)
            query = query.Where(t => visibleCreators.Contains(t.CreatedByUserId));

        var rows = await query
            .OrderByDescending(t => t.UpdatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var names = await LoadUserNamesAsync(db, rows.Select(t => t.CreatedByUserId), ct)
            .ConfigureAwait(false);
        return Ok(rows.Select(t => ToListDto(t, names, canWrite, userId)).ToList());
    }

    [HttpGet("page")]
    public async Task<ActionResult<PagedResult<SchedulerListDto>>> GetPage(
        int skip = 0, int take = 15, string? q = null, Guid? createdBy = null, CancellationToken ct = default)
    {
        if (!TryGetCaller(out var userId, out var isPlatformAdmin, out var canWrite, out var homeLeagueId, out var error))
            return error;

        (skip, take) = Paging.Clamp(skip, take);
        await using var db = _dbs.CreateMaster();
        var visibleCreators = await VisibleCreatorIdsAsync(db, userId, isPlatformAdmin, homeLeagueId, ct)
            .ConfigureAwait(false);
        var query = db.SchedulerTournaments.AsNoTracking().AsQueryable();
        if (visibleCreators is not null)
            query = query.Where(t => visibleCreators.Contains(t.CreatedByUserId));
        if (isPlatformAdmin && createdBy is Guid ownerId && ownerId != Guid.Empty)
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
        return Ok(new PagedResult<SchedulerListDto>(
            rows.Select(t => ToListDto(t, names, canWrite, userId)).ToList(), total, skip, take));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SchedulerDetailDto>> Get(Guid id, CancellationToken ct)
    {
        if (!TryGetCaller(out var userId, out var isPlatformAdmin, out _, out var homeLeagueId, out var error))
            return error;

        await using var db = _dbs.CreateMaster();
        var visibleCreators = await VisibleCreatorIdsAsync(db, userId, isPlatformAdmin, homeLeagueId, ct)
            .ConfigureAwait(false);
        var row = await LoadAsync(db, id, userId, isPlatformAdmin, visibleCreators, tracked: false, ct)
            .ConfigureAwait(false);
        if (row is null)
            return NotFound();
        var dto = ToDetailDto(row);
        dto.CreatedByUsername = await db.Users.AsNoTracking()
            .Where(user => user.Id == row.CreatedByUserId)
            .Select(user => user.Username)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        return Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<SchedulerDetailDto>> Create(
        [FromBody] SchedulerUpsertRequest request, CancellationToken ct)
    {
        if (!TryGetCaller(out var userId, out _, out var canWrite, out _, out var error))
            return error;
        if (!canWrite)
            return StatusCode(StatusCodes.Status403Forbidden, "League Admin required to manage Tournament Scheduler.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Enter a tournament name.");

        await using var db = _dbs.CreateMaster();
        var entity = new SchedulerTournament
        {
            Id = request.Id is Guid id && id != Guid.Empty ? id : Guid.NewGuid(),
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            Version = 1
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
        if (!TryGetCaller(out var userId, out _, out var canWrite, out _, out var error))
            return error;
        if (!canWrite)
            return StatusCode(StatusCodes.Status403Forbidden, "League Admin required to manage Tournament Scheduler.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Enter a tournament name.");

        await using var db = _dbs.CreateMaster();
        var current = await db.SchedulerTournaments.AsNoTracking()
            .Where(t => t.Id == id && t.CreatedByUserId == userId)
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
                .SetProperty(t => t.Version, t => t.Version + 1)
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

        var saved = await LoadAsync(db, id, userId, isPlatformAdmin: false, visibleCreators: null, tracked: false, ct)
            .ConfigureAwait(false);
        return Ok(ToDetailDto(saved!));
    }

    /// <summary>
    /// Applies one durable client outbox operation. The receipt and aggregate commit together,
    /// so a retry after a lost response cannot create duplicates or apply twice.
    /// </summary>
    [HttpPost("sync")]
    public async Task<ActionResult<SchedulerSyncResponse>> Sync(
        [FromBody] SchedulerSyncRequest request, CancellationToken ct)
    {
        if (!TryGetCaller(out var userId, out _, out var canWrite, out _, out var error))
            return error;
        if (!canWrite)
            return StatusCode(StatusCodes.Status403Forbidden, "League Admin required to manage Tournament Scheduler.");
        if (request.OperationId == Guid.Empty || request.TournamentId == Guid.Empty)
            return BadRequest("Scheduler operation and tournament ids are required.");
        if (request.ExpectedVersion < 0)
            return BadRequest("Expected scheduler version is invalid.");
        var isDelete = string.Equals(request.Operation, "Delete", StringComparison.OrdinalIgnoreCase);
        if (!isDelete && !string.Equals(request.Operation, "Upsert", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Unsupported scheduler operation.");

        SchedulerUpsertRequest? payload = null;
        if (!isDelete)
        {
            try { payload = request.Payload.Deserialize<SchedulerUpsertRequest>(SchedulerPayloadJson); }
            catch (JsonException) { return BadRequest("Scheduler payload is invalid."); }
            if (payload is null || payload.Id != request.TournamentId || string.IsNullOrWhiteSpace(payload.Name))
                return BadRequest("Scheduler payload is incomplete or has a mismatched id.");
        }

        var payloadJson = request.Payload.ValueKind == JsonValueKind.Undefined
            ? "null"
            : request.Payload.GetRawText();
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{request.TournamentId:D}|{request.ExpectedVersion}|{(isDelete ? "Delete" : "Upsert")}|{payloadJson}")));

        await using var db = _dbs.CreateMaster();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            .ConfigureAwait(false);

        var receipt = await db.SchedulerSyncReceipts.AsNoTracking()
            .FirstOrDefaultAsync(row => row.OperationId == request.OperationId, ct).ConfigureAwait(false);
        if (receipt is not null)
        {
            if (!string.Equals(receipt.PayloadHash, payloadHash, StringComparison.Ordinal))
                return Conflict("Scheduler operation id was reused with different content.");
            return Ok(new SchedulerSyncResponse
            {
                TournamentId = receipt.TournamentId,
                AppliedVersion = receipt.AppliedVersion,
                WasDuplicate = true
            });
        }

        var current = await db.SchedulerTournaments.IgnoreQueryFilters()
            .Include(row => row.Teams)
            .Include(row => row.Matches)
            .FirstOrDefaultAsync(row => row.Id == request.TournamentId
                && row.CreatedByUserId == userId, ct).ConfigureAwait(false);

        long appliedVersion;
        if (request.ExpectedVersion == 0)
        {
            if (isDelete || current is not null)
                return Conflict("Scheduler was created or changed on another client.");

            current = new SchedulerTournament
            {
                Id = request.TournamentId,
                CreatedByUserId = userId,
                CreatedAtUtc = DateTime.UtcNow,
                Version = 1
            };
            Apply(current, payload!);
            current.Version = 1;
            db.SchedulerTournaments.Add(current);
            appliedVersion = current.Version;
        }
        else
        {
            if (current is null)
                return NotFound("Scheduler is no longer available to this account.");
            if (current.Version != request.ExpectedVersion)
                return Conflict($"SYNC_CONFLICT: Scheduler changed on another client (server version {current.Version}, local base {request.ExpectedVersion}). Local changes are still saved on this device.");
            if (current.IsDeleted)
                return Conflict("SYNC_CONFLICT: Scheduler was deleted on another client.");

            if (isDelete)
            {
                if (current.Status == 3 || current.WinnerTeamId is not null)
                    return Conflict("SYNC_CONFLICT: A completed scheduler cannot be deleted.");
                current.IsDeleted = true;
                current.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                if (current.Status == 3 && current.WinnerTeamId is not null)
                    return Conflict("SYNC_CONFLICT: A completed scheduler cannot be edited.");
                ApplyExisting(current, payload!);
            }

            current.Version++;
            appliedVersion = current.Version;
        }

        db.SchedulerSyncReceipts.Add(new SchedulerSyncReceipt
        {
            OperationId = request.OperationId,
            TournamentId = request.TournamentId,
            AppliedVersion = appliedVersion,
            PayloadHash = payloadHash,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        return Ok(new SchedulerSyncResponse
        {
            TournamentId = request.TournamentId,
            AppliedVersion = appliedVersion,
            WasDuplicate = false
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!TryGetCaller(out var userId, out _, out var canWrite, out _, out var error))
            return error;
        if (!canWrite)
            return StatusCode(StatusCodes.Status403Forbidden, "League Admin required to manage Tournament Scheduler.");

        await using var db = _dbs.CreateMaster();
        var row = await db.SchedulerTournaments.AsNoTracking()
            .Where(t => t.Id == id && t.CreatedByUserId == userId)
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
                .SetProperty(t => t.UpdatedAtUtc, DateTime.Now)
                .SetProperty(t => t.Version, t => t.Version + 1), ct)
            .ConfigureAwait(false);
        if (affected == 0)
            return NotFound();
        return NoContent();
    }

    private bool TryGetCaller(
        out Guid userId,
        out bool isPlatformAdmin,
        out bool canWrite,
        out Guid? homeLeagueId,
        out ActionResult error)
    {
        userId = Guid.Empty;
        isPlatformAdmin = false;
        canWrite = false;
        homeLeagueId = null;
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

        if (HttpContext.Items.TryGetValue("JplHomeLeagueId", out var leagueObj) && leagueObj is Guid lid && lid != Guid.Empty)
            homeLeagueId = lid;
        else
        {
            var leagueText = User.FindFirst("league_id")?.Value;
            if (Guid.TryParse(leagueText, out var claimLeague) && claimLeague != Guid.Empty)
                homeLeagueId = claimLeague;
        }

        isPlatformAdmin = string.Equals(role, nameof(AppRole.SuperAdmin), StringComparison.OrdinalIgnoreCase)
                          && PlatformAdmin.Is(username);
        // League Admin writes only — SuperAdmin is user management, not scheduler ops.
        canWrite = string.Equals(role, nameof(AppRole.Admin), StringComparison.OrdinalIgnoreCase);
        error = null!;
        return true;
    }

    /// <summary>
    /// null = no filter (platform admin sees all).
    /// Otherwise restrict to creators in the caller's home league (League Admin + League User view).
    /// </summary>
    private static async Task<HashSet<Guid>?> VisibleCreatorIdsAsync(
        MasterDbContext db,
        Guid userId,
        bool isPlatformAdmin,
        Guid? homeLeagueId,
        CancellationToken ct)
    {
        if (isPlatformAdmin)
            return null;

        if (homeLeagueId is Guid leagueId && leagueId != Guid.Empty)
        {
            var ids = await db.Users.AsNoTracking()
                .Where(u => u.LeagueId == leagueId)
                .Select(u => u.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (!ids.Contains(userId))
                ids.Add(userId);
            return ids.ToHashSet();
        }

        return new HashSet<Guid> { userId };
    }

    private static async Task<SchedulerTournament?> LoadAsync(
        MasterDbContext db,
        Guid id,
        Guid userId,
        bool isPlatformAdmin,
        HashSet<Guid>? visibleCreators,
        bool tracked,
        CancellationToken ct)
    {
        var query = tracked
            ? db.SchedulerTournaments.Include(t => t.Teams).Include(t => t.Matches)
            : db.SchedulerTournaments.AsNoTracking().Include(t => t.Teams).Include(t => t.Matches);
        // Expression trees cannot use `is not null` pattern matching (CS8122).
        var creators = visibleCreators;
        return await query
            .FirstOrDefaultAsync(
                t => t.Id == id && (
                    isPlatformAdmin
                    || t.CreatedByUserId == userId
                    || (creators != null && creators.Contains(t.CreatedByUserId))),
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

    private static void ApplyExisting(SchedulerTournament entity, SchedulerUpsertRequest request)
    {
        var isCustom = request.RoundRobinType == 2;
        entity.Name = request.Name.Trim();
        entity.GroupCount = isCustom ? 1 : request.GroupCount;
        entity.RoundRobinType = request.RoundRobinType;
        entity.QualifyPerGroup = request.QualifyPerGroup;
        entity.KnockoutFormat = request.KnockoutFormat;
        entity.Status = request.Status;
        entity.WinnerTeamId = request.WinnerTeamId;
        entity.QualificationPicksJson = request.QualificationPicksJson;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.IsDeleted = false;

        var teams = (request.Teams ?? []).ToDictionary(row => row.Id);
        foreach (var current in entity.Teams.ToList())
        {
            if (!teams.Remove(current.Id, out var next))
            {
                entity.Teams.Remove(current);
                continue;
            }
            current.Name = (next.Name ?? string.Empty).Trim();
            current.GroupNumber = isCustom ? 1 : next.GroupNumber;
            current.SortOrder = next.SortOrder;
        }
        foreach (var next in teams.Values)
            entity.Teams.Add(new SchedulerTeam
            {
                Id = next.Id == Guid.Empty ? Guid.NewGuid() : next.Id,
                TournamentId = entity.Id,
                Name = (next.Name ?? string.Empty).Trim(),
                GroupNumber = isCustom ? 1 : next.GroupNumber,
                SortOrder = next.SortOrder
            });

        var matches = (request.Matches ?? []).ToDictionary(row => row.Id);
        foreach (var current in entity.Matches.ToList())
        {
            if (!matches.Remove(current.Id, out var next))
            {
                entity.Matches.Remove(current);
                continue;
            }
            CopyMatch(current, next);
        }
        foreach (var next in matches.Values)
        {
            var match = new SchedulerMatch { Id = next.Id == Guid.Empty ? Guid.NewGuid() : next.Id, TournamentId = entity.Id };
            CopyMatch(match, next);
            entity.Matches.Add(match);
        }
    }

    private static void CopyMatch(SchedulerMatch target, SchedulerMatchDto source)
    {
        target.Stage = source.Stage;
        target.GroupNumber = source.GroupNumber;
        target.Sequence = source.Sequence;
        target.Label = source.Label ?? string.Empty;
        target.TeamAId = source.TeamAId;
        target.TeamBId = source.TeamBId;
        target.TeamASlot = source.TeamASlot;
        target.TeamBSlot = source.TeamBSlot;
        target.Result = source.Result;
        target.WinnerFeedsMatchId = source.WinnerFeedsMatchId;
        target.WinnerFeedsIsTeamA = source.WinnerFeedsIsTeamA;
        target.LoserFeedsMatchId = source.LoserFeedsMatchId;
        target.LoserFeedsIsTeamA = source.LoserFeedsIsTeamA;
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

    private static SchedulerListDto ToListDto(
        SchedulerTournament t,
        IReadOnlyDictionary<Guid, string> userNames,
        bool canWrite,
        Guid userId)
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
            // Only the creating League Admin may edit; same-league peers are view-only.
            CanEdit = canWrite && t.CreatedByUserId == userId && t.Status != 3,
            CreatedByUserId = t.CreatedByUserId,
            CreatedByUsername = userNames.GetValueOrDefault(t.CreatedByUserId, "Unknown user"),
            UpdatedAtUtc = t.UpdatedAtUtc,
            Version = t.Version
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
        CreatedByUserId = t.CreatedByUserId,
            CreatedByUsername = string.Empty,
        Version = t.Version,
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
