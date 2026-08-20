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
    public bool AllowMutate { get; init; } = true;
    public bool CanDelete => IsActive && AllowMutate;
    public bool NeedsSync { get; init; }
    public string ActionLabel => IsActive && AllowMutate ? "Continue" : "Scorecard";
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
    string ScoreText);

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
    public IReadOnlyList<BowlingCardRow> Bowling { get; set; } = Array.Empty<BowlingCardRow>();
    public string TotalText => $"{TotalRuns}/{Wickets}";
    public string ExtrasText => $"Extras {Extras}";
}

public interface INetSessionService
{
    Task<IReadOnlyList<NetSessionListItem>> GetAllAsync(CancellationToken ct = default);
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
    private readonly IPlayerService _players;
    private readonly IApiClient _api;

    public NetSessionService(
        IDbContextFactory<JplDbContext> dbFactory,
        IOutboxWriter outbox,
        ISyncTrigger syncTrigger,
        ISyncService sync,
        IAuthService auth,
        IPlayerService players,
        IApiClient api)
    {
        _dbFactory = dbFactory;
        _outbox = outbox;
        _syncTrigger = syncTrigger;
        _sync = sync;
        _auth = auth;
        _players = players;
        _api = api;
    }

    public async Task<IReadOnlyList<NetSessionListItem>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var sessions = await db.NetSessions.AsNoTracking()
            .Where(s => s.Status == NetSessionStatus.Setup
                        || s.Status == NetSessionStatus.InProgress
                        || s.Status == NetSessionStatus.Completed)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var pending = await _sync.GetPendingNetSessionIdsAsync(ct).ConfigureAwait(false);
        var allow = _auth.CanWrite;
        var byId = new Dictionary<Guid, NetSessionListItem>();
        foreach (var s in sessions)
        {
            var balls = await db.NetDeliveries.AsNoTracking()
                .Where(d => d.SessionId == s.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            byId[s.Id] = ToListItem(s, balls, pending.Contains(s.Id), allow);
        }

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

        return byId.Values
            .OrderByDescending(s => s.IsActive)
            .ThenByDescending(s => s.SessionDateUtc)
            .ToList();
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
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
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
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<NetSession?> GetAsync(Guid sessionId, CancellationToken ct = default)
    {
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

        await EnsureSoleActiveAsync(db, sessionId, ct).ConfigureAwait(false);

        var deliveries = await db.NetDeliveries
            .Where(d => d.SessionId == sessionId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var legalSoFar = deliveries.Count(d => d.IsLegalDelivery);
        var sequence = deliveries.Count == 0 ? 1 : deliveries.Max(d => d.Sequence) + 1;
        var overNumber = legalSoFar / 6;
        var ballInOver = legalSoFar % 6;
        var isWideOrNb = input.ExtraType is ExtraType.Wide or ExtraType.NoBall;
        var isLegal = !isWideOrNb;

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
            FielderId = input.IsWicket ? input.FielderId : null,
            IsLegalDelivery = isLegal
        };

        session.Status = NetSessionStatus.InProgress;
        session.UpdatedAtUtc = DateTime.Now;
        session.Version++;

        db.NetDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
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

        db.NetDeliveries.Remove(last);
        session.UpdatedAtUtc = DateTime.Now;
        session.Version++;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return undone;
    }

    public async Task<NetLiveSnapshot?> GetLiveSnapshotAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var session = await db.NetSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false);
        if (session is null)
            return null;

        var deliveries = await db.NetDeliveries.AsNoTracking()
            .Where(d => d.SessionId == sessionId)
            .OrderBy(d => d.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var runs = deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns);
        var wickets = deliveries.Count(d => d.IsWicket);
        var legal = deliveries.Count(d => d.IsLegalDelivery);
        var overs = legal / 6;
        var balls = legal % 6;
        var last = deliveries.LastOrDefault();
        return new NetLiveSnapshot(
            sessionId,
            runs,
            wickets,
            overs,
            balls,
            last?.BatterId,
            last?.BowlerId,
            $"{runs}/{wickets} ({overs}.{balls})");
    }

    public async Task<NetScoringContext?> GetScoringContextAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var session = await db.NetSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            .ConfigureAwait(false);
        if (session is null)
            return null;

        var roster = NetRosterPayload.TryParse(session.PlayerRosterJson);
        var players = roster?.Players ?? new List<SquadPlayer>();
        return new NetScoringContext(session.Id, session.Name, players);
    }

    public async Task<NetSessionScorecard?> GetScorecardAsync(
        Guid sessionId, Guid? strikerId = null, CancellationToken ct = default)
    {
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
            try
            {
                var remote = await _api.GetNetSessionScorecardAsync(sessionId, ct).ConfigureAwait(false);
                if (remote is not null)
                    return remote;
            }
            catch
            {
                /* offline */
            }
            if (session is null)
                return null;
        }

        var roster = NetRosterPayload.TryParse(session.PlayerRosterJson);
        var names = (roster?.Players ?? new List<SquadPlayer>())
            .ToDictionary(p => p.Id, p => p.FullName);

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
                Maidens = CountMaidens(bowled)
            });
        }

        var legal = deliveries.Count(d => d.IsLegalDelivery);
        return new NetSessionScorecard
        {
            SessionId = session.Id,
            Name = session.Name,
            MetaLine = $"{session.SessionDateUtc:dd MMM yyyy} · unlimited overs",
            TotalRuns = deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns),
            Wickets = deliveries.Count(d => d.IsWicket),
            Extras = deliveries.Sum(d => d.ExtraRuns),
            OversText = $"{legal / 6}.{legal % 6}",
            Batting = batting,
            Bowling = bowling
        };
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
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
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
            NetSessionStatus.Completed,
            row.SessionDateUtc,
            row.PlayerCount,
            row.Runs,
            row.Wickets,
            row.Overs,
            row.BallsInOver)
        {
            AllowMutate = allowMutate,
            NeedsSync = needsSync
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
            NeedsSync = needsSync && s.Status == NetSessionStatus.Completed
        };
    }

    private static List<BattingCardRow> BuildNetsBattingRows(
        IReadOnlyList<NetDelivery> deliveries,
        IReadOnlyList<Guid> battingOrder,
        Guid? strikerId,
        Func<Guid, string> nameOf)
    {
        var rows = new List<BattingCardRow>();
        foreach (var pid in battingOrder)
        {
            var spells = SplitBatterSpells(deliveries, pid);
            var lastClosedOut = spells.Count > 0 && SpellEndedOut(spells[^1], pid);
            if (spells.Count == 0 || (strikerId == pid && lastClosedOut))
                spells.Add(new List<NetDelivery>());

            for (var i = 0; i < spells.Count; i++)
            {
                var faced = spells[i];
                var isOut = SpellEndedOut(faced, pid);
                var outBall = isOut ? faced[^1] : null;
                var currentlyIn = strikerId == pid && i == spells.Count - 1 && !isOut;
                rows.Add(new BattingCardRow
                {
                    PlayerId = pid,
                    Name = nameOf(pid),
                    Runs = faced.Sum(d => d.RunsOffBat),
                    Balls = faced.Count(d => d.IsLegalDelivery),
                    Fours = faced.Count(d => d.RunsOffBat == 4),
                    Sixes = faced.Count(d => d.RunsOffBat == 6),
                    IsOut = isOut,
                    Outs = isOut ? 1 : 0,
                    IsOnStrike = currentlyIn,
                    Dismissal = outBall is null ? string.Empty : FormatDismissal(outBall, nameOf)
                });
            }
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
