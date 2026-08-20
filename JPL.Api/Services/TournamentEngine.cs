using JPL.Api.Data;
using JPL.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JPL.Api.Services;

/// <summary>Round-robin schedule + standings + knockout resolution for tournaments.</summary>
public static class TournamentEngine
{
    public const int PointsWin = 2;
    public const int PointsTie = 1;

    public static void ValidateSetup(int groupCount, int qualifyPerGroup, IReadOnlyList<TournamentTeam> teams)
    {
        if (groupCount is < 1 or > 2)
            throw new InvalidOperationException("Tournament must have 1 or 2 groups.");
        if (qualifyPerGroup is < 1 or > 2)
            throw new InvalidOperationException("Qualify per group must be 1 or 2.");
        if (teams.Count == 0)
            throw new InvalidOperationException("Add at least one team to the tournament.");

        for (var g = 1; g <= groupCount; g++)
        {
            var count = teams.Count(t => t.GroupNumber == g);
            if (count < qualifyPerGroup)
                throw new InvalidOperationException(
                    $"Group {g} needs at least {qualifyPerGroup} team(s) (currently {count}).");
        }

        if (teams.Any(t => t.GroupNumber < 1 || t.GroupNumber > groupCount))
            throw new InvalidOperationException("Every team must be assigned to a valid group.");

        if (teams.Select(t => t.TeamId).Distinct().Count() != teams.Count)
            throw new InvalidOperationException("Duplicate teams are not allowed.");
    }

    public static List<TournamentFixture> BuildFixtures(Tournament tournament)
    {
        var fixtures = new List<TournamentFixture>();
        var seq = 1;

        for (var g = 1; g <= tournament.GroupCount; g++)
        {
            var groupTeams = tournament.Teams
                .Where(t => t.GroupNumber == g)
                .Select(t => t.TeamId)
                .OrderBy(id => id)
                .ToList();

            for (var i = 0; i < groupTeams.Count; i++)
            {
                for (var j = i + 1; j < groupTeams.Count; j++)
                {
                    fixtures.Add(new TournamentFixture
                    {
                        Id = Guid.NewGuid(),
                        TournamentId = tournament.Id,
                        Stage = TournamentFixtureStage.Group,
                        GroupNumber = g,
                        Sequence = seq++,
                        Label = $"Group {g} · RR",
                        TeamAId = groupTeams[i],
                        TeamBId = groupTeams[j]
                    });
                }
            }
        }

        if (tournament.GroupCount == 1 && tournament.QualifyPerGroup == 1)
        {
            // No knockout — group leader is champion when RR completes.
            return fixtures;
        }

        if (tournament.GroupCount == 1 && tournament.QualifyPerGroup == 2)
        {
            fixtures.Add(new TournamentFixture
            {
                Id = Guid.NewGuid(),
                TournamentId = tournament.Id,
                Stage = TournamentFixtureStage.Final,
                Sequence = seq++,
                Label = "Final",
                TeamASlot = "G1 #1",
                TeamBSlot = "G1 #2"
            });
            return fixtures;
        }

        if (tournament.GroupCount == 2 && tournament.QualifyPerGroup == 1)
        {
            fixtures.Add(new TournamentFixture
            {
                Id = Guid.NewGuid(),
                TournamentId = tournament.Id,
                Stage = TournamentFixtureStage.Final,
                Sequence = seq++,
                Label = "Final",
                TeamASlot = "G1 #1",
                TeamBSlot = "G2 #1"
            });
            return fixtures;
        }

        // 2 groups × 2 qualify → semis then final
        fixtures.Add(new TournamentFixture
        {
            Id = Guid.NewGuid(),
            TournamentId = tournament.Id,
            Stage = TournamentFixtureStage.SemiFinal,
            Sequence = seq++,
            Label = "Semi-final 1",
            TeamASlot = "G1 #1",
            TeamBSlot = "G2 #2"
        });
        fixtures.Add(new TournamentFixture
        {
            Id = Guid.NewGuid(),
            TournamentId = tournament.Id,
            Stage = TournamentFixtureStage.SemiFinal,
            Sequence = seq++,
            Label = "Semi-final 2",
            TeamASlot = "G2 #1",
            TeamBSlot = "G1 #2"
        });
        fixtures.Add(new TournamentFixture
        {
            Id = Guid.NewGuid(),
            TournamentId = tournament.Id,
            Stage = TournamentFixtureStage.Final,
            Sequence = seq++,
            Label = "Final",
            TeamASlot = "SF1 Winner",
            TeamBSlot = "SF2 Winner"
        });
        return fixtures;
    }

    public static async Task<(
            List<TournamentStandingRow> Standings,
            bool Changed)> ResolveProgressAsync(
        ApiDbContext db,
        Tournament tournament,
        CancellationToken ct)
    {
        var changed = false;
        var fixtures = tournament.Fixtures.OrderBy(f => f.Sequence).ToList();
        var groupFixtures = fixtures.Where(f => f.Stage == TournamentFixtureStage.Group).ToList();

        var matchIds = fixtures.Where(f => f.MatchId is Guid).Select(f => f.MatchId!.Value).Distinct().ToList();
        var matches = matchIds.Count == 0
            ? new List<Match>()
            : await db.Matches.AsNoTracking()
                .Where(m => matchIds.Contains(m.Id) && !m.IsDeleted)
                .ToListAsync(ct).ConfigureAwait(false);
        var matchById = matches.ToDictionary(m => m.Id);

        var innings = matchIds.Count == 0
            ? new List<Innings>()
            : await db.Innings.AsNoTracking()
                .Where(i => matchIds.Contains(i.MatchId))
                .Include(i => i.Deliveries)
                .ToListAsync(ct).ConfigureAwait(false);
        var innsByMatch = innings.GroupBy(i => i.MatchId).ToDictionary(g => g.Key, g => g.ToList());

        var standings = BuildStandings(tournament, groupFixtures, matchById, innsByMatch);

        // Draft has no schedule yet — never crown a champion / change status here.
        // (Empty groupFixtures used to mean "complete", which auto-finished 1-group/1-qualify drafts.)
        if (tournament.Status == TournamentStatus.Draft)
            return (standings, changed);

        // Round robin is complete only when there is at least one group fixture and all are done.
        // Zero fixtures after Save is the single-team edge case (handled in Generate, not here).
        var groupComplete = groupFixtures.Count > 0
            && groupFixtures.All(f =>
                f.MatchId is Guid mid
                && matchById.TryGetValue(mid, out var m)
                && m.Status == MatchStatus.Completed);

        if (groupComplete)
        {
            if (tournament.GroupCount == 1 && tournament.QualifyPerGroup == 1)
            {
                var top = standings.FirstOrDefault(s => s.GroupNumber == 1);
                if (top is not null && tournament.WinnerTeamId != top.TeamId)
                {
                    tournament.WinnerTeamId = top.TeamId;
                    tournament.Status = TournamentStatus.Completed;
                    changed = true;
                }
            }
            else
            {
                changed |= AssignKnockoutTeams(tournament, fixtures, standings, matchById, innsByMatch);
            }
        }

        // Final winner (tied knockout → group points then NRR)
        var final = fixtures.FirstOrDefault(f => f.Stage == TournamentFixtureStage.Final);
        if (final?.MatchId is Guid finalMatchId
            && matchById.TryGetValue(finalMatchId, out var finalMatch)
            && finalMatch.Status == MatchStatus.Completed
            && innsByMatch.TryGetValue(finalMatchId, out var finalInns))
        {
            var champion = KnockoutWinner(finalMatch, finalInns, standings);
            if (champion is Guid w && tournament.WinnerTeamId != w)
            {
                tournament.WinnerTeamId = w;
                tournament.Status = TournamentStatus.Completed;
                changed = true;
            }
        }

        if (tournament.Status == TournamentStatus.Scheduled
            && matches.Any(m => m.Status is MatchStatus.Setup or MatchStatus.InProgress or MatchStatus.Completed))
        {
            tournament.Status = TournamentStatus.InProgress;
            changed = true;
        }

        if (changed)
        {
            tournament.UpdatedAtUtc = DateTime.Now;
            tournament.Version++;
        }

        return (standings, changed);
    }

    private static bool AssignKnockoutTeams(
        Tournament tournament,
        List<TournamentFixture> fixtures,
        List<TournamentStandingRow> standings,
        Dictionary<Guid, Match> matchById,
        Dictionary<Guid, List<Innings>> innsByMatch)
    {
        var changed = false;
        Guid? Pick(int group, int rank)
            => standings.Where(s => s.GroupNumber == group).Skip(rank - 1).FirstOrDefault()?.TeamId;

        foreach (var f in fixtures.Where(x => x.Stage != TournamentFixtureStage.Group))
        {
            Guid? a = null, b = null;
            if (f.Stage == TournamentFixtureStage.Final
                && tournament.GroupCount == 1 && tournament.QualifyPerGroup == 2)
            {
                a = Pick(1, 1);
                b = Pick(1, 2);
            }
            else if (f.Stage == TournamentFixtureStage.Final
                     && tournament.GroupCount == 2 && tournament.QualifyPerGroup == 1)
            {
                a = Pick(1, 1);
                b = Pick(2, 1);
            }
            else if (f.Stage == TournamentFixtureStage.SemiFinal && f.Label.Contains('1'))
            {
                a = Pick(1, 1);
                b = Pick(2, 2);
            }
            else if (f.Stage == TournamentFixtureStage.SemiFinal)
            {
                a = Pick(2, 1);
                b = Pick(1, 2);
            }
            else if (f.Stage == TournamentFixtureStage.Final
                     && tournament.GroupCount == 2 && tournament.QualifyPerGroup == 2)
            {
                var semis = fixtures.Where(x => x.Stage == TournamentFixtureStage.SemiFinal)
                    .OrderBy(x => x.Sequence).ToList();
                if (semis.Count >= 2)
                {
                    a = WinnerOf(semis[0], matchById, innsByMatch, standings);
                    b = WinnerOf(semis[1], matchById, innsByMatch, standings);
                }
            }

            if (a is Guid aa && f.TeamAId != aa)
            {
                f.TeamAId = aa;
                changed = true;
            }
            if (b is Guid bb && f.TeamBId != bb)
            {
                f.TeamBId = bb;
                changed = true;
            }
        }

        return changed;
    }

    private static Guid? WinnerOf(
        TournamentFixture fixture,
        Dictionary<Guid, Match> matchById,
        Dictionary<Guid, List<Innings>> innsByMatch,
        List<TournamentStandingRow> standings)
    {
        if (fixture.MatchId is not Guid mid
            || !matchById.TryGetValue(mid, out var match)
            || match.Status != MatchStatus.Completed
            || !innsByMatch.TryGetValue(mid, out var inns))
            return null;
        return KnockoutWinner(match, inns, standings);
    }

    /// <summary>
    /// Knockout winner: match result, or on a tie the better group-table side (points, then NRR, then runs).
    /// </summary>
    public static Guid? KnockoutWinner(
        Match match,
        List<Innings> inns,
        IReadOnlyList<TournamentStandingRow> standings)
    {
        var (winner, tied) = ResolveWinner(match, inns);
        if (!tied)
            return winner;
        return BetterOnGroupTable(match.TeamAId, match.TeamBId, standings);
    }

    public static Guid? BetterOnGroupTable(
        Guid teamA,
        Guid teamB,
        IReadOnlyList<TournamentStandingRow> standings)
    {
        var a = standings.FirstOrDefault(s => s.TeamId == teamA);
        var b = standings.FirstOrDefault(s => s.TeamId == teamB);
        if (a is null && b is null) return null;
        if (a is null) return teamB;
        if (b is null) return teamA;

        var cmp = a.Points.CompareTo(b.Points);
        if (cmp != 0) return cmp > 0 ? teamA : teamB;
        cmp = a.NetRunRate.CompareTo(b.NetRunRate);
        if (cmp != 0) return cmp > 0 ? teamA : teamB;
        cmp = a.RunsFor.CompareTo(b.RunsFor);
        if (cmp != 0) return cmp > 0 ? teamA : teamB;
        return teamA;
    }

    public static List<TournamentStandingRow> BuildStandings(
        Tournament tournament,
        List<TournamentFixture> groupFixtures,
        Dictionary<Guid, Match> matchById,
        Dictionary<Guid, List<Innings>> innsByMatch)
    {
        var rows = new Dictionary<(int Group, Guid Team), TournamentStandingRow>();
        foreach (var t in tournament.Teams)
        {
            rows[(t.GroupNumber, t.TeamId)] = new TournamentStandingRow
            {
                GroupNumber = t.GroupNumber,
                TeamId = t.TeamId
            };
        }

        foreach (var f in groupFixtures)
        {
            if (f.TeamAId is not Guid a || f.TeamBId is not Guid b)
                continue;
            if (f.MatchId is not Guid mid
                || !matchById.TryGetValue(mid, out var match)
                || match.Status != MatchStatus.Completed
                || !innsByMatch.TryGetValue(mid, out var inns))
                continue;

            var (aRuns, aBalls) = TeamTotals(inns, a);
            var (bRuns, bBalls) = TeamTotals(inns, b);
            var g = f.GroupNumber ?? 1;
            var ra = rows[(g, a)];
            var rb = rows[(g, b)];
            ra.Played++;
            rb.Played++;
            ra.RunsFor += aRuns;
            ra.RunsAgainst += bRuns;
            ra.BallsFor += aBalls;
            ra.BallsAgainst += bBalls;
            rb.RunsFor += bRuns;
            rb.RunsAgainst += aRuns;
            rb.BallsFor += bBalls;
            rb.BallsAgainst += aBalls;

            var (winner, tied) = ResolveWinner(match, inns);
            if (tied)
            {
                ra.Tied++;
                rb.Tied++;
                ra.Points += PointsTie;
                rb.Points += PointsTie;
            }
            else if (winner == a)
            {
                ra.Won++;
                rb.Lost++;
                ra.Points += PointsWin;
            }
            else if (winner == b)
            {
                rb.Won++;
                ra.Lost++;
                rb.Points += PointsWin;
            }
        }

        return rows.Values
            .OrderBy(r => r.GroupNumber)
            .ThenByDescending(r => r.Points)
            .ThenByDescending(r => r.NetRunRate)
            .ThenByDescending(r => r.RunsFor)
            .ToList();
    }

    public static (Guid? Winner, bool Tied) MatchResult(Match match, List<Innings> inns)
        => ResolveWinner(match, inns);

    private static (Guid? Winner, bool Tied) ResolveWinner(Match match, List<Innings> inns)
    {
        var (aRuns, _) = TeamTotals(inns, match.TeamAId);
        var (bRuns, _) = TeamTotals(inns, match.TeamBId);
        if (aRuns == bRuns) return (null, true);
        return (aRuns > bRuns ? match.TeamAId : match.TeamBId, false);
    }

    private static (int Runs, int Balls) TeamTotals(List<Innings> inns, Guid teamId)
    {
        var teamInns = inns.Where(i => i.BattingTeamId == teamId).ToList();
        var runs = teamInns.Sum(i => i.Deliveries.Sum(d => d.RunsOffBat + d.ExtraRuns));
        var balls = teamInns.Sum(i => i.Deliveries.Count(d => d.IsLegalDelivery));
        return (runs, balls);
    }
}

public sealed class TournamentStandingRow
{
    public int GroupNumber { get; set; }
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Tied { get; set; }
    public int Points { get; set; }
    public int RunsFor { get; set; }
    public int RunsAgainst { get; set; }
    public int BallsFor { get; set; }
    public int BallsAgainst { get; set; }
    public double NetRunRate
    {
        get
        {
            var of = BallsFor <= 0 ? 0 : RunsFor / (BallsFor / 6.0);
            var oa = BallsAgainst <= 0 ? 0 : RunsAgainst / (BallsAgainst / 6.0);
            return Math.Round(of - oa, 3);
        }
    }
}
