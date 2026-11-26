using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public sealed class SeriesListItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string MetaLine { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string WinnerLine { get; init; } = string.Empty;
    public byte Status { get; init; }
    public bool NeedsSync { get; init; }
}

public sealed class SeriesDetail
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid TeamAId { get; init; }
    public Guid TeamBId { get; init; }
    public string TeamAName { get; init; } = string.Empty;
    public string TeamBName { get; init; } = string.Empty;
    public int MatchCount { get; init; }
    public Guid VenueId { get; init; }
    public string VenueName { get; init; } = string.Empty;
    public Guid? FormatId { get; init; }
    public byte Format { get; init; }
    public string FormatText { get; init; } = string.Empty;
    public int OversLimit { get; init; }
    public OversMode OversMode { get; init; }
    public bool LastBatsmanCanBatAsSingleWicket { get; init; }
    public int BowlerMaxOvers { get; init; }
    public byte Status { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public Guid? WinnerTeamId { get; set; }
    public string? WinnerTeamName { get; set; }
    public Guid? PlayerOfTheSeriesPlayerId { get; set; }
    public string? PlayerOfTheSeriesName { get; set; }
    public Guid? BestBatsmanPlayerId { get; set; }
    public string? BestBatsmanName { get; set; }
    public Guid? BestBowlerPlayerId { get; set; }
    public string? BestBowlerName { get; set; }
    public Guid? BestFielderPlayerId { get; set; }
    public string? BestFielderName { get; set; }
    public bool IsTied { get; set; }
    public string ScoreLine { get; set; } = string.Empty;
    public IReadOnlyList<SeriesFixtureItem> Fixtures { get; init; } = Array.Empty<SeriesFixtureItem>();
    public IReadOnlyList<SeriesStandingItem> Standings { get; set; } = Array.Empty<SeriesStandingItem>();
}

public sealed class SeriesFixtureItem
{
    public Guid Id { get; init; }
    public string Label { get; init; } = string.Empty;
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public string TeamAName { get; set; } = string.Empty;
    public string TeamBName { get; set; } = string.Empty;
    public Guid? MatchId { get; set; }
    public string MatchStatusText { get; set; } = string.Empty;
    public string ResultText { get; set; } = string.Empty;
    public bool CanStart { get; set; }
    public bool CanResume { get; set; }
    public bool CanOpenScorecard { get; set; }
    public string VsLine => $"{TeamAName}  vs  {TeamBName}";
    public bool HasResult => !string.IsNullOrWhiteSpace(ResultText);
    public string ScorecardLabel =>
        string.Equals(MatchStatusText, "Live", StringComparison.OrdinalIgnoreCase)
            ? "Main Score"
            : "Scorecard";
}

public sealed class SeriesStandingItem
{
    public Guid TeamId { get; init; }
    public string TeamName { get; init; } = string.Empty;
    public int Rank { get; init; }
    public int Played { get; init; }
    public int Won { get; init; }
    public int Lost { get; init; }
    public int Tied { get; init; }
    public int Points { get; init; }
    public double NetRunRate { get; init; }
    public string NrrText => NetRunRate.ToString("+0.000;-0.000;0.000");
    public string RecordLine => $"P{Played} W{Won} L{Lost} T{Tied}";
}

public sealed record SeriesDraft(
    string Name,
    Guid TeamAId,
    Guid TeamBId,
    int MatchCount,
    Guid VenueId,
    byte Format,
    int OversLimit,
    bool LastBatsmanCanBatAsSingleWicket,
    int BowlerMaxOvers,
    OversMode OversMode,
    Guid? FormatId = null);

public interface ISeriesService
{
    Task<IReadOnlyList<SeriesListItem>> GetAllAsync(CancellationToken ct = default);
    Task<ApiPagedResult<SeriesListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default);
    Task<SeriesDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<SeriesDetail> CreateAsync(SeriesDraft draft, CancellationToken ct = default);
    Task<SeriesDetail> UpdateAsync(Guid id, SeriesDraft draft, CancellationToken ct = default);
    Task<SeriesDetail> GenerateAsync(Guid id, CancellationToken ct = default);
    Task LinkMatchAsync(Guid seriesId, Guid fixtureId, Guid matchId, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> IsReadyToSyncAsync(Guid seriesId, CancellationToken ct = default);
    Task EnsureReadyToSyncAsync(Guid seriesId, CancellationToken ct = default);
}

public sealed class SeriesService : ISeriesService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly ISyncService _sync;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IOutboxWriter _outbox;
    private readonly ISyncTrigger _syncTrigger;

    public SeriesService(
        IApiClient api,
        IAuthService auth,
        ILeagueContext league,
        ISyncService sync,
        IDbContextFactory<JplDbContext> dbFactory,
        IOutboxWriter outbox,
        ISyncTrigger syncTrigger)
    {
        _api = api;
        _auth = auth;
        _league = league;
        _sync = sync;
        _dbFactory = dbFactory;
        _outbox = outbox;
        _syncTrigger = syncTrigger;
    }

    public async Task<IReadOnlyList<SeriesListItem>> GetAllAsync(CancellationToken ct = default)
    {
        var canUseLocal = LeagueSyncPolicy.UsesLocalStorage(_auth.Current);
        if (canUseLocal)
            return await LoadLocalListAsync(ct).ConfigureAwait(false);

        try
        {
            var remote = await _api.GetSeriesListAsync(ct).ConfigureAwait(false);
            var pending = await _sync.GetPendingSeriesIdsAsync(ct).ConfigureAwait(false);
            var list = new List<SeriesListItem>();
            foreach (var s in remote)
            {
                var waiting = pending.Contains(s.Id);
                var ready = waiting && await IsReadyToSyncAsync(s.Id, ct).ConfigureAwait(false);
                var score = $"{s.TeamAWins}–{s.TeamBWins}";
                var winner = s.IsTied
                    ? "Series tied"
                    : string.IsNullOrWhiteSpace(s.WinnerTeamName) ? "" : $"Winner: {s.WinnerTeamName}";
                list.Add(new SeriesListItem
                {
                    Id = s.Id,
                    Name = s.Name,
                    Status = s.Status,
                    StatusText = s.StatusText,
                    MetaLine = $"{s.TeamAName} vs {s.TeamBName} · {s.MatchCount} matches · {score}",
                    WinnerLine = winner,
                    NeedsSync = ready
                });
            }
            return list;
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex))
        {
            if (canUseLocal)
                return await LoadLocalListAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<IReadOnlyList<SeriesListItem>> LoadLocalListAsync(CancellationToken ct)
    {
        await using var localDb = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var ids = await localDb.Series.AsNoTracking().OrderByDescending(row => row.UpdatedAtUtc)
            .Select(row => row.Id).ToListAsync(ct).ConfigureAwait(false);
        var localList = new List<SeriesListItem>(ids.Count);
        foreach (var id in ids)
        {
            var detail = await GetLocalDetailAsync(id, ct).ConfigureAwait(false);
            if (detail is not null) localList.Add(ToListItem(detail));
        }
        return localList;
    }

    public async Task<ApiPagedResult<SeriesListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default)
    {
        var local = await GetAllAsync(ct).ConfigureAwait(false);
        var filteredLocal = string.IsNullOrWhiteSpace(q) ? local
            : local.Where(row => row.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var localRows = filteredLocal.ToList();
        return new ApiPagedResult<SeriesListItem>(localRows.Skip(Math.Max(0, skip))
            .Take(ListPaging.PageSize).ToList(), localRows.Count, Math.Max(0, skip), ListPaging.PageSize);
    }

    public async Task<SeriesDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var canUseLocal = LeagueSyncPolicy.UsesLocalStorage(_auth.Current);
        if (canUseLocal)
            return await GetLocalDetailAsync(id, ct).ConfigureAwait(false);

        try
        {
            var dto = await _api.GetSeriesAsync(id, ct).ConfigureAwait(false);
            if (dto is null) return canUseLocal ? await GetLocalDetailAsync(id, ct).ConfigureAwait(false) : null;
            var detail = ToDetail(dto);
            if (canUseLocal)
                await OverlayLocalMatchStateAsync(detail, ct).ConfigureAwait(false);
            return detail;
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex))
        {
            if (canUseLocal)
                return await GetLocalDetailAsync(id, ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<SeriesDetail> CreateAsync(SeriesDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        Validate(draft);
        var now = DateTime.UtcNow;
        var entity = new Series
        {
            Id = Guid.NewGuid(), Name = draft.Name.Trim(), TeamAId = draft.TeamAId,
            TeamBId = draft.TeamBId, MatchCount = draft.MatchCount, VenueId = draft.VenueId,
            FormatId = draft.FormatId, Format = (MatchFormat)draft.Format,
            OversLimit = OversRules.StoredOversLimit(draft.OversMode, draft.OversLimit),
            OversMode = draft.OversMode, LastBatsmanCanBatAsSingleWicket = draft.LastBatsmanCanBatAsSingleWicket,
            BowlerMaxOvers = draft.BowlerMaxOvers, Status = SeriesStatus.Draft,
            Version = 1, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await ValidateLocalReferencesAsync(db, draft, ct).ConfigureAwait(false);
        db.Series.Add(entity);
        _outbox.EnqueueEntity(db, entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _syncTrigger.RequestSyncNow();
        return await GetLocalDetailAsync(entity.Id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The series was saved locally but could not be loaded.");
    }

    public async Task<SeriesDetail> UpdateAsync(Guid id, SeriesDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        Validate(draft);
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await ValidateLocalReferencesAsync(db, draft, ct).ConfigureAwait(false);
        var entity = await db.Series.FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Series not found.");
        if (entity.Status != SeriesStatus.Draft)
            throw new InvalidOperationException("Only draft series can be edited. Saved series are locked.");
        entity.Name = draft.Name.Trim(); entity.TeamAId = draft.TeamAId; entity.TeamBId = draft.TeamBId;
        entity.MatchCount = draft.MatchCount; entity.VenueId = draft.VenueId; entity.FormatId = draft.FormatId;
        entity.Format = (MatchFormat)draft.Format;
        entity.OversLimit = OversRules.StoredOversLimit(draft.OversMode, draft.OversLimit);
        entity.OversMode = draft.OversMode;
        entity.LastBatsmanCanBatAsSingleWicket = draft.LastBatsmanCanBatAsSingleWicket;
        entity.BowlerMaxOvers = draft.BowlerMaxOvers; entity.Version++; entity.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.EnqueueEntity(db, entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _syncTrigger.RequestSyncNow();
        return await GetLocalDetailAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The series was saved locally but could not be loaded.");
    }

    public async Task<SeriesDetail> GenerateAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.Series.Include(row => row.Fixtures)
            .FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Series not found.");
        if (entity.Status != SeriesStatus.Draft)
            throw new InvalidOperationException("Series already saved. Edit / delete is only allowed while Draft.");
        foreach (var fixture in entity.Fixtures.ToList())
        {
            db.SeriesFixtures.Remove(fixture);
            _outbox.EnqueueEntity(db, fixture, "Delete");
        }
        entity.Fixtures.Clear();
        for (var sequence = 1; sequence <= entity.MatchCount; sequence++)
        {
            var fixture = new SeriesFixture
            {
                Id = Guid.NewGuid(), SeriesId = entity.Id, Sequence = sequence,
                Label = $"Match {sequence}", TeamAId = entity.TeamAId, TeamBId = entity.TeamBId
            };
            db.SeriesFixtures.Add(fixture);
            _outbox.EnqueueEntity(db, fixture, "Upsert");
        }
        entity.Status = SeriesStatus.Scheduled; entity.WinnerTeamId = null;
        entity.PlayerOfTheSeriesPlayerId = null; entity.BestBatsmanPlayerId = null;
        entity.BestBowlerPlayerId = null; entity.BestFielderPlayerId = null; entity.IsTied = false;
        entity.UpdatedAtUtc = DateTime.UtcNow; entity.Version++;
        _outbox.EnqueueEntity(db, entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _syncTrigger.RequestSyncNow();
        return await GetLocalDetailAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The generated series was saved locally but could not be loaded.");
    }

    public async Task LinkMatchAsync(Guid seriesId, Guid fixtureId, Guid matchId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.Series.Include(row => row.Fixtures)
            .FirstOrDefaultAsync(row => row.Id == seriesId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Series not found.");
        if (entity.Status == SeriesStatus.Draft)
            throw new InvalidOperationException("Save the series first before starting matches.");
        var fixture = entity.Fixtures.FirstOrDefault(row => row.Id == fixtureId)
            ?? throw new InvalidOperationException("Series fixture not found.");
        fixture.MatchId = matchId;
        if (entity.Status == SeriesStatus.Scheduled) entity.Status = SeriesStatus.InProgress;
        entity.Version++; entity.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.EnqueueEntity(db, fixture, "Upsert");
        _outbox.EnqueueEntity(db, entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _syncTrigger.RequestSyncNow();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.Series.FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
        if (entity is null) return;
        if (entity.Status != SeriesStatus.Draft)
            throw new InvalidOperationException("Only draft series can be deleted. Saved series are locked.");
        entity.IsDeleted = true; entity.Version++; entity.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.EnqueueEntity(db, entity, "Delete");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _syncTrigger.RequestSyncNow();
    }

    public async Task<bool> IsReadyToSyncAsync(Guid seriesId, CancellationToken ct = default)
    {
        var pending = await _sync.GetPendingSeriesIdsAsync(ct).ConfigureAwait(false);
        if (!pending.Contains(seriesId))
            return false;

        try
        {
            var detail = await GetAsync(seriesId, ct).ConfigureAwait(false);
            return detail is not null && IsSeriesFullyComplete(detail);
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureReadyToSyncAsync(Guid seriesId, CancellationToken ct = default)
    {
        if (await IsReadyToSyncAsync(seriesId, ct).ConfigureAwait(false))
            return;
        throw new InvalidOperationException(
            "Sync only after every series match is completed or abandoned.");
    }

    private static bool IsSeriesFullyComplete(SeriesDetail detail)
    {
        if (detail.Status == 3 || detail.WinnerTeamId is Guid || detail.IsTied)
            return true;
        if (detail.Fixtures.Count == 0)
            return false;
        return detail.Fixtures.All(f =>
            string.Equals(f.MatchStatusText, "Completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.MatchStatusText, "Abandoned", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task ValidateLocalReferencesAsync(JplDbContext db, SeriesDraft draft, CancellationToken ct)
    {
        if (!await db.Teams.AnyAsync(row => row.Id == draft.TeamAId, ct).ConfigureAwait(false)
            || !await db.Teams.AnyAsync(row => row.Id == draft.TeamBId, ct).ConfigureAwait(false))
            throw new InvalidOperationException("A selected team is missing from local league data. Sync before creating this series.");
        if (!await db.Venues.AnyAsync(row => row.Id == draft.VenueId, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Venue not found in local league data. Sync before creating this series.");
        if (draft.FormatId is Guid formatId
            && !await db.Formats.AnyAsync(row => row.Id == formatId, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Format not found in local league data. Sync before creating this series.");
    }

    private static void Validate(SeriesDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name))
            throw new ArgumentException("Series name is required.");
        if (draft.TeamAId == Guid.Empty || draft.TeamBId == Guid.Empty)
            throw new ArgumentException("Select both teams.");
        if (draft.TeamAId == draft.TeamBId)
            throw new ArgumentException("A series must be between two different teams.");
        if (draft.MatchCount is < 1 or > 7)
            throw new ArgumentException("A series must have between 1 and 7 matches.");
        if (draft.VenueId == Guid.Empty)
            throw new ArgumentException("Venue is required.");
        if (draft.Format is not (0 or 1))
            throw new ArgumentException("Select Single inning or Double innings.");
        OversRules.Validate(draft.OversMode, draft.OversLimit, draft.BowlerMaxOvers);
    }

    private static object ToBody(SeriesDraft draft) => new
    {
        name = draft.Name.Trim(),
        teamAId = draft.TeamAId,
        teamBId = draft.TeamBId,
        matchCount = draft.MatchCount,
        venueId = draft.VenueId,
        formatId = draft.FormatId,
        format = draft.Format,
        oversLimit = OversRules.StoredOversLimit(draft.OversMode, draft.OversLimit),
        oversMode = (byte)draft.OversMode,
        lastBatsmanCanBatAsSingleWicket = draft.LastBatsmanCanBatAsSingleWicket,
        bowlerMaxOvers = draft.BowlerMaxOvers
    };

    private static SeriesDetail ToDetail(ApiSeriesDetailDto dto)
    {
        var aWins = dto.Standings.FirstOrDefault(s => s.TeamId == dto.TeamAId)?.Won ?? 0;
        var bWins = dto.Standings.FirstOrDefault(s => s.TeamId == dto.TeamBId)?.Won ?? 0;
        var score = $"{aWins}–{bWins}";
        return new SeriesDetail
        {
            Id = dto.Id,
            Name = dto.Name,
            TeamAId = dto.TeamAId,
            TeamBId = dto.TeamBId,
            TeamAName = dto.TeamAName,
            TeamBName = dto.TeamBName,
            MatchCount = dto.MatchCount,
            VenueId = dto.VenueId,
            VenueName = dto.VenueName,
            FormatId = dto.FormatId,
            Format = dto.Format,
            FormatText = string.IsNullOrWhiteSpace(dto.FormatText)
                ? (dto.Format == 1 ? "Double innings" : "Single inning")
                : dto.FormatText,
            OversLimit = dto.OversLimit,
            OversMode = (OversMode)dto.OversMode,
            LastBatsmanCanBatAsSingleWicket = dto.LastBatsmanCanBatAsSingleWicket,
            BowlerMaxOvers = dto.BowlerMaxOvers,
            Status = dto.Status,
            StatusText = dto.StatusText,
            WinnerTeamId = dto.WinnerTeamId,
            WinnerTeamName = dto.WinnerTeamName,
            PlayerOfTheSeriesPlayerId = dto.PlayerOfTheSeriesPlayerId,
            PlayerOfTheSeriesName = dto.PlayerOfTheSeriesName,
            BestBatsmanPlayerId = dto.BestBatsmanPlayerId,
            BestBatsmanName = dto.BestBatsmanName,
            BestBowlerPlayerId = dto.BestBowlerPlayerId,
            BestBowlerName = dto.BestBowlerName,
            BestFielderPlayerId = dto.BestFielderPlayerId,
            BestFielderName = dto.BestFielderName,
            IsTied = dto.IsTied,
            ScoreLine = dto.IsTied ? $"Series tied {score}" : score,
            Fixtures = dto.Fixtures.Select(f => new SeriesFixtureItem
            {
                Id = f.Id,
                Label = f.Label,
                TeamAId = f.TeamAId,
                TeamBId = f.TeamBId,
                TeamAName = f.TeamAName,
                TeamBName = f.TeamBName,
                MatchId = f.MatchId,
                MatchStatusText = f.MatchStatusText,
                ResultText = f.ResultText ?? string.Empty,
                CanStart = f.CanStart,
                CanResume = false,
                CanOpenScorecard = f.MatchId is Guid
            }).ToList(),
            Standings = dto.Standings.Select(s => new SeriesStandingItem
            {
                TeamId = s.TeamId,
                TeamName = s.TeamName,
                Rank = s.Rank,
                Played = s.Played,
                Won = s.Won,
                Lost = s.Lost,
                Tied = s.Tied,
                Points = s.Points,
                NetRunRate = s.NetRunRate
            }).ToList()
        };
    }

    private async Task<SeriesDetail?> GetLocalDetailAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.Series.AsNoTracking().Include(row => row.Fixtures)
            .FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
        if (entity is null) return null;

        var teamIds = new[] { entity.TeamAId, entity.TeamBId };
        var teams = await db.Teams.AsNoTracking().Where(row => teamIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.Name, ct).ConfigureAwait(false);
        var venueName = await db.Venues.AsNoTracking().Where(row => row.Id == entity.VenueId)
            .Select(row => row.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        var awardIds = new[] { entity.PlayerOfTheSeriesPlayerId, entity.BestBatsmanPlayerId,
            entity.BestBowlerPlayerId, entity.BestFielderPlayerId }.OfType<Guid>().Distinct().ToArray();
        var awards = awardIds.Length == 0 ? new Dictionary<Guid, string>()
            : await db.Players.AsNoTracking().Where(row => awardIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.FullName, ct).ConfigureAwait(false);
        var fixtureIds = entity.Fixtures.Select(fixture => fixture.Id).ToArray();
        var matches = await db.Matches.AsNoTracking().Where(row => row.SeriesId == id
                || (row.SeriesFixtureId != null && fixtureIds.Contains(row.SeriesFixtureId.Value)))
            .ToListAsync(ct).ConfigureAwait(false);
        var matchesById = matches.ToDictionary(row => row.Id);
        var matchesByFixture = matches.Where(row => row.SeriesFixtureId is not null)
            .GroupBy(row => row.SeriesFixtureId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.UpdatedAtUtc).First());

        var dto = new ApiSeriesDetailDto
        {
            Id = entity.Id, Name = entity.Name, TeamAId = entity.TeamAId, TeamBId = entity.TeamBId,
            TeamAName = teams.GetValueOrDefault(entity.TeamAId, string.Empty),
            TeamBName = teams.GetValueOrDefault(entity.TeamBId, string.Empty), MatchCount = entity.MatchCount,
            VenueId = entity.VenueId, VenueName = venueName, FormatId = entity.FormatId,
            Format = (byte)entity.Format,
            FormatText = entity.FormatId is Guid formatId
                ? await db.Formats.AsNoTracking().Where(row => row.Id == formatId).Select(row => row.Name)
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? FormatRules.DetailsLine(entity.Format, entity.OversMode, entity.OversLimit)
                : FormatRules.DetailsLine(entity.Format, entity.OversMode, entity.OversLimit),
            OversLimit = entity.OversLimit, OversMode = (byte)entity.OversMode,
            LastBatsmanCanBatAsSingleWicket = entity.LastBatsmanCanBatAsSingleWicket,
            BowlerMaxOvers = entity.BowlerMaxOvers, Status = (byte)entity.Status,
            StatusText = entity.Status.ToString(), WinnerTeamId = entity.WinnerTeamId,
            WinnerTeamName = entity.WinnerTeamId is Guid winnerId ? teams.GetValueOrDefault(winnerId) : null,
            PlayerOfTheSeriesPlayerId = entity.PlayerOfTheSeriesPlayerId,
            PlayerOfTheSeriesName = entity.PlayerOfTheSeriesPlayerId is Guid playerOfSeriesId
                ? awards.GetValueOrDefault(playerOfSeriesId) : null,
            BestBatsmanPlayerId = entity.BestBatsmanPlayerId,
            BestBatsmanName = entity.BestBatsmanPlayerId is Guid batsmanId ? awards.GetValueOrDefault(batsmanId) : null,
            BestBowlerPlayerId = entity.BestBowlerPlayerId,
            BestBowlerName = entity.BestBowlerPlayerId is Guid bowlerId ? awards.GetValueOrDefault(bowlerId) : null,
            BestFielderPlayerId = entity.BestFielderPlayerId,
            BestFielderName = entity.BestFielderPlayerId is Guid fielderId ? awards.GetValueOrDefault(fielderId) : null,
            IsTied = entity.IsTied,
            Fixtures = entity.Fixtures.OrderBy(row => row.Sequence).Select(row =>
            {
                var match = row.MatchId is Guid linkedId && matchesById.TryGetValue(linkedId, out var direct)
                    ? direct : matchesByFixture.GetValueOrDefault(row.Id);
                return new ApiSeriesFixtureDto
                {
                    Id = row.Id, Sequence = row.Sequence, Label = row.Label,
                    TeamAId = row.TeamAId, TeamBId = row.TeamBId,
                    TeamAName = teams.GetValueOrDefault(row.TeamAId, string.Empty),
                    TeamBName = teams.GetValueOrDefault(row.TeamBId, string.Empty), MatchId = match?.Id,
                    MatchStatus = match is null ? null : (byte)match.Status,
                    MatchStatusText = match?.Status.ToString() ?? string.Empty,
                    CanStart = match is null && entity.Status is not SeriesStatus.Draft and not SeriesStatus.Completed,
                    CanOpenScorecard = match is not null
                };
            }).ToList(),
            Standings = new List<ApiSeriesStandingDto>
            {
                new() { TeamId = entity.TeamAId, TeamName = teams.GetValueOrDefault(entity.TeamAId, string.Empty) },
                new() { TeamId = entity.TeamBId, TeamName = teams.GetValueOrDefault(entity.TeamBId, string.Empty) }
            }
        };

        var detail = ToDetail(dto);
        await OverlayLocalMatchStateAsync(detail, ct).ConfigureAwait(false);
        return detail;
    }

    private static SeriesListItem ToListItem(SeriesDetail detail)
    {
        var aWins = detail.Standings.FirstOrDefault(row => row.TeamId == detail.TeamAId)?.Won ?? 0;
        var bWins = detail.Standings.FirstOrDefault(row => row.TeamId == detail.TeamBId)?.Won ?? 0;
        var score = $"{aWins}–{bWins}";
        var winner = detail.IsTied ? "Series tied"
            : string.IsNullOrWhiteSpace(detail.WinnerTeamName) ? string.Empty : $"Winner: {detail.WinnerTeamName}";
        return new SeriesListItem
        {
            Id = detail.Id, Name = detail.Name, Status = detail.Status,
            StatusText = detail.StatusText,
            MetaLine = $"{detail.TeamAName} vs {detail.TeamBName} · {detail.MatchCount} matches · {score}",
            WinnerLine = winner
        };
    }

    private async Task<bool> IsBootstrappedAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct).ConfigureAwait(false);
    }

    private async Task OverlayLocalMatchStateAsync(SeriesDetail detail, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var localMatches = await db.Matches.AsNoTracking()
            .Where(m => m.SeriesId == detail.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byId = localMatches.ToDictionary(m => m.Id);
        var byFixture = localMatches
            .Where(m => m.SeriesFixtureId is Guid)
            .GroupBy(m => m.SeriesFixtureId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.UpdatedAtUtc).First());

        if (localMatches.Count > 0)
        {
        var matchIds = localMatches.Select(m => m.Id).ToList();
        var innings = await db.Innings.AsNoTracking()
            .Where(i => matchIds.Contains(i.MatchId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var deliveries = await db.Deliveries.AsNoTracking()
            .Where(d => matchIds.Contains(d.MatchId))
            .Select(d => new { d.InningsId, d.RunsOffBat, d.ExtraRuns, d.IsWicket, d.IsLegalDelivery })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var delsByInn = deliveries
            .GroupBy(d => d.InningsId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => (x.RunsOffBat, x.ExtraRuns, x.IsWicket, x.IsLegalDelivery)).ToList());
        var innsByMatch = innings.GroupBy(i => i.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var f in detail.Fixtures)
        {
            Match? local = null;
            if (f.MatchId is Guid mid && byId.TryGetValue(mid, out var byMatch))
                local = byMatch;
            else if (byFixture.TryGetValue(f.Id, out var byFix))
                local = byFix;
            if (local is null) continue;

            f.MatchId = local.Id;
            f.MatchStatusText = local.Status switch
            {
                MatchStatus.Setup => "Setup",
                MatchStatus.InProgress => "Live",
                MatchStatus.Completed => "Completed",
                MatchStatus.Abandoned => "Abandoned",
                _ => f.MatchStatusText
            };
            if (local.Status == MatchStatus.Completed
                && innsByMatch.TryGetValue(local.Id, out var matchInns))
            {
                var (aRuns, aWkts) = LocalTeamTotals(matchInns, local.TeamAId, delsByInn);
                var (bRuns, bWkts) = LocalTeamTotals(matchInns, local.TeamBId, delsByInn);
                var firstBat = matchInns.OrderBy(i => i.InningsNumber).FirstOrDefault()?.BattingTeamId;
                f.ResultText = LocalResultText(
                    local, f.TeamAName, f.TeamBName, aRuns, aWkts, bRuns, bWkts, firstBat);
            }
            else if (local.Status == MatchStatus.InProgress
                     && innsByMatch.TryGetValue(local.Id, out var liveInns))
            {
                var (aRuns, aWkts) = LocalTeamTotals(liveInns, local.TeamAId, delsByInn);
                var (bRuns, bWkts) = LocalTeamTotals(liveInns, local.TeamBId, delsByInn);
                f.ResultText = $"LIVE · {aRuns}/{aWkts} vs {bRuns}/{bWkts}";
            }
            else if (local.Status == MatchStatus.Abandoned)
            {
                f.ResultText = "No result";
            }
        }

        ApplyLocalStandings(detail, byId, innsByMatch, delsByInn);

        var allDecided = detail.Fixtures.Count > 0 && detail.Fixtures.All(f =>
            string.Equals(f.MatchStatusText, "Completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.MatchStatusText, "Abandoned", StringComparison.OrdinalIgnoreCase));
        if (allDecided)
        {
            var aWins = detail.Standings.FirstOrDefault(s => s.TeamId == detail.TeamAId)?.Won ?? 0;
            var bWins = detail.Standings.FirstOrDefault(s => s.TeamId == detail.TeamBId)?.Won ?? 0;
            detail.ScoreLine = $"{aWins}–{bWins}";
            if (aWins == bWins)
            {
                detail.IsTied = true;
                detail.WinnerTeamId = null;
                detail.WinnerTeamName = "Series tied";
                detail.ScoreLine = $"Series tied {aWins}–{bWins}";
            }
            else
            {
                detail.IsTied = false;
                detail.WinnerTeamId = aWins > bWins ? detail.TeamAId : detail.TeamBId;
                detail.WinnerTeamName = aWins > bWins ? detail.TeamAName : detail.TeamBName;
            }

            if (detail.Status != 3)
            {
                detail.Status = 3;
                detail.StatusText = "Completed";
            }
        }
        }

        var draft = detail.Status == 0;
        var liveId = (await db.Matches.AsNoTracking()
                .Where(m => m.Status == MatchStatus.Setup || m.Status == MatchStatus.InProgress)
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .OrderByDescending(m => m.Status == MatchStatus.InProgress)
            .ThenByDescending(m => m.UpdatedAtUtc)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefault();

        foreach (var f in detail.Fixtures)
        {
            Match? local = null;
            if (f.MatchId is Guid mid && byId.TryGetValue(mid, out var byMatch))
                local = byMatch;
            else if (byFixture.TryGetValue(f.Id, out var byFix))
                local = byFix;

            // Start stays visible even if another match is active; StartFixtureAsync gates that.
            f.CanStart = f.MatchId is null && !draft
                         && detail.Status is not 0 and not 3;
            f.CanResume = local is not null
                          && (local.Status is MatchStatus.Setup or MatchStatus.InProgress)
                          && !draft
                          && liveId == local.Id
                          && ScoringOwnership.IsOwner(local, _auth);
            f.CanOpenScorecard = f.MatchId is Guid && !f.CanResume;
            if (f.CanResume || f.CanOpenScorecard)
                f.CanStart = false;
        }
    }

    private static void ApplyLocalStandings(
        SeriesDetail detail,
        Dictionary<Guid, Match> matchesById,
        Dictionary<Guid, List<Innings>> innsByMatch,
        Dictionary<Guid, List<(int RunsOffBat, int ExtraRuns, bool IsWicket, bool IsLegalDelivery)>> delsByInn)
    {
        var rows = new Dictionary<Guid, SeriesStandingItem>
        {
            [detail.TeamAId] = new() { TeamId = detail.TeamAId, TeamName = detail.TeamAName },
            [detail.TeamBId] = new() { TeamId = detail.TeamBId, TeamName = detail.TeamBName }
        };
        var acc = new Dictionary<Guid, (int P, int W, int L, int T, int Pts, int Rf, int Ra, int Bf, int Ba)>
        {
            [detail.TeamAId] = default,
            [detail.TeamBId] = default
        };

        foreach (var f in detail.Fixtures)
        {
            if (f.MatchId is not Guid mid
                || !matchesById.TryGetValue(mid, out var match)
                || match.Status != MatchStatus.Completed
                || !innsByMatch.TryGetValue(mid, out var inns))
                continue;

            var (aRuns, aBalls) = LocalTeamRunsBalls(inns, detail.TeamAId, delsByInn);
            var (bRuns, bBalls) = LocalTeamRunsBalls(inns, detail.TeamBId, delsByInn);
            var a = acc[detail.TeamAId];
            var b = acc[detail.TeamBId];
            a.P++; b.P++;
            a.Rf += aRuns; a.Ra += bRuns; a.Bf += aBalls; a.Ba += bBalls;
            b.Rf += bRuns; b.Ra += aRuns; b.Bf += bBalls; b.Ba += aBalls;
            if (aRuns == bRuns) { a.T++; b.T++; a.Pts += 1; b.Pts += 1; }
            else if (aRuns > bRuns) { a.W++; b.L++; a.Pts += 2; }
            else { b.W++; a.L++; b.Pts += 2; }
            acc[detail.TeamAId] = a;
            acc[detail.TeamBId] = b;
        }

        double Nrr(int rf, int ra, int bf, int ba)
        {
            var of = bf <= 0 ? 0 : rf / (bf / 6.0);
            var oa = ba <= 0 ? 0 : ra / (ba / 6.0);
            return of - oa;
        }

        var ordered = acc
            .OrderByDescending(kv => kv.Value.Pts)
            .ThenByDescending(kv => kv.Value.W)
            .ThenByDescending(kv => Nrr(kv.Value.Rf, kv.Value.Ra, kv.Value.Bf, kv.Value.Ba))
            .ToList();
        var rank = 1;
        var list = new List<SeriesStandingItem>();
        foreach (var kv in ordered)
        {
            var v = kv.Value;
            list.Add(new SeriesStandingItem
            {
                TeamId = kv.Key,
                TeamName = kv.Key == detail.TeamAId ? detail.TeamAName : detail.TeamBName,
                Rank = rank++,
                Played = v.P,
                Won = v.W,
                Lost = v.L,
                Tied = v.T,
                Points = v.Pts,
                NetRunRate = Math.Round(Nrr(v.Rf, v.Ra, v.Bf, v.Ba), 3)
            });
        }

        detail.Standings = list;
        _ = rows;
    }

    private static (int Runs, int Balls) LocalTeamRunsBalls(
        List<Innings> inns,
        Guid teamId,
        Dictionary<Guid, List<(int RunsOffBat, int ExtraRuns, bool IsWicket, bool IsLegalDelivery)>> delsByInn)
    {
        var runs = 0;
        var balls = 0;
        foreach (var inn in inns.Where(i => i.BattingTeamId == teamId))
        {
            if (!delsByInn.TryGetValue(inn.Id, out var dels)) continue;
            foreach (var d in dels)
            {
                runs += d.RunsOffBat + d.ExtraRuns;
                if (d.IsLegalDelivery) balls++;
            }
        }

        return (runs, balls);
    }

    private static (int Runs, int Wickets) LocalTeamTotals(
        List<Innings> inns,
        Guid teamId,
        Dictionary<Guid, List<(int RunsOffBat, int ExtraRuns, bool IsWicket, bool IsLegalDelivery)>> delsByInn)
    {
        var runs = 0;
        var wickets = 0;
        foreach (var inn in inns.Where(i => i.BattingTeamId == teamId))
        {
            if (!delsByInn.TryGetValue(inn.Id, out var dels)) continue;
            foreach (var d in dels)
            {
                runs += d.RunsOffBat + d.ExtraRuns;
                if (d.IsWicket) wickets++;
            }
        }

        return (runs, wickets);
    }

    private static string LocalResultText(
        Match match, string teamA, string teamB,
        int aRuns, int aWkts, int bRuns, int bWkts, Guid? firstBattingTeamId)
    {
        if (aRuns == bRuns)
            return "Match tied";

        var aWon = aRuns > bRuns;
        var winner = aWon ? teamA : teamB;
        var winnerRuns = aWon ? aRuns : bRuns;
        var loserRuns = aWon ? bRuns : aRuns;

        if (firstBattingTeamId is Guid first)
        {
            var chasingIsA = first == match.TeamBId;
            var chasingWon = chasingIsA ? aWon : !aWon;
            if (chasingWon)
            {
                var roster = MatchRosterPayload.TryParse(match.SquadRosterJson);
                var squadSize = chasingIsA
                    ? roster?.TeamAPlayers.Count ?? 0
                    : roster?.TeamBPlayers.Count ?? 0;
                var chasingWkts = chasingIsA ? aWkts : bWkts;
                var wktsLeft = MatchResultRules.WicketsRemaining(
                    squadSize, chasingWkts, match.LastBatsmanCanBatAsSingleWicket);
                return $"{winner} won by {wktsLeft} wkt{(wktsLeft == 1 ? "" : "s")}";
            }

            var margin = winnerRuns - loserRuns;
            return $"{winner} won by {margin} run{(margin == 1 ? "" : "s")}";
        }

        return $"{winner} won";
    }
}
