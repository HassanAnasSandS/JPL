using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public sealed record MatchListItem(
    Guid Id,
    string Title,
    Guid TeamAId,
    Guid TeamBId,
    string TeamAName,
    string TeamBName,
    MatchStatus Status,
    MatchFormat Format,
    DateTime MatchDateUtc,
    int OversLimit,
    string? Venue,
    int TeamARuns,
    int TeamAWickets,
    int TeamBRuns,
    int TeamBWickets,
    string ResultText,
    Guid? WinnerTeamId,
    string? TeamACaptainName = null,
    string? TeamBCaptainName = null,
    Guid? PlayerOfTheMatchPlayerId = null,
    string? PlayerOfTheMatchName = null)
{
    public bool HasPlayerOfTheMatch => PlayerOfTheMatchPlayerId is not null
                                       && !string.IsNullOrWhiteSpace(PlayerOfTheMatchName);
    public string PlayerOfTheMatchLine => HasPlayerOfTheMatch
        ? $"Player of the Match: {PlayerOfTheMatchName}"
        : string.Empty;
    public string FormatText => Format == MatchFormat.DoubleInnings ? "Double innings" : "Single inning";
    public OversMode OversMode { get; init; } = OversMode.PerInnings;
    public string FormatOversText => $"{FormatText} · {OversRules.ShortLabel(OversMode, OversLimit)}";
    public string TeamAScore => $"{TeamARuns}/{TeamAWickets}";
    public string TeamBScore => $"{TeamBRuns}/{TeamBWickets}";
    public string DateLabel => MatchDateUtc.ToString("dd MMM yyyy");
    public string DateKey => MatchDateUtc.ToString("yyyy-MM-dd");
    public string CaptainsText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(TeamACaptainName))
                parts.Add(WithCaptainMark(TeamACaptainName!));
            if (!string.IsNullOrWhiteSpace(TeamBCaptainName))
                parts.Add(WithCaptainMark(TeamBCaptainName!));
            return parts.Count == 0 ? string.Empty : string.Join(" / ", parts);
        }
    }

    private static string WithCaptainMark(string name)
    {
        var t = name.Trim();
        return t.EndsWith("(C)", StringComparison.OrdinalIgnoreCase) ? t : $"{t} (C)";
    }
    public string MetaLine
    {
        get
        {
            var parts = new List<string> { FormatOversText, DateLabel };
            if (!string.IsNullOrWhiteSpace(Venue))
                parts.Add(Venue!);
            if (!string.IsNullOrWhiteSpace(CaptainsText))
                parts.Add($"C: {CaptainsText}");
            return string.Join(" · ", parts);
        }
    }
    public bool IsActive => Status is MatchStatus.Setup or MatchStatus.InProgress;
    public bool HasLiveStream { get; init; }
    /// <summary>False = score on this phone only; sync after the match is completed.</summary>
    public bool IsLiveTelecast { get; init; } = true;
    /// <summary>True when this device has the SQLite scoring row (only then can balls be entered).</summary>
    public bool IsLocal { get; init; } = true;
    /// <summary>True when the signed-in admin is the one who started this match on this device.</summary>
    public bool IsScoringOwner { get; init; }
    /// <summary>Admin/SuperAdmin only — User role is read-only.</summary>
    public bool AllowMutate { get; init; } = true;
    public bool CanScore => IsActive && AllowMutate && IsLocal && IsScoringOwner;
    public bool CanDelete => CanScore;
    public string ActionLabel => CanScore
        ? "Continue"
        : Status == MatchStatus.InProgress ? "Main Score" : "Scorecard";
    public bool TeamAWon => WinnerTeamId == TeamAId;
    public bool TeamBWon => WinnerTeamId == TeamBId;
    public bool IsTie => Status == MatchStatus.Completed
                         && WinnerTeamId is null
                         && ResultText.Contains("tied", StringComparison.OrdinalIgnoreCase);
    /// <summary>Completed standalone match with pending outbox — show Sync on Matches list.</summary>
    public bool NeedsSync { get; init; }
    public string StatusText => Status switch
    {
        MatchStatus.Setup => "Setup",
        MatchStatus.InProgress => IsLiveTelecast ? "LIVE" : "LOCAL",
        MatchStatus.Completed => "Completed",
        MatchStatus.Abandoned => "Abandoned",
        _ => Status.ToString()
    };
}

public sealed record MatchDraft(
    string Title,
    Guid TeamAId,
    Guid TeamBId,
    Guid? TeamACaptainId,
    Guid? TeamBCaptainId,
    int OversLimit,
    MatchFormat Format,
    string? Venue,
    Guid? TossWinnerTeamId,
    bool TossWinnerBatsFirst,
    bool LastBatsmanCanBatAsSingleWicket = false,
    int BowlerMaxOvers = 0,
    Guid? TournamentId = null,
    Guid? TournamentFixtureId = null,
    Guid? SeriesId = null,
    Guid? SeriesFixtureId = null,
    OversMode OversMode = OversMode.MatchTotal,
    string? LiveStreamUrl = null,
    bool IsLiveTelecast = false,
    Guid? FormatId = null);

public sealed record DeliveryInput(
    Guid BatterId,
    Guid NonStrikerId,
    Guid BowlerId,
    int RunsOffBat,
    int ExtraRuns,
    ExtraType ExtraType,
    bool IsWicket,
    WicketType WicketType,
    Guid? DismissedPlayerId,
    Guid? FielderId = null,
    byte ShotZone = 0);

/// <summary>Crease/bowler state stored on the deleted ball — restore UI after undo.</summary>
public sealed record UndoneDelivery(
    Guid BatterId,
    Guid NonStrikerId,
    Guid BowlerId,
    bool IsWicket,
    Guid? DismissedPlayerId);

public sealed record LiveScoreSnapshot(
    Guid MatchId,
    Guid InningsId,
    int InningsNumber,
    int Runs,
    int Wickets,
    int Overs,
    int BallsInCurrentOver,
    int OversLimit,
    OversMode OversMode,
    MatchFormat Format,
    bool InningsOversComplete,
    Guid? StrikerId,
    Guid? NonStrikerId,
    Guid? BowlerId,
    string ScoreText,
    string SituationText,
    int? Target,
    int? RunsRequired,
    int BallsRemaining,
    double CurrentRunRate,
    double? RequiredRunRate,
    int ProjectedScore,
    bool IsChase);

public sealed record ScoringContext(
    Guid MatchId,
    Guid InningsId,
    int InningsNumber,
    Guid BattingTeamId,
    Guid BowlingTeamId,
    string BattingTeamName,
    string BowlingTeamName,
    MatchFormat Format,
    int OversLimit,
    OversMode OversMode,
    int MaxInnings,
    string TeamAName,
    string TeamBName,
    string FormatHint,
    bool LastBatsmanCanBatAsSingleWicket,
    int BowlerMaxOvers,
    IReadOnlyList<SquadPlayer> Batters,
    IReadOnlyList<SquadPlayer> Bowlers,
    IReadOnlyList<SquadPlayer> Fielders,
    bool IsTournamentMatch = false,
    string? LiveStreamUrl = null,
    bool IsLiveTelecast = true);

public sealed record EndInningsResult(
    bool MatchCompleted,
    bool CanStartNext,
    bool FollowOnAvailable,
    int LeadBy,
    Guid? NextBattingTeamId,
    Guid? NextBowlingTeamId,
    string Message,
    Guid? PlayerOfTheMatchPlayerId = null,
    string? PlayerOfTheMatchName = null);

public sealed class BattingCardRow
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Runs { get; set; }
    public int Balls { get; set; }
    public int Fours { get; set; }
    public int Sixes { get; set; }
    public bool IsOut { get; set; }
    /// <summary>Times this batter was dismissed (nets can be more than once).</summary>
    public int Outs { get; set; }
    public bool IsOnStrike { get; set; }
    public bool IsCaptain { get; set; }
    public string Dismissal { get; set; } = string.Empty;
    public double StrikeRate => Balls == 0 ? 0 : Math.Round(Runs * 100.0 / Balls, 1);
    public string DisplayName
    {
        get
        {
            var n = IsCaptain ? $"{Name} (C)" : Name;
            return IsOnStrike ? $"{n}*" : n;
        }
    }
    public string StatusText => IsOut
        ? (string.IsNullOrWhiteSpace(Dismissal) ? "out" : Dismissal)
        : "not out";
    public string StrikeRateText => StrikeRate.ToString("0.0");
    public string LineText =>
        $"{DisplayName}  {Runs} ({Balls})" +
        $"  {StatusText}" +
        $"  4s:{Fours} 6s:{Sixes}  SR {StrikeRateText}";
}

public sealed class BowlingCardRow
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BallsBowled { get; set; }
    public int RunsConceded { get; set; }
    public int Wickets { get; set; }
    public int Maidens { get; set; }
    public bool IsCaptain { get; set; }
    public string DisplayName => IsCaptain ? $"{Name} (C)" : Name;
    public string OversText
    {
        get
        {
            var o = BallsBowled / 6;
            var b = BallsBowled % 6;
            return $"{o}.{b}";
        }
    }
    public double Economy => BallsBowled == 0 ? 0 : Math.Round(RunsConceded * 6.0 / BallsBowled, 2);
    public string EconomyText => Economy.ToString("0.00");
    public string FiguresText => $"{OversText}-{Maidens}-{RunsConceded}-{Wickets}";
    public string LineText =>
        $"{DisplayName}  {FiguresText}  Econ {EconomyText}";
}

public sealed record InningsScorecard(
    IReadOnlyList<BattingCardRow> Batting,
    IReadOnlyList<BowlingCardRow> Bowling,
    int Extras,
    int TotalRuns,
    int Wickets,
    string DidNotBatText = "",
    string DidNotBowlText = "");

public sealed class InningsScorecardSection
{
    public int InningsNumber { get; set; }
    public Guid BattingTeamId { get; set; }
    public Guid BowlingTeamId { get; set; }
    public string BattingTeamName { get; set; } = string.Empty;
    public string BowlingTeamName { get; set; } = string.Empty;
    public bool IsFollowOn { get; set; }
    public int Extras { get; set; }
    public int TotalRuns { get; set; }
    public int Wickets { get; set; }
    public IReadOnlyList<BattingCardRow> Batting { get; set; } = Array.Empty<BattingCardRow>();
    public IReadOnlyList<BowlingCardRow> Bowling { get; set; } = Array.Empty<BowlingCardRow>();
    public string DidNotBatText { get; set; } = string.Empty;
    public string DidNotBowlText { get; set; } = string.Empty;
    public string OversFaced { get; set; } = string.Empty;
    public string FallOfWicketsText { get; set; } = string.Empty;
    private string? extrasText;

    public string HeaderText =>
        IsFollowOn
            ? $"Innings {InningsNumber} — {BattingTeamName} (follow-on)"
            : $"Innings {InningsNumber} — {BattingTeamName}";
    public string SubHeaderText => string.IsNullOrWhiteSpace(OversFaced)
        ? $"vs {BowlingTeamName}"
        : $"vs {BowlingTeamName} · {OversFaced} ov";
    public string TotalText => $"{TotalRuns}/{Wickets}";
    public string ExtrasText
    {
        get => string.IsNullOrWhiteSpace(extrasText) ? $"Extras {Extras}" : extrasText;
        set => extrasText = value;
    }
    public string TotalLineText => $"TOTAL  {TotalRuns}/{Wickets}";
    public bool HasDidNotBat => !string.IsNullOrWhiteSpace(DidNotBatText);
    public bool HasDidNotBowl => !string.IsNullOrWhiteSpace(DidNotBowlText);
    public bool HasFallOfWickets => !string.IsNullOrWhiteSpace(FallOfWicketsText);
}

public sealed class MatchFullScorecard
{
    public Guid MatchId { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public string TeamAName { get; set; } = string.Empty;
    public string TeamBName { get; set; } = string.Empty;
    public int TeamARuns { get; set; }
    public int TeamAWickets { get; set; }
    public int TeamBRuns { get; set; }
    public int TeamBWickets { get; set; }
    public string ResultText { get; set; } = string.Empty;
    public string MetaLine { get; set; } = string.Empty;
    public string TossText { get; set; } = string.Empty;
    public Guid? WinnerTeamId { get; set; }
    public Guid? PlayerOfTheMatchPlayerId { get; set; }
    public string? PlayerOfTheMatchName { get; set; }
    public string? LiveStreamUrl { get; set; }
    public string CurrentBatter { get; set; } = "—";
    public string CurrentBowler { get; set; } = "—";
    public string CurrentOver { get; set; } = "0.0";
    public string TotalOvers { get; set; } = "—";
    public string CurrentNonStriker { get; set; } = "—";
    public string BugTeamScore { get; set; } = "—";
    public string CurrentRunRate { get; set; } = "-";
    public List<OverBallMark> ThisOver { get; set; } = new();
    public byte Status { get; set; }
    public bool IsChase { get; set; }
    public string ChaseNeedText { get; set; } = string.Empty;
    public string RequiredRunRate { get; set; } = "-";
    public string? TeamACaptainName { get; set; }
    public string? TeamBCaptainName { get; set; }
    public IReadOnlyList<InningsScorecardSection> Innings { get; set; } = Array.Empty<InningsScorecardSection>();

    public string TeamAScore => $"{TeamARuns}/{TeamAWickets}";
    public string TeamBScore => $"{TeamBRuns}/{TeamBWickets}";
    public bool TeamAWon => WinnerTeamId == TeamAId;
    public bool TeamBWon => WinnerTeamId == TeamBId;
    public bool HasPlayerOfTheMatch => PlayerOfTheMatchPlayerId is not null
                                       && !string.IsNullOrWhiteSpace(PlayerOfTheMatchName);
    public bool IsFinished => Status is (byte)MatchStatus.Completed or (byte)MatchStatus.Abandoned
                              || TeamAWon
                              || TeamBWon
                              || HasPlayerOfTheMatch
                              || ResultLooksFinished(ResultText);
    public string CaptainsLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(TeamACaptainName))
                parts.Add(WithCaptainMark(TeamACaptainName!));
            if (!string.IsNullOrWhiteSpace(TeamBCaptainName))
                parts.Add(WithCaptainMark(TeamBCaptainName!));
            return parts.Count == 0 ? string.Empty : $"Captains: {string.Join(" / ", parts)}";
        }
    }
    public string PlayerOfTheMatchLine => HasPlayerOfTheMatch
        ? $"Player of the Match: {PlayerOfTheMatchName}"
        : string.Empty;

    private static string WithCaptainMark(string name)
    {
        var t = name.Trim();
        return t.EndsWith("(C)", StringComparison.OrdinalIgnoreCase) ? t : $"{t} (C)";
    }

    private static bool ResultLooksFinished(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return false;
        if (result.Equals("LIVE", StringComparison.OrdinalIgnoreCase)
            || result.Equals("Upcoming", StringComparison.OrdinalIgnoreCase)
            || result.Equals("In progress", StringComparison.OrdinalIgnoreCase))
            return false;
        return result.Contains("won", StringComparison.OrdinalIgnoreCase)
               || result.Contains("tied", StringComparison.OrdinalIgnoreCase)
               || result.Contains("abandon", StringComparison.OrdinalIgnoreCase)
               || result.Contains("completed", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class OverBallMark
{
    public string Text { get; set; } = "•";
    public string Kind { get; set; } = "dot";
}

public interface IMatchService
{
    Task<IReadOnlyList<MatchListItem>> GetAllAsync(CancellationToken ct = default);
    /// <summary>Local live/setup (+ unsynced completed) rows to pin at the top of the list.</summary>
    Task<IReadOnlyList<MatchListItem>> GetPinnedListAsync(CancellationToken ct = default);
    /// <summary>Completed matches from the API, 5 at a time.</summary>
    Task<ApiPagedResult<MatchListItem>> GetCompletedPageAsync(
        int skip, string? q, string? captain, string? date, CancellationToken ct = default);
    /// <summary>The single Setup/InProgress match in the current league on this device, if any.</summary>
    Task<MatchListItem?> GetActiveMatchAsync(CancellationToken ct = default);
    /// <summary>True when this phone has the SQLite scoring row AND the current admin started the match.</summary>
    Task<bool> IsScoredOnThisDeviceAsync(Guid matchId, CancellationToken ct = default);
    Task<Match> CreateAsync(MatchDraft draft, CancellationToken ct = default);
    Task<Innings> StartInningsAsync(Guid matchId, Guid battingTeamId, Guid bowlingTeamId, bool isFollowOn = false, CancellationToken ct = default);
    Task<EndInningsResult> EndInningsAsync(Guid matchId, CancellationToken ct = default);
    Task<Delivery> RecordDeliveryAsync(Guid matchId, DeliveryInput input, CancellationToken ct = default);
    Task<UndoneDelivery> UndoLastDeliveryAsync(Guid matchId, CancellationToken ct = default);
    Task<LiveScoreSnapshot?> GetLiveSnapshotAsync(Guid matchId, CancellationToken ct = default);
    Task<ScoringContext?> GetScoringContextAsync(Guid matchId, CancellationToken ct = default);
    Task<InningsScorecard?> GetLiveScorecardAsync(Guid matchId, Guid? strikerId = null, Guid? nonStrikerId = null, CancellationToken ct = default);
    /// <summary>Full batting/bowling scorecard for every innings (completed or local ongoing).</summary>
    Task<MatchFullScorecard?> GetMatchScorecardAsync(Guid matchId, CancellationToken ct = default);
    Task<MatchAnalyticsReport?> GetMatchAnalyticsAsync(Guid matchId, CancellationToken ct = default);
    Task CompleteMatchAsync(Guid matchId, CancellationToken ct = default);
    /// <summary>Squad players for POTM picker (from match roster snapshot).</summary>
    Task<IReadOnlyList<SquadPlayer>> GetMatchSquadPlayersAsync(Guid matchId, CancellationToken ct = default);
    /// <summary>Save Player of the Match on a completed match and re-queue sync.</summary>
    Task SetPlayerOfTheMatchAsync(Guid matchId, Guid playerId, CancellationToken ct = default);
    /// <summary>Set or clear the live telecast URL while the match is in progress.</summary>
    Task SetLiveStreamUrlAsync(Guid matchId, string? url, CancellationToken ct = default);
    /// <summary>Soft-delete Setup/InProgress only. Completed matches cannot be deleted.</summary>
    Task DeleteOngoingAsync(Guid matchId, CancellationToken ct = default);
    /// <summary>Flush completed outbox then return: local ongoing + API completed.</summary>
    Task<IReadOnlyList<MatchListItem>> RefreshListAsync(CancellationToken ct = default);
}

public sealed class MatchService : IMatchService
{
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IOutboxWriter _outbox;
    private readonly ISyncTrigger _syncTrigger;
    private readonly ISyncService _sync;
    private readonly IApiClient _api;
    private readonly IMatchSquadCache _squadCache;
    private readonly IAuthService _auth;
    private readonly ILivePublisher _live;

    public MatchService(
        IDbContextFactory<JplDbContext> dbFactory,
        IOutboxWriter outbox,
        ISyncTrigger syncTrigger,
        ISyncService sync,
        IApiClient api,
        IMatchSquadCache squadCache,
        IAuthService auth,
        ILivePublisher live)
    {
        _dbFactory = dbFactory;
        _outbox = outbox;
        _syncTrigger = syncTrigger;
        _sync = sync;
        _api = api;
        _squadCache = squadCache;
        _auth = auth;
        _live = live;
    }

    private static int MaxInningsFor(MatchFormat format)
        => format == MatchFormat.DoubleInnings ? 4 : 2;

    public Task<IReadOnlyList<MatchListItem>> RefreshListAsync(CancellationToken ct = default)
        => GetAllAsync(ct);

    public async Task<IReadOnlyList<MatchListItem>> GetAllAsync(CancellationToken ct = default)
    {
        // Do NOT flush outbox on every list open — that freezes Matches UI.
        // Standalone match-complete / lifecycle / per-match Sync push.
        await EnsureLocalScoringOwnersAsync(ct).ConfigureAwait(false);

        var local = await LoadLocalListAsync(
                m => m.Status == MatchStatus.Setup || m.Status == MatchStatus.InProgress
                     || (m.TournamentId == null && m.SeriesId == null && m.Status == MatchStatus.Completed),
                ct)
            .ConfigureAwait(false);

        var pendingSync = await _sync.GetPendingStandaloneMatchIdsAsync(ct).ConfigureAwait(false);
        var byId = local.ToDictionary(
            m => m.Id,
            m => m with { NeedsSync = m.Status == MatchStatus.Completed && pendingSync.Contains(m.Id) });

        try
        {
            var remote = await _api.GetCompletedMatchesAsync(ct).ConfigureAwait(false);
            foreach (var m in remote.Select(FromApi))
            {
                // Never let API overwrite a local in-progress/setup match.
                if (byId.TryGetValue(m.Id, out var existing) && existing.IsActive)
                    continue;
                byId[m.Id] = m with
                {
                    NeedsSync = m.Status == MatchStatus.Completed && pendingSync.Contains(m.Id)
                };
            }

            try
            {
                var live = await _api.GetLiveMatchesAsync(ct).ConfigureAwait(false);
                foreach (var m in live.Select(FromApi))
                {
                    if (byId.TryGetValue(m.Id, out var existing) && existing.IsLocal)
                        continue;
                    byId[m.Id] = m with { IsLocal = false, AllowMutate = false };
                }
            }
            catch
            {
                /* older API without live index */
            }
        }
        catch
        {
            /* offline — local only */
        }

        return byId.Values
            .OrderByDescending(m => m.IsActive)
            .ThenByDescending(m => m.MatchDateUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<MatchListItem>> GetPinnedListAsync(CancellationToken ct = default)
    {
        await EnsureLocalScoringOwnersAsync(ct).ConfigureAwait(false);
        var pendingSync = await _sync.GetPendingStandaloneMatchIdsAsync(ct).ConfigureAwait(false);
        var pendingIds = pendingSync.ToList();
        var local = await LoadLocalListAsync(
                m => m.Status == MatchStatus.Setup || m.Status == MatchStatus.InProgress
                     || (m.TournamentId == null && m.SeriesId == null && m.Status == MatchStatus.Completed && pendingIds.Contains(m.Id)),
                ct)
            .ConfigureAwait(false);

        var byId = local.ToDictionary(
            m => m.Id,
            m => m with { NeedsSync = m.Status == MatchStatus.Completed && pendingSync.Contains(m.Id) });

        try
        {
            var live = await _api.GetLiveMatchesAsync(ct).ConfigureAwait(false);
            foreach (var m in live.Select(FromApi))
            {
                if (byId.TryGetValue(m.Id, out var existing) && existing.IsLocal)
                    continue;
                byId[m.Id] = m with { IsLocal = false, AllowMutate = false };
            }
        }
        catch
        {
            /* older API without live index */
        }

        return byId.Values
            .OrderByDescending(m => m.IsActive)
            .ThenByDescending(m => m.MatchDateUtc)
            .ToList();
    }

    public async Task<ApiPagedResult<MatchListItem>> GetCompletedPageAsync(
        int skip, string? q, string? captain, string? date, CancellationToken ct = default)
    {
        await EnsureLocalScoringOwnersAsync(ct).ConfigureAwait(false);
        var pendingSync = await _sync.GetPendingStandaloneMatchIdsAsync(ct).ConfigureAwait(false);
        var canWrite = _auth.CanWrite;
        try
        {
            var page = await _api.GetCompletedMatchesPageAsync(skip, q, captain, date, ct).ConfigureAwait(false);
            var items = (page.Items ?? []).Select(m =>
            {
                var mapped = FromApi(m);
                return mapped with
                {
                    NeedsSync = mapped.Status == MatchStatus.Completed && pendingSync.Contains(mapped.Id),
                    AllowMutate = canWrite
                };
            }).ToList();
            return page.WithItems(items);
        }
        catch
        {
            return new ApiPagedResult<MatchListItem>([], 0, skip, ListPaging.PageSize);
        }
    }

    public async Task<MatchListItem?> GetActiveMatchAsync(CancellationToken ct = default)
    {
        await EnsureLocalScoringOwnersAsync(ct).ConfigureAwait(false);
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        // Sort in memory so active match is the latest in-progress row.
        var match = (await db.Matches.AsNoTracking()
                .Where(m => m.Status == MatchStatus.Setup || m.Status == MatchStatus.InProgress)
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .OrderByDescending(m => m.Status == MatchStatus.InProgress)
            .ThenByDescending(m => m.MatchDateUtc)
            .FirstOrDefault();
        if (match is null)
            return null;

        var list = await LoadLocalListAsync(m => m.Id == match.Id, ct).ConfigureAwait(false);
        return list.FirstOrDefault();
    }

    public async Task<bool> IsScoredOnThisDeviceAsync(Guid matchId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches.AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.Id == matchId
                     && (m.Status == MatchStatus.Setup || m.Status == MatchStatus.InProgress),
                ct)
            .ConfigureAwait(false);
        return match is not null && ScoringOwnership.IsOwner(match, _auth);
    }

    private async Task EnsureLocalScoringOwnersAsync(CancellationToken ct)
    {
        if (_auth.Current?.UserId is null)
            return;
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var rows = await db.Matches
            .Where(m =>
                (m.Status == MatchStatus.Setup || m.Status == MatchStatus.InProgress)
                && (m.StartedByUserId == null || m.StartedOnDeviceId == null))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (rows.Count == 0)
            return;
        foreach (var match in rows)
            ScoringOwnership.Stamp(match, _auth);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private void EnsureScoringOwner(Match match) => ScoringOwnership.EnsureOwner(match, _auth);

    private static string OneLeagueMatchMessage(string title) =>
        $"Only one match can run at a time in this league on this device. Finish or complete \"{title}\" first.";

    private async Task<Match?> FindOtherActiveMatchAsync(
        JplDbContext db, Guid? exceptMatchId, CancellationToken ct)
    {
        // Query filter already scopes to the current league. Other leagues on this
        // device may have their own unsynced local match after logout/login.
        var rows = await db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.Setup || m.Status == MatchStatus.InProgress)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (exceptMatchId is Guid id)
            rows = rows.Where(m => m.Id != id).ToList();
        return rows
            .OrderByDescending(m => m.Status == MatchStatus.InProgress)
            .ThenByDescending(m => m.UpdatedAtUtc)
            .FirstOrDefault();
    }

    /// <param name="inProgressOnly">
    /// When true, leftover Setup drafts do not block scoring the current live match.
    /// </param>
    private async Task EnsureSoleLeagueMatchAsync(
        JplDbContext db, Guid? exceptMatchId, bool inProgressOnly, CancellationToken ct)
    {
        var other = await FindOtherActiveMatchAsync(db, exceptMatchId, ct).ConfigureAwait(false);
        if (other is null)
            return;
        if (inProgressOnly && other.Status != MatchStatus.InProgress)
            return;
        throw new InvalidOperationException(OneLeagueMatchMessage(other.Title));
    }

    private async Task<IReadOnlyList<MatchListItem>> LoadLocalListAsync(
        Expression<Func<Match, bool>> predicate, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var matches = await db.Matches.AsNoTracking()
                .Where(predicate)
                .OrderByDescending(m => m.MatchDateUtc)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        if (matches.Count == 0)
            return Array.Empty<MatchListItem>();

        var matchIds = matches.Select(m => m.Id).ToList();
        var innings = await db.Innings.AsNoTracking()
            .Where(i => matchIds.Contains(i.MatchId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var deliveries = await db.Deliveries.AsNoTracking()
            .Where(d => matchIds.Contains(d.MatchId))
            .Select(d => new { d.InningsId, d.RunsOffBat, d.ExtraRuns, d.IsWicket })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var delsByInnings = deliveries
            .GroupBy(d => d.InningsId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<(int RunsOffBat, int ExtraRuns, bool IsWicket)>)
                    g.Select(x => (x.RunsOffBat, x.ExtraRuns, x.IsWicket)).ToList());

        var innsByMatch = innings.GroupBy(i => i.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        var (playerNames, teamNames) = await LiveMasterNames.LoadAsync(_api, _dbFactory, ct)
            .ConfigureAwait(false);

        return matches.Select(m =>
        {
            var roster = MatchRosterPayload.TryParse(m.SquadRosterJson);
            var oldA = roster?.TeamAName ?? "Team A";
            var oldB = roster?.TeamBName ?? "Team B";
            roster?.ApplyLiveNames(playerNames, teamNames);
            var teamA = LiveMasterNames.Lookup(teamNames, m.TeamAId, roster?.TeamAName ?? "Team A");
            var teamB = LiveMasterNames.Lookup(teamNames, m.TeamBId, roster?.TeamBName ?? "Team B");
            var title = LiveMasterNames.RefreshVsTitle(m.Title, oldA, oldB, teamA, teamB);
            innsByMatch.TryGetValue(m.Id, out var matchInns);
            matchInns ??= new List<Innings>();

            var (aRuns, aWkts) = TeamTotalsLite(matchInns, m.TeamAId, delsByInnings);
            var (bRuns, bWkts) = TeamTotalsLite(matchInns, m.TeamBId, delsByInnings);
            var firstBat = matchInns.OrderBy(i => i.InningsNumber).FirstOrDefault()?.BattingTeamId;
            var (result, winnerId) = BuildResultText(
                m, teamA, teamB, aRuns, aWkts, bRuns, bWkts, firstBat,
                roster?.TeamAPlayers.Count ?? 0,
                roster?.TeamBPlayers.Count ?? 0);
            string? potmName = null;
            if (m.PlayerOfTheMatchPlayerId is Guid potmId && roster is not null)
            {
                potmName = roster.TeamAPlayers.Concat(roster.TeamBPlayers)
                    .FirstOrDefault(p => p.Id == potmId)?.FullName;
            }

            return new MatchListItem(
                m.Id,
                title,
                m.TeamAId,
                m.TeamBId,
                teamA,
                teamB,
                m.Status,
                m.Format,
                m.MatchDateUtc,
                m.OversLimit,
                m.Venue,
                aRuns,
                aWkts,
                bRuns,
                bWkts,
                result,
                winnerId,
                roster?.TeamACaptainName,
                roster?.TeamBCaptainName,
                m.PlayerOfTheMatchPlayerId,
                potmName) { OversMode = m.OversMode, IsLiveTelecast = m.IsLiveTelecast, HasLiveStream = m.IsLiveTelecast && LiveStreamUrls.HasValue(m.LiveStreamUrl) && m.Status is MatchStatus.Setup or MatchStatus.InProgress, IsScoringOwner = ScoringOwnership.IsOwner(m, _auth) };
        }).ToList();
    }

    private static (int Runs, int Wickets) TeamTotalsLite(
        IReadOnlyList<Innings> inns,
        Guid teamId,
        IReadOnlyDictionary<Guid, IReadOnlyList<(int RunsOffBat, int ExtraRuns, bool IsWicket)>> delsByInnings)
    {
        var runs = 0;
        var wickets = 0;
        foreach (var inn in inns.Where(i => i.BattingTeamId == teamId))
        {
            if (!delsByInnings.TryGetValue(inn.Id, out var dels))
                continue;
            foreach (var d in dels)
            {
                runs += d.RunsOffBat + d.ExtraRuns;
                if (d.IsWicket) wickets++;
            }
        }

        return (runs, wickets);
    }

    private static MatchListItem FromApi(ApiMatchSummaryDto m)
        => new(
            m.Id,
            m.Title,
            m.TeamAId,
            m.TeamBId,
            m.TeamAName,
            m.TeamBName,
            (MatchStatus)m.Status,
            (MatchFormat)m.Format,
            m.MatchDateUtc,
            m.OversLimit,
            m.Venue,
            m.TeamARuns,
            m.TeamAWickets,
            m.TeamBRuns,
            m.TeamBWickets,
            m.ResultText,
            m.WinnerTeamId,
            PlayerOfTheMatchPlayerId: m.PlayerOfTheMatchPlayerId,
            PlayerOfTheMatchName: m.PlayerOfTheMatchName) { OversMode = (OversMode)m.OversMode, IsLiveTelecast = true, HasLiveStream = LiveStreamUrls.HasValue(m.LiveStreamUrl) && (MatchStatus)m.Status is MatchStatus.Setup or MatchStatus.InProgress };

    private static (int Runs, int Wickets) TeamTotals(
        IReadOnlyList<Innings> inns,
        Guid teamId,
        IReadOnlyDictionary<Guid, IReadOnlyList<Delivery>> delsByInnings)
    {
        var runs = 0;
        var wickets = 0;
        foreach (var inn in inns.Where(i => i.BattingTeamId == teamId))
        {
            if (!delsByInnings.TryGetValue(inn.Id, out var dels))
                continue;
            foreach (var d in dels)
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
    private static (string Text, Guid? WinnerId) BuildResultText(
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
        if (match.Status == MatchStatus.Setup)
            return ("Not started", null);
        if (match.Status == MatchStatus.Abandoned)
            return ("Abandoned", null);
        if (match.Status == MatchStatus.InProgress)
            return ("In progress", null);
        if (match.Status != MatchStatus.Completed)
            return (match.Status.ToString(), null);

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

    public async Task<Match> CreateAsync(MatchDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        if (draft.TeamAId == draft.TeamBId)
            throw new InvalidOperationException("Select two different teams.");
        OversRules.Validate(draft.OversMode, draft.OversLimit, draft.BowlerMaxOvers);

        var title = RequireTitle(draft.Title);
        var venue = string.IsNullOrWhiteSpace(draft.Venue) ? null : draft.Venue.Trim();
        if (venue is null)
            throw new ArgumentException("Venue is required.");
        if (venue.Length > 120)
            throw new ArgumentException("Venue must be 120 characters or fewer.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await EnsureSoleLeagueMatchAsync(db, exceptMatchId: null, inProgressOnly: false, ct)
            .ConfigureAwait(false);

        var titleKey = title.ToLowerInvariant();
        var titleTaken = await db.Matches.AsNoTracking()
            .AnyAsync(m => m.Title.ToLower() == titleKey, ct)
            .ConfigureAwait(false);
        if (titleTaken)
            throw new InvalidOperationException($"Match title \"{title}\" already exists.");

        var teamA = await _api.GetTeamAsync(draft.TeamAId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Team A not found. Check the live Teams list.");
        var teamB = await _api.GetTeamAsync(draft.TeamBId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Team B not found. Check the live Teams list.");
        if (teamA.IsDeleted || !teamA.IsActive)
            throw new InvalidOperationException("Team A is not active.");
        if (teamB.IsDeleted || !teamB.IsActive)
            throw new InvalidOperationException("Team B is not active.");

        var playingA = TeamRoster.PlayingListOnly(teamA);
        var playingB = TeamRoster.PlayingListOnly(teamB);
        var allPlayers = await _api.GetPlayersAsync(ct).ConfigureAwait(false);
        var livePlayers = allPlayers.Where(p => !p.IsDeleted).ToList();
        if (playingA.Count == 0)
            throw new InvalidOperationException($"{teamA.Name} has no playing players. Set the playing list on the team before starting the match.");
        if (playingB.Count == 0)
            throw new InvalidOperationException($"{teamB.Name} has no playing players. Set the playing list on the team before starting the match.");

        var sharedIds = playingA.Intersect(playingB).ToList();
        if (sharedIds.Count > 0)
        {
            var names = livePlayers
                .Where(p => sharedIds.Contains(p.Id))
                .Select(p => p.FullName)
                .OrderBy(n => n)
                .ToList();
            var list = names.Count == 0
                ? string.Join(", ", sharedIds.Select(id => id.ToString("N")[..8]))
                : string.Join(", ", names);
            throw new InvalidOperationException(
                $"Both playing lists share player(s): {list}. Playing lists must be different before starting the match.");
        }

        var roster = MatchRosterPayload.From(teamA, teamB, livePlayers);

        var match = new Match
        {
            Title = title,
            TeamAId = draft.TeamAId,
            TeamBId = draft.TeamBId,
            TeamACaptainId = draft.TeamACaptainId ?? teamA.CaptainPlayerId,
            TeamBCaptainId = draft.TeamBCaptainId ?? teamB.CaptainPlayerId,
            OversLimit = OversRules.StoredOversLimit(draft.OversMode, draft.OversLimit),
            OversMode = draft.OversMode,
            Format = draft.Format,
            FormatId = draft.FormatId is Guid formatId && formatId != Guid.Empty
                ? formatId
                : FormatRules.MigratedId(
                    draft.Format,
                    draft.OversMode,
                    OversRules.StoredOversLimit(draft.OversMode, draft.OversLimit)),
            Venue = venue,
            TossWinnerTeamId = draft.TossWinnerTeamId,
            TossWinnerBatsFirst = draft.TossWinnerBatsFirst,
            LastBatsmanCanBatAsSingleWicket = draft.LastBatsmanCanBatAsSingleWicket,
            BowlerMaxOvers = Math.Max(0, draft.BowlerMaxOvers),
            TournamentId = draft.TournamentId,
            TournamentFixtureId = draft.TournamentFixtureId,
            SeriesId = draft.SeriesId,
            SeriesFixtureId = draft.SeriesFixtureId,
            Status = MatchStatus.Setup,
            SquadRosterJson = MatchRosterPayload.Serialize(roster),
            IsLiveTelecast = draft.IsLiveTelecast,
            LiveStreamUrl = draft.IsLiveTelecast ? LiveStreamUrls.Normalize(draft.LiveStreamUrl) : null,
            StartedByUserId = _auth.Current?.UserId,
            StartedOnDeviceId = DeviceInstall.Id
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        db.Matches.Add(match);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        _squadCache.Put(new MatchSquadSnapshot
        {
            MatchId = match.Id,
            TeamAId = roster.TeamAId,
            TeamBId = roster.TeamBId,
            TeamAName = roster.TeamAName,
            TeamBName = roster.TeamBName,
            TeamACaptainId = roster.TeamACaptainId,
            TeamBCaptainId = roster.TeamBCaptainId,
            TeamACaptainName = roster.TeamACaptainName,
            TeamBCaptainName = roster.TeamBCaptainName,
            TeamAPlayers = roster.TeamAPlayers,
            TeamBPlayers = roster.TeamBPlayers,
            BattingTeamId = match.TeamAId,
            BowlingTeamId = match.TeamBId
        });
        return match;
    }

    private static string RequireTitle(string? title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("Match title is required.");
        if (trimmed.Length > 160)
            throw new ArgumentException("Match title must be 160 characters or fewer.");
        return trimmed;
    }

    public async Task<Innings> StartInningsAsync(
        Guid matchId,
        Guid battingTeamId,
        Guid bowlingTeamId,
        bool isFollowOn = false,
        CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches.Include(m => m.Innings).FirstOrDefaultAsync(m => m.Id == matchId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Match not found.");
        ScoringOwnership.Stamp(match, _auth);
        EnsureScoringOwner(match);

        if (match.Status == MatchStatus.Completed)
            throw new InvalidOperationException("Match is already completed.");

        await EnsureSoleLeagueMatchAsync(
                db,
                matchId,
                inProgressOnly: match.Status == MatchStatus.InProgress,
                ct)
            .ConfigureAwait(false);

        if (battingTeamId == bowlingTeamId)
            throw new InvalidOperationException("Batting and bowling teams must differ.");

        if (match.Innings.Any(i => !i.IsCompleted))
            throw new InvalidOperationException("Finish the current innings before starting the next.");

        var max = MaxInningsFor(match.Format);
        var number = match.Innings.Count + 1;
        if (number > max)
            throw new InvalidOperationException(
                match.Format == MatchFormat.SingleInning
                    ? "Single inning match already has both sides' turns."
                    : "Double innings match already has all four turns.");

        if (isFollowOn && (match.Format != MatchFormat.DoubleInnings || number != 3))
            throw new InvalidOperationException("Follow-on can only start the 3rd innings in a double-innings match.");

        if (match.OversMode == OversMode.MatchTotal)
        {
            var matchLegal = await db.Deliveries.CountAsync(d => d.MatchId == matchId && d.IsLegalDelivery, ct)
                .ConfigureAwait(false);
            if (OversRules.MatchPoolExhausted(match.OversMode, match.OversLimit, matchLegal))
                throw new InvalidOperationException("Match overs are complete — no balls remaining.");
        }

        var innings = new Innings
        {
            MatchId = matchId,
            InningsNumber = number,
            BattingTeamId = battingTeamId,
            BowlingTeamId = bowlingTeamId,
            IsFollowOn = isFollowOn
        };

        match.Status = MatchStatus.InProgress;
        match.UpdatedAtUtc = DateTime.Now;
        match.Version++;

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        db.Innings.Add(innings);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        await _squadCache.WarmAsync(matchId, ct).ConfigureAwait(false);
        _squadCache.SetInningsSides(matchId, battingTeamId, bowlingTeamId);
        await PublishLiveMatchAsync(match, innings.Id, deliveryId: null, ct).ConfigureAwait(false);
        return innings;
    }

    public async Task<EndInningsResult> EndInningsAsync(Guid matchId, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches
            .Include(m => m.Innings)
            .ThenInclude(i => i.Deliveries)
            .FirstOrDefaultAsync(m => m.Id == matchId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Match not found.");
        EnsureScoringOwner(match);

        var current = match.Innings
            .Where(i => !i.IsCompleted)
            .OrderByDescending(i => i.InningsNumber)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No active innings to end.");

        current.IsCompleted = true;
        match.UpdatedAtUtc = DateTime.Now;
        match.Version++;

        var completed = match.Innings.OrderBy(i => i.InningsNumber).ToList();
        var max = MaxInningsFor(match.Format);
        var matchLegal = completed.SelectMany(i => i.Deliveries).Count(d => d.IsLegalDelivery);
        var plan = PlanNextInnings(match, completed, matchLegal);

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        string? potmName = null;
        Guid? potmId = null;
        if (plan.MatchCompleted)
        {
            match.Status = MatchStatus.Completed;
            potmId = AssignPlayerOfTheMatchByPerformance(match);
            potmName = ResolveSquadPlayerName(match, potmId);
            EnqueueCompletedMatchSnapshot(db, match);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        if (plan.MatchCompleted)
        {
            _squadCache.Clear(matchId);
            // Tournament matches stay local until tournament Sync; standalone matches push now.
            if (match.TournamentId is null && match.SeriesId is null)
                _syncTrigger.RequestSyncNow();
        }

        await PublishLiveMatchAsync(match, current.Id, deliveryId: null, ct).ConfigureAwait(false);

        var completeMsg = $"Innings {current.InningsNumber}/{max} ended — match complete.";
        if (!string.IsNullOrWhiteSpace(potmName))
            completeMsg += $" Player of the Match: {potmName}.";

        return plan with
        {
            Message = plan.MatchCompleted
                ? completeMsg
                : plan.FollowOnAvailable
                    ? $"Innings {current.InningsNumber} ended. Lead {plan.LeadBy}. Enforce follow-on or bat again."
                    : $"Innings {current.InningsNumber} ended. Start next turn.",
            PlayerOfTheMatchPlayerId = potmId,
            PlayerOfTheMatchName = potmName
        };
    }

    public async Task<Delivery> RecordDeliveryAsync(Guid matchId, DeliveryInput input, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var innings = await db.Innings
            .Include(i => i.Deliveries)
            .Where(i => i.MatchId == matchId && !i.IsCompleted)
            .OrderByDescending(i => i.InningsNumber)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Start an innings before scoring.");

        var match = await db.Matches.FirstAsync(m => m.Id == matchId, ct).ConfigureAwait(false);
        ScoringOwnership.Stamp(match, _auth);
        EnsureScoringOwner(match);
        await EnsureSoleLeagueMatchAsync(db, matchId, inProgressOnly: true, ct).ConfigureAwait(false);

        var legalSoFar = innings.Deliveries.Count(d => d.IsLegalDelivery);
        var matchLegal = legalSoFar;
        if (match.OversMode == OversMode.MatchTotal)
        {
            var otherLegal = await db.Deliveries
                .CountAsync(d => d.MatchId == matchId && d.InningsId != innings.Id && d.IsLegalDelivery, ct)
                .ConfigureAwait(false);
            matchLegal = otherLegal + legalSoFar;
        }

        if (OversRules.InningsOversComplete(match.OversMode, match.OversLimit, legalSoFar, matchLegal))
        {
            throw new InvalidOperationException(match.OversMode == OversMode.MatchTotal
                ? $"Match overs complete ({match.OversLimit}) — end this innings."
                : $"Overs complete ({match.OversLimit}) — end this innings.");
        }

        var sequence = innings.Deliveries.Count == 0 ? 1 : innings.Deliveries.Max(d => d.Sequence) + 1;
        var overNumber = legalSoFar / 6;
        var ballInOver = legalSoFar % 6;

        var isWideOrNb = input.ExtraType is ExtraType.Wide or ExtraType.NoBall;
        var isLegal = !isWideOrNb;

        var delivery = new Delivery
        {
            MatchId = matchId,
            InningsId = innings.Id,
            Sequence = sequence,
            OverNumber = overNumber,
            BallInOver = isLegal ? ballInOver + 1 : ballInOver,
            BatterId = input.BatterId,
            NonStrikerId = input.NonStrikerId,
            BowlerId = input.BowlerId,
            RunsOffBat = Math.Max(0, input.RunsOffBat),
            ExtraRuns = Math.Max(0, input.ExtraRuns),
            ExtraType = input.ExtraType,
            IsWicket = input.IsWicket,
            WicketType = input.IsWicket ? input.WicketType : WicketType.None,
            DismissedPlayerId = input.IsWicket ? (input.DismissedPlayerId ?? input.BatterId) : null,
            FielderId = input.IsWicket ? input.FielderId : null,
            IsLegalDelivery = isLegal,
            ShotZone = input.ShotZone
        };

        match.Status = MatchStatus.InProgress;
        match.UpdatedAtUtc = DateTime.Now;
        match.Version++;

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        await PublishLiveMatchAsync(match, delivery.InningsId, delivery.Id, ct).ConfigureAwait(false);
        return delivery;
    }

    public async Task<UndoneDelivery> UndoLastDeliveryAsync(Guid matchId, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Sequence restarts at 1 each innings — never pick "last" across the whole match
        // or 2nd-innings undo deletes a high-sequence ball from the 1st innings.
        var innings = await db.Innings
            .Where(i => i.MatchId == matchId && !i.IsCompleted)
            .OrderByDescending(i => i.InningsNumber)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("No active innings to undo.");

        var last = await db.Deliveries
            .Where(d => d.InningsId == innings.Id)
            .OrderByDescending(d => d.Sequence)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (last is null)
            throw new InvalidOperationException("No deliveries left to undo in this innings.");

        var undoneId = last.Id;
        var undone = new UndoneDelivery(
            last.BatterId,
            last.NonStrikerId,
            last.BowlerId,
            last.IsWicket,
            last.DismissedPlayerId);

        var match = await db.Matches.FirstAsync(m => m.Id == matchId, ct).ConfigureAwait(false);
        EnsureScoringOwner(match);

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        db.Deliveries.Remove(last);
        match.UpdatedAtUtc = DateTime.Now;
        match.Version++;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        if (match.IsLiveTelecast)
        {
            try
            {
                await _live.EnqueueDeliveryDeleteAsync(matchId, undoneId, ct).ConfigureAwait(false);
            }
            catch { /* live queue must not block undo */ }
            _live.RequestFlush();
        }
        return undone;
    }

    public async Task<LiveScoreSnapshot?> GetLiveSnapshotAsync(Guid matchId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct)
            .ConfigureAwait(false);
        if (match is null)
            return null;

        var allInnings = await db.Innings.AsNoTracking()
            .Include(i => i.Deliveries)
            .Where(i => i.MatchId == matchId)
            .OrderBy(i => i.InningsNumber)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var innings = allInnings.Where(i => !i.IsCompleted).OrderByDescending(i => i.InningsNumber).FirstOrDefault();
        if (innings is null)
            return null;

        var deliveries = innings.Deliveries.OrderBy(d => d.Sequence).ToList();
        var runs = deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns);
        var wickets = deliveries.Count(d => d.IsWicket);
        var legal = deliveries.Count(d => d.IsLegalDelivery);
        var matchLegal = allInnings.Sum(i => i.Deliveries.Count(d => d.IsLegalDelivery));
        var overs = legal / 6;
        var balls = legal % 6;
        var last = deliveries.LastOrDefault();
        var remaining = OversRules.BallsRemaining(match.OversMode, match.OversLimit, legal, matchLegal);
        var oversDone = remaining == 0;
        var ballsRemaining = remaining ?? 0;

        var situation = BuildSituation(match, innings, runs, remaining, allInnings);
        var currentRunRate = legal == 0 ? 0 : Math.Round(runs * 6.0 / legal, 2);
        var projectedScore = remaining is null || legal == 0
            ? 0
            : (int)Math.Round(runs * (legal + ballsRemaining) / (double)legal);
        double? requiredRunRate = null;
        if (remaining is int left && situation.IsChase && situation.RunsRequired is int need && need > 0 && left > 0)
            requiredRunRate = Math.Round(need * 6.0 / left, 2);
        else if (situation.IsChase && situation.RunsRequired == 0)
            requiredRunRate = 0;

        return new LiveScoreSnapshot(
            matchId,
            innings.Id,
            innings.InningsNumber,
            runs,
            wickets,
            overs,
            balls,
            match.OversLimit,
            match.OversMode,
            match.Format,
            oversDone,
            last?.BatterId,
            last?.NonStrikerId,
            last?.BowlerId,
            $"{runs}/{wickets} ({OversRules.ScoreOversText(match.OversMode, match.OversLimit, overs, balls, remaining)})",
            situation.Text,
            situation.Target,
            situation.RunsRequired,
            ballsRemaining,
            currentRunRate,
            requiredRunRate,
            projectedScore,
            situation.IsChase);
    }

    public async Task<ScoringContext?> GetScoringContextAsync(Guid matchId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matchId, ct)
            .ConfigureAwait(false);
        if (match is null)
            return null;

        var innings = await db.Innings.AsNoTracking()
            .Where(i => i.MatchId == matchId && !i.IsCompleted)
            .OrderByDescending(i => i.InningsNumber)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (innings is null)
            return null;

        var squad = await _squadCache.WarmAsync(matchId, ct).ConfigureAwait(false);
        _squadCache.SetInningsSides(matchId, innings.BattingTeamId, innings.BowlingTeamId);
        squad = _squadCache.TryGet(matchId) ?? squad;

        var max = MaxInningsFor(match.Format);
        var formatName = match.Format == MatchFormat.DoubleInnings ? "Double innings" : "Single inning";
        var hint = $"{formatName} · Inn {innings.InningsNumber}/{max} · {OversRules.ShortLabel(match.OversMode, match.OversLimit)}"
                   + (innings.IsFollowOn ? " · Follow-on" : string.Empty);

        var battingName = innings.BattingTeamId == squad.TeamAId ? squad.TeamAName
            : innings.BattingTeamId == squad.TeamBId ? squad.TeamBName
            : "?";
        var bowlingName = innings.BowlingTeamId == squad.TeamAId ? squad.TeamAName
            : innings.BowlingTeamId == squad.TeamBId ? squad.TeamBName
            : "?";

        var dismissedIds = await db.Deliveries.AsNoTracking()
            .Where(d => d.InningsId == innings.Id && d.IsWicket && d.DismissedPlayerId != null)
            .Select(d => d.DismissedPlayerId!.Value)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var dismissed = dismissedIds.ToHashSet();
        var availableBatters = squad.Batters.Where(p => !dismissed.Contains(p.Id)).ToList();

        // Hide bowlers who have completed their max overs (like dismissed batters leave the list).
        IReadOnlyList<SquadPlayer> availableBowlers = squad.Bowlers;
        if (match.BowlerMaxOvers > 0)
        {
            var legalByBowler = await db.Deliveries.AsNoTracking()
                .Where(d => d.InningsId == innings.Id && d.IsLegalDelivery)
                .GroupBy(d => d.BowlerId)
                .Select(g => new { BowlerId = g.Key, LegalBalls = g.Count() })
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var exhausted = legalByBowler
                .Where(x => x.LegalBalls / 6 >= match.BowlerMaxOvers)
                .Select(x => x.BowlerId)
                .ToHashSet();
            availableBowlers = squad.Bowlers.Where(p => !exhausted.Contains(p.Id)).ToList();
            if (exhausted.Count > 0)
                hint += $" · Max {match.BowlerMaxOvers} ov/bowler";
        }

        return new ScoringContext(
            matchId,
            innings.Id,
            innings.InningsNumber,
            innings.BattingTeamId,
            innings.BowlingTeamId,
            battingName,
            bowlingName,
            match.Format,
            match.OversLimit,
            match.OversMode,
            max,
            squad.TeamAName,
            squad.TeamBName,
            hint,
            match.LastBatsmanCanBatAsSingleWicket,
            match.BowlerMaxOvers,
            availableBatters,
            availableBowlers,
            squad.Fielders,
            match.TournamentId is not null || match.SeriesId is not null,
            match.LiveStreamUrl,
            match.IsLiveTelecast);
    }

    public async Task<InningsScorecard?> GetLiveScorecardAsync(
        Guid matchId,
        Guid? strikerId = null,
        Guid? nonStrikerId = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var innings = await db.Innings.AsNoTracking()
            .Where(i => i.MatchId == matchId && !i.IsCompleted)
            .OrderByDescending(i => i.InningsNumber)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (innings is null)
            return null;

        var deliveries = await db.Deliveries.AsNoTracking()
            .Where(d => d.InningsId == innings.Id)
            .OrderBy(d => d.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var playerIds = deliveries
            .SelectMany(d => new[] { d.BatterId, d.BowlerId, d.DismissedPlayerId ?? Guid.Empty, d.FielderId ?? Guid.Empty })
            .Concat(new[] { strikerId ?? Guid.Empty, nonStrikerId ?? Guid.Empty })
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var squad = await _squadCache.WarmAsync(matchId, ct).ConfigureAwait(false);
        var names = squad.AllPlayers
            .Where(p => playerIds.Contains(p.Id))
            .ToDictionary(p => p.Id, p => p.FullName);

        var battingCaptainId = CaptainOfTeam(squad, innings.BattingTeamId);
        var bowlingCaptainId = CaptainOfTeam(squad, innings.BowlingTeamId);
        var battingSquad = SquadForTeam(squad, innings.BattingTeamId);
        var bowlingSquad = SquadForTeam(squad, innings.BowlingTeamId);

        return BuildInningsScorecard(
            deliveries, names, strikerId, nonStrikerId, battingCaptainId, bowlingCaptainId,
            battingSquad, bowlingSquad);
    }

    private static Guid? CaptainOfTeam(MatchSquadSnapshot squad, Guid teamId)
    {
        if (teamId == squad.TeamAId)
            return squad.TeamACaptainId;
        if (teamId == squad.TeamBId)
            return squad.TeamBCaptainId;
        return null;
    }

    public async Task<MatchFullScorecard?> GetMatchScorecardAsync(Guid matchId, CancellationToken ct = default)
    {
        // Always prefer a local card that already has innings (live, just-completed, or
        // tournament match waiting for tournament Sync). Waiting on the API first made
        // unsynced scorecards sit on a spinner until 404/timeout, then fill in slowly.
        var local = await TryBuildLocalScorecardAsync(matchId, ct).ConfigureAwait(false);
        if (local is not null && local.Innings.Count > 0)
            return local;

        try
        {
            var remote = await _api.GetMatchScorecardAsync(matchId, ct).ConfigureAwait(false);
            if (remote is not null && remote.Innings.Count > 0)
                return remote;
        }
        catch
        {
            /* offline — local only */
        }

        return local;
    }

    public async Task<MatchAnalyticsReport?> GetMatchAnalyticsAsync(Guid matchId, CancellationToken ct = default)
    {
        var local = await TryBuildLocalAnalyticsAsync(matchId, ct).ConfigureAwait(false);
        if (local is not null && local.Innings.Count > 0)
            return local;

        try
        {
            var remote = await _api.GetMatchAnalyticsAsync(matchId, ct).ConfigureAwait(false);
            if (remote is not null && remote.Innings.Count > 0)
                return remote;
        }
        catch
        {
            /* offline */
        }

        return local;
    }

    private async Task<MatchAnalyticsReport?> TryBuildLocalAnalyticsAsync(Guid matchId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId, ct)
            .ConfigureAwait(false);
        if (match is null)
            return null;

        var innings = await db.Innings.AsNoTracking()
            .Where(i => i.MatchId == matchId)
            .OrderBy(i => i.InningsNumber)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (innings.Count == 0)
            return null;

        var inningsIds = innings.Select(i => i.Id).ToList();
        var deliveries = await db.Deliveries.AsNoTracking()
            .Where(d => inningsIds.Contains(d.InningsId))
            .OrderBy(d => d.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (deliveries.Count == 0)
            return null;

        var roster = MatchRosterPayload.TryParse(match.SquadRosterJson);
        var (playerNames, teamNames) = await LiveMasterNames.LoadAsync(_api, _dbFactory, ct)
            .ConfigureAwait(false);
        roster?.ApplyLiveNames(playerNames, teamNames);
        var names = new Dictionary<Guid, string>();
        if (roster is not null)
        {
            foreach (var p in roster.TeamAPlayers.Concat(roster.TeamBPlayers))
                names[p.Id] = p.FullName;
        }
        foreach (var kv in playerNames)
            names[kv.Key] = kv.Value;

        var teamA = LiveMasterNames.Lookup(teamNames, match.TeamAId, roster?.TeamAName ?? "Team A");
        var teamB = LiveMasterNames.Lookup(teamNames, match.TeamBId, roster?.TeamBName ?? "Team B");
        var byInn = deliveries.GroupBy(d => d.InningsId).ToDictionary(g => g.Key, g => g.ToList());
        var rows = new List<(int Number, string TeamName, IReadOnlyList<Delivery> Balls)>();
        foreach (var inn in innings)
        {
            byInn.TryGetValue(inn.Id, out var balls);
            var batting = inn.BattingTeamId == match.TeamAId ? teamA : teamB;
            rows.Add((inn.InningsNumber, batting, balls ?? new List<Delivery>()));
        }

        var result = match.Status == MatchStatus.InProgress ? "LIVE" : match.Status == MatchStatus.Completed ? "Completed" : "";
        return MatchAnalyticsBuilder.Build(match.Title, teamA, teamB, result, Math.Max(match.OversLimit, 1), rows, names);
    }

    private async Task<MatchFullScorecard?> TryBuildLocalScorecardAsync(Guid matchId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId, ct)
            .ConfigureAwait(false);
        if (match is null)
            return null;

        var innings = await db.Innings.AsNoTracking()
            .Where(i => i.MatchId == matchId)
            .OrderBy(i => i.InningsNumber)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var inningsIds = innings.Select(i => i.Id).ToList();
        var deliveries = await db.Deliveries.AsNoTracking()
            .Where(d => inningsIds.Contains(d.InningsId))
            .OrderBy(d => d.Sequence)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var roster = MatchRosterPayload.TryParse(match.SquadRosterJson);
        var (playerNames, teamNames) = await LiveMasterNames.LoadAsync(_api, _dbFactory, ct)
            .ConfigureAwait(false);
        var oldA = roster?.TeamAName ?? "Team A";
        var oldB = roster?.TeamBName ?? "Team B";
        roster?.ApplyLiveNames(playerNames, teamNames);
        var teams = new Dictionary<Guid, string>
        {
            [match.TeamAId] = LiveMasterNames.Lookup(teamNames, match.TeamAId, roster?.TeamAName ?? "Team A"),
            [match.TeamBId] = LiveMasterNames.Lookup(teamNames, match.TeamBId, roster?.TeamBName ?? "Team B")
        };

        var playerIds = deliveries
            .SelectMany(d => new[] { d.BatterId, d.BowlerId, d.DismissedPlayerId ?? Guid.Empty, d.FielderId ?? Guid.Empty })
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var names = new Dictionary<Guid, string>();
        if (roster is not null)
        {
            foreach (var p in roster.TeamAPlayers.Concat(roster.TeamBPlayers))
                names[p.Id] = p.FullName;
        }

        foreach (var kv in playerNames)
            names[kv.Key] = kv.Value;

        foreach (var id in playerIds.Where(id => !names.ContainsKey(id)))
            names[id] = id.ToString("N")[..8];

        var teamA = teams.GetValueOrDefault(match.TeamAId, "?");
        var teamB = teams.GetValueOrDefault(match.TeamBId, "?");
        var byInnings = deliveries.GroupBy(d => d.InningsId).ToDictionary(g => g.Key, g => g.ToList());

        Guid? CaptainOf(Guid teamId)
        {
            if (teamId == match.TeamAId)
                return match.TeamACaptainId ?? roster?.TeamACaptainId;
            if (teamId == match.TeamBId)
                return match.TeamBCaptainId ?? roster?.TeamBCaptainId;
            return null;
        }

        var sections = new List<InningsScorecardSection>();
        foreach (var inn in innings)
        {
            byInnings.TryGetValue(inn.Id, out var balls);
            balls ??= new List<Delivery>();
            var battingSquad = SquadForTeam(roster, match.TeamAId, match.TeamBId, inn.BattingTeamId);
            var bowlingSquad = SquadForTeam(roster, match.TeamAId, match.TeamBId, inn.BowlingTeamId);
            var lastBall = balls.LastOrDefault();
            var innCard = BuildInningsScorecard(
                balls, names,
                inn.IsCompleted ? null : lastBall?.BatterId,
                inn.IsCompleted ? null : lastBall?.NonStrikerId,
                CaptainOf(inn.BattingTeamId),
                CaptainOf(inn.BowlingTeamId),
                battingSquad,
                bowlingSquad);
            var legal = balls.Count(d => d.IsLegalDelivery);
            sections.Add(new InningsScorecardSection
            {
                InningsNumber = inn.InningsNumber,
                BattingTeamId = inn.BattingTeamId,
                BowlingTeamId = inn.BowlingTeamId,
                BattingTeamName = teams.GetValueOrDefault(inn.BattingTeamId, "?"),
                BowlingTeamName = teams.GetValueOrDefault(inn.BowlingTeamId, "?"),
                IsFollowOn = inn.IsFollowOn,
                Extras = innCard.Extras,
                TotalRuns = innCard.TotalRuns,
                Wickets = innCard.Wickets,
                Batting = innCard.Batting,
                Bowling = innCard.Bowling,
                DidNotBatText = innCard.DidNotBatText,
                DidNotBowlText = innCard.DidNotBowlText,
                OversFaced = $"{legal / 6}.{legal % 6}",
                ExtrasText = FormatExtrasBreakdown(balls),
                FallOfWicketsText = FormatFallOfWickets(balls, names)
            });
        }

        var (aRuns, aWkts) = TeamTotalsFromSections(sections, match.TeamAId);
        var (bRuns, bWkts) = TeamTotalsFromSections(sections, match.TeamBId);
        var firstBat = innings.OrderBy(i => i.InningsNumber).FirstOrDefault()?.BattingTeamId;
        var (result, winnerId) = BuildResultText(
            match, teamA, teamB, aRuns, aWkts, bRuns, bWkts, firstBat,
            roster?.TeamAPlayers.Count ?? 0,
            roster?.TeamBPlayers.Count ?? 0);

        var formatText = match.Format == MatchFormat.DoubleInnings ? "Double innings" : "Single inning";
        var meta = $"{formatText} · {OversRules.ShortLabel(match.OversMode, match.OversLimit)} · {match.MatchDateUtc:dd MMM yyyy}";
        if (!string.IsNullOrWhiteSpace(match.Venue))
            meta += $" · {match.Venue}";

        string tossText = string.Empty;
        if (match.TossWinnerTeamId is Guid tossId)
        {
            var tossName = teams.GetValueOrDefault(tossId, "?");
            tossText = match.TossWinnerBatsFirst
                ? $"Toss: {tossName} elected to bat"
                : $"Toss: {tossName} elected to bowl";
        }

        var card = new MatchFullScorecard
        {
            MatchId = match.Id,
            Title = LiveMasterNames.RefreshVsTitle(match.Title, oldA, oldB, teamA, teamB),
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
            PlayerOfTheMatchName = match.PlayerOfTheMatchPlayerId is Guid potmId
                ? roster?.TeamAPlayers.Concat(roster.TeamBPlayers).FirstOrDefault(p => p.Id == potmId)?.FullName
                  ?? names.GetValueOrDefault(potmId)
                : null,
            LiveStreamUrl = match.LiveStreamUrl,
            Status = (byte)match.Status,
            TeamACaptainName = roster?.TeamACaptainName,
            TeamBCaptainName = roster?.TeamBCaptainName,
            Innings = sections
        };
        ApplyCreasePanel(card, match, innings, byInnings);
        ApplyChasePanel(card, match, innings, byInnings);
        return card;
    }

    private static void ApplyCreasePanel(
        MatchFullScorecard card,
        Match match,
        IReadOnlyList<Innings> innings,
        IReadOnlyDictionary<Guid, List<Delivery>> byInnings)
    {
        card.TotalOvers = match.OversMode == OversMode.Unlimited
            ? "Unlimited"
            : match.OversLimit.ToString();

        var liveInn = innings.LastOrDefault(i => !i.IsCompleted) ?? innings.LastOrDefault();
        if (liveInn is null)
        {
            card.CurrentBatter = "—";
            card.CurrentBowler = "—";
            card.CurrentOver = "0.0";
            return;
        }

        byInnings.TryGetValue(liveInn.Id, out var balls);
        balls ??= new List<Delivery>();
        var legal = balls.Count(d => d.IsLegalDelivery);
        card.CurrentOver = $"{legal / 6}.{legal % 6}";

        var last = balls.LastOrDefault();
        var section = card.Innings.FirstOrDefault(s => s.InningsNumber == liveInn.InningsNumber);
        if (last is null)
        {
            card.CurrentBatter = "—";
            card.CurrentBowler = "—";
            card.CurrentNonStriker = "—";
            card.BugTeamScore = section is null ? "—" : $"{section.BattingTeamName} {section.TotalText}";
            card.ThisOver = new List<OverBallMark>();
            return;
        }

        var bat = section?.Batting.FirstOrDefault(b => b.PlayerId == last.BatterId);
        card.CurrentBatter = bat is null
            ? "—"
            : $"{bat.Name}  {bat.Runs}({bat.Balls})";

        var ns = section?.Batting.FirstOrDefault(b => b.PlayerId == last.NonStrikerId && b.PlayerId != last.BatterId);
        card.CurrentNonStriker = ns is null
            ? "—"
            : $"{ns.Name}  {ns.Runs}({ns.Balls})";

        var bowl = section?.Bowling.FirstOrDefault(b => b.PlayerId == last.BowlerId);
        card.CurrentBowler = bowl is null
            ? "—"
            : $"{bowl.Name}  {bowl.FiguresText}";

        card.BugTeamScore = section is null ? "—" : $"{section.BattingTeamName} {section.TotalText}";
        card.CurrentRunRate = legal == 0 || section is null
            ? "-"
            : Math.Round(section.TotalRuns * 6.0 / legal, 2).ToString("0.00");
        card.ThisOver = BuildThisOver(balls);
    }

    private static void ApplyChasePanel(
        MatchFullScorecard card,
        Match match,
        IReadOnlyList<Innings> innings,
        IReadOnlyDictionary<Guid, List<Delivery>> byInnings)
    {
        card.IsChase = false;
        card.ChaseNeedText = string.Empty;
        card.RequiredRunRate = "-";
        if (match.Status != MatchStatus.InProgress)
            return;

        var liveInn = innings.LastOrDefault(i => !i.IsCompleted);
        if (liveInn is null)
            return;

        byInnings.TryGetValue(liveInn.Id, out var balls);
        balls ??= new List<Delivery>();
        var currentRuns = balls.Sum(d => d.RunsOffBat + d.ExtraRuns);
        var legal = balls.Count(d => d.IsLegalDelivery);
        var matchLegal = byInnings.Values.Sum(list => list.Count(d => d.IsLegalDelivery));
        var remaining = OversRules.BallsRemaining(match.OversMode, match.OversLimit, legal, matchLegal);

        int InnRuns(Innings inn)
        {
            if (inn.Id == liveInn.Id)
                return currentRuns;
            return byInnings.TryGetValue(inn.Id, out var b)
                ? b.Sum(d => d.RunsOffBat + d.ExtraRuns)
                : 0;
        }

        int CompletedTeamRuns(Guid teamId) => innings.Where(i => i.BattingTeamId == teamId && i.Id != liveInn.Id).Sum(InnRuns);

        if (match.Format == MatchFormat.SingleInning && liveInn.InningsNumber == 2)
        {
            var first = innings.FirstOrDefault(i => i.InningsNumber == 1);
            var target = (first is null ? 0 : InnRuns(first)) + 1;
            var need = Math.Max(0, target - currentRuns);
            card.IsChase = true;
            card.ChaseNeedText = FormatChaseNeed(target, need, remaining, match.OversMode);
            card.RequiredRunRate = FormatRequiredRunRate(need, remaining);
            return;
        }

        if (match.Format == MatchFormat.DoubleInnings && liveInn.InningsNumber >= 4)
        {
            var inningsTarget = CompletedTeamRuns(liveInn.BowlingTeamId) - CompletedTeamRuns(liveInn.BattingTeamId) + 1;
            if (inningsTarget < 1)
                inningsTarget = 1;
            var need = Math.Max(0, inningsTarget - currentRuns);
            card.IsChase = true;
            card.ChaseNeedText = FormatChaseNeed(inningsTarget, need, remaining, match.OversMode);
            card.RequiredRunRate = FormatRequiredRunRate(need, remaining);
        }
    }

    private static string FormatChaseNeed(int target, int need, int? ballsRemaining, OversMode oversMode)
    {
        if (need == 0)
            return $"Target {target} achieved";
        if (oversMode == OversMode.Unlimited || ballsRemaining is null)
            return $"Need {need} runs  ·  Target {target}";
        var oversLeft = $"{ballsRemaining.Value / 6}.{ballsRemaining.Value % 6}";
        return $"Need {need} from {ballsRemaining} balls ({oversLeft} ov)  ·  Target {target}";
    }

    private static string FormatRequiredRunRate(int need, int? ballsRemaining)
    {
        if (need == 0)
            return "0.00";
        if (ballsRemaining is not int left || left <= 0)
            return "-";
        return Math.Round(need * 6.0 / left, 2).ToString("0.00");
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

    private static string FormatFallOfWickets(IReadOnlyList<Delivery> balls, IReadOnlyDictionary<Guid, string> names)
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
            var who = names.GetValueOrDefault(whoId, "?");
            parts.Add($"{runs}/{wkts} ({who}, {legal / 6}.{legal % 6})");
        }

        return parts.Count == 0 ? string.Empty : $"FOW: {string.Join(" · ", parts)}";
    }

    private static List<OverBallMark> BuildThisOver(IReadOnlyList<Delivery> balls)
    {
        if (balls.Count == 0)
            return new List<OverBallMark>();
        var overNo = balls[^1].OverNumber;
        return balls.Where(d => d.OverNumber == overNo).Select(ToOverBall).ToList();
    }

    private static OverBallMark ToOverBall(Delivery d)
    {
        if (d.IsWicket)
            return new OverBallMark { Text = "W", Kind = "wicket" };
        if (d.ExtraType == ExtraType.Wide)
            return new OverBallMark { Text = "wd", Kind = "extra" };
        if (d.ExtraType == ExtraType.NoBall)
            return new OverBallMark { Text = "nb", Kind = "extra" };
        var runs = d.RunsOffBat + (d.ExtraType is ExtraType.Bye or ExtraType.LegBye ? d.ExtraRuns : 0);
        if (runs >= 6)
            return new OverBallMark { Text = "6", Kind = "six" };
        if (runs == 4)
            return new OverBallMark { Text = "4", Kind = "four" };
        if (runs == 0)
            return new OverBallMark { Text = "•", Kind = "dot" };
        return new OverBallMark { Text = runs.ToString(), Kind = "run" };
    }

    private static (int Runs, int Wickets) TeamTotalsFromSections(
        IReadOnlyList<InningsScorecardSection> sections, Guid teamId)
    {
        var runs = 0;
        var wickets = 0;
        foreach (var s in sections.Where(x => x.BattingTeamId == teamId))
        {
            runs += s.TotalRuns;
            wickets += s.Wickets;
        }
        return (runs, wickets);
    }

    private static IReadOnlyList<SquadPlayer> SquadForTeam(MatchSquadSnapshot squad, Guid teamId)
    {
        if (teamId == squad.TeamAId)
            return squad.TeamAPlayers;
        if (teamId == squad.TeamBId)
            return squad.TeamBPlayers;
        return Array.Empty<SquadPlayer>();
    }

    private static IReadOnlyList<SquadPlayer> SquadForTeam(
        MatchRosterPayload? roster, Guid teamAId, Guid teamBId, Guid teamId)
    {
        if (roster is null)
            return Array.Empty<SquadPlayer>();
        if (teamId == teamAId)
            return roster.TeamAPlayers;
        if (teamId == teamBId)
            return roster.TeamBPlayers;
        return Array.Empty<SquadPlayer>();
    }

    private static string FormatDidNotLine(string label, IReadOnlyList<SquadPlayer>? squad, IEnumerable<Guid> usedIds)
    {
        if (squad is null || squad.Count == 0)
            return string.Empty;

        var used = usedIds as HashSet<Guid> ?? usedIds.ToHashSet();
        var leftover = squad
            .Where(p => !used.Contains(p.Id))
            .Select(p => p.DisplayName)
            .ToList();
        return leftover.Count == 0
            ? string.Empty
            : $"{label}: {string.Join(", ", leftover)}";
    }

    private static InningsScorecard BuildInningsScorecard(
        IReadOnlyList<Delivery> deliveries,
        IReadOnlyDictionary<Guid, string> names,
        Guid? strikerId,
        Guid? nonStrikerId,
        Guid? battingCaptainId = null,
        Guid? bowlingCaptainId = null,
        IReadOnlyList<SquadPlayer>? battingSquad = null,
        IReadOnlyList<SquadPlayer>? bowlingSquad = null)
    {
        string RawName(Guid id) => names.GetValueOrDefault(id, "?");
        string NameOf(Guid id)
        {
            var n = RawName(id);
            if (battingCaptainId == id || bowlingCaptainId == id)
                return $"{n} (C)";
            return n;
        }

        var battingOrder = new List<Guid>();
        void EnsureBatter(Guid id)
        {
            if (id == Guid.Empty || battingOrder.Contains(id))
                return;
            battingOrder.Add(id);
        }

        foreach (var d in deliveries)
            EnsureBatter(d.BatterId);
        if (strikerId is Guid s) EnsureBatter(s);
        if (nonStrikerId is Guid ns) EnsureBatter(ns);

        var batting = new List<BattingCardRow>();
        foreach (var pid in battingOrder)
        {
            var faced = deliveries.Where(d => d.BatterId == pid).ToList();
            var runs = faced.Sum(d => d.RunsOffBat);
            var balls = faced.Count(d => d.IsLegalDelivery);
            var fours = faced.Count(d => d.RunsOffBat == 4);
            var sixes = faced.Count(d => d.RunsOffBat == 6);

            var outDelivery = deliveries.LastOrDefault(d =>
                d.IsWicket && d.DismissedPlayerId == pid);
            var isOut = outDelivery is not null;
            var dismissal = isOut ? FormatDismissal(outDelivery!, NameOf) : string.Empty;

            batting.Add(new BattingCardRow
            {
                PlayerId = pid,
                Name = RawName(pid),
                Runs = runs,
                Balls = balls,
                Fours = fours,
                Sixes = sixes,
                IsOut = isOut,
                IsOnStrike = strikerId == pid,
                IsCaptain = battingCaptainId == pid,
                Dismissal = dismissal
            });
        }

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
            var balls = bowled.Count(d => d.IsLegalDelivery);
            var runsConceded = bowled.Sum(d => d.RunsOffBat + d.ExtraRuns);
            var wickets = bowled.Count(d =>
                d.IsWicket && d.WicketType is not WicketType.RunOut and not WicketType.Retired and not WicketType.None);
            var maidens = CountMaidens(bowled);

            bowling.Add(new BowlingCardRow
            {
                PlayerId = pid,
                Name = RawName(pid),
                BallsBowled = balls,
                RunsConceded = runsConceded,
                Wickets = wickets,
                Maidens = maidens,
                IsCaptain = bowlingCaptainId == pid
            });
        }

        var extras = deliveries.Sum(d => d.ExtraRuns);
        var totalRuns = deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns);
        var totalWickets = deliveries.Count(d => d.IsWicket);
        var didNotBat = FormatDidNotLine("Did not bat", battingSquad, battingOrder);
        var didNotBowl = FormatDidNotLine("Did not bowl", bowlingSquad, bowlingOrder);

        return new InningsScorecard(batting, bowling, extras, totalRuns, totalWickets, didNotBat, didNotBowl);
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

    public async Task CompleteMatchAsync(Guid matchId, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches
            .Include(m => m.Innings)
            .ThenInclude(i => i.Deliveries)
            .FirstOrDefaultAsync(m => m.Id == matchId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Match not found.");
        EnsureScoringOwner(match);

        foreach (var inn in match.Innings)
            inn.IsCompleted = true;

        match.Status = MatchStatus.Completed;
        match.UpdatedAtUtc = DateTime.Now;
        match.Version++;
        AssignPlayerOfTheMatchByPerformance(match);

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        EnqueueCompletedMatchSnapshot(db, match);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        _squadCache.Clear(matchId);
        if (match.TournamentId is null && match.SeriesId is null)
            _syncTrigger.RequestSyncNow();
        await PublishLiveMatchAsync(match, inningsId: null, deliveryId: null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Auto-picks Player of the Match from batting / bowling / fielding impact. No UI prompt.
    /// </summary>
    private static Guid? AssignPlayerOfTheMatchByPerformance(Match match)
    {
        if (match.PlayerOfTheMatchPlayerId is Guid existing && existing != Guid.Empty)
            return existing;

        var deliveries = match.Innings.SelectMany(i => i.Deliveries).ToList();
        if (deliveries.Count == 0)
            return null;

        var roster = MatchRosterPayload.TryParse(match.SquadRosterJson);
        var squadIds = roster is null
            ? null
            : roster.TeamAPlayers.Concat(roster.TeamBPlayers).Select(p => p.Id).ToHashSet();

        var points = new Dictionary<Guid, double>();
        var runs = new Dictionary<Guid, int>();
        var wickets = new Dictionary<Guid, int>();

        void Add(Guid id, double pts)
        {
            if (id == Guid.Empty) return;
            if (squadIds is not null && !squadIds.Contains(id)) return;
            points[id] = points.GetValueOrDefault(id) + pts;
        }

        foreach (var d in deliveries)
        {
            Add(d.BatterId, d.RunsOffBat);
            runs[d.BatterId] = runs.GetValueOrDefault(d.BatterId) + d.RunsOffBat;
            if (d.RunsOffBat == 4) Add(d.BatterId, 1);
            if (d.RunsOffBat == 6) Add(d.BatterId, 2);

            var bowlerWicket = d.IsWicket && d.WicketType is not WicketType.RunOut
                               and not WicketType.Retired and not WicketType.None;
            if (bowlerWicket)
            {
                Add(d.BowlerId, 25);
                wickets[d.BowlerId] = wickets.GetValueOrDefault(d.BowlerId) + 1;
            }

            if (d.IsWicket && d.FielderId is Guid fielderId &&
                d.WicketType is WicketType.Caught or WicketType.RunOut or WicketType.Stumped)
            {
                Add(fielderId, d.WicketType == WicketType.Caught ? 12 : 10);
            }
        }

        // Maiden overs bonus.
        foreach (var bowlerGroup in deliveries.GroupBy(d => d.BowlerId))
        {
            var maidens = CountMaidens(bowlerGroup.OrderBy(d => d.Sequence).ToList());
            if (maidens > 0)
                Add(bowlerGroup.Key, maidens * 8);
        }

        if (points.Count == 0)
            return null;

        var best = points
            .OrderByDescending(kv => kv.Value)
            .ThenByDescending(kv => wickets.GetValueOrDefault(kv.Key))
            .ThenByDescending(kv => runs.GetValueOrDefault(kv.Key))
            .Select(kv => kv.Key)
            .First();

        match.PlayerOfTheMatchPlayerId = best;
        return best;
    }

    private static string? ResolveSquadPlayerName(Match match, Guid? playerId)
    {
        if (playerId is not Guid id || id == Guid.Empty)
            return null;

        var roster = MatchRosterPayload.TryParse(match.SquadRosterJson);
        return roster?.TeamAPlayers.Concat(roster.TeamBPlayers)
            .FirstOrDefault(p => p.Id == id)?.FullName;
    }

    public async Task<IReadOnlyList<SquadPlayer>> GetMatchSquadPlayersAsync(Guid matchId, CancellationToken ct = default)
    {
        var squad = await _squadCache.WarmAsync(matchId, ct).ConfigureAwait(false);
        return squad.AllPlayers
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .OrderBy(p => p.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SetPlayerOfTheMatchAsync(Guid matchId, Guid playerId, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        if (playerId == Guid.Empty)
            throw new ArgumentException("Player of the Match is required.");

        var squad = await GetMatchSquadPlayersAsync(matchId, ct).ConfigureAwait(false);
        if (squad.All(p => p.Id != playerId))
            throw new InvalidOperationException("Player of the Match must be in the match squad.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches
            .Include(m => m.Innings)
            .ThenInclude(i => i.Deliveries)
            .FirstOrDefaultAsync(m => m.Id == matchId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Match not found.");

        if (match.Status != MatchStatus.Completed)
            throw new InvalidOperationException("Choose Player of the Match after the match is completed.");

        match.PlayerOfTheMatchPlayerId = playerId;
        match.UpdatedAtUtc = DateTime.Now;
        match.Version++;

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        // Re-upsert match (and children) so server receives POTM with the completed graph.
        EnqueueCompletedMatchSnapshot(db, match);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        if (match.TournamentId is null && match.SeriesId is null)
            _syncTrigger.RequestSyncNow();
    }

    public async Task SetLiveStreamUrlAsync(Guid matchId, string? url, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var normalized = LiveStreamUrls.Normalize(url);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Match not found.");
        EnsureScoringOwner(match);

        if (match.Status is MatchStatus.Completed or MatchStatus.Abandoned)
            throw new InvalidOperationException("Live stream link can only be changed while the match is in progress.");
        if (!match.IsLiveTelecast)
            throw new InvalidOperationException("Live stream is only for live-telecast matches.");

        match.LiveStreamUrl = normalized;
        match.UpdatedAtUtc = DateTime.Now;
        match.Version++;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await PublishLiveMatchAsync(match, inningsId: null, deliveryId: null, ct).ConfigureAwait(false);
    }

    public async Task DeleteOngoingAsync(Guid matchId, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Match not found.");
        EnsureScoringOwner(match);

        if (match.Status is MatchStatus.Completed)
            throw new InvalidOperationException("Completed matches cannot be deleted.");

        if (match.Status is MatchStatus.Abandoned)
            throw new InvalidOperationException("Abandoned matches cannot be deleted.");

        // Local-only cancel — incomplete matches are never synced.
        match.IsDeleted = true;
        match.UpdatedAtUtc = DateTime.Now;
        match.Version++;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _squadCache.Clear(matchId);
        if (match.IsLiveTelecast)
        {
            try
            {
                await _live.EnqueueMatchDeleteAsync(matchId, ct).ConfigureAwait(false);
            }
            catch { }
            _live.RequestFlush();
        }
    }

    /// <summary>
    /// Queue full match graph for sync only after the match is completed.
    /// Live/incomplete matches stay local until then.
    /// </summary>
    private void EnqueueCompletedMatchSnapshot(JplDbContext db, Match match)
    {
        _outbox.Enqueue(db, nameof(Match), match.Id, "Upsert", ToMatchPayload(match));
        foreach (var inn in match.Innings.OrderBy(i => i.InningsNumber))
        {
            _outbox.Enqueue(db, nameof(Innings), inn.Id, "Upsert", ToInningsPayload(inn));
            foreach (var d in inn.Deliveries.OrderBy(x => x.Sequence))
                _outbox.Enqueue(db, nameof(Delivery), d.Id, "Upsert", ToDeliveryPayload(d));
        }
    }

    private static EndInningsResult PlanNextInnings(
        Match match,
        IReadOnlyList<Innings> completedOrdered,
        int matchLegal)
    {
        if (OversRules.MatchPoolExhausted(match.OversMode, match.OversLimit, matchLegal))
            return new EndInningsResult(true, false, false, 0, null, null, string.Empty);

        var max = MaxInningsFor(match.Format);
        var done = completedOrdered.Count;
        if (done >= max)
            return new EndInningsResult(true, false, false, 0, null, null, string.Empty);

        var firstBat = completedOrdered[0].BattingTeamId;
        var secondBat = firstBat == match.TeamAId ? match.TeamBId : match.TeamAId;

        if (done == 1)
        {
            return new EndInningsResult(
                false, true, false, 0,
                secondBat, firstBat, string.Empty);
        }

        if (done == 2 && match.Format == MatchFormat.SingleInning)
            return new EndInningsResult(true, false, false, 0, null, null, string.Empty);

        if (done == 2 && match.Format == MatchFormat.DoubleInnings)
        {
            var score1 = InningsRuns(completedOrdered[0]);
            var score2 = InningsRuns(completedOrdered[1]);
            var lead = score1 - score2;
            var followOn = lead > 0;
            return new EndInningsResult(
                false, true, followOn, Math.Max(0, lead),
                firstBat, secondBat, string.Empty);
        }

        if (done == 3)
        {
            var thirdBat = completedOrdered[2].BattingTeamId;
            var fourthBat = thirdBat == firstBat ? secondBat : firstBat;
            return new EndInningsResult(
                false, true, false, 0,
                fourthBat, thirdBat, string.Empty);
        }

        return new EndInningsResult(true, false, false, 0, null, null, string.Empty);
    }

    private readonly record struct SituationInfo(string Text, int? Target, int? RunsRequired, bool IsChase);

    private static SituationInfo BuildSituation(
        Match match,
        Innings current,
        int currentRuns,
        int? ballsRemaining,
        IReadOnlyList<Innings> allInnings)
    {
        var ballsLabel = ballsRemaining is int left ? BallsLabel(left) : "no over limit";
        var rrr = (int need) =>
            ballsRemaining is not int rem || rem <= 0 ? 0 : Math.Round(need * 6.0 / rem, 2);

        string ChaseLine(int target, int need)
        {
            if (need == 0)
                return $"Target {target} · Achieved";
            if (ballsRemaining is null)
                return $"Target {target} · Need {need} runs";
            return $"Target {target} · Need {need} runs from {ballsRemaining} balls ({ballsLabel}) · RRR {rrr(need)}";
        }

        if (current.InningsNumber == 1)
        {
            return new SituationInfo(
                ballsRemaining is null
                    ? "1st innings · set a total"
                    : $"1st innings · set a total · {ballsLabel} left",
                null,
                null,
                false);
        }

        int TeamRuns(Guid teamId) => allInnings
            .Where(i => i.BattingTeamId == teamId)
            .Sum(i => i.Id == current.Id ? currentRuns : InningsRuns(i));

        int CompletedTeamRuns(Guid teamId) => allInnings
            .Where(i => i.BattingTeamId == teamId && i.Id != current.Id)
            .Sum(InningsRuns);

        // Single inning — 2nd turn is always a chase.
        if (match.Format == MatchFormat.SingleInning && current.InningsNumber == 2)
        {
            var first = allInnings.FirstOrDefault(i => i.InningsNumber == 1);
            var firstScore = first is null ? 0 : InningsRuns(first);
            var target = firstScore + 1;
            var need = Math.Max(0, target - currentRuns);
            return new SituationInfo(ChaseLine(target, need), target, need, true);
        }

        // Double innings — 2nd: trail/lead vs 1st innings (trial).
        if (match.Format == MatchFormat.DoubleInnings && current.InningsNumber == 2)
        {
            var first = allInnings.FirstOrDefault(i => i.InningsNumber == 1);
            var firstScore = first is null ? 0 : InningsRuns(first);
            var diff = currentRuns - firstScore;
            var leadTrail = diff < 0
                ? $"Trail by {-diff}"
                : diff == 0
                    ? "Scores level"
                    : $"Lead by {diff}";
            var leftBit = ballsRemaining is null ? string.Empty : $" · {ballsLabel} left";
            return new SituationInfo(
                $"{leadTrail} (vs 1st inn {firstScore}){leftBit}",
                null,
                null,
                false);
        }

        // Double — 3rd: overall lead/trail (after both first digs; follow-on noted).
        if (match.Format == MatchFormat.DoubleInnings && current.InningsNumber == 3)
        {
            var batTot = TeamRuns(current.BattingTeamId);
            var bowlTot = TeamRuns(current.BowlingTeamId);
            var diff = batTot - bowlTot;
            var leadTrail = diff < 0
                ? $"Trail by {-diff}"
                : diff == 0
                    ? "Scores level"
                    : $"Lead by {diff}";
            var follow = current.IsFollowOn ? " · Follow-on" : string.Empty;
            var leftBit = ballsRemaining is null ? string.Empty : $" · {ballsLabel} left";
            return new SituationInfo(
                $"{leadTrail} overall{follow}{leftBit}",
                null,
                null,
                false);
        }

        // Double — 4th: chase to win.
        if (match.Format == MatchFormat.DoubleInnings && current.InningsNumber >= 4)
        {
            var oppTotal = CompletedTeamRuns(current.BowlingTeamId);
            var ownPrev = CompletedTeamRuns(current.BattingTeamId);
            var inningsTarget = oppTotal - ownPrev + 1;
            if (inningsTarget < 1)
                inningsTarget = 1;
            var need = Math.Max(0, inningsTarget - currentRuns);
            return new SituationInfo(ChaseLine(inningsTarget, need), inningsTarget, need, true);
        }

        return new SituationInfo(ballsRemaining is null ? "In progress" : $"{ballsLabel} left", null, null, false);
    }

    private static string BallsLabel(int ballsRemaining)
    {
        var o = ballsRemaining / 6;
        var b = ballsRemaining % 6;
        return $"{ballsRemaining} balls ({o}.{b} ov)";
    }

    private static int InningsRuns(Innings innings)
        => innings.Deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns);

    private static object ToMatchPayload(Match m) => new
    {
        m.Id,
        m.Title,
        m.TeamAId,
        m.TeamBId,
        m.TeamACaptainId,
        m.TeamBCaptainId,
        m.OversLimit,
        m.OversMode,
        m.Format,
        m.FormatId,
        m.MatchDateUtc,
        m.Venue,
        m.Status,
        m.TossWinnerTeamId,
        m.TossWinnerBatsFirst,
        m.LastBatsmanCanBatAsSingleWicket,
        m.BowlerMaxOvers,
        m.PlayerOfTheMatchPlayerId,
        m.TournamentId,
        m.TournamentFixtureId,
        m.SeriesId,
        m.SeriesFixtureId,
        m.Version,
        m.UpdatedAtUtc,
        m.IsDeleted,
        m.CreatedAtUtc,
        m.SquadRosterJson,
        m.IsLiveTelecast,
        m.LiveStreamUrl,
        m.StartedByUserId,
        m.StartedOnDeviceId
    };

    private static object ToInningsPayload(Innings i) => new
    {
        i.Id,
        i.MatchId,
        i.InningsNumber,
        i.BattingTeamId,
        i.BowlingTeamId,
        i.IsCompleted,
        i.IsFollowOn,
        i.StartedAtUtc
    };

    private static object ToDeliveryPayload(Delivery d) => new
    {
        d.Id,
        d.MatchId,
        d.InningsId,
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

    private async Task PublishLiveMatchAsync(Match match, Guid? inningsId, Guid? deliveryId, CancellationToken ct)
    {
        if (!match.IsLiveTelecast)
            return;

        try
        {
            await _live.EnqueueMatchAsync(match.Id, ct).ConfigureAwait(false);
            if (inningsId is Guid iid)
                await _live.EnqueueInningsAsync(iid, ct).ConfigureAwait(false);
            if (deliveryId is Guid did)
                await _live.EnqueueDeliveryAsync(did, ct).ConfigureAwait(false);
        }
        catch
        {
            /* scoring stays local if live queue fails */
        }
        _live.RequestFlush();
    }
}
