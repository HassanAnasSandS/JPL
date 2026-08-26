using System.Net.Http.Json;
using System.Text.Json;

namespace JPL.Services;

public sealed record ApiLoginResult(
    Guid UserId,
    string Username,
    string Role,
    string Token,
    Guid? LeagueId = null,
    bool NeedsLeaguePicker = false);

public sealed record ApiLeagueDto(
    Guid Id,
    string Name,
    string? ImageBase64,
    bool IsActive,
    DateTime CreatedAtUtc,
    int LiveMatchCount = 0,
    int LiveNetsCount = 0);

public sealed record ApiUserDto(
    Guid Id,
    string Username,
    string Role,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid? LeagueId = null,
    string? LeagueName = null);

public sealed record ApiPlayerDto(
    Guid Id,
    string FullName,
    long Version,
    DateTime UpdatedAtUtc,
    bool IsDeleted,
    bool IsActive = true,
    string? NickName = null,
    string? PlayingRole = null,
    string? BattingStyle = null,
    string? BowlingStyle = null,
    string? Bio = null);

public sealed record ApiTeamDto(
    Guid Id,
    string Name,
    Guid? CaptainPlayerId,
    IReadOnlyList<Guid> PlayerIds,
    long Version,
    DateTime UpdatedAtUtc,
    bool IsDeleted,
    bool IsActive,
    IReadOnlyList<Guid>? PlayingPlayerIds = null);

public sealed record ApiMatchSummaryDto(
    Guid Id,
    string Title,
    Guid TeamAId,
    Guid TeamBId,
    string TeamAName,
    string TeamBName,
    byte Status,
    byte Format,
    DateTime MatchDateUtc,
    int OversLimit,
    string? Venue,
    int TeamARuns,
    int TeamAWickets,
    int TeamBRuns,
    int TeamBWickets,
    string ResultText,
    Guid? WinnerTeamId,
    Guid? PlayerOfTheMatchPlayerId = null,
    string? PlayerOfTheMatchName = null,
    byte OversMode = 0,
    Guid? TournamentId = null,
    Guid? SeriesId = null,
    string? LiveStreamUrl = null);

public sealed record ApiNetSessionSummaryDto(
    Guid Id,
    string Name,
    DateTime SessionDateUtc,
    byte Status,
    int PlayerCount,
    int Runs,
    int Wickets,
    int Overs,
    int BallsInOver);

public sealed class HallOfFameBundle
{
    public List<TrophyRow> PlayerOfTheMatch { get; set; } = new();
    public List<TrophyRow> CaptainTrophies { get; set; } = new();
    public List<TrophyRow> TournamentChampionTrophies { get; set; } = new();
    public List<TrophyRow> SeriesChampionTrophies { get; set; } = new();
}

public sealed class TrophyRow
{
    public int Rank { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int Trophies { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string RankText => $"#{Rank}";
    public string TrophyText => Trophies == 1 ? "1 trophy" : $"{Trophies} trophies";
}

public sealed class ApiAuctionDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime AuctionDateUtc { get; set; }
    public string? Notes { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public List<ApiAuctionCaptainDto> Captains { get; set; } = new();
    public List<ApiAuctionLotDto> Lots { get; set; } = new();
}

public sealed class ApiAuctionCaptainDto
{
    public Guid PlayerId { get; set; }
    public int WalletAmount { get; set; }
}

public sealed class ApiAuctionLotDto
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public int BasePrice { get; set; }
    public bool IsSold { get; set; }
    public int? SoldPrice { get; set; }
    public Guid? SoldToCaptainPlayerId { get; set; }
}

public sealed class ApiFormatDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte Format { get; set; }
    public int OversLimit { get; set; }
    public byte OversMode { get; set; }
    public string DetailsLine { get; set; } = string.Empty;
    public string Hint { get; set; } = string.Empty;
    public long Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class ApiVenueDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class ApiVenueBookingDto
{
    public Guid Id { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string CreatedByUsername { get; set; } = string.Empty;
    public Guid VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public DateTime BookingDateUtc { get; set; }
    public Guid BookedByPlayerId { get; set; }
    public string BookedByPlayerName { get; set; } = string.Empty;
    public decimal BookingCost { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public List<ApiVenueBookingParticipantDto> Participants { get; set; } = new();
}

public sealed class ApiVenueBookingParticipantDto
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
}

public sealed class ApiTournamentListDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GroupCount { get; set; }
    public int QualifyPerGroup { get; set; }
    public byte Status { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public int TeamCount { get; set; }
    public int FixtureCount { get; set; }
    public Guid? WinnerTeamId { get; set; }
    public string? WinnerTeamName { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ApiTournamentDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GroupCount { get; set; }
    public int QualifyPerGroup { get; set; }
    public Guid VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public Guid? FormatId { get; set; }
    public byte Format { get; set; }
    public string FormatText { get; set; } = string.Empty;
    public int OversLimit { get; set; }
    public byte OversMode { get; set; }
    public bool LastBatsmanCanBatAsSingleWicket { get; set; }
    public int BowlerMaxOvers { get; set; }
    public byte Status { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public Guid? WinnerTeamId { get; set; }
    public string? WinnerTeamName { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<ApiTournamentTeamDto> Teams { get; set; } = new();
    public List<ApiTournamentFixtureDto> Fixtures { get; set; } = new();
    public List<ApiTournamentStandingDto> Standings { get; set; } = new();
}

public sealed class ApiTournamentTeamDto
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int GroupNumber { get; set; }
}

public sealed class ApiTournamentFixtureDto
{
    public Guid Id { get; set; }
    public byte Stage { get; set; }
    public string StageText { get; set; } = string.Empty;
    public int? GroupNumber { get; set; }
    public int Sequence { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid? TeamAId { get; set; }
    public Guid? TeamBId { get; set; }
    public string TeamAName { get; set; } = string.Empty;
    public string TeamBName { get; set; } = string.Empty;
    public string? TeamASlot { get; set; }
    public string? TeamBSlot { get; set; }
    public Guid? MatchId { get; set; }
    public byte? MatchStatus { get; set; }
    public string MatchStatusText { get; set; } = string.Empty;
    public string ResultText { get; set; } = string.Empty;
    public bool CanStart { get; set; }
    public bool CanResume { get; set; }
    public bool CanOpenScorecard { get; set; }
}

public sealed class ApiTournamentStandingDto
{
    public int GroupNumber { get; set; }
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Tied { get; set; }
    public int Points { get; set; }
    public double NetRunRate { get; set; }
    public int Rank { get; set; }
}

public sealed class ApiSeriesListDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public string TeamAName { get; set; } = string.Empty;
    public string TeamBName { get; set; } = string.Empty;
    public int MatchCount { get; set; }
    public byte Status { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public int FixtureCount { get; set; }
    public int TeamAWins { get; set; }
    public int TeamBWins { get; set; }
    public Guid? WinnerTeamId { get; set; }
    public string? WinnerTeamName { get; set; }
    public bool IsTied { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ApiSeriesDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public string TeamAName { get; set; } = string.Empty;
    public string TeamBName { get; set; } = string.Empty;
    public int MatchCount { get; set; }
    public Guid VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public Guid? FormatId { get; set; }
    public byte Format { get; set; }
    public string FormatText { get; set; } = string.Empty;
    public int OversLimit { get; set; }
    public byte OversMode { get; set; }
    public bool LastBatsmanCanBatAsSingleWicket { get; set; }
    public int BowlerMaxOvers { get; set; }
    public byte Status { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public Guid? WinnerTeamId { get; set; }
    public string? WinnerTeamName { get; set; }
    public bool IsTied { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<ApiSeriesFixtureDto> Fixtures { get; set; } = new();
    public List<ApiSeriesStandingDto> Standings { get; set; } = new();
}

public sealed class ApiSeriesFixtureDto
{
    public Guid Id { get; set; }
    public int Sequence { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public string TeamAName { get; set; } = string.Empty;
    public string TeamBName { get; set; } = string.Empty;
    public Guid? MatchId { get; set; }
    public byte? MatchStatus { get; set; }
    public string MatchStatusText { get; set; } = string.Empty;
    public string ResultText { get; set; } = string.Empty;
    public bool CanStart { get; set; }
    public bool CanResume { get; set; }
    public bool CanOpenScorecard { get; set; }
}

public sealed class ApiSeriesStandingDto
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Tied { get; set; }
    public int Points { get; set; }
    public double NetRunRate { get; set; }
    public int Rank { get; set; }
}

/// <summary>
/// Direct REST for master data (players/teams) and completed match/session lists.
/// Live scoring (matches + nets) uses local SQLite + outbox sync on complete — never POST create.
/// </summary>
public interface IApiClient
{
    Task<bool> PingAsync(CancellationToken ct = default);
    Task<ApiLoginResult?> LoginAsync(string username, string password, CancellationToken ct = default);
    /// <summary>Validates current Bearer session; inactive/deleted account → SessionExpiredException.</summary>
    Task<ApiLoginResult?> GetMeAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ApiUserDto>> GetUsersAsync(CancellationToken ct = default);
    Task<ApiUserDto?> GetUserAsync(Guid id, CancellationToken ct = default);
    Task<ApiUserDto> CreateUserAsync(object body, CancellationToken ct = default);
    Task<ApiUserDto> UpdateUserAsync(Guid id, object body, CancellationToken ct = default);
    Task DeleteUserAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ApiPlayerDto>> GetPlayersAsync(CancellationToken ct = default);
    Task<ApiPlayerDto?> GetPlayerAsync(Guid id, CancellationToken ct = default);
    Task<ApiPlayerDto> CreatePlayerAsync(object body, CancellationToken ct = default);
    Task<ApiPlayerDto> UpdatePlayerAsync(Guid id, object body, CancellationToken ct = default);
    Task DeletePlayerAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetPlayerPhotoIdsAsync(CancellationToken ct = default);
    Task<byte[]?> GetPlayerImageAsync(Guid id, CancellationToken ct = default);
    Task UploadPlayerImageAsync(Guid id, byte[] bytes, string fileName, CancellationToken ct = default);
    Task DeletePlayerImageAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ApiTeamDto>> GetTeamsAsync(CancellationToken ct = default);
    Task<ApiTeamDto?> GetTeamAsync(Guid id, CancellationToken ct = default);
    Task<ApiTeamDto> CreateTeamAsync(object body, CancellationToken ct = default);
    Task<ApiTeamDto> UpdateTeamAsync(Guid id, object body, CancellationToken ct = default);
    Task DeleteTeamAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetTeamPhotoIdsAsync(CancellationToken ct = default);
    Task<byte[]?> GetTeamImageAsync(Guid id, CancellationToken ct = default);
    Task UploadTeamImageAsync(Guid id, byte[] bytes, string fileName, CancellationToken ct = default);
    Task DeleteTeamImageAsync(Guid id, CancellationToken ct = default);

    /// <summary>Completed matches from server (ongoing stays local).</summary>
    Task<IReadOnlyList<ApiMatchSummaryDto>> GetCompletedMatchesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ApiMatchSummaryDto>> GetLiveMatchesAsync(CancellationToken ct = default);
    Task<MatchFullScorecard?> GetMatchScorecardAsync(Guid matchId, CancellationToken ct = default);
    Task<MatchAnalyticsReport?> GetMatchAnalyticsAsync(Guid matchId, CancellationToken ct = default);

    /// <summary>Completed nets sessions from server (live scoring stays local until complete + outbox sync).</summary>
    Task<IReadOnlyList<ApiNetSessionSummaryDto>> GetCompletedNetSessionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ApiNetSessionSummaryDto>> GetLiveNetSessionsAsync(CancellationToken ct = default);
    Task<NetSessionScorecard?> GetNetSessionScorecardAsync(Guid sessionId, CancellationToken ct = default);

    Task<IReadOnlyList<PlayerCareerSummary>> GetStatsLeaderboardAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        string source = "matches",
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default);
    Task<PlayerCareerSummary?> GetPlayerCareerAsync(Guid playerId, string source = "matches", CancellationToken ct = default);
    Task<IReadOnlyList<ConfrontationStat>> GetBattingVsBowlersAsync(Guid playerId, string source = "matches", CancellationToken ct = default);
    Task<IReadOnlyList<ConfrontationStat>> GetBowlingVsBattersAsync(Guid playerId, string source = "matches", CancellationToken ct = default);
    Task<IReadOnlyList<CaptainVsStat>> GetCaptainVsAsync(Guid playerId, CancellationToken ct = default);
    Task<RankingsBundle> GetRankingsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RankingRow>> GetTeamRankingsAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<TeamCareerSummary>> GetTeamLeaderboardAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<TeamVsStat>> GetTeamVsAsync(
        Guid teamId,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default);
    Task<HallOfFameBundle> GetHallOfFameAsync(IReadOnlyList<Guid>? formatIds = null, CancellationToken ct = default);

    Task<IReadOnlyList<ApiFormatDto>> GetFormatsAsync(CancellationToken ct = default);
    Task<ApiFormatDto?> GetFormatAsync(Guid id, CancellationToken ct = default);
    Task<ApiFormatDto> CreateFormatAsync(object body, CancellationToken ct = default);
    Task<ApiFormatDto> UpdateFormatAsync(Guid id, object body, CancellationToken ct = default);
    Task DeleteFormatAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ApiAuctionDto>> GetAuctionsAsync(CancellationToken ct = default);
    Task<ApiAuctionDto> CreateAuctionAsync(object body, CancellationToken ct = default);
    Task<ApiAuctionDto> UpdateAuctionAsync(Guid id, object body, CancellationToken ct = default);
    Task DeleteAuctionAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AuctionReportRow>> GetAuctionReportAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ApiVenueDto>> GetVenuesAsync(CancellationToken ct = default);
    Task<ApiVenueDto?> GetVenueAsync(Guid id, CancellationToken ct = default);
    Task<ApiVenueDto> CreateVenueAsync(object body, CancellationToken ct = default);
    Task<ApiVenueDto> UpdateVenueAsync(Guid id, object body, CancellationToken ct = default);
    Task DeleteVenueAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ApiVenueBookingDto>> GetVenueBookingsAsync(CancellationToken ct = default);
    Task<ApiVenueBookingDto?> GetVenueBookingAsync(Guid id, CancellationToken ct = default);
    Task<ApiVenueBookingDto> CreateVenueBookingAsync(object body, CancellationToken ct = default);
    Task<ApiVenueBookingDto> UpdateVenueBookingAsync(Guid id, object body, CancellationToken ct = default);
    Task DeleteVenueBookingAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ApiTournamentListDto>> GetTournamentsAsync(CancellationToken ct = default);
    Task<ApiTournamentDetailDto?> GetTournamentAsync(Guid id, CancellationToken ct = default);
    Task<ApiTournamentDetailDto> CreateTournamentAsync(object body, CancellationToken ct = default);
    Task<ApiTournamentDetailDto> UpdateTournamentAsync(Guid id, object body, CancellationToken ct = default);
    Task<ApiTournamentDetailDto> GenerateTournamentAsync(Guid id, CancellationToken ct = default);
    Task<ApiTournamentDetailDto> LinkTournamentMatchAsync(Guid id, Guid fixtureId, Guid matchId, CancellationToken ct = default);
    Task DeleteTournamentAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ApiSeriesListDto>> GetSeriesListAsync(CancellationToken ct = default);
    Task<ApiSeriesDetailDto?> GetSeriesAsync(Guid id, CancellationToken ct = default);
    Task<ApiSeriesDetailDto> CreateSeriesAsync(object body, CancellationToken ct = default);
    Task<ApiSeriesDetailDto> UpdateSeriesAsync(Guid id, object body, CancellationToken ct = default);
    Task<ApiSeriesDetailDto> GenerateSeriesAsync(Guid id, CancellationToken ct = default);
    Task<ApiSeriesDetailDto> LinkSeriesMatchAsync(Guid id, Guid fixtureId, Guid matchId, CancellationToken ct = default);
    Task DeleteSeriesAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ApiLeagueDto>> GetLeaguesAsync(CancellationToken ct = default);
    Task<ApiLeagueDto?> GetLeagueAsync(Guid id, CancellationToken ct = default);
    Task<ApiLeagueDto> CreateLeagueAsync(object body, CancellationToken ct = default);
    Task<ApiLeagueDto> UpdateLeagueAsync(Guid id, object body, CancellationToken ct = default);
    Task DeleteLeagueAsync(Guid id, CancellationToken ct = default);
}

public sealed class ApiClient : IApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public ApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            var http = _httpClientFactory.CreateClient(ApiOptions.HttpClientName);
            using var response = await http.GetAsync("api/health", ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public Task<ApiLoginResult?> LoginAsync(string username, string password, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.PostAsJsonAsync(
                    "api/auth/login",
                    new { username, password },
                    JsonOptions,
                    ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<ApiLoginResult>(JsonOptions, ct)
                .ConfigureAwait(false);
        }, ct);

    public Task<ApiLoginResult?> GetMeAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/auth/me", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<ApiLoginResult>(JsonOptions, ct)
                .ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<ApiUserDto>> GetUsersAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/users", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiUserDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiUserDto>)(list ?? new List<ApiUserDto>());
        }, ct);

    public Task<ApiUserDto?> GetUserAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/users/{id:D}", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<ApiUserDto>(JsonOptions, ct)
                .ConfigureAwait(false);
        }, ct);

    public Task<ApiUserDto> CreateUserAsync(object body, CancellationToken ct = default)
        => SendJsonAsync<ApiUserDto>(HttpMethod.Post, "api/users", body, ct);

    public Task<ApiUserDto> UpdateUserAsync(Guid id, object body, CancellationToken ct = default)
        => SendJsonAsync<ApiUserDto>(HttpMethod.Put, $"api/users/{id:D}", body, ct);

    public Task DeleteUserAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.DeleteAsync($"api/users/{id:D}", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<ApiPlayerDto>> GetPlayersAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/players", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiPlayerDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiPlayerDto>)(list ?? new List<ApiPlayerDto>());
        }, ct);

    public Task<ApiPlayerDto?> GetPlayerAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/players/{id:D}", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<ApiPlayerDto>(JsonOptions, ct)
                .ConfigureAwait(false);
        }, ct);

    public Task<ApiPlayerDto> CreatePlayerAsync(object body, CancellationToken ct = default)
        => SendJsonAsync<ApiPlayerDto>(HttpMethod.Post, "api/players", body, ct);

    public Task<ApiPlayerDto> UpdatePlayerAsync(Guid id, object body, CancellationToken ct = default)
        => SendJsonAsync<ApiPlayerDto>(HttpMethod.Put, $"api/players/{id:D}", body, ct);

    public Task DeletePlayerAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.DeleteAsync($"api/players/{id:D}", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<Guid>> GetPlayerPhotoIdsAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/players/photos", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<Guid>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<Guid>)(list ?? new List<Guid>());
        }, ct);

    public Task<byte[]?> GetPlayerImageAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/players/{id:D}/image", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }, ct);

    public Task UploadPlayerImageAsync(Guid id, byte[] bytes, string fileName, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var content = new MultipartFormDataContent();
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(part, "file", string.IsNullOrWhiteSpace(fileName) ? "photo.jpg" : fileName);
            using var response = await http.PostAsync($"api/players/{id:D}/image", content, ct)
                .ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException(
                    "Photo upload is not on the server yet. Publish the latest API, then try again.");
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    public Task DeletePlayerImageAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.DeleteAsync($"api/players/{id:D}/image", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<ApiTeamDto>> GetTeamsAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/teams", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiTeamDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiTeamDto>)(list ?? new List<ApiTeamDto>());
        }, ct);

    public Task<ApiTeamDto?> GetTeamAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/teams/{id:D}", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<ApiTeamDto>(JsonOptions, ct)
                .ConfigureAwait(false);
        }, ct);

    public Task<ApiTeamDto> CreateTeamAsync(object body, CancellationToken ct = default)
        => SendJsonAsync<ApiTeamDto>(HttpMethod.Post, "api/teams", body, ct);

    public Task<ApiTeamDto> UpdateTeamAsync(Guid id, object body, CancellationToken ct = default)
        => SendJsonAsync<ApiTeamDto>(HttpMethod.Put, $"api/teams/{id:D}", body, ct);

    public Task DeleteTeamAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.DeleteAsync($"api/teams/{id:D}", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<Guid>> GetTeamPhotoIdsAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/teams/photos", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<Guid>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<Guid>)(list ?? new List<Guid>());
        }, ct);

    public Task<byte[]?> GetTeamImageAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/teams/{id:D}/image", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }, ct);

    public Task UploadTeamImageAsync(Guid id, byte[] bytes, string fileName, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var content = new MultipartFormDataContent();
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(part, "file", string.IsNullOrWhiteSpace(fileName) ? "logo.jpg" : fileName);
            using var response = await http.PostAsync($"api/teams/{id:D}/image", content, ct)
                .ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException(
                    "Logo upload is not on the server yet. Publish the latest API, then try again.");
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    public Task DeleteTeamImageAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.DeleteAsync($"api/teams/{id:D}/image", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<ApiMatchSummaryDto>> GetCompletedMatchesAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/matches", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiMatchSummaryDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiMatchSummaryDto>)(list ?? new List<ApiMatchSummaryDto>());
        }, ct);

    public Task<IReadOnlyList<ApiMatchSummaryDto>> GetLiveMatchesAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/live/matches", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiMatchSummaryDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiMatchSummaryDto>)(list ?? new List<ApiMatchSummaryDto>());
        }, ct);

    public Task<MatchFullScorecard?> GetMatchScorecardAsync(Guid matchId, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/matches/{matchId:D}/scorecard", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<MatchFullScorecard>(JsonOptions, ct).ConfigureAwait(false);
        }, ct);

    public Task<MatchAnalyticsReport?> GetMatchAnalyticsAsync(Guid matchId, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/matches/{matchId:D}/analytics", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<MatchAnalyticsReport>(JsonOptions, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<ApiNetSessionSummaryDto>> GetCompletedNetSessionsAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/netsessions", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiNetSessionSummaryDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiNetSessionSummaryDto>)(list ?? new List<ApiNetSessionSummaryDto>());
        }, ct);

    public Task<IReadOnlyList<ApiNetSessionSummaryDto>> GetLiveNetSessionsAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/live/nets", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiNetSessionSummaryDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiNetSessionSummaryDto>)(list ?? new List<ApiNetSessionSummaryDto>());
        }, ct);

    public Task<NetSessionScorecard?> GetNetSessionScorecardAsync(Guid sessionId, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/netsessions/{sessionId:D}/scorecard", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<NetSessionScorecard>(JsonOptions, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<PlayerCareerSummary>> GetStatsLeaderboardAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        string source = "matches",
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default)
        => RunAsync(async http =>
        {
            var query = new List<string>();
            if (fromUtc is DateTime from)
                query.Add($"fromUtc={Uri.EscapeDataString(from.ToString("O"))}");
            if (toExclusiveUtc is DateTime to)
                query.Add($"toExclusiveUtc={Uri.EscapeDataString(to.ToString("O"))}");
            if (venueId is Guid vid && vid != Guid.Empty)
                query.Add($"venueId={vid:D}");
            if (!string.IsNullOrWhiteSpace(source)
                && !string.Equals(source, "matches", StringComparison.OrdinalIgnoreCase))
                query.Add($"source={Uri.EscapeDataString(source)}");
            AppendFormatIds(query, formatIds);
            var url = "api/stats/leaderboard" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<PlayerCareerSummary>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<PlayerCareerSummary>)(list ?? new List<PlayerCareerSummary>());
        }, ct);

    public Task<PlayerCareerSummary?> GetPlayerCareerAsync(Guid playerId, string source = "matches", CancellationToken ct = default)
        => RunAsync(async http =>
        {
            var url = $"api/stats/players/{playerId:D}";
            if (!string.IsNullOrWhiteSpace(source)
                && !string.Equals(source, "matches", StringComparison.OrdinalIgnoreCase))
                url += $"?source={Uri.EscapeDataString(source)}";
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<PlayerCareerSummary>(JsonOptions, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<ConfrontationStat>> GetBattingVsBowlersAsync(Guid playerId, string source = "matches", CancellationToken ct = default)
        => RunAsync(async http =>
        {
            var url = $"api/stats/confrontations/{playerId:D}/vs-bowlers";
            if (!string.IsNullOrWhiteSpace(source)
                && !string.Equals(source, "matches", StringComparison.OrdinalIgnoreCase))
                url += $"?source={Uri.EscapeDataString(source)}";
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ConfrontationStat>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ConfrontationStat>)(list ?? new List<ConfrontationStat>());
        }, ct);

    public Task<IReadOnlyList<ConfrontationStat>> GetBowlingVsBattersAsync(Guid playerId, string source = "matches", CancellationToken ct = default)
        => RunAsync(async http =>
        {
            var url = $"api/stats/confrontations/{playerId:D}/vs-batters";
            if (!string.IsNullOrWhiteSpace(source)
                && !string.Equals(source, "matches", StringComparison.OrdinalIgnoreCase))
                url += $"?source={Uri.EscapeDataString(source)}";
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ConfrontationStat>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ConfrontationStat>)(list ?? new List<ConfrontationStat>());
        }, ct);

    public Task<IReadOnlyList<CaptainVsStat>> GetCaptainVsAsync(Guid playerId, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/stats/confrontations/{playerId:D}/vs-captains", ct)
                .ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (IReadOnlyList<CaptainVsStat>)Array.Empty<CaptainVsStat>();
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<CaptainVsStat>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<CaptainVsStat>)(list ?? new List<CaptainVsStat>());
        }, ct);

    public Task<RankingsBundle> GetRankingsAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/stats/rankings", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var board = await GetStatsLeaderboardAsync(ct: ct).ConfigureAwait(false);
                return RankingsBuilder.FromLeaderboard(board);
            }

            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var dto = await response.Content.ReadFromJsonAsync<RankingsBundle>(JsonOptions, ct).ConfigureAwait(false);
            return dto ?? new RankingsBundle();
        }, ct);

    public Task<IReadOnlyList<RankingRow>> GetTeamRankingsAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default)
        => RunAsync(async http =>
        {
            var query = new List<string>();
            if (fromUtc is DateTime from)
                query.Add($"fromUtc={Uri.EscapeDataString(from.ToString("O"))}");
            if (toExclusiveUtc is DateTime to)
                query.Add($"toExclusiveUtc={Uri.EscapeDataString(to.ToString("O"))}");
            AppendFormatIds(query, formatIds);
            var url = "api/stats/team-rankings" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (IReadOnlyList<RankingRow>)Array.Empty<RankingRow>();
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<RankingRow>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<RankingRow>)(list ?? new List<RankingRow>());
        }, ct);

    public Task<IReadOnlyList<TeamCareerSummary>> GetTeamLeaderboardAsync(
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default)
        => RunAsync(async http =>
        {
            var query = new List<string>();
            if (fromUtc is DateTime from)
                query.Add($"fromUtc={Uri.EscapeDataString(from.ToString("O"))}");
            if (toExclusiveUtc is DateTime to)
                query.Add($"toExclusiveUtc={Uri.EscapeDataString(to.ToString("O"))}");
            if (venueId is Guid vid && vid != Guid.Empty)
                query.Add($"venueId={vid:D}");
            AppendFormatIds(query, formatIds);
            var url = "api/stats/teams" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (IReadOnlyList<TeamCareerSummary>)Array.Empty<TeamCareerSummary>();
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<TeamCareerSummary>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<TeamCareerSummary>)(list ?? new List<TeamCareerSummary>());
        }, ct);

    public Task<IReadOnlyList<TeamVsStat>> GetTeamVsAsync(
        Guid teamId,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        IReadOnlyList<Guid>? formatIds = null,
        CancellationToken ct = default)
        => RunAsync(async http =>
        {
            var query = new List<string>();
            if (fromUtc is DateTime from)
                query.Add($"fromUtc={Uri.EscapeDataString(from.ToString("O"))}");
            if (toExclusiveUtc is DateTime to)
                query.Add($"toExclusiveUtc={Uri.EscapeDataString(to.ToString("O"))}");
            if (venueId is Guid vid && vid != Guid.Empty)
                query.Add($"venueId={vid:D}");
            AppendFormatIds(query, formatIds);
            var url = $"api/stats/teams/{teamId:D}/vs" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (IReadOnlyList<TeamVsStat>)Array.Empty<TeamVsStat>();
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<TeamVsStat>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<TeamVsStat>)(list ?? new List<TeamVsStat>());
        }, ct);

    public Task<HallOfFameBundle> GetHallOfFameAsync(IReadOnlyList<Guid>? formatIds = null, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            var query = new List<string>();
            AppendFormatIds(query, formatIds);
            var url = "api/hall-of-fame" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var dto = await response.Content.ReadFromJsonAsync<HallOfFameBundle>(JsonOptions, ct).ConfigureAwait(false);
            return dto ?? new HallOfFameBundle();
        }, ct);

    public Task<IReadOnlyList<ApiFormatDto>> GetFormatsAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/formats", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiFormatDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiFormatDto>)(list ?? new List<ApiFormatDto>());
        }, ct);

    public Task<ApiFormatDto?> GetFormatAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/formats/{id:D}", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<ApiFormatDto>(JsonOptions, ct)
                .ConfigureAwait(false);
        }, ct);

    public Task<ApiFormatDto> CreateFormatAsync(object body, CancellationToken ct = default)
        => SendJsonAsync<ApiFormatDto>(HttpMethod.Post, "api/formats", body, ct);

    public Task<ApiFormatDto> UpdateFormatAsync(Guid id, object body, CancellationToken ct = default)
        => SendJsonAsync<ApiFormatDto>(HttpMethod.Put, $"api/formats/{id:D}", body, ct);

    public Task DeleteFormatAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.DeleteAsync($"api/formats/{id:D}", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<ApiAuctionDto>> GetAuctionsAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/auctions", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiAuctionDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiAuctionDto>)(list ?? new List<ApiAuctionDto>());
        }, ct);

    public Task<ApiAuctionDto> CreateAuctionAsync(object body, CancellationToken ct = default)
        => SendJsonAsync<ApiAuctionDto>(HttpMethod.Post, "api/auctions", body, ct);

    public Task<ApiAuctionDto> UpdateAuctionAsync(Guid id, object body, CancellationToken ct = default)
        => SendJsonAsync<ApiAuctionDto>(HttpMethod.Put, $"api/auctions/{id:D}", body, ct);

    public Task DeleteAuctionAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.DeleteAsync($"api/auctions/{id:D}", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<AuctionReportRow>> GetAuctionReportAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/auctions/report", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<AuctionReportRow>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<AuctionReportRow>)(list ?? new List<AuctionReportRow>());
        }, ct);

    public Task<IReadOnlyList<ApiVenueDto>> GetVenuesAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/venues", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiVenueDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiVenueDto>)(list ?? new List<ApiVenueDto>());
        }, ct);

    public Task<ApiVenueDto?> GetVenueAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/venues/{id:D}", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<ApiVenueDto>(JsonOptions, ct)
                .ConfigureAwait(false);
        }, ct);

    public Task<ApiVenueDto> CreateVenueAsync(object body, CancellationToken ct = default)
        => SendJsonAsync<ApiVenueDto>(HttpMethod.Post, "api/venues", body, ct);

    public Task<ApiVenueDto> UpdateVenueAsync(Guid id, object body, CancellationToken ct = default)
        => SendJsonAsync<ApiVenueDto>(HttpMethod.Put, $"api/venues/{id:D}", body, ct);

    public Task DeleteVenueAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.DeleteAsync($"api/venues/{id:D}", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<ApiVenueBookingDto>> GetVenueBookingsAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/venuebookings", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiVenueBookingDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiVenueBookingDto>)(list ?? new List<ApiVenueBookingDto>());
        }, ct);

    public Task<ApiVenueBookingDto?> GetVenueBookingAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/venuebookings/{id:D}", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<ApiVenueBookingDto>(JsonOptions, ct)
                .ConfigureAwait(false);
        }, ct);

    public Task<ApiVenueBookingDto> CreateVenueBookingAsync(object body, CancellationToken ct = default)
        => SendJsonAsync<ApiVenueBookingDto>(HttpMethod.Post, "api/venuebookings", body, ct);

    public Task<ApiVenueBookingDto> UpdateVenueBookingAsync(Guid id, object body, CancellationToken ct = default)
        => SendJsonAsync<ApiVenueBookingDto>(HttpMethod.Put, $"api/venuebookings/{id:D}", body, ct);

    public Task DeleteVenueBookingAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.DeleteAsync($"api/venuebookings/{id:D}", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<ApiTournamentListDto>> GetTournamentsAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/tournaments", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiTournamentListDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiTournamentListDto>)(list ?? new List<ApiTournamentListDto>());
        }, ct);

    public Task<ApiTournamentDetailDto?> GetTournamentAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/tournaments/{id:D}", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<ApiTournamentDetailDto>(JsonOptions, ct)
                .ConfigureAwait(false);
        }, ct);

    public Task<ApiTournamentDetailDto> CreateTournamentAsync(object body, CancellationToken ct = default)
        => SendJsonAsync<ApiTournamentDetailDto>(HttpMethod.Post, "api/tournaments", body, ct);

    public Task<ApiTournamentDetailDto> UpdateTournamentAsync(Guid id, object body, CancellationToken ct = default)
        => SendJsonAsync<ApiTournamentDetailDto>(HttpMethod.Put, $"api/tournaments/{id:D}", body, ct);

    public Task<ApiTournamentDetailDto> GenerateTournamentAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.PostAsync($"api/tournaments/{id:D}/generate", null, ct)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<ApiTournamentDetailDto>(JsonOptions, ct)
                       .ConfigureAwait(false)
                   ?? throw new InvalidOperationException("Empty API response.");
        }, ct);

    public Task<ApiTournamentDetailDto> LinkTournamentMatchAsync(
        Guid id, Guid fixtureId, Guid matchId, CancellationToken ct = default)
        => SendJsonAsync<ApiTournamentDetailDto>(
            HttpMethod.Post,
            $"api/tournaments/{id:D}/fixtures/{fixtureId:D}/link-match",
            new { matchId },
            ct);

    public Task DeleteTournamentAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.DeleteAsync($"api/tournaments/{id:D}", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<ApiSeriesListDto>> GetSeriesListAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/series", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiSeriesListDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiSeriesListDto>)(list ?? new List<ApiSeriesListDto>());
        }, ct);

    public Task<ApiSeriesDetailDto?> GetSeriesAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/series/{id:D}", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<ApiSeriesDetailDto>(JsonOptions, ct)
                .ConfigureAwait(false);
        }, ct);

    public Task<ApiSeriesDetailDto> CreateSeriesAsync(object body, CancellationToken ct = default)
        => SendJsonAsync<ApiSeriesDetailDto>(HttpMethod.Post, "api/series", body, ct);

    public Task<ApiSeriesDetailDto> UpdateSeriesAsync(Guid id, object body, CancellationToken ct = default)
        => SendJsonAsync<ApiSeriesDetailDto>(HttpMethod.Put, $"api/series/{id:D}", body, ct);

    public Task<ApiSeriesDetailDto> GenerateSeriesAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.PostAsync($"api/series/{id:D}/generate", null, ct)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<ApiSeriesDetailDto>(JsonOptions, ct)
                       .ConfigureAwait(false)
                   ?? throw new InvalidOperationException("Empty API response.");
        }, ct);

    public Task<ApiSeriesDetailDto> LinkSeriesMatchAsync(
        Guid id, Guid fixtureId, Guid matchId, CancellationToken ct = default)
        => SendJsonAsync<ApiSeriesDetailDto>(
            HttpMethod.Post,
            $"api/series/{id:D}/fixtures/{fixtureId:D}/link-match",
            new { matchId },
            ct);

    public Task DeleteSeriesAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.DeleteAsync($"api/series/{id:D}", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    public Task<IReadOnlyList<ApiLeagueDto>> GetLeaguesAsync(CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync("api/leagues", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var list = await response.Content.ReadFromJsonAsync<List<ApiLeagueDto>>(JsonOptions, ct)
                .ConfigureAwait(false);
            return (IReadOnlyList<ApiLeagueDto>)(list ?? new List<ApiLeagueDto>());
        }, ct);

    public Task<ApiLeagueDto?> GetLeagueAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.GetAsync($"api/leagues/{id:D}", ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<ApiLeagueDto>(JsonOptions, ct).ConfigureAwait(false);
        }, ct);

    public Task<ApiLeagueDto> CreateLeagueAsync(object body, CancellationToken ct = default)
        => SendJsonAsync<ApiLeagueDto>(HttpMethod.Post, "api/leagues", body, ct);

    public Task<ApiLeagueDto> UpdateLeagueAsync(Guid id, object body, CancellationToken ct = default)
        => SendJsonAsync<ApiLeagueDto>(HttpMethod.Put, $"api/leagues/{id:D}", body, ct);

    public Task DeleteLeagueAsync(Guid id, CancellationToken ct = default)
        => RunAsync(async http =>
        {
            using var response = await http.DeleteAsync($"api/leagues/{id:D}", ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        }, ct);

    private static void AppendFormatIds(List<string> query, IReadOnlyList<Guid>? formatIds)
    {
        if (formatIds is null)
            return;
        foreach (var id in formatIds)
        {
            if (id != Guid.Empty)
                query.Add($"formatIds={id:D}");
        }
    }

    private Task<T> SendJsonAsync<T>(HttpMethod method, string url, object body, CancellationToken ct)
        => RunAsync(async http =>
        {
            using var request = new HttpRequestMessage(method, url)
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            };
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
            return result ?? throw new InvalidOperationException("Empty API response.");
        }, ct);

    private async Task<T> RunAsync<T>(Func<HttpClient, Task<T>> work, CancellationToken ct)
    {
        try
        {
            // Do not trust Connectivity.NetworkAccess here — on Windows it often reports
            // "no internet" even when HTTPS / LAN works, which blocked Statistics entirely.
            var http = _httpClientFactory.CreateClient(ApiOptions.HttpClientName);
            return await work(http).ConfigureAwait(false);
        }
        catch (SessionExpiredException)
        {
            throw;
        }
        catch (OfflineException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // HttpClient.Timeout throws TaskCanceledException without cancelling `ct`.
            throw new OfflineException(NetworkUi.OfflineMessage, ex);
        }
        catch (Exception ex) when (NetworkUi.IsNetworkFailure(ex))
        {
            throw new OfflineException(NetworkUi.OfflineMessage, ex);
        }
    }

    private async Task RunAsync(Func<HttpClient, Task> work, CancellationToken ct)
    {
        await RunAsync(async http =>
        {
            await work(http).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var code = (int)response.StatusCode;
        var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var reason = string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase : detail.Trim();
        if (reason is { Length: >= 2 } && reason[0] == '"' && reason[^1] == '"')
            reason = reason[1..^1];
        if (code is 502 or 503 or 504)
            throw new OfflineException(NetworkUi.OfflineMessage);

        throw new HttpRequestException($"API {code}: {reason}");
    }
}
