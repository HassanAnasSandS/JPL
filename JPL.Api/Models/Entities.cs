namespace JPL.Api.Models;

public enum AppRole : byte
{
    User = 0,
    Admin = 1,
    SuperAdmin = 2,
    GroundOwner = 3,
    PublicUser = 4,
    Guest = 5
}

public sealed class League
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public byte[]? ImageBytes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    /// <summary>Auto-created SQL catalog or SQLite file. Null = share the default database.</summary>
    public string? DatabaseName { get; set; }
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public AppRole Role { get; set; } = AppRole.User;
    public Guid? LeagueId { get; set; }
    public string? WhatsAppNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
}

public sealed class Player
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? NickName { get; set; }
    public string? PlayingRole { get; set; }
    public string? BattingStyle { get; set; }
    public string? BowlingStyle { get; set; }
    public string? Bio { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    /// <summary>Relative filename under the persistent player-images folder. Null = no photo.</summary>
    public string? ImagePath { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public ICollection<TeamPlayer> TeamPlayers { get; set; } = new List<TeamPlayer>();
}

public sealed class Team
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? CaptainPlayerId { get; set; }
    public Player? Captain { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    /// <summary>Relative filename under the persistent team-images folder. Null = no logo.</summary>
    public string? ImagePath { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public ICollection<TeamPlayer> TeamPlayers { get; set; } = new List<TeamPlayer>();
}

public sealed class TeamPlayer
{
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;
    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public DateTime JoinedAtUtc { get; set; } = DateTime.Now;
    /// <summary>True = in the current playing list for the next match. False = squad/bench only.</summary>
    public bool IsPlaying { get; set; } = true;
}

public sealed class SyncReceipt
{
    public Guid IdempotencyKey { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.Now;
}

public enum MatchStatus : byte { Setup = 0, InProgress = 1, Completed = 2, Abandoned = 3 }
public enum MatchFormat : byte { SingleInning = 0, DoubleInnings = 1 }
public enum OversMode : byte { PerInnings = 0, MatchTotal = 1, Unlimited = 2 }
public enum ExtraType : byte { None = 0, Wide = 1, NoBall = 2, Bye = 3, LegBye = 4 }
public enum WicketType : byte
{
    None = 0, Bowled = 1, Caught = 2, Lbw = 3, RunOut = 4, Stumped = 5, HitWicket = 6, Retired = 7, Other = 8
}

public sealed class Match
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public Guid? TeamACaptainId { get; set; }
    public Guid? TeamBCaptainId { get; set; }
    public int OversLimit { get; set; } = 20;
    public OversMode OversMode { get; set; } = OversMode.PerInnings;
    public MatchFormat Format { get; set; } = MatchFormat.SingleInning;
    public Guid? FormatId { get; set; }
    public DateTime MatchDateUtc { get; set; } = DateTime.Now;
    public string? Venue { get; set; }
    public MatchStatus Status { get; set; }
    public Guid? TossWinnerTeamId { get; set; }
    public bool TossWinnerBatsFirst { get; set; } = true;
    public bool LastBatsmanCanBatAsSingleWicket { get; set; }
    public int BowlerMaxOvers { get; set; }
    public Guid? PlayerOfTheMatchPlayerId { get; set; }
    public Guid? TournamentId { get; set; }
    public Guid? TournamentFixtureId { get; set; }
    public Guid? SeriesId { get; set; }
    public Guid? SeriesFixtureId { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
    /// <summary>Optional playing XI snapshot for this match. When set, scorecard Did-not-bat uses it instead of the live team roster.</summary>
    public string? SquadRosterJson { get; set; }
    /// <summary>When true, this match is published ball-by-ball for live score/stream. When false it arrives on complete-sync only.</summary>
    public bool IsLiveTelecast { get; set; } = true;

    /// <summary>Optional YouTube/Facebook/https live telecast URL shown in an iframe with the live score.</summary>
    public string? LiveStreamUrl { get; set; }
    public Guid? StartedByUserId { get; set; }
    public Guid? StartedOnDeviceId { get; set; }
    public ICollection<Innings> Innings { get; set; } = new List<Innings>();
}

public sealed class Innings
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public int InningsNumber { get; set; } = 1;
    public Guid BattingTeamId { get; set; }
    public Guid BowlingTeamId { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsFollowOn { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.Now;
    public ICollection<Delivery> Deliveries { get; set; } = new List<Delivery>();
}

public sealed class Delivery
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid InningsId { get; set; }
    public Innings Innings { get; set; } = null!;
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
    public bool IsLegalDelivery { get; set; } = true;
    public byte ShotZone { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public long Version { get; set; } = 1;
}

public sealed class Auction
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime AuctionDateUtc { get; set; } = DateTime.Now;
    public string? Notes { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
    public ICollection<AuctionCaptain> Captains { get; set; } = new List<AuctionCaptain>();
    public ICollection<AuctionLot> Lots { get; set; } = new List<AuctionLot>();
}

public sealed class AuctionCaptain
{
    public Guid AuctionId { get; set; }
    public Auction Auction { get; set; } = null!;
    public Guid PlayerId { get; set; }
    public int WalletAmount { get; set; }
}

public sealed class AuctionLot
{
    public Guid Id { get; set; }
    public Guid AuctionId { get; set; }
    public Auction Auction { get; set; } = null!;
    public Guid PlayerId { get; set; }
    public int BasePrice { get; set; }
    public int? SoldPrice { get; set; }
    public Guid? SoldToCaptainPlayerId { get; set; }
    public bool IsSold { get; set; }
}

public sealed class Venue
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
}

public sealed class Format
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MatchFormat Kind { get; set; } = MatchFormat.SingleInning;
    public int OversLimit { get; set; } = 8;
    public OversMode OversMode { get; set; } = OversMode.MatchTotal;
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
}

public static class VenueBookingPaymentStatus
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";

    public static bool IsValid(string? status)
        => string.Equals(status, Pending, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, Paid, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? status)
        => string.Equals(status, Paid, StringComparison.OrdinalIgnoreCase) ? Paid : Pending;
}

public sealed class VenueBooking
{
    public Guid Id { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid VenueId { get; set; }
    public DateTime BookingDateUtc { get; set; } = DateTime.Now;
    public Guid BookedByPlayerId { get; set; }
    public decimal BookingCost { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
    public ICollection<VenueBookingParticipant> Participants { get; set; } = new List<VenueBookingParticipant>();
}

public sealed class VenueBookingParticipant
{
    public Guid VenueBookingId { get; set; }
    public VenueBooking VenueBooking { get; set; } = null!;
    public Guid PlayerId { get; set; }
    public string Status { get; set; } = VenueBookingPaymentStatus.Pending;
}

public enum TournamentStatus : byte
{
    Draft = 0,
    Scheduled = 1,
    InProgress = 2,
    Completed = 3
}

public enum TournamentFixtureStage : byte
{
    Group = 0,
    SemiFinal = 1,
    Final = 2
}

public sealed class Tournament
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GroupCount { get; set; } = 1;
    public int QualifyPerGroup { get; set; } = 1;
    /// <summary>Venue applied to every fixture match (required).</summary>
    public Guid VenueId { get; set; }
    public Guid? FormatId { get; set; }
    public byte Format { get; set; } // MatchFormat
    public int OversLimit { get; set; } = 8;
    public OversMode OversMode { get; set; } = OversMode.PerInnings;
    public bool LastBatsmanCanBatAsSingleWicket { get; set; }
    public int BowlerMaxOvers { get; set; }
    public TournamentStatus Status { get; set; } = TournamentStatus.Draft;
    public Guid? WinnerTeamId { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
    public ICollection<TournamentTeam> Teams { get; set; } = new List<TournamentTeam>();
    public ICollection<TournamentFixture> Fixtures { get; set; } = new List<TournamentFixture>();
}

public sealed class TournamentTeam
{
    public Guid TournamentId { get; set; }
    public Tournament Tournament { get; set; } = null!;
    public Guid TeamId { get; set; }
    public int GroupNumber { get; set; } = 1;
}

public sealed class TournamentFixture
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public Tournament Tournament { get; set; } = null!;
    public TournamentFixtureStage Stage { get; set; } = TournamentFixtureStage.Group;
    public int? GroupNumber { get; set; }
    public int Sequence { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid? TeamAId { get; set; }
    public Guid? TeamBId { get; set; }
    public string? TeamASlot { get; set; }
    public string? TeamBSlot { get; set; }
    public Guid? MatchId { get; set; }
}

public enum SeriesStatus : byte
{
    Draft = 0,
    Scheduled = 1,
    InProgress = 2,
    Completed = 3
}

public sealed class Series
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public int MatchCount { get; set; } = 3;
    public Guid VenueId { get; set; }
    public Guid? FormatId { get; set; }
    public byte Format { get; set; }
    public int OversLimit { get; set; } = 8;
    public OversMode OversMode { get; set; } = OversMode.PerInnings;
    public bool LastBatsmanCanBatAsSingleWicket { get; set; }
    public int BowlerMaxOvers { get; set; }
    public SeriesStatus Status { get; set; } = SeriesStatus.Draft;
    public Guid? WinnerTeamId { get; set; }
    public bool IsTied { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
    public ICollection<SeriesFixture> Fixtures { get; set; } = new List<SeriesFixture>();
}

public sealed class SeriesFixture
{
    public Guid Id { get; set; }
    public Guid SeriesId { get; set; }
    public Series Series { get; set; } = null!;
    public int Sequence { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid TeamAId { get; set; }
    public Guid TeamBId { get; set; }
    public Guid? MatchId { get; set; }
}

public enum NetSessionStatus : byte
{
    Setup = 0,
    InProgress = 1,
    Completed = 2
}

public sealed class NetSession
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime SessionDateUtc { get; set; } = DateTime.Now;
    public NetSessionStatus Status { get; set; }
    public string? PlayerRosterJson { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
    public ICollection<NetDelivery> Deliveries { get; set; } = new List<NetDelivery>();
}

public sealed class NetDelivery
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public NetSession Session { get; set; } = null!;
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
    public bool IsLegalDelivery { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public long Version { get; set; } = 1;
}
