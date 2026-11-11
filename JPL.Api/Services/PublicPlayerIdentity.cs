using Microsoft.EntityFrameworkCore;
using JPL.Api.Data;
using JPL.Api.Models;

namespace JPL.Api.Services;

public sealed class PublicPlayerCard
{
    public Guid PlayerId { get; set; }
    public string FullName { get; set; } = "";
    public string? NickName { get; set; }
    public string? PlayingRole { get; set; }
    public bool IsActive { get; set; }
    public bool HasPhoto { get; set; }
    public string? PhotoUrl { get; set; }
    public string ProfileUrl { get; set; } = "";
    public string? TeamName { get; set; }
    public int Matches { get; set; }
    public int Won { get; set; }
    public int Loss { get; set; }
    public int Tied { get; set; }
    public int Runs { get; set; }
    public int Wickets { get; set; }
    public int Fours { get; set; }
    public int Sixes { get; set; }
    public int Catches { get; set; }
    public int RunOutsEffected { get; set; }
    public int Stumpings { get; set; }
    public int FieldTotal { get; set; }
    public int BallsFaced { get; set; }
    public int Dismissals { get; set; }
    public int NotOuts { get; set; }
    public int InningsBatted { get; set; }
    public int InningsBowled { get; set; }
    public int OversBowledBalls { get; set; }
    public int RunsConceded { get; set; }
    public int Maidens { get; set; }
    public int HatTricks { get; set; }
    public int FiveWicketHauls { get; set; }
    public int Fifties { get; set; }
    public int Hundreds { get; set; }
    public int Ducks { get; set; }
    public int Potm { get; set; }
    public string HighestScoreText { get; set; } = "-";
    public string BestBowlingText { get; set; } = "-";
    public string AvgText { get; set; } = "-";
    public string SrText { get; set; } = "-";
    public string EconText { get; set; } = "-";
    public string BowlAvgText { get; set; } = "-";
    public int? BatRank { get; set; }
    public int? BowlRank { get; set; }
    public int? AllRounderRank { get; set; }
    public int? FieldRank { get; set; }
    public int? CaptainRank { get; set; }
    public string Headline { get; set; } = "";
}

public sealed class PublicLeaderRow
{
    public int Rank { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public string ProfileUrl { get; set; } = "";
    public string Primary { get; set; } = "";
    public string Secondary { get; set; } = "";
    public string Detail { get; set; } = "";
    public double SortScore { get; set; }
    public double BarRatio { get; set; }
    public string BarColor { get; set; } = "#F5B400";
    public string Pts { get; set; } = "";
}

public sealed class PublicPlayerDirectory
{
    public Guid LeagueId { get; set; }
    public string LeagueName { get; set; } = "";
    public int PlayerCount { get; set; }
    public int CompletedMatches { get; set; }
    public int TotalRuns { get; set; }
    public int TotalWickets { get; set; }
    public List<PublicLeaderRow> TopBatting { get; set; } = new();
    public List<PublicLeaderRow> TopBowling { get; set; } = new();
    public List<PublicLeaderRow> TopAllRounder { get; set; } = new();
    public List<PublicLeaderRow> TopFielding { get; set; } = new();
    public List<PublicLeaderRow> TopCaptain { get; set; } = new();
    public List<PublicPlayerCard> Players { get; set; } = new();
}

public sealed class PublicHonour
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public sealed class PublicRankBadge
{
    public string Category { get; set; } = "";
    public int Rank { get; set; }
    public string Detail { get; set; } = "";
    public string Primary { get; set; } = "";
    public double SortScore { get; set; }
    public double BarRatio { get; set; }
    public string BarColor { get; set; } = "#F5B400";
}

public sealed class PublicStrength
{
    public string Label { get; set; } = "";
    public int Score { get; set; }
}

public sealed class PublicFormInnings
{
    public string Label { get; set; } = "";
    public int Runs { get; set; }
    public int Balls { get; set; }
    public bool NotOut { get; set; }
    public string Text => NotOut ? $"{Runs}* ({Balls})" : $"{Runs} ({Balls})";
}

public sealed class PublicDuel
{
    public string Opponent { get; set; } = "";
    public int Balls { get; set; }
    public int Runs { get; set; }
    public int Outs { get; set; }
    public int Dots { get; set; }
    public int Ones { get; set; }
    public int Twos { get; set; }
    public int Threes { get; set; }
    public int Fours { get; set; }
    public int Sixes { get; set; }
    public int Bowled { get; set; }
    public int Caught { get; set; }
    public int Lbw { get; set; }
    public int RunOut { get; set; }
    public int Stumped { get; set; }
    public int HitWicket { get; set; }
    public int ExtrasConceded { get; set; }
    public double StrikeRate => Balls <= 0 ? 0 : Math.Round(Runs * 100.0 / Balls, 1);
    public string AverageText => Outs <= 0 ? "-" : Math.Round((double)Runs / Outs, 2).ToString("0.##");
    public double Economy => Balls <= 0 ? 0 : Math.Round((Runs + ExtrasConceded) * 6.0 / Balls, 2);
    public string Line { get; set; } = "";
}

public sealed class PublicPlayerProfile
{
    public Guid LeagueId { get; set; }
    public string LeagueName { get; set; } = "";
    public Guid PlayerId { get; set; }
    public string FullName { get; set; } = "";
    public string? NickName { get; set; }
    public string? PlayingRole { get; set; }
    public string? BattingStyle { get; set; }
    public string? BowlingStyle { get; set; }
    public string? Bio { get; set; }
    public bool IsActive { get; set; }
    public bool HasPhoto { get; set; }
    public string? PhotoUrl { get; set; }
    public string ProfileUrl { get; set; } = "";
    public string DirectoryUrl { get; set; } = "";
    public List<string> Teams { get; set; } = new();
    public PlayerCareerDto Matches { get; set; } = new();
    public PlayerCareerDto? Nets { get; set; }
    public List<PublicRankBadge> Ranks { get; set; } = new();
    public List<PublicHonour> Honours { get; set; } = new();
    public List<PublicStrength> Strength { get; set; } = new();
    public List<PublicFormInnings> RecentForm { get; set; } = new();
    public List<PublicDuel> VsBowlers { get; set; } = new();
    public List<PublicDuel> VsBatters { get; set; } = new();
    public List<string> Insights { get; set; } = new();
}

public static class PublicPlayerIdentity
{
    public static string ProfilePath(Guid leagueId, Guid playerId)
        => $"/p/{leagueId:D}/{playerId:D}";

    public static string DirectoryPath(Guid leagueId)
        => $"/p/{leagueId:D}";

    public static async Task<PublicPlayerDirectory?> DirectoryAsync(
        ApiDbContext db, Guid leagueId, string leagueName, CancellationToken ct)
    {
        var players = await db.Players.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.FullName)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (players.Count == 0)
            return new PublicPlayerDirectory { LeagueId = leagueId, LeagueName = leagueName };

        var board = await StatsCalculator.BuildLeaderboardAsync(db, ct: ct).ConfigureAwait(false);
        var byId = board.ToDictionary(p => p.PlayerId);
        var ranks = StatsCalculator.BuildRankingsFromBoard(board);
        var teams = await db.Teams.AsNoTracking()
            .Where(t => !t.IsDeleted)
            .Include(t => t.TeamPlayers)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var potmMap = await db.Matches.AsNoTracking()
            .Where(m => !m.IsDeleted && m.Status == MatchStatus.Completed && m.PlayerOfTheMatchPlayerId != null)
            .GroupBy(m => m.PlayerOfTheMatchPlayerId!.Value)
            .Select(g => new { PlayerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlayerId, x => x.Count, ct)
            .ConfigureAwait(false);
        var completedMatches = await db.Matches.AsNoTracking()
            .CountAsync(m => !m.IsDeleted && m.Status == MatchStatus.Completed, ct)
            .ConfigureAwait(false);

        static string AvgText(PlayerCareerDto? c)
        {
            if (c is null || (c.InningsBatted <= 0 && c.Runs <= 0)) return "-";
            return c.Dismissals <= 0 ? c.Runs.ToString("0.#") : (c.Runs / (double)c.Dismissals).ToString("0.#");
        }
        static string SrText(PlayerCareerDto? c)
            => c is null || c.BallsFaced <= 0 ? "-" : (c.Runs * 100.0 / c.BallsFaced).ToString("0.#");
        static string EconText(PlayerCareerDto? c)
            => c is null || c.OversBowledBalls <= 0 ? "-" : (c.RunsConceded * 6.0 / c.OversBowledBalls).ToString("0.##");
        static string BowlAvgText(PlayerCareerDto? c)
            => c is null || c.Wickets <= 0 ? "-" : (c.RunsConceded / (double)c.Wickets).ToString("0.#");
        static int? RankOf(IReadOnlyList<RankingRowDto> rows, Guid id)
            => rows.FirstOrDefault(r => r.PlayerId == id)?.Rank;

        var cards = new List<PublicPlayerCard>();
        foreach (var p in players)
        {
            byId.TryGetValue(p.Id, out var career);
            var team = teams.FirstOrDefault(t => t.TeamPlayers.Any(tp => tp.PlayerId == p.Id));
            var runs = career?.Runs ?? 0;
            var wkts = career?.Wickets ?? 0;
            var field = (career?.Catches ?? 0) + (career?.RunOutsEffected ?? 0) + (career?.Stumpings ?? 0);
            potmMap.TryGetValue(p.Id, out var potm);
            cards.Add(new PublicPlayerCard
            {
                PlayerId = p.Id,
                FullName = p.FullName,
                NickName = BlankToNull(p.NickName),
                PlayingRole = BlankToNull(p.PlayingRole),
                IsActive = p.IsActive,
                HasPhoto = !string.IsNullOrWhiteSpace(p.ImagePath),
                PhotoUrl = string.IsNullOrWhiteSpace(p.ImagePath)
                    ? null
                    : $"/api/public/leagues/{leagueId:D}/players/{p.Id:D}/image",
                ProfileUrl = ProfilePath(leagueId, p.Id),
                TeamName = team?.Name,
                Matches = career?.Matches ?? 0,
                Won = career?.Won ?? 0,
                Loss = career?.Loss ?? 0,
                Tied = career?.Tied ?? 0,
                Runs = runs,
                Wickets = wkts,
                Fours = career?.Fours ?? 0,
                Sixes = career?.Sixes ?? 0,
                Catches = career?.Catches ?? 0,
                RunOutsEffected = career?.RunOutsEffected ?? 0,
                Stumpings = career?.Stumpings ?? 0,
                FieldTotal = field,
                BallsFaced = career?.BallsFaced ?? 0,
                Dismissals = career?.Dismissals ?? 0,
                NotOuts = career?.NotOuts ?? 0,
                InningsBatted = career?.InningsBatted ?? 0,
                InningsBowled = career?.InningsBowled ?? 0,
                OversBowledBalls = career?.OversBowledBalls ?? 0,
                RunsConceded = career?.RunsConceded ?? 0,
                Maidens = career?.Maidens ?? 0,
                HatTricks = career?.HatTricks ?? 0,
                FiveWicketHauls = career?.FiveWicketHauls ?? 0,
                Fifties = career?.Fifties ?? 0,
                Hundreds = career?.Hundreds ?? 0,
                Ducks = career?.Ducks ?? 0,
                Potm = potm,
                HighestScoreText = career?.HighestScoreText ?? "-",
                BestBowlingText = string.IsNullOrWhiteSpace(career?.BestBowlingText) ? "-" : career!.BestBowlingText,
                AvgText = AvgText(career),
                SrText = SrText(career),
                EconText = EconText(career),
                BowlAvgText = BowlAvgText(career),
                BatRank = RankOf(ranks.Batting, p.Id),
                BowlRank = RankOf(ranks.Bowling, p.Id),
                AllRounderRank = RankOf(ranks.AllRounder, p.Id),
                FieldRank = RankOf(ranks.Fielder, p.Id),
                CaptainRank = RankOf(ranks.Captain, p.Id),
                Headline = runs + wkts == 0
                    ? (BlankToNull(p.PlayingRole) ?? "Cric360 player")
                    : $"{runs} runs · {wkts} wkts"
            });
        }

        List<PublicLeaderRow> Leaders(IReadOnlyList<RankingRowDto> rows)
            => WithBarRatios(rows, leagueId);

        return new PublicPlayerDirectory
        {
            LeagueId = leagueId,
            LeagueName = leagueName,
            PlayerCount = cards.Count,
            CompletedMatches = completedMatches,
            TotalRuns = board.Sum(x => x.Runs),
            TotalWickets = board.Sum(x => x.Wickets),
            TopBatting = Leaders(ranks.Batting),
            TopBowling = Leaders(ranks.Bowling),
            TopAllRounder = Leaders(ranks.AllRounder),
            TopFielding = Leaders(ranks.Fielder),
            TopCaptain = Leaders(ranks.Captain),
            Players = cards
        };
    }

    public static async Task<PublicPlayerProfile?> ProfileAsync(
        ApiDbContext db, Guid leagueId, string leagueName, Guid playerId, CancellationToken ct)
    {
        var player = await db.Players.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playerId && !p.IsDeleted, ct)
            .ConfigureAwait(false);
        if (player is null)
            return null;

        var board = await StatsCalculator.BuildLeaderboardAsync(db, ct: ct).ConfigureAwait(false);
        var career = board.FirstOrDefault(p => p.PlayerId == playerId)
                     ?? new PlayerCareerDto { PlayerId = player.Id, PlayerName = player.FullName };
        var ranks = StatsCalculator.BuildRankingsFromBoard(board);
        var netsBoard = await StatsCalculator.BuildNetsLeaderboardAsync(db, ct: ct).ConfigureAwait(false);
        var nets = netsBoard.FirstOrDefault(p => p.PlayerId == playerId);

        var vsBowl = await StatsCalculator.BuildConfrontationsAsync(db, playerId, asBatterPerspective: true, ct)
            .ConfigureAwait(false);
        var vsBat = await StatsCalculator.BuildConfrontationsAsync(db, playerId, asBatterPerspective: false, ct)
            .ConfigureAwait(false);

        var teamNames = await db.Teams.AsNoTracking()
            .Where(t => !t.IsDeleted && t.TeamPlayers.Any(tp => tp.PlayerId == playerId))
            .Select(t => t.CaptainPlayerId == playerId ? t.Name + " (C)" : t.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var profile = new PublicPlayerProfile
        {
            LeagueId = leagueId,
            LeagueName = leagueName,
            PlayerId = player.Id,
            FullName = player.FullName,
            NickName = BlankToNull(player.NickName),
            PlayingRole = BlankToNull(player.PlayingRole),
            BattingStyle = BlankToNull(player.BattingStyle),
            BowlingStyle = BlankToNull(player.BowlingStyle),
            Bio = BlankToNull(player.Bio),
            IsActive = player.IsActive,
            HasPhoto = !string.IsNullOrWhiteSpace(player.ImagePath),
            PhotoUrl = string.IsNullOrWhiteSpace(player.ImagePath)
                ? null
                : $"/api/public/leagues/{leagueId:D}/players/{player.Id:D}/image",
            ProfileUrl = ProfilePath(leagueId, player.Id),
            DirectoryUrl = DirectoryPath(leagueId),
            Teams = teamNames,
            Matches = career,
            Nets = nets is { Sessions: > 0 } ? nets : null,
            Ranks = Badges(playerId, ranks),
            Honours = await HonoursAsync(db, playerId, ct).ConfigureAwait(false),
            Strength = Strength(career, board),
            RecentForm = await FormAsync(db, playerId, ct).ConfigureAwait(false),
            VsBowlers = vsBowl.OrderByDescending(d => d.Balls).Select(d => ToDuel(d, d.BowlerName)).ToList(),
            VsBatters = vsBat.OrderByDescending(d => d.Outs).ThenByDescending(d => d.Balls)
                .Select(d => ToDuel(d, d.BatterName)).ToList()
        };
        profile.Insights = Insights(profile);
        return profile;
    }

    private static readonly string[] RankBarPalette =
    [
        "#F5B400", "#FF7043", "#42A5F5", "#66BB6A", "#AB47BC", "#26C6DA",
        "#EC407A", "#7E57C2", "#FFA726", "#26A69A", "#5C6BC0", "#EF5350"
    ];

    private static List<PublicLeaderRow> WithBarRatios(IReadOnlyList<RankingRowDto> rows, Guid leagueId)
    {
        var max = rows.Select(r => Math.Abs(r.SortScore)).DefaultIfEmpty(1).Max();
        if (max <= 0) max = 1;
        return rows.Select((r, i) =>
        {
            var ratio = r.SortScore == 0 ? 0 : Math.Clamp(Math.Abs(r.SortScore) / max, 0.06, 1);
            return new PublicLeaderRow
            {
                Rank = r.Rank,
                PlayerId = r.PlayerId,
                PlayerName = r.PlayerName,
                ProfileUrl = ProfilePath(leagueId, r.PlayerId),
                Primary = string.IsNullOrWhiteSpace(r.PrimaryValue)
                    ? r.Detail
                    : $"{r.PrimaryLabel} {r.PrimaryValue}".Trim(),
                Secondary = string.IsNullOrWhiteSpace(r.SecondaryValue)
                    ? ""
                    : $"{r.SecondaryLabel} {r.SecondaryValue}".Trim(),
                Detail = r.Detail,
                SortScore = r.SortScore,
                BarRatio = ratio,
                BarColor = r.SortScore < 0 ? "#C62828" : RankBarPalette[i % RankBarPalette.Length],
                Pts = string.IsNullOrWhiteSpace(r.PrimaryValue) ? r.Detail : r.PrimaryValue
            };
        }).ToList();
    }

    private static List<PublicRankBadge> Badges(Guid playerId, RankingsDto ranks)
    {
        var list = new List<PublicRankBadge>();
        void Add(string cat, IReadOnlyList<RankingRowDto> rows)
        {
            var row = rows.FirstOrDefault(r => r.PlayerId == playerId);
            if (row is null) return;
            var max = rows.Select(r => Math.Abs(r.SortScore)).DefaultIfEmpty(1).Max();
            if (max <= 0) max = 1;
            var idx = Math.Max(0, rows.ToList().FindIndex(r => r.PlayerId == playerId));
            var ratio = row.SortScore == 0 ? 0 : Math.Clamp(Math.Abs(row.SortScore) / max, 0.06, 1);
            list.Add(new PublicRankBadge
            {
                Category = cat,
                Rank = row.Rank,
                Detail = row.Detail,
                Primary = string.IsNullOrWhiteSpace(row.PrimaryValue)
                    ? row.Detail
                    : $"{row.PrimaryLabel} {row.PrimaryValue}".Trim(),
                SortScore = row.SortScore,
                BarRatio = ratio,
                BarColor = row.SortScore < 0 ? "#C62828" : RankBarPalette[idx % RankBarPalette.Length]
            });
        }
        Add("Batting", ranks.Batting);
        Add("Bowling", ranks.Bowling);
        Add("All-rounder", ranks.AllRounder);
        Add("Fielding", ranks.Fielder);
        Add("Captaincy", ranks.Captain);
        return list;
    }

    private static async Task<List<PublicHonour>> HonoursAsync(ApiDbContext db, Guid playerId, CancellationToken ct)
    {
        var matches = await db.Matches.AsNoTracking()
            .Where(m => !m.IsDeleted && m.Status == MatchStatus.Completed)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var potm = matches.Count(m => m.PlayerOfTheMatchPlayerId == playerId);
        var list = new List<PublicHonour>();
        if (potm > 0)
            list.Add(new PublicHonour { Label = "Player of the Match", Count = potm });

        var tour = await db.Tournaments.AsNoTracking()
            .Where(t => !t.IsDeleted && t.Status == TournamentStatus.Completed && t.WinnerTeamId != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (tour.Count > 0)
        {
            var winnerIds = tour.Select(t => t.WinnerTeamId!.Value).Distinct().ToList();
            var capCount = await db.Teams.AsNoTracking()
                .CountAsync(t => winnerIds.Contains(t.Id) && t.CaptainPlayerId == playerId, ct)
                .ConfigureAwait(false);
            if (capCount > 0)
                list.Add(new PublicHonour { Label = "Tournament champion (captain)", Count = capCount });
        }
        return list;
    }

    private static List<PublicStrength> Strength(PlayerCareerDto p, IReadOnlyList<PlayerCareerDto> board)
    {
        static int Rel(double v, double max) => max <= 0 ? 0 : (int)Math.Clamp(Math.Round(100 * v / max), 0, 100);
        var maxRuns = board.Select(x => (double)x.Runs).DefaultIfEmpty(0).Max();
        var maxWkts = board.Select(x => (double)x.Wickets).DefaultIfEmpty(0).Max();
        var maxField = board.Select(x => (double)(x.Catches + x.RunOutsEffected + x.Stumpings)).DefaultIfEmpty(0).Max();
        var maxCap = board.Select(x => (double)x.CaptainWon).DefaultIfEmpty(0).Max();
        var field = p.Catches + p.RunOutsEffected + p.Stumpings;
        return
        [
            new PublicStrength { Label = "Batting", Score = Rel(p.Runs, maxRuns) },
            new PublicStrength { Label = "Bowling", Score = Rel(p.Wickets, maxWkts) },
            new PublicStrength { Label = "Fielding", Score = Rel(field, maxField) },
            new PublicStrength { Label = "Captaincy", Score = Rel(p.CaptainWon, maxCap) }
        ];
    }

    private static async Task<List<PublicFormInnings>> FormAsync(ApiDbContext db, Guid playerId, CancellationToken ct)
    {
        var balls = await db.Deliveries.AsNoTracking()
            .Where(d => d.BatterId == playerId)
            .Select(d => new { d.MatchId, d.InningsId, d.Sequence, d.RunsOffBat, d.IsLegalDelivery, d.IsWicket, d.DismissedPlayerId })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (balls.Count == 0)
            return [];

        var matchIds = balls.Select(x => x.MatchId).Distinct().ToList();
        var dates = await db.Matches.AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.MatchDateUtc })
            .ToDictionaryAsync(x => x.Id, x => x.MatchDateUtc, ct)
            .ConfigureAwait(false);

        var rows = balls
            .GroupBy(d => d.InningsId)
            .Select(g =>
            {
                var ordered = g.OrderBy(x => x.Sequence).ToList();
                var runs = ordered.Sum(x => x.RunsOffBat);
                var faced = ordered.Count(x => x.IsLegalDelivery);
                var outThis = ordered.Any(x => x.IsWicket && x.DismissedPlayerId == playerId);
                var matchId = g.First().MatchId;
                var when = dates.GetValueOrDefault(matchId);
                var seq = ordered.Max(x => x.Sequence);
                return new { matchId, when, seq, runs, faced, outThis };
            })
            .OrderByDescending(x => x.when)
            .ThenByDescending(x => x.seq)
            .Take(8)
            .Reverse()
            .Select(x => new PublicFormInnings
            {
                Label = "",
                Runs = x.runs,
                Balls = x.faced,
                NotOut = !x.outThis
            })
            .ToList();
        return rows;
    }

    private static List<string> Insights(PublicPlayerProfile p)
    {
        var list = new List<string>();
        var m = p.Matches;
        var batRank = p.Ranks.FirstOrDefault(r => r.Category == "Batting");
        if (batRank is not null)
            list.Add($"#{batRank.Rank} batter in {p.LeagueName}.");
        var bowlRank = p.Ranks.FirstOrDefault(r => r.Category == "Bowling");
        if (bowlRank is not null)
            list.Add($"#{bowlRank.Rank} bowler in {p.LeagueName}.");
        if (m.InningsBatted > 0 && m.BallsFaced > 0)
            list.Add($"Strike rate {(m.Runs * 100.0 / m.BallsFaced):0.0} from {m.InningsBatted} innings. HS {m.HighestScoreText}.");
        if (m.Wickets > 0)
            list.Add($"Best bowling {m.BestBowlingText} · {m.Wickets} wickets" + (m.HatTricks > 0 ? $" · {m.HatTricks} hat-trick(s)." : "."));
        var potm = p.Honours.FirstOrDefault(h => h.Label.Contains("Player of the Match"));
        if (potm is not null)
            list.Add($"{potm.Count}× Player of the Match.");
        if (p.Nets is { Sessions: > 0 })
            list.Add($"Nets: {p.Nets.Runs} runs and {p.Nets.Wickets} wickets across {p.Nets.Sessions} sessions.");
        if (list.Count == 0)
            list.Add("Career is just getting started. Stats unlock as matches are scored.");
        return list.Take(5).ToList();
    }

    private static PublicDuel ToDuel(ConfrontationDto d, string opponent) => new()
    {
        Opponent = opponent,
        Balls = d.Balls,
        Runs = d.Runs,
        Outs = d.Outs,
        Dots = d.Dots,
        Ones = d.Ones,
        Twos = d.Twos,
        Threes = d.Threes,
        Fours = d.Fours,
        Sixes = d.Sixes,
        Bowled = d.Bowled,
        Caught = d.Caught,
        Lbw = d.Lbw,
        RunOut = d.RunOut,
        Stumped = d.Stumped,
        HitWicket = d.HitWicket,
        ExtrasConceded = d.ExtrasConceded,
        Line = $"{d.Runs} ({d.Balls})" + (d.Outs > 0 ? $" · {d.Outs} out" : "")
    };

    private static string? BlankToNull(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
