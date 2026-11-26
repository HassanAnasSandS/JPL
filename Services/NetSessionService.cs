using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public sealed class NetRosterPayload
{
    public List<SquadPlayer> Players { get; set; } = new();

    public static NetRosterPayload? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<NetRosterPayload>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static string Serialize(NetRosterPayload roster)
        => JsonSerializer.Serialize(roster, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}

public sealed record NetSessionListItem(
    Guid Id,
    string Name,
    NetSessionStatus Status,
    DateTime SessionDateUtc,
    int PlayerCount,
    int Runs,
    int Wickets,
    int Overs,
    int BallsInOver)
{
    public bool IsActive => Status is NetSessionStatus.Setup or NetSessionStatus.InProgress;
    public bool IsLocal { get; init; } = true;
    public bool AllowMutate { get; init; } = true;
    public bool CanScore => IsActive && AllowMutate && IsLocal;
    public bool CanDelete => CanScore;
    public bool NeedsSync { get; init; }
    public string ActionLabel => CanScore ? "Continue" : "Scorecard";
    public string DateLabel => SessionDateUtc.ToString("dd MMM yyyy");
    public string ScoreText => $"{Runs}/{Wickets} ({Overs}.{BallsInOver})";
    public string MetaLine => $"{PlayerCount} players · {DateLabel}";
    public string StatusText => Status switch
    {
        NetSessionStatus.Setup => "Setup",
        NetSessionStatus.InProgress => "In progress",
        NetSessionStatus.Completed => "Completed",
        _ => Status.ToString()
    };
}

public sealed record NetSessionDraft(
    string Name,
    IReadOnlyList<Guid> PlayerIds,
    DateTime? SessionDateUtc = null);

public sealed record NetLiveSnapshot(
    Guid SessionId,
    int Runs,
    int Wickets,
    int Overs,
    int BallsInCurrentOver,
    Guid? BatterId,
    Guid? BowlerId,
    string ScoreText)
{
    public int CurrentOverNumber { get; init; } = 1;
    public IReadOnlyList<NetOverBallMark> ThisOver { get; init; } = Array.Empty<NetOverBallMark>();
}

public sealed class NetOverBallMark
{
    public string Text { get; init; } = "•";
    public string Kind { get; init; } = "dot";
    public int BallNumber { get; init; }
}

public sealed record NetScoringContext(
    Guid SessionId,
    string Name,
    IReadOnlyList<SquadPlayer> Players);

public sealed class NetSessionScorecard
{
    public Guid SessionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MetaLine { get; set; } = string.Empty;
    public int TotalRuns { get; set; }
    public int Wickets { get; set; }
    public int Extras { get; set; }
    public string OversText { get; set; } = "0.0";
    public IReadOnlyList<BattingCardRow> Batting { get; set; } = Array.Empty<BattingCardRow>();
    /// <summary>One row per batter with session totals (bowling-style compact card).</summary>
    public IReadOnlyList<BattingCardRow> CombinedBatting { get; set; } = Array.Empty<BattingCardRow>();
    public IReadOnlyList<BowlingCardRow> Bowling { get; set; } = Array.Empty<BowlingCardRow>();
    public IReadOnlyList<NetScorecardDelivery> Deliveries { get; set; } = Array.Empty<NetScorecardDelivery>();
    public IReadOnlyList<SquadPlayer> Players { get; set; } = Array.Empty<SquadPlayer>();
    public IReadOnlyList<MatchPointsRow> Points { get; set; } = Array.Empty<MatchPointsRow>();
    public Guid? PlayerOfTheSessionPlayerId { get; set; }
    public Guid? BestBatsmanPlayerId { get; set; }
    public Guid? BestBowlerPlayerId { get; set; }
    public Guid? BestFielderPlayerId { get; set; }
    public string TotalText => $"{TotalRuns}/{Wickets}";
    public string ExtrasText => $"Extras {Extras}";
}

/// <summary>Ball data returned with a Nets scorecard so points can be calculated on any device.</summary>
public sealed class NetScorecardDelivery
{
    public Guid Id { get; set; }
    public int Sequence { get; set; }
    public int OverNumber { get; set; }
    public int BallInOver { get; set; }
    public Guid BatterId { get; set; }
    public Guid NonStrikerId { get; set; }
    public Guid BowlerId { get; set; }
    public int RunsOffBat { get; set; }
    public int ExtraRuns { get; set; }
    public ExtraType ExtraType { get; set; }
    public bool IsWicket { get; set; }
    public WicketType WicketType { get; set; }
    public Guid? DismissedPlayerId { get; set; }
    public Guid? FielderId { get; set; }
    public bool IsDroppedCatch { get; set; }
    public bool IsLegalDelivery { get; set; }
}

public interface INetSessionService
{
    Task<IReadOnlyList<NetSessionListItem>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<NetSessionListItem>> GetPinnedListAsync(CancellationToken ct = default);
    Task<ApiPagedResult<NetSessionListItem>> GetCompletedPageAsync(
        int skip, string? q, string? date, CancellationToken ct = default);
    Task<NetSessionListItem?> GetActiveAsync(CancellationToken ct = default);
    Task<HashSet<Guid>> GetPendingSyncIdsAsync(CancellationToken ct = default);
    Task<NetSession> CreateAsync(NetSessionDraft draft, CancellationToken ct = default);
    Task UpdateSetupAsync(Guid sessionId, NetSessionDraft draft, CancellationToken ct = default);
    Task<NetSession?> GetAsync(Guid sessionId, CancellationToken ct = default);
    Task<NetDelivery> RecordDeliveryAsync(Guid sessionId, DeliveryInput input, CancellationToken ct = default);
    Task<UndoneDelivery> UndoLastDeliveryAsync(Guid sessionId, CancellationToken ct = default);
    Task<NetLiveSnapshot?> GetLiveSnapshotAsync(Guid sessionId, CancellationToken ct = default);
    Task<NetScoringContext?> GetScoringContextAsync(Guid sessionId, CancellationToken ct = default);
    Task<NetSessionScorecard?> GetScorecardAsync(Guid sessionId, Guid? strikerId = null, CancellationToken ct = default);
    Task CompleteAsync(Guid sessionId, CancellationToken ct = default);
    Task DeleteOngoingAsync(Guid sessionId, CancellationToken ct = default);
}

public sealed class NetSessionService : INetSessionService
{
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IOutboxWriter _outbox;
    private readonly ISyncTrigger _syncTrigger;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly IPlayerService _players;
    private readonly IApiClient _api;
    private readonly ILivePublisher _live;

    public NetSessionService(
        IDbContextFactory<JplDbContext> dbFactory,
        IOutboxWriter outbox,
        ISyncTrigger syncTrigger,
        ISyncService sync,
        IAuthService auth,
        ILeagueContext league,
        IPlayerService players,
        IApiClient api,
        ILivePublisher live)
    {
        _dbFactory = dbFactory;
        _outbox = outbox;
        _syncTrigger = syncTrigger;
        _sync = sync;
        _auth = auth;
        _league = league;
        _players = players;
        _api = api;
        _live = live;
    }

    public async Task<IReadOnlyList<NetSessionListItem>> GetAllAsync(CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetLiveNetSessionsAsync(ct).ConfigureAwait(false);
            var completed = await _api.GetCompletedNetSessionsAsync(ct).ConfigureAwait(false);
            return live.Concat(completed).GroupBy(row => row.Id).Select(group => group.First())
                .Select(row => FromApi(row, needsSync: false, allowMutate: false))
                .OrderByDescending(row => row.IsActive).ThenByDescending(row => row.SessionDateUtc).ToList();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var preferLocal = await LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct).ConfigureAwait(false);
        var sessions = await db.NetSessions.AsNoTracking()
            .Where(s => s.Status == NetSessionStatus.Setup
                        || s.Status == NetSessionStatus.InProgress
                        || s.Status == NetSessionStatus.Completed)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var pending = await _sync.GetPendingNetSessionIdsAsync(ct).ConfigureAwait(false);
        var allow = _auth.CanWrite;
        var byId = await BuildLocalListItemsAsync(db, sessions, pending, allow, ct).ConfigureAwait(false);

        if (!preferLocal)
        {
            try
            {
                var remote = await _api.GetCompletedNetSessionsAsync(ct).ConfigureAwait(false);
                foreach (var row in remote)
                {
                    if (byId.TryGetValue(row.Id, out var existing) && existing.IsActive)
                        continue;
                    byId[row.Id] = FromApi(row, pending.Contains(row.Id), allow);
                }
            }
            catch
            {
                /* offline — local only */
            }
        }

        return byId.Values
            .OrderByDescending(s => s.IsActive)
            .ThenByDescending(s => s.SessionDateUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<NetSessionListItem>> GetPinnedListAsync(CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
            return (await _api.GetLiveNetSessionsAsync(ct).ConfigureAwait(false))
                .Select(row => FromApi(row, needsSync: false, allowMutate: false)).ToList();

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var pending = await _sync.GetPendingNetSessionIdsAsync(ct).ConfigureAwait(false);
        var pendingIds = pending.ToList();
        var allow = _auth.CanWrite;
        var sessions = await db.NetSessions.AsNoTracking()
            .Where(s => s.Status == NetSessionStatus.Setup
                        || s.Status == NetSessionStatus.InProgress
                        || (s.Status == NetSessionStatus.Completed && pendingIds.Contains(s.Id)))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byId = await BuildLocalListItemsAsync(db, sessions, pending, allow, ct).ConfigureAwait(false);
        return byId.Values
            .OrderByDescending(s => s.IsActive)
            .ThenByDescending(s => s.SessionDateUtc)
            .ToList();
    }

    private static async Task<Dictionary<Guid, NetSessionListItem>> BuildLocalListItemsAsync(
        JplDbContext db,
        List<NetSession> sessions,
        HashSet<Guid> pending,
        bool allow,
        CancellationToken ct)
    {
        var byId = new Dictionary<Guid, NetSessionListItem>();
        if (sessions.Count == 0)
            return byId;

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var stats = await db.NetDeliveries.AsNoTracking()
            .Where(d => sessionIds.Contains(d.SessionId))
            .GroupBy(d => d.SessionId)
            .Select(g => new
            {
                SessionId = g.Key,
                Runs = g.Sum(d => d.RunsOffBat + d.ExtraRuns),
                Wickets = g.Count(d => d.IsWicket),
                Legal = g.Count(d => d.IsLegalDelivery),
                PlayerCount = 0
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var statsById = stats.ToDictionary(row => row.SessionId);

        foreach (var s in sessions)
        {
            statsById.TryGetValue(s.Id, out var row);
            var runs = row?.Runs ?? 0;
            var wickets = row?.Wickets ?? 0;
            var legal = row?.Legal ?? 0;
            var roster = NetRosterPayload.TryParse(s.PlayerRosterJson);
            byId[s.Id] = new NetSessionListItem(
                s.Id,
                s.Name,
                s.Status,
                s.SessionDateUtc,
                roster?.Players.Count ?? 0,
                runs,
                wickets,
                legal / 6,
                legal % 6)
            {
                AllowMutate = allow,
                NeedsSync = pending.Contains(s.Id) && s.Status == NetSessionStatus.Completed,
                IsLocal = true
            };
        }

        return byId;
    }

    public async Task<ApiPagedResult<NetSessionListItem>> GetCompletedPageAsync(
        int skip, string? q, string? date, CancellationToken ct = default)
    {
        var canUseLocal = LeagueSyncPolicy.UsesLocalStorage(_auth.Current);
        var pending = await _sync.GetPendingNetSessionIdsAsync(ct).ConfigureAwait(false);
        var allow = _auth.CanWrite;
        await using (var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            if (await LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct).ConfigureAwait(false))
                return await GetLocalCompletedPageAsync(skip, q, date, pending, allow, ct).ConfigureAwait(false);
        }

        try
        {
            var page = await _api.GetCompletedNetSessionsPageAsync(skip, q, date, ct).ConfigureAwait(false);
            var items = (page.Items ?? [])
                .Select(row => FromApi(row, pending.Contains(row.Id), allow))
                .ToList();
            return page.WithItems(items);
        }
        catch
        {
            if (canUseLocal)
                return await GetLocalCompletedPageAsync(skip, q, date, pending, allow, ct).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<ApiPagedResult<NetSessionListItem>> GetLocalCompletedPageAsync(
        int skip, string? q, string? date, HashSet<Guid> pending, bool allow, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var sessions = await db.NetSessions.AsNoTracking()
            .Where(session => session.Status == NetSessionStatus.Completed)
            .ToListAsync(ct).ConfigureAwait(false);
        var ids = sessions.Select(session => session.Id).ToHashSet();
        var deliveries = await db.NetDeliveries.AsNoTracking()
            .Where(delivery => ids.Contains(delivery.SessionId))
            .OrderBy(delivery => delivery.Sequence)
            .ToListAsync(ct).ConfigureAwait(false);
        var deliveriesBySession = deliveries.GroupBy(delivery => delivery.SessionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<NetDelivery>)group.ToList());
        IEnumerable<NetSessionListItem> rows = sessions.Select(session => ToListItem(session,
            deliveriesBySession.GetValueOrDefault(session.Id, Array.Empty<NetDelivery>()),
            pending.Contains(session.Id), allow));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            rows = rows.Where(row => row.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(date))
        {
            var term = date.Trim();
            rows = rows.Where(row => row.SessionDateUtc.ToString("yyyy-MM-dd")
                .Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        var materialized = rows.OrderByDescending(row => row.SessionDateUtc).ToList();
        var pageSize = ListPaging.PageSize;
        return new ApiPagedResult<NetSessionListItem>(materialized.Skip(Math.Max(0, skip))
                .Take(pageSize).ToList(), materialized.Count, Math.Max(0, skip), pageSize);
    }

    public async Task<NetSessionListItem?> GetActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        // Materialize then sort — keeps list ordering consistent with merged API rows.
        var session = (await db.NetSessions.AsNoTracking()
                .Where(s => s.Status == NetSessionStatus.Setup || s.Status == NetSessionStatus.InProgress)
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .OrderByDescending(s => s.Status == NetSessionStatus.InProgress)
            .ThenByDescending(s => s.UpdatedAtUtc)
            .FirstOrDefault();
        if (session is null)
            return null;

        var balls = await db.NetDeliveries.AsNoTracking()
            .Where(d => d.SessionId == session.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return ToListItem(session, balls, needsSync: false, allowMutate: _auth.CanWrite);
    }

    public Task<HashSet<Guid>> GetPendingSyncIdsAsync(CancellationToken ct = default)
        => _sync.GetPendingNetSessionIdsAsync(ct);

    /// <summary>Local SQLite only — never POST to API. Sync happens on complete via outbox.</summary>
    public async Task<NetSession> CreateAsync(NetSessionDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var name = RequireName(draft.Name);
        var roster = await BuildRosterAsync(draft.PlayerIds, ct).ConfigureAwait(false);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await EnsureSoleActiveAsync(db, exceptId: null, ct).ConfigureAwait(false);

        var session = new NetSession
        {
            Name = name,
            SessionDateUtc = draft.SessionDateUtc ?? DateTime.Now,
            Status = NetSessionStatus.Setup,
            PlayerRosterJson = NetRosterPayload.Serialize(roster)
        };
        db.NetSessions.Add(session);
        await _live.EnqueueNetSessionAsync(db, session, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _live.RequestFlush();
        return session;
    }

    public async Task UpdateSetupAsync(Guid sessionId, NetSessionDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var name = RequireName(draft.Name);
        var roster = await BuildRosterAsync(draft.PlayerIds, ct).ConfigureAwait(false);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var session = await db.NetSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Session not found.");

        if (session.Status == NetSessionStatus.Completed)
            throw new InvalidOperationException("Completed sessions cannot be edited.");

        session.Name = name;
        session.PlayerRosterJson = NetRosterPayload.Serialize(roster);
        if (draft.SessionDateUtc is DateTime when)
            session.SessionDateUtc = when;
        session.UpdatedAtUtc = DateTime.Now;
        session.Version++;
        await _live.EnqueueNetSessionAsync(db, session, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _live.RequestFlush();
    }

    public async Task<NetSession?> GetAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.NetSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false);
    }

    public async Task<NetDelivery> RecordDeliveryAsync(Guid sessionId, DeliveryInput input, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var session = await db.NetSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Session not found.");

        if (session.Status == NetSessionStatus.Completed)
            throw new InvalidOperationException("Session is already completed.");

        // Sole-active checked at session start — skip per-ball query.

        var legalSoFar = await db.NetDeliveries
            .CountAsync(d => d.SessionId == sessionId && d.IsLegalDelivery, ct)
            .ConfigureAwait(false);
        var maxSequence = await db.NetDeliveries
            .Where(d => d.SessionId == sessionId)
            .Select(d => (int?)d.Sequence)
            .MaxAsync(ct)
            .ConfigureAwait(false);
        var sequence = (maxSequence ?? 0) + 1;
        var overNumber = legalSoFar / 6;
        var ballInOver = legalSoFar % 6;
        var isWideOrNb = input.ExtraType is ExtraType.Wide or ExtraType.NoBall;
        var isLegal = !isWideOrNb;
        if (input.RunsOffBat == 5)
            throw new InvalidOperationException("5 runs off the bat is not allowed.");

        var delivery = new NetDelivery
        {
            SessionId = sessionId,
            Sequence = sequence,
            OverNumber = overNumber,
            BallInOver = isLegal ? ballInOver + 1 : ballInOver,
            BatterId = input.BatterId,
            NonStrikerId = input.BatterId,
            BowlerId = input.BowlerId,
            RunsOffBat = Math.Max(0, input.RunsOffBat),
            ExtraRuns = Math.Max(0, input.ExtraRuns),
            ExtraType = input.ExtraType,
            IsWicket = input.IsWicket,
            WicketType = input.IsWicket ? input.WicketType : WicketType.None,
            DismissedPlayerId = input.IsWicket ? (input.DismissedPlayerId ?? input.BatterId) : null,
            IsDroppedCatch = !input.IsWicket && input.IsDroppedCatch,
            FielderId = input.IsWicket || (!input.IsWicket && input.IsDroppedCatch)
                ? input.FielderId
                : null,
            IsLegalDelivery = isLegal
        };

        session.Status = NetSessionStatus.InProgress;
        session.UpdatedAtUtc = DateTime.Now;
        session.Version++;

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        db.NetDeliveries.Add(delivery);
        await _live.EnqueueNetSessionAsync(db, session, ct).ConfigureAwait(false);
        await _live.EnqueueNetDeliveryAsync(db, delivery, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        _live.RequestFlush();
        return delivery;
    }

    public async Task<UndoneDelivery> UndoLastDeliveryAsync(Guid sessionId, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var session = await db.NetSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Session not found.");

        if (session.Status == NetSessionStatus.Completed)
            throw new InvalidOperationException("Completed sessions cannot be undone.");

        var last = await db.NetDeliveries
            .Where(d => d.SessionId == sessionId)
            .OrderByDescending(d => d.Sequence)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("No deliveries left to undo.");

        var undone = new UndoneDelivery(
            last.BatterId,
            last.NonStrikerId,
            last.BowlerId,
            last.IsWicket,
            last.DismissedPlayerId);

        var undoneId = last.Id;
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        db.NetDeliveries.Remove(last);
        session.UpdatedAtUtc = DateTime.Now;
        session.Version++;
        await _live.EnqueueNetDeliveryDeleteAsync(db, session, undoneId, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        _live.RequestFlush();
        return undone;
    }

    public async Task<NetLiveSnapshot?> GetLiveSnapshotAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var session = await db.NetSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false);
        if (session is null)
            return null;

        var runs = await db.NetDeliveries.AsNoTracking()
            .Where(d => d.SessionId == sessionId)
            .SumAsync(d => d.RunsOffBat + d.ExtraRuns, ct)
            .ConfigureAwait(false);
        var wickets = await db.NetDeliveries.AsNoTracking()
            .CountAsync(d => d.SessionId == sessionId && d.IsWicket, ct)
            .ConfigureAwait(false);
        var legal = await db.NetDeliveries.AsNoTracking()
            .CountAsync(d => d.SessionId == sessionId && d.IsLegalDelivery, ct)
            .ConfigureAwait(false);
        var overs = legal / 6;
        var balls = legal % 6;
        var last = await db.NetDeliveries.AsNoTracking()
            .Where(d => d.SessionId == sessionId)
            .OrderByDescending(d => d.Sequence)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        // This-over chips need only the current over's balls, not the whole session.
        var thisOverNumber = last?.OverNumber ?? 0;
        var thisOverBalls = await db.NetDeliveries.AsNoTracking()
            .Where(d => d.SessionId == sessionId && d.OverNumber == thisOverNumber)
            .OrderBy(d => d.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return new NetLiveSnapshot(
            sessionId,
            runs,
            wickets,
            overs,
            balls,
            last?.BatterId,
            last?.BowlerId,
            $"{runs}/{wickets} ({overs}.{balls})")
        {
            CurrentOverNumber = thisOverNumber + 1,
            ThisOver = BuildNetsThisOver(thisOverBalls)
        };
    }

    private static IReadOnlyList<NetOverBallMark> BuildNetsThisOver(IReadOnlyList<NetDelivery> deliveries)
    {
        if (deliveries.Count == 0)
            return Array.Empty<NetOverBallMark>();

        var overNumber = deliveries[^1].OverNumber;
        return deliveries
            .Where(d => d.OverNumber == overNumber)
            .OrderBy(d => d.Sequence)
            .Select((delivery, index) => ToNetsOverBall(delivery, index + 1))
            .ToList();
    }

    private static NetOverBallMark ToNetsOverBall(NetDelivery delivery, int ballNumber)
    {
        var mark = delivery.IsWicket
            ? new NetOverBallMark { Text = "W", Kind = "wicket" }
            : delivery.ExtraType == ExtraType.Wide
                ? new NetOverBallMark { Text = "wd", Kind = "extra" }
                : delivery.ExtraType == ExtraType.NoBall
                    ? new NetOverBallMark { Text = "nb", Kind = "extra" }
                    : (delivery.RunsOffBat + (delivery.ExtraType is ExtraType.Bye or ExtraType.LegBye ? delivery.ExtraRuns : 0)) switch
                    {
                        >= 6 => new NetOverBallMark { Text = "6", Kind = "six" },
                        4 => new NetOverBallMark { Text = "4", Kind = "four" },
                        0 => new NetOverBallMark { Text = "•", Kind = "dot" },
                        var runs => new NetOverBallMark { Text = runs.ToString(), Kind = "run" }
                    };

        return new NetOverBallMark
        {
            Text = mark.Text,
            Kind = mark.Kind,
            BallNumber = ballNumber
        };
    }

    public async Task<NetScoringContext?> GetScoringContextAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var session = await db.NetSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false);
        if (session is null)
            return null;

        var roster = NetRosterPayload.TryParse(session.PlayerRosterJson);
        var players = roster?.Players ?? new List<SquadPlayer>();
        try
        {
            var (livePlayers, _) = await LiveMasterNames.LoadAsync(_api, _dbFactory, ct).ConfigureAwait(false);
            players = MatchRosterPayload.Relabel(players, livePlayers);
        }
        catch
        {
            /* keep roster snapshot */
        }
        return new NetScoringContext(session.Id, session.Name, players);
    }

    public async Task<NetSessionScorecard?> GetScorecardAsync(
        Guid sessionId, Guid? strikerId = null, CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetNetSessionScorecardAsync(sessionId, ct).ConfigureAwait(false);
            if (live is not null)
            {
                EnsureCombinedBatting(live);
                ApplySessionPoints(live);
            }
            return live;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var session = await db.NetSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false);
        var deliveries = await db.NetDeliveries.AsNoTracking()
            .Where(d => d.SessionId == sessionId)
            .OrderBy(d => d.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (session is null || (session.Status == NetSessionStatus.Completed && deliveries.Count == 0))
        {
            if (session is null)
                return null;
        }

        var roster = NetRosterPayload.TryParse(session.PlayerRosterJson);
        var names = (roster?.Players ?? new List<SquadPlayer>())
            .ToDictionary(p => p.Id, p => p.FullName);
        try
        {
            var (livePlayers, _) = await LiveMasterNames.LoadAsync(_api, _dbFactory, ct).ConfigureAwait(false);
            foreach (var kv in livePlayers)
                names[kv.Key] = kv.Value;
        }
        catch
        {
            /* keep roster snapshot */
        }

        string NameOf(Guid id) => names.GetValueOrDefault(id, "?");

        var battingOrder = new List<Guid>();
        foreach (var d in deliveries)
        {
            if (d.BatterId != Guid.Empty && !battingOrder.Contains(d.BatterId))
                battingOrder.Add(d.BatterId);
        }
        if (strikerId is Guid sid && sid != Guid.Empty && !battingOrder.Contains(sid))
            battingOrder.Add(sid);

        var batting = BuildNetsBattingRows(deliveries, battingOrder, strikerId, NameOf);
        var combinedBatting = BuildNetsCombinedBattingRows(deliveries, battingOrder, strikerId, NameOf);

        var bowlingOrder = new List<Guid>();
        foreach (var d in deliveries)
        {
            if (!bowlingOrder.Contains(d.BowlerId))
                bowlingOrder.Add(d.BowlerId);
        }

        var bowling = new List<BowlingCardRow>();
        foreach (var pid in bowlingOrder)
        {
            var bowled = deliveries.Where(d => d.BowlerId == pid).ToList();
            bowling.Add(new BowlingCardRow
            {
                PlayerId = pid,
                Name = NameOf(pid),
                BallsBowled = bowled.Count(d => d.IsLegalDelivery),
                RunsConceded = bowled.Sum(d => d.RunsOffBat + d.ExtraRuns),
                Wickets = bowled.Count(d =>
                    d.IsWicket && d.WicketType is not WicketType.RunOut and not WicketType.Retired and not WicketType.None),
                Maidens = CountMaidens(bowled),
                Wides = bowled.Where(d => d.ExtraType == ExtraType.Wide).Sum(d => d.ExtraRuns),
                NoBalls = bowled.Where(d => d.ExtraType == ExtraType.NoBall).Sum(d => d.ExtraRuns),
                Byes = bowled.Where(d => d.ExtraType == ExtraType.Bye).Sum(d => d.ExtraRuns),
                LegByes = bowled.Where(d => d.ExtraType == ExtraType.LegBye).Sum(d => d.ExtraRuns)
            });
        }

        var legal = deliveries.Count(d => d.IsLegalDelivery);
        var scorecard = new NetSessionScorecard
        {
            SessionId = session.Id,
            Name = session.Name,
            MetaLine = $"{session.SessionDateUtc:dd MMM yyyy} · unlimited overs",
            TotalRuns = deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns),
            Wickets = deliveries.Count(d => d.IsWicket),
            Extras = deliveries.Sum(d => d.ExtraRuns),
            OversText = $"{legal / 6}.{legal % 6}",
            Batting = batting,
            CombinedBatting = combinedBatting,
            Bowling = bowling,
            Deliveries = deliveries.Select(d => new NetScorecardDelivery
            {
                Id = d.Id,
                Sequence = d.Sequence,
                OverNumber = d.OverNumber,
                BallInOver = d.BallInOver,
                BatterId = d.BatterId,
                NonStrikerId = d.NonStrikerId,
                BowlerId = d.BowlerId,
                RunsOffBat = d.RunsOffBat,
                ExtraRuns = d.ExtraRuns,
                ExtraType = d.ExtraType,
                IsWicket = d.IsWicket,
                WicketType = d.WicketType,
                DismissedPlayerId = d.DismissedPlayerId,
                FielderId = d.FielderId,
                IsDroppedCatch = d.IsDroppedCatch,
                IsLegalDelivery = d.IsLegalDelivery
            }).ToList(),
            Players = (roster?.Players ?? new List<SquadPlayer>())
                .Select(p => new SquadPlayer { Id = p.Id, FullName = names.GetValueOrDefault(p.Id, p.FullName) })
                .Concat(deliveries
                    .SelectMany(d => new[] { d.BatterId, d.NonStrikerId, d.BowlerId, d.DismissedPlayerId ?? Guid.Empty, d.FielderId ?? Guid.Empty })
                    .Where(id => id != Guid.Empty && !(roster?.Players.Any(p => p.Id == id) ?? false))
                    .Distinct()
                    .Select(id => new SquadPlayer { Id = id, FullName = names.GetValueOrDefault(id, id.ToString("N")[..8]) }))
                .GroupBy(p => p.Id)
                .Select(g => g.First())
                .ToList()
        };
        EnsureCombinedBatting(scorecard);
        ApplySessionPoints(scorecard);
        return scorecard;
    }

    /// <summary>
    /// Fills CombinedBatting when missing (API payload) by rolling spell rows up per player.
    /// </summary>
    private static void EnsureCombinedBatting(NetSessionScorecard card)
    {
        if (card.CombinedBatting is { Count: > 0 })
            return;

        if (card.Batting is not { Count: > 0 })
        {
            card.CombinedBatting = Array.Empty<BattingCardRow>();
            return;
        }

        card.CombinedBatting = card.Batting
            .GroupBy(row => row.PlayerId)
            .Select(group =>
            {
                var spells = group.ToList();
                var first = spells[0];
                var last = spells[^1];
                var outs = spells.Sum(s => s.Outs);
                return new BattingCardRow
                {
                    PlayerId = first.PlayerId,
                    Name = first.Name,
                    Runs = spells.Sum(s => s.Runs),
                    Balls = spells.Sum(s => s.Balls),
                    Fours = spells.Sum(s => s.Fours),
                    Sixes = spells.Sum(s => s.Sixes),
                    Outs = outs,
                    IsOut = last.IsOut,
                    IsOnStrike = last.IsOnStrike,
                    IsCaptain = first.IsCaptain,
                    Dismissal = outs == 0 ? string.Empty : $"{outs} out{(outs == 1 ? string.Empty : "s")}"
                };
            })
            .ToList();
    }

    private static void ApplySessionPoints(NetSessionScorecard card)
    {
        var deliveries = card.Deliveries.Select(d => new Delivery
        {
            Id = d.Id,
            MatchId = card.SessionId,
            InningsId = card.SessionId,
            Sequence = d.Sequence,
            OverNumber = d.OverNumber,
            BallInOver = d.BallInOver,
            BatterId = d.BatterId,
            NonStrikerId = d.NonStrikerId,
            BowlerId = d.BowlerId,
            RunsOffBat = d.RunsOffBat,
            ExtraRuns = d.ExtraRuns,
            ExtraType = d.ExtraType,
            IsWicket = d.IsWicket,
            WicketType = d.WicketType,
            DismissedPlayerId = d.DismissedPlayerId,
            FielderId = d.FielderId,
            IsDroppedCatch = d.IsDroppedCatch,
            IsLegalDelivery = d.IsLegalDelivery
        }).ToList();

        var players = card.Players
            .Where(p => p.Id != Guid.Empty)
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => new MatchPointsCalculator.PlayerInfo(
                string.IsNullOrWhiteSpace(g.First().FullName) ? g.Key.ToString("N")[..8] : g.First().FullName,
                "Nets"));

        var points = MatchPointsCalculator.Calculate(deliveries, players);
        // Match completion picks Player of the Match from ball participants only;
        // keep that same rule so an unused roster player cannot win on zero points.
        var overall = deliveries.Count == 0
            ? null
            : MatchPointsCalculator.Calculate(deliveries).FirstOrDefault();
        var bestBatter = points
            .Where(p => p.Runs > 0 || p.Balls > 0)
            .OrderByDescending(p => p.BattingPoints)
            .ThenByDescending(p => p.Runs)
            .ThenBy(p => p.Order)
            .FirstOrDefault();
        var bestBowler = points
            .Where(p => p.MatchStats.OversBowledBalls > 0
                        || p.MatchStats.WidesBowled > 0
                        || p.MatchStats.NoBallsBowled > 0
                        || p.MatchStats.ByesConceded > 0
                        || p.MatchStats.LegByesConceded > 0)
            .OrderByDescending(p => p.BowlingPoints)
            .ThenByDescending(p => p.Wickets)
            .ThenByDescending(p => p.Maidens)
            .ThenBy(p => p.MatchStats.RunsConceded)
            .ThenBy(p => p.Order)
            .FirstOrDefault();
        var bestFielder = points
            .Where(p => p.Catches + p.RunOuts + p.Stumpings + p.MatchStats.DroppedCatches > 0)
            .OrderByDescending(p => p.FieldingPoints)
            .ThenByDescending(p => p.Catches + p.RunOuts + p.Stumpings)
            .ThenBy(p => p.MatchStats.DroppedCatches)
            .ThenBy(p => p.Order)
            .FirstOrDefault();

        card.PlayerOfTheSessionPlayerId = overall?.PlayerId;
        card.BestBatsmanPlayerId = bestBatter?.PlayerId;
        card.BestBowlerPlayerId = bestBowler?.PlayerId;
        card.BestFielderPlayerId = bestFielder?.PlayerId;
        card.Points = MatchPointsCalculator.Calculate(deliveries, players, overall?.PlayerId);
    }

    public async Task CompleteAsync(Guid sessionId, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var session = await db.NetSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Session not found.");

        var deliveries = await db.NetDeliveries
            .Where(d => d.SessionId == sessionId)
            .OrderBy(d => d.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        session.Status = NetSessionStatus.Completed;
        session.UpdatedAtUtc = DateTime.Now;
        session.Version++;

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        EnqueueCompletedSnapshot(db, session, deliveries);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        _syncTrigger.RequestSyncNow();
    }

    public async Task DeleteOngoingAsync(Guid sessionId, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var session = await db.NetSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Session not found.");

        if (session.Status == NetSessionStatus.Completed)
            throw new InvalidOperationException("Completed sessions cannot be deleted.");

        session.IsDeleted = true;
        session.UpdatedAtUtc = DateTime.Now;
        session.Version++;
        await _live.EnqueueNetSessionDeleteAsync(db, sessionId, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _live.RequestFlush();
    }

    private async Task<NetRosterPayload> BuildRosterAsync(IReadOnlyList<Guid> playerIds, CancellationToken ct)
    {
        if (playerIds is null || playerIds.Count == 0)
            throw new InvalidOperationException("Select at least two players for a nets session.");
        var distinct = playerIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinct.Count < 2)
            throw new InvalidOperationException("Select at least two players for a nets session.");

        var all = await _players.GetActiveAsync(ct).ConfigureAwait(false);
        var byId = all.ToDictionary(p => p.Id);
        var players = new List<SquadPlayer>();
        foreach (var id in distinct)
        {
            if (!byId.TryGetValue(id, out var p))
                throw new InvalidOperationException("One of the selected players is missing or inactive.");
            players.Add(new SquadPlayer { Id = p.Id, FullName = p.FullName });
        }

        return new NetRosterPayload { Players = players };
    }

    private static string RequireName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0)
            throw new InvalidOperationException("Session name is required.");
        return n.Length > 160 ? n[..160] : n;
    }

    private static async Task EnsureSoleActiveAsync(JplDbContext db, Guid? exceptId, CancellationToken ct)
    {
        var other = await db.NetSessions.AsNoTracking()
            .Where(s => s.Status == NetSessionStatus.Setup || s.Status == NetSessionStatus.InProgress)
            .Where(s => exceptId == null || s.Id != exceptId)
            .OrderByDescending(s => s.Status == NetSessionStatus.InProgress)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (other is not null)
            throw new InvalidOperationException(
                $"Only one nets session can run at a time in this league on this device. Finish or complete \"{other}\" first.");
    }

    private static NetSessionListItem FromApi(ApiNetSessionSummaryDto row, bool needsSync, bool allowMutate)
        => new(
            row.Id,
            row.Name,
            (NetSessionStatus)row.Status,
            row.SessionDateUtc,
            row.PlayerCount,
            row.Runs,
            row.Wickets,
            row.Overs,
            row.BallsInOver)
        {
            AllowMutate = allowMutate,
            NeedsSync = needsSync,
            IsLocal = false
        };

    private static NetSessionListItem ToListItem(
        NetSession s, IReadOnlyList<NetDelivery> balls, bool needsSync, bool allowMutate)
    {
        var roster = NetRosterPayload.TryParse(s.PlayerRosterJson);
        var legal = balls.Count(d => d.IsLegalDelivery);
        return new NetSessionListItem(
            s.Id,
            s.Name,
            s.Status,
            s.SessionDateUtc,
            roster?.Players.Count ?? 0,
            balls.Sum(d => d.RunsOffBat + d.ExtraRuns),
            balls.Count(d => d.IsWicket),
            legal / 6,
            legal % 6)
        {
            AllowMutate = allowMutate,
            NeedsSync = needsSync && s.Status == NetSessionStatus.Completed,
            IsLocal = true
        };
    }

    private static List<BattingCardRow> BuildNetsBattingRows(
        IReadOnlyList<NetDelivery> deliveries,
        IReadOnlyList<Guid> battingOrder,
        Guid? strikerId,
        Func<Guid, string> nameOf)
    {
        // One row per batting spell (out → new spell), matching the detailed BATTING card.
        var rows = new List<BattingCardRow>();
        foreach (var pid in battingOrder)
        {
            var spells = SplitBatterSpells(deliveries, pid);
            if (spells.Count == 0)
                continue;

            for (var i = 0; i < spells.Count; i++)
            {
                var spell = spells[i];
                var endedOut = SpellEndedOut(spell, pid);
                var isLastSpell = i == spells.Count - 1;
                var returnedAfterOut = isLastSpell && strikerId == pid && endedOut;
                rows.Add(new BattingCardRow
                {
                    PlayerId = pid,
                    Name = nameOf(pid),
                    Runs = spell.Sum(d => d.RunsOffBat),
                    Balls = spell.Count(d => d.IsLegalDelivery),
                    Fours = spell.Count(d => d.RunsOffBat == 4),
                    Sixes = spell.Count(d => d.RunsOffBat == 6),
                    IsOut = endedOut && !returnedAfterOut,
                    Outs = endedOut ? 1 : 0,
                    IsOnStrike = isLastSpell && strikerId == pid && (!endedOut || returnedAfterOut),
                    Dismissal = endedOut ? FormatDismissal(spell[^1], nameOf) : string.Empty
                });
            }
        }

        return rows;
    }

    private static List<BattingCardRow> BuildNetsCombinedBattingRows(
        IReadOnlyList<NetDelivery> deliveries,
        IReadOnlyList<Guid> battingOrder,
        Guid? strikerId,
        Func<Guid, string> nameOf)
    {
        // One row per player — session totals for the compact COMBINED BATTING card.
        var rows = new List<BattingCardRow>();
        foreach (var pid in battingOrder)
        {
            var faced = deliveries.Where(d => d.BatterId == pid).ToList();
            if (faced.Count == 0)
                continue;

            var spells = SplitBatterSpells(deliveries, pid);
            var lastClosedOut = spells.Count > 0 && SpellEndedOut(spells[^1], pid);
            var returnedAfterOut = strikerId == pid && lastClosedOut;
            var outs = deliveries.Count(d => d.IsWicket && d.DismissedPlayerId == pid);

            rows.Add(new BattingCardRow
            {
                PlayerId = pid,
                Name = nameOf(pid),
                Runs = faced.Sum(d => d.RunsOffBat),
                Balls = faced.Count(d => d.IsLegalDelivery),
                Fours = faced.Count(d => d.RunsOffBat == 4),
                Sixes = faced.Count(d => d.RunsOffBat == 6),
                IsOut = lastClosedOut && !returnedAfterOut,
                Outs = outs,
                IsOnStrike = strikerId == pid && (!lastClosedOut || returnedAfterOut),
                Dismissal = outs == 0 ? string.Empty : $"{outs} out{(outs == 1 ? string.Empty : "s")}"
            });
        }

        return rows;
    }

    private static List<List<NetDelivery>> SplitBatterSpells(IReadOnlyList<NetDelivery> deliveries, Guid pid)
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

    private static bool SpellEndedOut(IReadOnlyList<NetDelivery> spell, Guid pid)
        => spell.Count > 0
           && spell[^1].IsWicket
           && spell[^1].DismissedPlayerId == pid;

    private void EnqueueCompletedSnapshot(JplDbContext db, NetSession session, IReadOnlyList<NetDelivery> deliveries)
    {
        _outbox.Enqueue(db, nameof(NetSession), session.Id, "Upsert", ToSessionPayload(session));
        foreach (var d in deliveries.OrderBy(x => x.Sequence))
            _outbox.Enqueue(db, nameof(NetDelivery), d.Id, "Upsert", ToDeliveryPayload(d));
    }

    private static object ToSessionPayload(NetSession s) => new
    {
        s.Id,
        s.Name,
        s.SessionDateUtc,
        s.Status,
        s.PlayerRosterJson,
        s.Version,
        s.UpdatedAtUtc,
        s.IsDeleted,
        s.CreatedAtUtc
    };

    private static object ToDeliveryPayload(NetDelivery d) => new
    {
        d.Id,
        d.SessionId,
        d.Sequence,
        d.OverNumber,
        d.BallInOver,
        d.BatterId,
        d.NonStrikerId,
        d.BowlerId,
        d.RunsOffBat,
        d.ExtraRuns,
        d.ExtraType,
        d.IsWicket,
        d.WicketType,
        d.DismissedPlayerId,
        d.FielderId,
        d.IsDroppedCatch,
        d.IsLegalDelivery,
        d.CreatedAtUtc,
        d.Version
    };

    private static int CountMaidens(List<NetDelivery> bowled)
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

    private static string FormatDismissal(NetDelivery d, Func<Guid, string> nameOf)
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
