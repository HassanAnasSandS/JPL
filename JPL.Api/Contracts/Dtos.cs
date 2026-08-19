namespace JPL.Api.Contracts;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(
    Guid UserId,
    string Username,
    string Role,
    string Token,
    Guid? LeagueId = null,
    bool NeedsLeaguePicker = false);

public sealed record LeagueDto(
    Guid Id,
    string Name,
    string? ImageBase64,
    bool IsActive,
    DateTime CreatedAtUtc);

public sealed record LeagueUpsertRequest(
    string Name,
    string? ImageBase64);

public sealed record UserDto(
    Guid Id,
    string Username,
    string Role,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid? LeagueId = null,
    string? LeagueName = null);

public sealed record UserUpsertRequest(
    Guid? Id,
    string Username,
    string? Password,
    string Role,
    bool IsActive = true,
    Guid? LeagueId = null);

public sealed record PlayerDto(
    Guid Id,
    string FullName,
    long Version,
    DateTime UpdatedAtUtc,
    bool IsDeleted,
    bool IsActive = true);

public sealed record PlayerUpsertRequest(
    Guid? Id,
    string FullName,
    bool IsActive = true);

public sealed record TeamDto(
    Guid Id,
    string Name,
    Guid? CaptainPlayerId,
    IReadOnlyList<Guid> PlayerIds,
    long Version,
    DateTime UpdatedAtUtc,
    bool IsDeleted,
    bool IsActive = true);

public sealed record TeamUpsertRequest(
    Guid? Id,
    string Name,
    Guid? CaptainPlayerId,
    IReadOnlyList<Guid> PlayerIds,
    bool IsActive = true);

public sealed record SyncPushRequest(
    Guid IdempotencyKey,
    string EntityType,
    Guid EntityId,
    string Operation,
    string PayloadJson);

public sealed record SyncPushResponse(bool Accepted, string Message);

/// <summary>Completed match card for MAUI Matches list (server is source of truth).</summary>
public sealed record MatchSummaryDto(
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
    string? PlayerOfTheMatchName = null);

/// <summary>Completed nets session card — list is read-only from server; create/score only via outbox sync.</summary>
public sealed record NetSessionSummaryDto(
    Guid Id,
    string Name,
    DateTime SessionDateUtc,
    byte Status,
    int PlayerCount,
    int Runs,
    int Wickets,
    int Overs,
    int BallsInOver);

public sealed class NetSessionScorecardDto
{
    public Guid SessionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MetaLine { get; set; } = string.Empty;
    public int TotalRuns { get; set; }
    public int Wickets { get; set; }
    public int Extras { get; set; }
    public string OversText { get; set; } = "0.0";
    public List<NetScorecardBattingDto> Batting { get; set; } = new();
    public List<NetScorecardBowlingDto> Bowling { get; set; } = new();
}

public sealed class NetScorecardBattingDto
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Runs { get; set; }
    public int Balls { get; set; }
    public int Fours { get; set; }
    public int Sixes { get; set; }
    public bool IsOut { get; set; }
    public int Outs { get; set; }
    public string Dismissal { get; set; } = string.Empty;
}

public sealed class NetScorecardBowlingDto
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BallsBowled { get; set; }
    public int RunsConceded { get; set; }
    public int Wickets { get; set; }
    public int Maidens { get; set; }
}

public sealed class MatchScorecardDto
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
    public List<InningsScorecardDto> Innings { get; set; } = new();
}

public sealed class HallOfFameDto
{
    public List<TrophyRowDto> PlayerOfTheMatch { get; set; } = new();
    public List<TrophyRowDto> CaptainTrophies { get; set; } = new();
    public List<TrophyRowDto> TournamentChampionTrophies { get; set; } = new();
}

public sealed class TrophyRowDto
{
    public int Rank { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int Trophies { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string RankText => $"#{Rank}";
    public string TrophyText => Trophies == 1 ? "1 trophy" : $"{Trophies} trophies";
}

public sealed class AuctionUpsertRequest
{
    public Guid? Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime AuctionDateUtc { get; set; }
    public string? Notes { get; set; }
    public List<AuctionCaptainRequest> Captains { get; set; } = new();
    public List<AuctionLotRequest> Lots { get; set; } = new();
}

public sealed class AuctionCaptainRequest
{
    public Guid PlayerId { get; set; }
    public int WalletAmount { get; set; }
}

public sealed class AuctionLotRequest
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public int BasePrice { get; set; }
    public bool IsSold { get; set; }
    public int? SoldPrice { get; set; }
    public Guid? SoldToCaptainPlayerId { get; set; }
}

public sealed class AuctionDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime AuctionDateUtc { get; set; }
    public string? Notes { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public List<AuctionCaptainDto> Captains { get; set; } = new();
    public List<AuctionLotDto> Lots { get; set; } = new();
}

public sealed class AuctionCaptainDto
{
    public Guid PlayerId { get; set; }
    public int WalletAmount { get; set; }
}

public sealed class AuctionLotDto
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public int BasePrice { get; set; }
    public bool IsSold { get; set; }
    public int? SoldPrice { get; set; }
    public Guid? SoldToCaptainPlayerId { get; set; }
}

public sealed class AuctionReportRowDto
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int AuctionsParticipated { get; set; }
    public int TimesSold { get; set; }
    public int TimesUnsold { get; set; }
    public int TotalBasePrice { get; set; }
    public int TotalSoldAmount { get; set; }
    public double AverageSoldPrice { get; set; }
    public int HighestSoldPrice { get; set; }
    public int TimesAsCaptain { get; set; }
    public int TotalWalletAssigned { get; set; }
    public int TotalSpentAsCaptain { get; set; }
    public int WalletRemaining { get; set; }
}

public sealed class VenueUpsertRequest
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class VenueDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class VenueBookingUpsertRequest
{
    public Guid? Id { get; set; }
    public Guid VenueId { get; set; }
    public DateTime BookingDateUtc { get; set; }
    public Guid BookedByPlayerId { get; set; }
    public decimal BookingCost { get; set; }
    public List<VenueBookingParticipantRequest> Participants { get; set; } = new();
}

public sealed class VenueBookingParticipantRequest
{
    public Guid PlayerId { get; set; }
    public string Status { get; set; } = "Pending";
}

public sealed class VenueBookingDto
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
    public List<VenueBookingParticipantDto> Participants { get; set; } = new();
}

public sealed class VenueBookingParticipantDto
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
}

public sealed class TournamentUpsertRequest
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GroupCount { get; set; } = 1;
    public int QualifyPerGroup { get; set; } = 1;
    public Guid VenueId { get; set; }
    public byte Format { get; set; }
    public int OversLimit { get; set; } = 8;
    public bool LastBatsmanCanBatAsSingleWicket { get; set; }
    public int BowlerMaxOvers { get; set; }
    public List<TournamentTeamRequest> Teams { get; set; } = new();
}

public sealed class TournamentTeamRequest
{
    public Guid TeamId { get; set; }
    public int GroupNumber { get; set; } = 1;
}

public sealed class TournamentListDto
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

public sealed class TournamentDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GroupCount { get; set; }
    public int QualifyPerGroup { get; set; }
    public Guid VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public byte Format { get; set; }
    public string FormatText { get; set; } = string.Empty;
    public int OversLimit { get; set; }
    public bool LastBatsmanCanBatAsSingleWicket { get; set; }
    public int BowlerMaxOvers { get; set; }
    public byte Status { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public Guid? WinnerTeamId { get; set; }
    public string? WinnerTeamName { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<TournamentTeamDto> Teams { get; set; } = new();
    public List<TournamentFixtureDto> Fixtures { get; set; } = new();
    public List<TournamentStandingDto> Standings { get; set; } = new();
}

public sealed class TournamentTeamDto
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int GroupNumber { get; set; }
}

public sealed class TournamentFixtureDto
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

public sealed class TournamentStandingDto
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

public sealed class TournamentLinkMatchRequest
{
    public Guid MatchId { get; set; }
}

public sealed class InningsScorecardDto
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
    public List<BattingRowDto> Batting { get; set; } = new();
    public List<BowlingRowDto> Bowling { get; set; } = new();
    public string DidNotBatText { get; set; } = string.Empty;
    public string DidNotBowlText { get; set; } = string.Empty;
}

public sealed class BattingRowDto
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Runs { get; set; }
    public int Balls { get; set; }
    public int Fours { get; set; }
    public int Sixes { get; set; }
    public bool IsOut { get; set; }
    public bool IsCaptain { get; set; }
    public string Dismissal { get; set; } = string.Empty;
}

public sealed class BowlingRowDto
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BallsBowled { get; set; }
    public int RunsConceded { get; set; }
    public int Wickets { get; set; }
    public int Maidens { get; set; }
    public bool IsCaptain { get; set; }
}

