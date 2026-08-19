using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Data;
using JPL.Api.Models;

namespace JPL.Api.Services;

public sealed class PlayerCareerDto
{
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    /// <summary>Completed nets sessions this player attended (roster or on a ball).</summary>
    public int Sessions { get; set; }
    public int Matches { get; set; }
    public int Won { get; set; }
    public int Loss { get; set; }
    public int Tied { get; set; }
    public int CaptainMatches { get; set; }
    public int CaptainWon { get; set; }
    public int CaptainLoss { get; set; }
    public int CaptainTied { get; set; }

    // Tournament (completed only) — same shape as Match
    public int Tournaments { get; set; }
    public int TournamentWon { get; set; }
    public int TournamentLoss { get; set; }
    public int TournamentTied { get; set; }
    public int CaptainTournaments { get; set; }
    public int CaptainTournamentWon { get; set; }
    public int CaptainTournamentLoss { get; set; }
    public int CaptainTournamentTied { get; set; }

    public int InningsBatted { get; set; }
    public int BallsFaced { get; set; }
    public int Runs { get; set; }
    public int Dots { get; set; }
    public int Ones { get; set; }
    public int Twos { get; set; }
    public int Threes { get; set; }
    public int Fours { get; set; }
    public int Fives { get; set; }
    public int Sixes { get; set; }
    public int WidesFaced { get; set; }
    public int NoBallsFaced { get; set; }
    public int Dismissals { get; set; }
    public int BowledOut { get; set; }
    public int CaughtOut { get; set; }
    public int LbwOut { get; set; }
    public int RunOut { get; set; }
    public int StumpedOut { get; set; }
    public int HitWicketOut { get; set; }
    public int RetiredOut { get; set; }
    public int OtherOut { get; set; }
    public int OversBowledBalls { get; set; }
    public int RunsConceded { get; set; }
    public int Wickets { get; set; }
    public int DotBallsBowled { get; set; }
    public int WidesBowled { get; set; }
    public int NoBallsBowled { get; set; }
    public int ByesConceded { get; set; }
    public int LegByesConceded { get; set; }
    public int BowledWkts { get; set; }
    public int CaughtWkts { get; set; }
    public int LbwWkts { get; set; }
    public int StumpedWkts { get; set; }
    public int HitWicketWkts { get; set; }
    public int RunOutWkts { get; set; }
    public int Catches { get; set; }
    public int RunOutsEffected { get; set; }
    public int Stumpings { get; set; }

    // Attendance (venue bookings): Total = all venue rows; Present = listed; Absent = not listed
    public int AttendanceTotal { get; set; }
    public int AttendancePresent { get; set; }
    public int AttendanceAbsent { get; set; }
    public double AttendancePct => AttendanceTotal == 0
        ? 0
        : Math.Round(AttendancePresent * 100.0 / AttendanceTotal, 1);
    public string AttendancePctText => AttendanceTotal == 0 ? "-" : AttendancePct.ToString("0.#") + "%";

    // Batting milestones (per innings)
    public int HighestScore { get; set; }
    public bool HighestScoreNotOut { get; set; }
    /// <summary>Legal balls faced in the highest-score innings.</summary>
    public int HighestScoreBalls { get; set; }
    public int NotOuts { get; set; }
    public int Ducks { get; set; }
    public int GoldenDucks { get; set; }
    public int Fifties { get; set; }
    public int Hundreds { get; set; }

    // Bowling milestones
    public int Maidens { get; set; }
    public int BestBowlingWickets { get; set; }
    public int BestBowlingRuns { get; set; }
    public string BestBowlingText { get; set; } = "-";
    /// <summary>Bowler-credited wickets in three consecutive deliveries (same innings).</summary>
    public int HatTricks { get; set; }
    public string HighestScoreText
    {
        get
        {
            if (InningsBatted <= 0) return "-";
            var star = HighestScoreNotOut ? "*" : "";
            return $"{HighestScore}({HighestScoreBalls}){star}";
        }
    }
}

public sealed class ConfrontationDto
{
    public Guid BatterId { get; set; }
    public string BatterName { get; set; } = string.Empty;
    public Guid BowlerId { get; set; }
    public string BowlerName { get; set; } = string.Empty;
    public int Balls { get; set; }
    public int Runs { get; set; }
    public int Dots { get; set; }
    public int Ones { get; set; }
    public int Twos { get; set; }
    public int Threes { get; set; }
    public int Fours { get; set; }
    public int Fives { get; set; }
    public int Sixes { get; set; }
    public int Outs { get; set; }
    public int ExtrasConceded { get; set; }
}

public sealed class RankingRowDto
{
    public int Rank { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public string PrimaryLabel { get; set; } = string.Empty;
    public string PrimaryValue { get; set; } = string.Empty;
    public string SecondaryLabel { get; set; } = string.Empty;
    public string SecondaryValue { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public double SortScore { get; set; }
}

public sealed class RankingsDto
{
    public IReadOnlyList<RankingRowDto> Batting { get; set; } = Array.Empty<RankingRowDto>();
    public IReadOnlyList<RankingRowDto> Bowling { get; set; } = Array.Empty<RankingRowDto>();
    public IReadOnlyList<RankingRowDto> AllRounder { get; set; } = Array.Empty<RankingRowDto>();
    public IReadOnlyList<RankingRowDto> Fielder { get; set; } = Array.Empty<RankingRowDto>();
    public IReadOnlyList<RankingRowDto> Captain { get; set; } = Array.Empty<RankingRowDto>();
}

public static class StatsCalculator
{
    public static async Task<RankingsDto> BuildRankingsAsync(ApiDbContext db, CancellationToken ct)
    {
        var board = await BuildLeaderboardAsync(db, ct: ct).ConfigureAwait(false);
        return BuildRankingsFromBoard(board);
    }

    public static RankingsDto BuildRankingsFromBoard(IReadOnlyList<PlayerCareerDto> board)
    {
        static double BatSr(PlayerCareerDto p) => p.BallsFaced == 0 ? 0 : p.Runs * 100.0 / p.BallsFaced;
        static double BowlEcon(PlayerCareerDto p) => p.OversBowledBalls == 0 ? double.MaxValue : p.RunsConceded * 6.0 / p.OversBowledBalls;
        static double BowlAvg(PlayerCareerDto p) => p.Wickets == 0 ? double.MaxValue : (double)p.RunsConceded / p.Wickets;
        static double BatAvg(PlayerCareerDto p) => p.Dismissals == 0 ? p.Runs : (double)p.Runs / p.Dismissals;
        static string BatAvgText(PlayerCareerDto p)
            => p.InningsBatted <= 0 && p.BallsFaced == 0 && p.Runs == 0
                ? "-"
                : BatAvg(p).ToString("0.##");
        static string BowlAvgText(PlayerCareerDto p) => p.Wickets == 0 ? "-" : BowlAvg(p).ToString("0.##");
        static string BowlSrText(PlayerCareerDto p)
            => p.Wickets == 0 ? "-" : (p.OversBowledBalls / (double)p.Wickets).ToString("0.##");
        static string OversText(PlayerCareerDto p)
            => $"{p.OversBowledBalls / 6}.{p.OversBowledBalls % 6}";
        static string EconText(PlayerCareerDto p)
            => p.OversBowledBalls == 0 ? "-" : BowlEcon(p).ToString("0.##");
        static int FieldActions(PlayerCareerDto p) => p.Catches + p.RunOutsEffected + p.Stumpings;

        static double BatPts(PlayerCareerDto p)
        {
            if (p.BallsFaced <= 0 && p.Runs <= 0) return 0;
            var innings = Math.Max(p.InningsBatted, p.BallsFaced > 0 || p.Runs > 0 ? 1 : 0);
            var avg = BatAvg(p);
            var sr = BatSr(p);
            // ICC T20 batting is 0–1000 from average, strike-rate and volume, damped until enough innings.
            // Elite T20 reference: Avg 40, SR 140. Full rating after 20 innings (ICC T20I-style).
            var avgF = Math.Min(avg / 40.0, 1.25);
            var srF = Math.Min(sr / 140.0, 1.25);
            var volF = Math.Min(p.Runs / 300.0, 1.0);
            var index = avgF * 0.45 + srF * 0.40 + volF * 0.15;
            var qualify = Math.Min(1.0, innings / 20.0);
            return Math.Round(Math.Clamp(1000.0 * index * qualify, 0, 1000));
        }

        static double BowlPts(PlayerCareerDto p)
        {
            if (p.Wickets <= 0 && p.OversBowledBalls <= 0) return 0;
            var avg = BowlAvg(p);
            var econ = BowlEcon(p);
            var overs = p.OversBowledBalls / 6.0;
            var bowlSr = p.Wickets == 0 ? double.MaxValue : p.OversBowledBalls / (double)p.Wickets;
            // Elite T20 reference: Avg 20, Econ 7.5, SR 16 balls/wkt. Full after 15 wkts or 15 overs.
            var avgF = avg >= double.MaxValue / 2 ? 0 : Math.Min(20.0 / Math.Max(avg, 8.0), 1.25);
            var econF = econ >= double.MaxValue / 2 ? 0 : Math.Min(7.5 / Math.Max(econ, 4.5), 1.25);
            var srF = p.Wickets <= 0 ? 0 : Math.Min(16.0 / Math.Max(bowlSr, 8.0), 1.25);
            var wktsF = Math.Min(p.Wickets / 20.0, 1.0);
            var index = avgF * 0.35 + econF * 0.35 + srF * 0.20 + wktsF * 0.10;
            var qualify = Math.Min(1.0, Math.Max(overs / 15.0, p.Wickets / 15.0));
            return Math.Round(Math.Clamp(1000.0 * index * qualify, 0, 1000));
        }

        static bool HasBatted(PlayerCareerDto p) => p.BallsFaced > 0 || p.Runs > 0;
        static bool HasBowled(PlayerCareerDto p) => p.OversBowledBalls > 0 || p.Wickets > 0;

        static double ArPts(PlayerCareerDto p)
        {
            if (!HasBatted(p) || !HasBowled(p)) return 0;
            // Official ICC all-rounder rating: geometric mean of batting and bowling ratings.
            return Math.Round(Math.Clamp(Math.Sqrt(BatPts(p) * BowlPts(p)), 0, 1000));
        }

        static double FieldPts(PlayerCareerDto p)
        {
            if (FieldActions(p) <= 0) return 0;
            var matches = Math.Max(p.Matches, 1);
            var dpm = FieldActions(p) / (double)matches;
            // ICC has no fielder table. Same 0–1000 scale: dismissals per match + volume (St weighted).
            var rateF = Math.Min(dpm / 0.7, 1.25);
            var volF = Math.Min(FieldActions(p) / 20.0, 1.0);
            var stF = Math.Min(p.Stumpings / 8.0, 1.0);
            var index = rateF * 0.60 + volF * 0.25 + stF * 0.15;
            var qualify = Math.Min(1.0, matches / 15.0);
            return Math.Round(Math.Clamp(1000.0 * index * qualify, 0, 1000));
        }

        static double CapWinPct(PlayerCareerDto p)
            => p.CaptainMatches == 0 ? 0 : (p.CaptainWon + p.CaptainTied * 0.5) / p.CaptainMatches;
        static string CapWinPctText(PlayerCareerDto p)
            => p.CaptainMatches == 0 ? "-" : (CapWinPct(p) * 100).ToString("0.#") + "%";
        static double CapPts(PlayerCareerDto p)
        {
            if (p.CaptainMatches <= 0) return 0;
            // ICC has no captain table. Win% (tie = ½, like ICC points) + wins + games, 0–1000.
            var winF = CapWinPct(p);
            var winsF = Math.Min(p.CaptainWon / 15.0, 1.0);
            var gamesF = Math.Min(p.CaptainMatches / 15.0, 1.0);
            var index = winF * 0.70 + winsF * 0.20 + gamesF * 0.10;
            var qualify = Math.Min(1.0, p.CaptainMatches / 10.0);
            return Math.Round(Math.Clamp(1000.0 * index * qualify, 0, 1000));
        }

        var batting = board
            .Where(p => p.BallsFaced > 0 || p.Runs > 0)
            .OrderByDescending(BatPts)
            .ThenByDescending(BatAvg)
            .ThenByDescending(p => p.Runs)
            .Select((p, i) => new RankingRowDto
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = BatPts(p).ToString("0"),
                SecondaryLabel = "Avg",
                SecondaryValue = BatAvgText(p),
                Detail = $"Runs {p.Runs} · HS {p.HighestScoreText} · Balls {p.BallsFaced} · Avg {BatAvgText(p)} · SR {BatSr(p):0.#} · 50s {p.Fifties} · 100s {p.Hundreds} · 4s {p.Fours} · 6s {p.Sixes}",
                SortScore = BatPts(p)
            })
            .ToList();

        var bowling = board
            .Where(p => p.Wickets > 0 || p.OversBowledBalls > 0)
            .OrderByDescending(BowlPts)
            .ThenBy(BowlAvg)
            .ThenByDescending(p => p.Wickets)
            .Select((p, i) => new RankingRowDto
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = BowlPts(p).ToString("0"),
                SecondaryLabel = "Avg",
                SecondaryValue = BowlAvgText(p),
                Detail = $"Wkts {p.Wickets} · Best {p.BestBowlingText} · Overs {OversText(p)} · Runs {p.RunsConceded} · Avg {BowlAvgText(p)} · SR {BowlSrText(p)} · Econ {EconText(p)} · Maidens {p.Maidens} · Dots {p.DotBallsBowled}",
                SortScore = BowlPts(p)
            })
            .ToList();

        var allRounder = board
            .Where(p => HasBatted(p) && HasBowled(p))
            .OrderByDescending(ArPts)
            .ThenByDescending(BatPts)
            .ThenByDescending(BowlPts)
            .Select((p, i) => new RankingRowDto
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = ArPts(p).ToString("0"),
                SecondaryLabel = "B/Bowl",
                SecondaryValue = $"{BatPts(p):0}/{BowlPts(p):0}",
                Detail = $"ICC √(Bat×Bowl) · Bat {BatPts(p):0} · Bowl {BowlPts(p):0} · Runs {p.Runs} · Wkts {p.Wickets} · Overs {OversText(p)}",
                SortScore = ArPts(p)
            })
            .ToList();

        var fielder = board
            .Where(p => FieldActions(p) > 0)
            .OrderByDescending(FieldPts)
            .ThenByDescending(p => p.Catches)
            .ThenByDescending(p => p.RunOutsEffected)
            .Select((p, i) => new RankingRowDto
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = FieldPts(p).ToString("0"),
                SecondaryLabel = "Ct",
                SecondaryValue = p.Catches.ToString(),
                Detail = $"Ct {p.Catches} · RO {p.RunOutsEffected} · St {p.Stumpings} · Total {FieldActions(p)}",
                SortScore = FieldPts(p)
            })
            .ToList();

        var captain = board
            .Where(p => p.CaptainMatches > 0)
            .OrderByDescending(CapPts)
            .ThenByDescending(CapWinPct)
            .ThenByDescending(p => p.CaptainWon)
            .ThenByDescending(p => p.CaptainMatches)
            .Select((p, i) => new RankingRowDto
            {
                Rank = i + 1,
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                PrimaryLabel = "Pts",
                PrimaryValue = CapPts(p).ToString("0"),
                SecondaryLabel = "Win%",
                SecondaryValue = CapWinPctText(p),
                Detail = $"Cap {p.CaptainMatches} · W {p.CaptainWon} · L {p.CaptainLoss} · T {p.CaptainTied} · Win% {CapWinPctText(p)}",
                SortScore = CapPts(p)
            })
            .ToList();

        return new RankingsDto
        {
            Batting = batting,
            Bowling = bowling,
            AllRounder = allRounder,
            Fielder = fielder,
            Captain = captain
        };
    }

    public static async Task<IReadOnlyList<PlayerCareerDto>> BuildLeaderboardAsync(
        ApiDbContext db,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        Guid? venueId = null,
        CancellationToken ct = default)
    {
        var players = await db.Players.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.FullName)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string? venueName = null;
        if (venueId is Guid vid)
        {
            venueName = await db.Venues.AsNoTracking()
                .Where(v => v.Id == vid && !v.IsDeleted)
                .Select(v => v.Name)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        var matchQuery = db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.Completed && !m.IsDeleted);
        if (fromUtc is DateTime from)
            matchQuery = matchQuery.Where(m => m.MatchDateUtc >= from);
        if (toExclusiveUtc is DateTime to)
            matchQuery = matchQuery.Where(m => m.MatchDateUtc < to);
        if (!string.IsNullOrWhiteSpace(venueName))
        {
            var vn = venueName.Trim();
            matchQuery = matchQuery.Where(m =>
                m.Venue != null && m.Venue.ToLower() == vn.ToLower());
        }

        var matches = await matchQuery
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var matchIds = matches.Select(m => m.Id).ToList();
        var allDeliveries = await db.Deliveries.AsNoTracking()
            .Where(d => matchIds.Contains(d.MatchId))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var innings = await db.Innings.AsNoTracking()
            .Where(i => matchIds.Contains(i.MatchId))
            .Include(i => i.Deliveries)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var teams = await db.Teams.AsNoTracking()
            .Include(t => t.TeamPlayers)
            .ToDictionaryAsync(t => t.Id, t => t, ct)
            .ConfigureAwait(false);

        IQueryable<Tournament> tournamentQuery = db.Tournaments.AsNoTracking()
            .Where(t => !t.IsDeleted && t.Status == TournamentStatus.Completed)
            .Include(t => t.Teams);
        if (fromUtc is DateTime tourFrom)
            tournamentQuery = tournamentQuery.Where(t => t.UpdatedAtUtc >= tourFrom);
        if (toExclusiveUtc is DateTime tourTo)
            tournamentQuery = tournamentQuery.Where(t => t.UpdatedAtUtc < tourTo);
        if (venueId is Guid tourVenueId)
            tournamentQuery = tournamentQuery.Where(t => t.VenueId == tourVenueId);
        var tournaments = await tournamentQuery.ToListAsync(ct).ConfigureAwait(false);

        var inningsByMatch = innings.GroupBy(i => i.MatchId).ToDictionary(g => g.Key, g => g.ToList());
        var batByPlayer = allDeliveries.GroupBy(d => d.BatterId).ToDictionary(g => g.Key, g => g.ToList());
        var bowlByPlayer = allDeliveries.GroupBy(d => d.BowlerId).ToDictionary(g => g.Key, g => g.ToList());
        // Includes non-striker run-outs / retired (DismissedPlayerId may differ from BatterId).
        var outsByPlayer = allDeliveries
            .Where(d => d.IsWicket && d.DismissedPlayerId is Guid)
            .GroupBy(d => d.DismissedPlayerId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var inningsById = innings.ToDictionary(i => i.Id);

        var venueQuery = db.VenueBookings.AsNoTracking()
            .Where(b => !b.IsDeleted);
        if (fromUtc is DateTime venueFrom)
            venueQuery = venueQuery.Where(b => b.BookingDateUtc >= venueFrom);
        if (toExclusiveUtc is DateTime venueTo)
            venueQuery = venueQuery.Where(b => b.BookingDateUtc < venueTo);
        if (venueId is Guid filterVenueId)
            venueQuery = venueQuery.Where(b => b.VenueId == filterVenueId);

        var venueBookings = await venueQuery
            .Include(b => b.Participants)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        // Total = sum of all venue booking rows; Present = listed as participant; Absent = Total − Present.
        var attendanceTotal = venueBookings.Count;
        var presentByPlayer = venueBookings
            .SelectMany(b => b.Participants.Select(p => new { p.PlayerId, BookingId = b.Id }))
            .GroupBy(x => x.PlayerId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.BookingId).Distinct().Count());

        var list = new List<PlayerCareerDto>();
        foreach (var player in players)
        {
            batByPlayer.TryGetValue(player.Id, out var asBatter);
            bowlByPlayer.TryGetValue(player.Id, out var asBowler);
            outsByPlayer.TryGetValue(player.Id, out var outs);
            asBatter ??= new List<Delivery>();
            asBowler ??= new List<Delivery>();
            outs ??= new List<Delivery>();

            var summary = new PlayerCareerDto { PlayerId = player.Id, PlayerName = player.FullName };
            ApplyBatting(summary, asBatter);
            ApplyDismissals(summary, outs);
            ApplyBowling(summary, asBowler);
            ApplyBattingMilestones(summary, asBatter, outs);
            ApplyBowlingMilestones(summary, asBowler);
            ApplyFielding(summary, allDeliveries, player.Id);
            ApplyMatchResults(summary, player.Id, matches, inningsByMatch, inningsById, asBatter, asBowler, teams);
            ApplyTournamentResults(summary, player.Id, tournaments, teams);
            ApplyAttendance(summary, attendanceTotal, presentByPlayer.GetValueOrDefault(player.Id));

            if (summary.Matches == 0 && summary.Runs == 0 && summary.Wickets == 0
                && asBatter.Count == 0 && asBowler.Count == 0
                && summary.AttendancePresent == 0
                && summary.Tournaments == 0)
                continue;

            list.Add(summary);
        }

        return list.OrderByDescending(x => x.Runs).ThenByDescending(x => x.Wickets).ThenBy(x => x.PlayerName).ToList();
    }

    public static async Task<IReadOnlyList<PlayerCareerDto>> BuildNetsLeaderboardAsync(
        ApiDbContext db,
        DateTime? fromUtc = null,
        DateTime? toExclusiveUtc = null,
        CancellationToken ct = default)
    {
        var players = await db.Players.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.FullName)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var sessionQuery = db.NetSessions.AsNoTracking()
            .Where(s => s.Status == NetSessionStatus.Completed && !s.IsDeleted);
        if (fromUtc is DateTime from)
            sessionQuery = sessionQuery.Where(s => s.SessionDateUtc >= from);
        if (toExclusiveUtc is DateTime to)
            sessionQuery = sessionQuery.Where(s => s.SessionDateUtc < to);

        var sessions = await sessionQuery
            .Select(s => new { s.Id, s.PlayerRosterJson })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (sessions.Count == 0)
            return Array.Empty<PlayerCareerDto>();

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var netBalls = await db.NetDeliveries.AsNoTracking()
            .Where(d => sessionIds.Contains(d.SessionId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var sessionsByPlayer = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var session in sessions)
        {
            foreach (var playerId in RosterPlayerIds(session.PlayerRosterJson))
                MarkSession(sessionsByPlayer, playerId, session.Id);
        }
        foreach (var d in netBalls)
        {
            MarkSession(sessionsByPlayer, d.BatterId, d.SessionId);
            MarkSession(sessionsByPlayer, d.BowlerId, d.SessionId);
            MarkSession(sessionsByPlayer, d.NonStrikerId, d.SessionId);
            if (d.FielderId is Guid fielderId)
                MarkSession(sessionsByPlayer, fielderId, d.SessionId);
            if (d.DismissedPlayerId is Guid dismissedId)
                MarkSession(sessionsByPlayer, dismissedId, d.SessionId);
        }

        var battingBalls = new List<Delivery>();
        var bowlingBalls = new List<Delivery>();
        foreach (var group in netBalls.GroupBy(d => d.SessionId))
        {
            var ordered = group.OrderBy(d => d.Sequence).ToList();
            bowlingBalls.AddRange(ordered.Select(d => FromNet(d, group.Key)));
            battingBalls.AddRange(CloneWithBattingSpells(ordered));
        }

        var batByPlayer = battingBalls.GroupBy(d => d.BatterId).ToDictionary(g => g.Key, g => g.ToList());
        var bowlByPlayer = bowlingBalls.GroupBy(d => d.BowlerId).ToDictionary(g => g.Key, g => g.ToList());
        var outsByPlayer = battingBalls
            .Where(d => d.IsWicket && d.DismissedPlayerId is Guid)
            .GroupBy(d => d.DismissedPlayerId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var list = new List<PlayerCareerDto>();
        foreach (var player in players)
        {
            batByPlayer.TryGetValue(player.Id, out var asBatter);
            bowlByPlayer.TryGetValue(player.Id, out var asBowler);
            outsByPlayer.TryGetValue(player.Id, out var outs);
            asBatter ??= new List<Delivery>();
            asBowler ??= new List<Delivery>();
            outs ??= new List<Delivery>();

            var summary = new PlayerCareerDto { PlayerId = player.Id, PlayerName = player.FullName };
            summary.Sessions = sessionsByPlayer.GetValueOrDefault(player.Id)?.Count ?? 0;
            ApplyBatting(summary, asBatter);
            ApplyDismissals(summary, outs);
            ApplyBowling(summary, asBowler);
            ApplyBattingMilestones(summary, asBatter, outs);
            ApplyBowlingMilestones(summary, asBowler);
            ApplyFielding(summary, bowlingBalls, player.Id);

            if (summary.Sessions == 0 && summary.Runs == 0 && summary.Wickets == 0
                && asBatter.Count == 0 && asBowler.Count == 0
                && summary.Catches == 0 && summary.RunOutsEffected == 0 && summary.Stumpings == 0)
                continue;

            list.Add(summary);
        }

        return list.OrderByDescending(x => x.Runs).ThenByDescending(x => x.Wickets).ThenBy(x => x.PlayerName).ToList();
    }

    private static void MarkSession(Dictionary<Guid, HashSet<Guid>> map, Guid playerId, Guid sessionId)
    {
        if (playerId == Guid.Empty)
            return;
        if (!map.TryGetValue(playerId, out var set))
        {
            set = new HashSet<Guid>();
            map[playerId] = set;
        }
        set.Add(sessionId);
    }

    private static IEnumerable<Guid> RosterPlayerIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<Guid>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("players", out var players)
                && !doc.RootElement.TryGetProperty("Players", out players))
                return Array.Empty<Guid>();

            var ids = new List<Guid>();
            foreach (var p in players.EnumerateArray())
            {
                if (TryReadGuid(p, "id", out var id)
                    || TryReadGuid(p, "Id", out id)
                    || TryReadGuid(p, "playerId", out id))
                    ids.Add(id);
            }
            return ids;
        }
        catch
        {
            return Array.Empty<Guid>();
        }
    }

    private static bool TryReadGuid(JsonElement el, string name, out Guid id)
    {
        id = Guid.Empty;
        if (!el.TryGetProperty(name, out var prop))
            return false;
        if (prop.ValueKind == JsonValueKind.String && Guid.TryParse(prop.GetString(), out id) && id != Guid.Empty)
            return true;
        return false;
    }

    private static List<Delivery> CloneWithBattingSpells(IReadOnlyList<NetDelivery> ordered)
    {
        var result = new List<Delivery>(ordered.Count);
        Guid? currentBatter = null;
        var spellId = Guid.NewGuid();
        foreach (var d in ordered)
        {
            if (currentBatter is null || currentBatter != d.BatterId)
            {
                spellId = Guid.NewGuid();
                currentBatter = d.BatterId;
            }

            result.Add(FromNet(d, spellId));
            if (d.IsWicket && (d.DismissedPlayerId ?? d.BatterId) == d.BatterId)
                currentBatter = null;
        }

        return result;
    }

    private static Delivery FromNet(NetDelivery d, Guid inningsId) => new()
    {
        Id = d.Id,
        MatchId = d.SessionId,
        InningsId = inningsId,
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
        IsLegalDelivery = d.IsLegalDelivery,
        CreatedAtUtc = d.CreatedAtUtc,
        Version = d.Version
    };

    public static async Task<IReadOnlyList<ConfrontationDto>> BuildConfrontationsAsync(
        ApiDbContext db,
        Guid playerId,
        bool asBatterPerspective,
        CancellationToken ct)
    {
        var deliveries = asBatterPerspective
            ? await db.Deliveries.AsNoTracking()
                .Where(d => d.BatterId == playerId || d.DismissedPlayerId == playerId)
                .ToListAsync(ct).ConfigureAwait(false)
            : await db.Deliveries.AsNoTracking().Where(d => d.BowlerId == playerId).ToListAsync(ct).ConfigureAwait(false);

        if (deliveries.Count == 0)
            return Array.Empty<ConfrontationDto>();

        var names = await db.Players.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
            .ConfigureAwait(false);

        var groups = asBatterPerspective
            ? deliveries.GroupBy(d => d.BowlerId)
            : deliveries.GroupBy(d => d.BatterId);

        var stats = new List<ConfrontationDto>();
        foreach (var g in groups)
        {
            var first = g.First();
            var row = new ConfrontationDto
            {
                BatterId = asBatterPerspective ? playerId : first.BatterId,
                BatterName = names.GetValueOrDefault(asBatterPerspective ? playerId : first.BatterId, "?"),
                BowlerId = first.BowlerId,
                BowlerName = names.GetValueOrDefault(first.BowlerId, "?")
            };
            foreach (var d in g)
            {
                if (asBatterPerspective && d.BatterId != playerId)
                {
                    // Non-striker dismissal only — count the out, not balls/runs on that delivery.
                    if (d.IsWicket && d.DismissedPlayerId == playerId)
                        row.Outs++;
                    continue;
                }
                ApplyConfrontation(row, d);
            }
            stats.Add(row);
        }

        return asBatterPerspective
            ? stats.OrderByDescending(s => s.Runs).ThenByDescending(s => s.Balls).ToList()
            : stats.OrderByDescending(s => s.Outs).ThenBy(s => s.Runs).ToList();
    }

    public static async Task<IReadOnlyList<ConfrontationDto>> BuildNetsConfrontationsAsync(
        ApiDbContext db,
        Guid playerId,
        bool asBatterPerspective,
        CancellationToken ct)
    {
        var completedIds = await db.NetSessions.AsNoTracking()
            .Where(s => s.Status == NetSessionStatus.Completed && !s.IsDeleted)
            .Select(s => s.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (completedIds.Count == 0)
            return Array.Empty<ConfrontationDto>();

        var netBalls = asBatterPerspective
            ? await db.NetDeliveries.AsNoTracking()
                .Where(d => completedIds.Contains(d.SessionId)
                            && (d.BatterId == playerId || d.DismissedPlayerId == playerId))
                .ToListAsync(ct).ConfigureAwait(false)
            : await db.NetDeliveries.AsNoTracking()
                .Where(d => completedIds.Contains(d.SessionId) && d.BowlerId == playerId)
                .ToListAsync(ct).ConfigureAwait(false);

        var deliveries = netBalls.Select(d => FromNet(d, d.SessionId)).ToList();
        if (deliveries.Count == 0)
            return Array.Empty<ConfrontationDto>();

        var names = await db.Players.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct)
            .ConfigureAwait(false);

        var groups = asBatterPerspective
            ? deliveries.GroupBy(d => d.BowlerId)
            : deliveries.GroupBy(d => d.BatterId);

        var stats = new List<ConfrontationDto>();
        foreach (var g in groups)
        {
            var first = g.First();
            var row = new ConfrontationDto
            {
                BatterId = asBatterPerspective ? playerId : first.BatterId,
                BatterName = names.GetValueOrDefault(asBatterPerspective ? playerId : first.BatterId, "?"),
                BowlerId = first.BowlerId,
                BowlerName = names.GetValueOrDefault(first.BowlerId, "?")
            };
            foreach (var d in g)
            {
                if (asBatterPerspective && d.BatterId != playerId)
                {
                    if (d.IsWicket && d.DismissedPlayerId == playerId)
                        row.Outs++;
                    continue;
                }
                ApplyConfrontation(row, d);
            }
            stats.Add(row);
        }

        return asBatterPerspective
            ? stats.OrderByDescending(s => s.Runs).ThenByDescending(s => s.Balls).ToList()
            : stats.OrderByDescending(s => s.Outs).ThenBy(s => s.Runs).ToList();
    }

    private static void ApplyMatchResults(
        PlayerCareerDto summary,
        Guid playerId,
        List<Match> completedMatches,
        Dictionary<Guid, List<Innings>> inningsByMatch,
        Dictionary<Guid, Innings> inningsById,
        List<Delivery> asBatter,
        List<Delivery> asBowler,
        Dictionary<Guid, Team> teams)
    {
        var played = asBatter.Select(d => d.MatchId).Concat(asBowler.Select(d => d.MatchId)).ToHashSet();
        foreach (var match in completedMatches.Where(m => played.Contains(m.Id)))
        {
            if (!inningsByMatch.TryGetValue(match.Id, out var matchInns))
                continue;
            var playerTeamId = ResolvePlayerTeam(playerId, asBatter, asBowler, inningsById, match.Id);
            if (playerTeamId is null)
                continue;

            summary.Matches++;
            var (winnerId, tied) = ResolveResult(match, matchInns);
            if (tied) summary.Tied++;
            else if (winnerId == playerTeamId) summary.Won++;
            else summary.Loss++;

            var captainA = match.TeamACaptainId ?? (teams.TryGetValue(match.TeamAId, out var ta) ? ta.CaptainPlayerId : null);
            var captainB = match.TeamBCaptainId ?? (teams.TryGetValue(match.TeamBId, out var tb) ? tb.CaptainPlayerId : null);
            if (playerId != captainA && playerId != captainB)
                continue;

            summary.CaptainMatches++;
            if (tied) summary.CaptainTied++;
            else if (winnerId == playerTeamId) summary.CaptainWon++;
            else summary.CaptainLoss++;
        }
    }

    private static void ApplyTournamentResults(
        PlayerCareerDto summary,
        Guid playerId,
        List<Tournament> tournaments,
        Dictionary<Guid, Team> teams)
    {
        foreach (var tour in tournaments)
        {
            Guid? playerTeamId = null;
            foreach (var tt in tour.Teams)
            {
                if (!teams.TryGetValue(tt.TeamId, out var team))
                    continue;
                if (team.TeamPlayers.Any(tp => tp.PlayerId == playerId))
                {
                    playerTeamId = tt.TeamId;
                    break;
                }
            }

            if (playerTeamId is null)
                continue;

            summary.Tournaments++;
            var won = tour.WinnerTeamId == playerTeamId;
            if (won) summary.TournamentWon++;
            else summary.TournamentLoss++;

            if (!teams.TryGetValue(playerTeamId.Value, out var playerTeam)
                || playerTeam.CaptainPlayerId != playerId)
                continue;

            summary.CaptainTournaments++;
            if (won) summary.CaptainTournamentWon++;
            else summary.CaptainTournamentLoss++;
        }
    }

    private static Guid? ResolvePlayerTeam(
        Guid playerId,
        List<Delivery> asBatter,
        List<Delivery> asBowler,
        Dictionary<Guid, Innings> inningsById,
        Guid matchId)
    {
        foreach (var d in asBatter.Where(x => x.MatchId == matchId))
            if (inningsById.TryGetValue(d.InningsId, out var inn))
                return inn.BattingTeamId;
        foreach (var d in asBowler.Where(x => x.MatchId == matchId))
            if (inningsById.TryGetValue(d.InningsId, out var inn))
                return inn.BowlingTeamId;
        return null;
    }

    private static (Guid? Winner, bool Tied) ResolveResult(Match match, List<Innings> inns)
    {
        var a = inns.Where(i => i.BattingTeamId == match.TeamAId).Sum(i => i.Deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns));
        var b = inns.Where(i => i.BattingTeamId == match.TeamBId).Sum(i => i.Deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns));
        if (a == b) return (null, true);
        return a > b ? (match.TeamAId, false) : (match.TeamBId, false);
    }

    private static void ApplyBatting(PlayerCareerDto s, List<Delivery> asBatter)
    {
        s.InningsBatted = asBatter.Select(d => d.InningsId).Distinct().Count();
        foreach (var d in asBatter)
        {
            if (d.ExtraType == ExtraType.Wide)
                s.WidesFaced++;
            else if (d.ExtraType == ExtraType.NoBall)
            {
                s.NoBallsFaced++;
                s.Runs += d.RunsOffBat;
                CountBat(s, d.RunsOffBat, false);
            }
            else             if (d.IsLegalDelivery)
            {
                s.BallsFaced++;
                s.Runs += d.RunsOffBat;
                CountBat(s, d.RunsOffBat, d.ExtraRuns == 0 && !d.IsWicket);
            }
        }
    }

    /// <summary>Count every dismissal for this player (striker or non-striker run-out).</summary>
    private static void ApplyDismissals(PlayerCareerDto s, List<Delivery> outs)
    {
        foreach (var d in outs)
        {
            s.Dismissals++;
            switch (d.WicketType)
            {
                case WicketType.Bowled: s.BowledOut++; break;
                case WicketType.Caught: s.CaughtOut++; break;
                case WicketType.Lbw: s.LbwOut++; break;
                case WicketType.RunOut: s.RunOut++; break;
                case WicketType.Stumped: s.StumpedOut++; break;
                case WicketType.HitWicket: s.HitWicketOut++; break;
                case WicketType.Retired: s.RetiredOut++; break;
                default: s.OtherOut++; break;
            }
        }
    }

    private static void CountBat(PlayerCareerDto s, int r, bool dot)
    {
        switch (r)
        {
            case 0 when dot: s.Dots++; break;
            case 1: s.Ones++; break;
            case 2: s.Twos++; break;
            case 3: s.Threes++; break;
            case 4: s.Fours++; break;
            case 5: s.Fives++; break;
            case >= 6: s.Sixes++; break;
        }
    }

    private static void ApplyBowling(PlayerCareerDto s, List<Delivery> asBowler)
    {
        foreach (var d in asBowler)
        {
            switch (d.ExtraType)
            {
                case ExtraType.Wide: s.WidesBowled++; break;
                case ExtraType.NoBall: s.NoBallsBowled++; break;
                case ExtraType.Bye: s.ByesConceded += d.ExtraRuns; break;
                case ExtraType.LegBye: s.LegByesConceded += d.ExtraRuns; break;
            }

            if (d.IsLegalDelivery)
            {
                s.OversBowledBalls++;
                if (d.RunsOffBat == 0 && d.ExtraRuns == 0 && !d.IsWicket)
                    s.DotBallsBowled++;
            }

            s.RunsConceded += d.RunsOffBat + d.ExtraRuns;
            if (!d.IsWicket) continue;
            switch (d.WicketType)
            {
                case WicketType.Bowled: s.BowledWkts++; s.Wickets++; break;
                case WicketType.Caught: s.CaughtWkts++; s.Wickets++; break;
                case WicketType.Lbw: s.LbwWkts++; s.Wickets++; break;
                case WicketType.Stumped: s.StumpedWkts++; s.Wickets++; break;
                case WicketType.HitWicket: s.HitWicketWkts++; s.Wickets++; break;
                case WicketType.RunOut: s.RunOutWkts++; break;
            }
        }
    }

    private static void ApplyBattingMilestones(PlayerCareerDto s, List<Delivery> asBatter, List<Delivery> outs)
    {
        var dismissedInnings = outs.Select(d => d.InningsId).ToHashSet();

        var hsRecorded = false;
        foreach (var innGroup in asBatter.GroupBy(d => d.InningsId))
        {
            var runs = 0;
            var balls = 0;
            var dismissed = dismissedInnings.Contains(innGroup.Key);
            foreach (var d in innGroup.OrderBy(x => x.Sequence))
            {
                if (d.ExtraType == ExtraType.Wide)
                {
                    // faced wide — no bat runs / balls
                }
                else if (d.ExtraType == ExtraType.NoBall)
                {
                    runs += d.RunsOffBat;
                }
                else if (d.IsLegalDelivery)
                {
                    balls++;
                    runs += d.RunsOffBat;
                }
            }

            if (!dismissed)
                s.NotOuts++;

            var betterHs = !hsRecorded
                || runs > s.HighestScore
                || (runs == s.HighestScore && !dismissed && !s.HighestScoreNotOut);
            if (betterHs)
            {
                s.HighestScore = runs;
                s.HighestScoreNotOut = !dismissed;
                s.HighestScoreBalls = balls;
                hsRecorded = true;
            }

            if (dismissed && runs == 0)
            {
                s.Ducks++;
                if (balls == 1)
                    s.GoldenDucks++;
            }

            if (runs >= 100) s.Hundreds++;
            else if (runs >= 50) s.Fifties++;
        }
    }

    private static void ApplyBowlingMilestones(PlayerCareerDto s, List<Delivery> asBowler)
    {
        var bestWkts = -1;
        var bestRuns = int.MaxValue;

        foreach (var innGroup in asBowler.GroupBy(d => d.InningsId))
        {
            var list = innGroup.OrderBy(d => d.Sequence).ToList();
            s.Maidens += CountMaidens(list);

            var wkts = 0;
            var runs = 0;
            foreach (var d in list)
            {
                runs += d.RunsOffBat + d.ExtraRuns;
                if (!d.IsWicket) continue;
                switch (d.WicketType)
                {
                    case WicketType.Bowled:
                    case WicketType.Caught:
                    case WicketType.Lbw:
                    case WicketType.Stumped:
                    case WicketType.HitWicket:
                        wkts++;
                        break;
                }
            }

            if (wkts > bestWkts || (wkts == bestWkts && runs < bestRuns))
            {
                bestWkts = wkts;
                bestRuns = runs;
            }

            s.HatTricks += CountHatTricks(list);
        }

        if (bestWkts >= 0)
        {
            s.BestBowlingWickets = bestWkts;
            s.BestBowlingRuns = bestRuns;
            s.BestBowlingText = $"{bestWkts}/{bestRuns}";
        }
    }

    private static bool IsBowlerWicket(Delivery d) =>
        d.IsWicket && d.WicketType is
            WicketType.Bowled or WicketType.Caught or WicketType.Lbw or
            WicketType.Stumped or WicketType.HitWicket;

    /// <summary>
    /// Three bowler-credited wickets in three consecutive deliveries (wides/no-balls included).
    /// Can span overs; run-outs break the streak.
    /// </summary>
    private static int CountHatTricks(IReadOnlyList<Delivery> bowledInInnings)
    {
        var hatTricks = 0;
        var streak = 0;
        foreach (var d in bowledInInnings)
        {
            if (IsBowlerWicket(d))
            {
                streak++;
                if (streak < 3)
                    continue;
                hatTricks++;
                streak = 0;
            }
            else
            {
                streak = 0;
            }
        }

        return hatTricks;
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

    private static void ApplyAttendance(PlayerCareerDto s, int venueTotal, int presentCount)
    {
        s.AttendanceTotal = venueTotal;
        s.AttendancePresent = presentCount;
        s.AttendanceAbsent = Math.Max(0, venueTotal - presentCount);
    }

    private static void ApplyFielding(PlayerCareerDto s, List<Delivery> all, Guid playerId)
    {
        foreach (var d in all.Where(x => x.FielderId == playerId && x.IsWicket))
        {
            switch (d.WicketType)
            {
                case WicketType.Caught: s.Catches++; break;
                case WicketType.RunOut: s.RunOutsEffected++; break;
                case WicketType.Stumped: s.Stumpings++; break;
            }
        }
    }

    private static void ApplyConfrontation(ConfrontationDto row, Delivery d)
    {
        if (d.IsLegalDelivery)
        {
            row.Balls++;
            row.Runs += d.RunsOffBat;
            switch (d.RunsOffBat)
            {
                case 0 when d.ExtraRuns == 0 && !d.IsWicket: row.Dots++; break;
                case 1: row.Ones++; break;
                case 2: row.Twos++; break;
                case 3: row.Threes++; break;
                case 4: row.Fours++; break;
                case 5: row.Fives++; break;
                case >= 6: row.Sixes++; break;
            }
        }
        row.ExtrasConceded += d.ExtraRuns;
        if (d.IsWicket && d.DismissedPlayerId == d.BatterId)
            row.Outs++;
    }
}
