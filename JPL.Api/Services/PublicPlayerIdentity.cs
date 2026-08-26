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
    public int Runs { get; set; }
    public int Wickets { get; set; }
    public string Headline { get; set; } = "";
}

public sealed class PublicPlayerDirectory
{
    public Guid LeagueId { get; set; }
    public string LeagueName { get; set; } = "";
    public int PlayerCount { get; set; }
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
        var teams = await db.Teams.AsNoTracking()
            .Where(t => !t.IsDeleted)
            .Include(t => t.TeamPlayers)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var cards = new List<PublicPlayerCard>();
        foreach (var p in players)
        {
            byId.TryGetValue(p.Id, out var career);
            var team = teams.FirstOrDefault(t => t.TeamPlayers.Any(tp => tp.PlayerId == p.Id));
            var runs = career?.Runs ?? 0;
            var wkts = career?.Wickets ?? 0;
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
                Runs = runs,
                Wickets = wkts,
                Headline = runs + wkts == 0
                    ? (BlankToNull(p.PlayingRole) ?? "Cric360 player")
                    : $"{runs} runs · {wkts} wkts"
            });
        }

        return new PublicPlayerDirectory
        {
            LeagueId = leagueId,
            LeagueName = leagueName,
            PlayerCount = cards.Count,
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
            VsBowlers = vsBowl.OrderByDescending(d => d.Balls).Take(5).Select(d => new PublicDuel
            {
                Opponent = d.BowlerName,
                Balls = d.Balls,
                Runs = d.Runs,
                Outs = d.Outs,
                Line = $"{d.Runs} ({d.Balls})" + (d.Outs > 0 ? $" · {d.Outs} out" : "")
            }).ToList(),
            VsBatters = vsBat.OrderByDescending(d => d.Outs).ThenByDescending(d => d.Balls).Take(5).Select(d => new PublicDuel
            {
                Opponent = d.BatterName,
                Balls = d.Balls,
                Runs = d.Runs,
                Outs = d.Outs,
                Line = $"{d.Outs} wkts · {d.Runs} runs ({d.Balls}b)"
            }).ToList()
        };
        profile.Insights = Insights(profile);
        return profile;
    }

    private static List<PublicRankBadge> Badges(Guid playerId, RankingsDto ranks)
    {
        var list = new List<PublicRankBadge>();
        void Add(string cat, IReadOnlyList<RankingRowDto> rows)
        {
            var row = rows.FirstOrDefault(r => r.PlayerId == playerId);
            if (row is not null)
                list.Add(new PublicRankBadge { Category = cat, Rank = row.Rank, Detail = row.Detail });
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
            list.Add($"#{batRank.Rank} batter in {p.LeagueName} — open ranking, no PRO paywall.");
        var bowlRank = p.Ranks.FirstOrDefault(r => r.Category == "Bowling");
        if (bowlRank is not null)
            list.Add($"#{bowlRank.Rank} bowler in this league.");
        if (m.InningsBatted > 0 && m.BallsFaced > 0)
            list.Add($"Strike rate {(m.Runs * 100.0 / m.BallsFaced):0.0} from {m.InningsBatted} innings. HS {m.HighestScoreText}.");
        if (m.Wickets > 0)
            list.Add($"Best bowling {m.BestBowlingText} · {m.Wickets} wickets.");
        var potm = p.Honours.FirstOrDefault(h => h.Label.Contains("Player of the Match"));
        if (potm is not null)
            list.Add($"{potm.Count}× Player of the Match.");
        if (p.Nets is { Sessions: > 0 })
            list.Add($"Nets: {p.Nets.Runs} runs and {p.Nets.Wickets} wickets across {p.Nets.Sessions} sessions — CricHeroes has no nets identity.");
        if (list.Count == 0)
            list.Add("Career is just getting started. Stats unlock as matches are scored.");
        return list.Take(5).ToList();
    }

    private static string? BlankToNull(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
