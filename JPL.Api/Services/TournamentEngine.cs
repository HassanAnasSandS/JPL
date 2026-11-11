using System.Text.Json;
using JPL.Api.Data;
using JPL.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JPL.Api.Services;

/// <summary>Round-robin schedule + standings + knockout resolution for tournaments.</summary>
public static class TournamentEngine
{
    public const int PointsWin = 2;
    public const int PointsTie = 1;

    public const byte RoundRobinSingle = 0;
    public const byte RoundRobinDouble = 1;
    public const byte RoundRobinCustom = 2;

    public static string RoundRobinLabel(byte type) => type switch
    {
        RoundRobinDouble => "Double Round Robin",
        RoundRobinCustom => "Custom",
        _ => "Single Round Robin"
    };

    public const byte KnockoutQuarterFinals = 0;
    public const byte KnockoutPlayoff = 1;
    public const byte KnockoutSemiFinals = 2;
    public const byte KnockoutFinalOnly = 3;
    public const byte KnockoutNone = 4;

    public static int KnockoutTeamCount(byte knockout) => knockout switch
    {
        KnockoutNone => 0,
        KnockoutQuarterFinals => 8,
        KnockoutPlayoff => 4,
        KnockoutSemiFinals => 4,
        KnockoutFinalOnly => 2,
        _ => 2
    };

    public static string KnockoutLabel(byte knockout) => knockout switch
    {
        KnockoutNone => "No",
        KnockoutQuarterFinals => "Quarter Finals → Semi Finals → Final",
        KnockoutPlayoff => "Playoff → Semi Final → Final",
        KnockoutSemiFinals => "Semi Finals → Final",
        KnockoutFinalOnly => "Final",
        _ => knockout.ToString()
    };

    public static IReadOnlyList<int> QualifyOptions(int groupCount, byte knockout)
    {
        if (groupCount is not (1 or 2 or 4))
            return Array.Empty<int>();
        if (knockout == KnockoutNone)
            return new[] { 0 };
        var need = KnockoutTeamCount(knockout);
        if (need % groupCount != 0)
            return Array.Empty<int>();
        var qualify = need / groupCount;
        return qualify >= 1 ? new[] { qualify } : Array.Empty<int>();
    }

    public static void ValidateSetup(
        int groupCount,
        int qualifyPerGroup,
        IReadOnlyList<TournamentTeam> teams,
        byte roundRobinType = RoundRobinSingle,
        byte knockoutFormat = KnockoutSemiFinals)
    {
        if (roundRobinType == RoundRobinCustom)
        {
            if (groupCount != 1)
                throw new InvalidOperationException("Custom type allows only 1 group.");
        }
        else if (groupCount is not (1 or 2 or 4))
            throw new InvalidOperationException("Groups must be 1, 2, or 4.");

        if (knockoutFormat == KnockoutNone)
        {
            if (qualifyPerGroup != 0)
                throw new InvalidOperationException("With Knockout = No, qualification is not used (set to 0).");
        }
        else
        {
            var options = QualifyOptions(groupCount, knockoutFormat);
            if (options.Count == 0 || !options.Contains(qualifyPerGroup))
                throw new InvalidOperationException(
                    $"{KnockoutLabel(knockoutFormat)} does not fit {groupCount} group{(groupCount == 1 ? "" : "s")}.");
        }

        if (teams.Count == 0)
            throw new InvalidOperationException("Add at least one team to the tournament.");

        for (var g = 1; g <= groupCount; g++)
        {
            var count = teams.Count(t => t.GroupNumber == g);
            var min = knockoutFormat == KnockoutNone ? 2 : Math.Max(2, qualifyPerGroup);
            if (count < min)
                throw new InvalidOperationException(
                    roundRobinType == RoundRobinCustom
                        ? $"Custom needs at least {min} teams (currently {count})."
                        : $"Group {g} needs at least {min} team{(min == 1 ? "" : "s")} (currently {count}).");
        }

        if (teams.Any(t => t.GroupNumber < 1 || t.GroupNumber > groupCount))
            throw new InvalidOperationException("Every team must be assigned to a valid group.");

        if (teams.Select(t => t.TeamId).Distinct().Count() != teams.Count)
            throw new InvalidOperationException("Duplicate teams are not allowed.");

        if (knockoutFormat != KnockoutNone)
        {
            var need = KnockoutTeamCount(knockoutFormat);
            if (teams.Count < need)
                throw new InvalidOperationException(
                    $"{KnockoutLabel(knockoutFormat)} needs {need} teams (currently {teams.Count}).");
        }
    }

    public static IReadOnlyList<int> CustomMatchesPerTeamOptions(int teamCount)
    {
        if (teamCount < 2)
            return Array.Empty<int>();

        var list = new List<int>();
        for (var n = 1; n <= teamCount - 1; n++)
        {
            if ((teamCount * n) % 2 == 0)
                list.Add(n);
        }

        return list;
    }

    public static int ExpectedCustomMatchCount(int teamCount, int matchesPerTeam)
    {
        if (teamCount < 2 || matchesPerTeam < 1)
            return 0;
        var slots = teamCount * matchesPerTeam;
        return slots % 2 == 0 ? slots / 2 : 0;
    }

    public static void ValidateCustomPairings(
        IReadOnlyList<Guid> teamIds,
        int matchesPerTeam,
        IReadOnlyList<(Guid A, Guid B)> pairs)
    {
        if (teamIds.Count < 2)
            throw new InvalidOperationException("Custom needs at least two teams.");
        if (matchesPerTeam < 1)
            throw new InvalidOperationException("Select matches per team for Custom.");
        if (matchesPerTeam >= teamIds.Count)
            throw new InvalidOperationException(
                $"With {teamIds.Count} teams, matches per team can be at most {teamIds.Count - 1}.");

        var options = CustomMatchesPerTeamOptions(teamIds.Count);
        if (!options.Contains(matchesPerTeam))
            throw new InvalidOperationException(
                $"Matches per team {matchesPerTeam} is not possible with {teamIds.Count} teams.");

        var expected = ExpectedCustomMatchCount(teamIds.Count, matchesPerTeam);
        if (pairs.Count != expected)
            throw new InvalidOperationException(
                $"Custom needs exactly {expected} match{(expected == 1 ? "" : "es")} ({matchesPerTeam} per team). Currently {pairs.Count}.");

        var idSet = teamIds.ToHashSet();
        var degree = teamIds.ToDictionary(id => id, _ => 0);
        var seen = new HashSet<(Guid, Guid)>();

        foreach (var (a, b) in pairs)
        {
            if (a == b)
                throw new InvalidOperationException("A team cannot play itself.");
            if (!idSet.Contains(a) || !idSet.Contains(b))
                throw new InvalidOperationException("Custom fixture uses an unknown team.");

            var key = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
            if (!seen.Add(key))
                throw new InvalidOperationException("Duplicate custom fixture (same two teams).");

            degree[a]++;
            degree[b]++;
        }

        foreach (var id in teamIds)
        {
            if (degree[id] != matchesPerTeam)
                throw new InvalidOperationException(
                    $"Every team must have exactly {matchesPerTeam} match{(matchesPerTeam == 1 ? "" : "es")}.");
        }
    }

    public static bool IsCustomPairingComplete(
        IReadOnlyList<Guid> teamIds,
        int matchesPerTeam,
        IReadOnlyList<(Guid A, Guid B)> pairs)
    {
        try
        {
            ValidateCustomPairings(teamIds, matchesPerTeam, pairs);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string CustomPairingHint(
        IReadOnlyList<Guid> teamIds,
        int matchesPerTeam,
        IReadOnlyList<(Guid A, Guid B)> pairs)
    {
        if (teamIds.Count < 2)
            return "Add at least two teams, then pick matches per team and fixtures.";

        var options = CustomMatchesPerTeamOptions(teamIds.Count);
        if (matchesPerTeam < 1 || !options.Contains(matchesPerTeam))
            return options.Count == 0
                ? "Not enough teams for a custom schedule."
                : $"Pick matches per team ({string.Join("/", options)}).";

        var expected = ExpectedCustomMatchCount(teamIds.Count, matchesPerTeam);
        if (pairs.Count > expected)
            return $"Too many fixtures — need exactly {expected} (currently {pairs.Count}).";

        var idSet = teamIds.ToHashSet();
        var degree = teamIds.ToDictionary(id => id, _ => 0);
        var seen = new HashSet<(Guid, Guid)>();
        foreach (var (a, b) in pairs)
        {
            if (a == b || !idSet.Contains(a) || !idSet.Contains(b))
                return "Remove invalid fixtures (unknown team or self-match).";
            var key = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
            if (!seen.Add(key))
                return "Remove duplicate fixtures (same two teams).";
            degree[a]++;
            degree[b]++;
        }

        if (degree.Values.Any(d => d > matchesPerTeam))
            return $"A team has more than {matchesPerTeam} matches — remove a fixture.";

        var remaining = expected - pairs.Count;
        if (remaining == 0 && degree.Values.All(d => d == matchesPerTeam))
            return $"Ready — {expected} custom match{(expected == 1 ? "" : "es")}, {matchesPerTeam} per team.";

        return remaining == 1
            ? $"Add 1 more match ({matchesPerTeam} per team)."
            : $"Add {remaining} more matches ({matchesPerTeam} per team).";
    }

    public static List<(Guid A, Guid B)> ReadCustomPairs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<(Guid A, Guid B)>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;
                if (!el.TryGetProperty("a", out var aEl) || !el.TryGetProperty("b", out var bEl))
                    continue;
                if (!Guid.TryParse(aEl.GetString(), out var a) || !Guid.TryParse(bEl.GetString(), out var b))
                    continue;
                list.Add((a, b));
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    public static string WriteCustomPairs(IEnumerable<(Guid A, Guid B)> pairs)
        => JsonSerializer.Serialize(pairs.Select(p => new { a = p.A, b = p.B }).ToList());

    public static List<TournamentFixture> BuildFixtures(
        Tournament tournament,
        IReadOnlyDictionary<Guid, string>? teamNames = null)
    {
        var fixtures = new List<TournamentFixture>();
        var seq = 1;

        if (tournament.RoundRobinType == RoundRobinCustom)
        {
            foreach (var (a, b) in ReadCustomPairs(tournament.CustomPairsJson))
            {
                fixtures.Add(new TournamentFixture
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournament.Id,
                    Stage = TournamentFixtureStage.Group,
                    GroupNumber = 1,
                    Sequence = seq++,
                    Label = "Custom",
                    TeamAId = a,
                    TeamBId = b
                });
            }
        }
        else
        {
            var rrLegs = tournament.RoundRobinType == RoundRobinDouble ? 2 : 1;

            for (var g = 1; g <= tournament.GroupCount; g++)
            {
                var groupTeams = tournament.Teams
                    .Where(t => t.GroupNumber == g)
                    .OrderBy(t => teamNames?.GetValueOrDefault(t.TeamId) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.TeamId)
                    .Select(t => t.TeamId)
                    .ToList();

                for (var leg = 0; leg < rrLegs; leg++)
                {
                    foreach (var round in BuildCircleRounds(groupTeams))
                    {
                        foreach (var (home, away) in round)
                        {
                            var a = leg == 0 ? home : away;
                            var b = leg == 0 ? away : home;
                            var suffix = rrLegs == 2 ? $" · Leg {leg + 1}" : "";
                            fixtures.Add(new TournamentFixture
                            {
                                Id = Guid.NewGuid(),
                                TournamentId = tournament.Id,
                                Stage = TournamentFixtureStage.Group,
                                GroupNumber = g,
                                Sequence = seq++,
                                Label = tournament.GroupCount == 1
                                    ? $"Round robin{suffix}"
                                    : $"Group {g}{suffix}",
                                TeamAId = a,
                                TeamBId = b
                            });
                        }
                    }
                }
            }
        }

        // Draft Custom with no pairings yet — no knockout skeleton.
        if (tournament.RoundRobinType == RoundRobinCustom
            && string.IsNullOrWhiteSpace(tournament.CustomPairsJson))
            return fixtures;

        fixtures.AddRange(BuildKnockoutSkeleton(tournament, ref seq));
        return fixtures;
    }

    private static List<TournamentFixture> BuildKnockoutSkeleton(Tournament tournament, ref int seq)
    {
        var list = new List<TournamentFixture>();
        switch (tournament.KnockoutFormat)
        {
            case KnockoutNone:
                return list;
            case KnockoutQuarterFinals:
            {
                OpeningQuarterSlots(tournament, out var q1a, out var q1b, out var q2a, out var q2b,
                    out var q3a, out var q3b, out var q4a, out var q4b);
                list.Add(Ko(tournament, TournamentFixtureStage.QuarterFinal, ref seq, "Quarter-final 1", q1a, q1b));
                list.Add(Ko(tournament, TournamentFixtureStage.QuarterFinal, ref seq, "Quarter-final 2", q2a, q2b));
                list.Add(Ko(tournament, TournamentFixtureStage.QuarterFinal, ref seq, "Quarter-final 3", q3a, q3b));
                list.Add(Ko(tournament, TournamentFixtureStage.QuarterFinal, ref seq, "Quarter-final 4", q4a, q4b));
                list.Add(Ko(tournament, TournamentFixtureStage.SemiFinal, ref seq, "Semi-final 1", "QF1 Winner", "QF2 Winner"));
                list.Add(Ko(tournament, TournamentFixtureStage.SemiFinal, ref seq, "Semi-final 2", "QF3 Winner", "QF4 Winner"));
                list.Add(Ko(tournament, TournamentFixtureStage.Final, ref seq, "Final", "SF1 Winner", "SF2 Winner"));
                break;
            }
            case KnockoutPlayoff:
            {
                OpeningPlayoffSlots(tournament, out var p1a, out var p1b, out var p2a, out var p2b);
                list.Add(Ko(tournament, TournamentFixtureStage.Playoff, ref seq, "Playoff 1", p1a, p1b));
                list.Add(Ko(tournament, TournamentFixtureStage.Playoff, ref seq, "Playoff 2", p2a, p2b));
                list.Add(Ko(tournament, TournamentFixtureStage.SemiFinal, ref seq, "Semi-final", "Playoff 1 Loser", "Playoff 2 Winner"));
                list.Add(Ko(tournament, TournamentFixtureStage.Final, ref seq, "Final", "Playoff 1 Winner", "Semi Winner"));
                break;
            }
            case KnockoutSemiFinals:
            {
                OpeningSemiSlots(tournament, out var a1, out var b1, out var a2, out var b2);
                list.Add(Ko(tournament, TournamentFixtureStage.SemiFinal, ref seq, "Semi-final 1", a1, b1));
                list.Add(Ko(tournament, TournamentFixtureStage.SemiFinal, ref seq, "Semi-final 2", a2, b2));
                list.Add(Ko(tournament, TournamentFixtureStage.Final, ref seq, "Final", "SF1 Winner", "SF2 Winner"));
                break;
            }
            default:
            {
                OpeningFinalSlots(tournament, out var a, out var b);
                list.Add(Ko(tournament, TournamentFixtureStage.Final, ref seq, "Final", a, b));
                break;
            }
        }

        return list;
    }

    private static TournamentFixture Ko(
        Tournament tournament,
        TournamentFixtureStage stage,
        ref int seq,
        string label,
        string slotA,
        string slotB) => new()
    {
        Id = Guid.NewGuid(),
        TournamentId = tournament.Id,
        Stage = stage,
        Sequence = seq++,
        Label = label,
        TeamASlot = slotA,
        TeamBSlot = slotB
    };

    private static string GroupTop(int group, int rank) => $"Group {group} (Top {rank})";
    private static string SeedSlot(int seed) => $"Seed {seed}";

    private static void OpeningSemiSlots(
        Tournament tournament, out string a1, out string b1, out string a2, out string b2)
    {
        if (tournament.GroupCount == 2)
        {
            a1 = GroupTop(1, 1); b1 = GroupTop(2, 2);
            a2 = GroupTop(1, 2); b2 = GroupTop(2, 1);
            return;
        }

        if (tournament.GroupCount == 4)
        {
            a1 = GroupTop(1, 1); b1 = GroupTop(4, 1);
            a2 = GroupTop(2, 1); b2 = GroupTop(3, 1);
            return;
        }

        a1 = SeedSlot(1); b1 = SeedSlot(4);
        a2 = SeedSlot(2); b2 = SeedSlot(3);
    }

    private static void OpeningPlayoffSlots(
        Tournament tournament, out string a1, out string b1, out string a2, out string b2)
    {
        if (tournament.GroupCount == 2)
        {
            a1 = GroupTop(1, 1); b1 = GroupTop(2, 2);
            a2 = GroupTop(1, 2); b2 = GroupTop(2, 1);
            return;
        }

        a1 = SeedSlot(1); b1 = SeedSlot(2);
        a2 = SeedSlot(3); b2 = SeedSlot(4);
    }

    private static void OpeningFinalSlots(Tournament tournament, out string a, out string b)
    {
        if (tournament.GroupCount == 2)
        {
            a = GroupTop(1, 1);
            b = GroupTop(2, 1);
            return;
        }

        a = SeedSlot(1);
        b = SeedSlot(2);
    }

    private static void OpeningQuarterSlots(
        Tournament tournament,
        out string a1, out string b1, out string a2, out string b2,
        out string a3, out string b3, out string a4, out string b4)
    {
        if (tournament.GroupCount == 2)
        {
            a1 = GroupTop(1, 1); b1 = GroupTop(2, 4);
            a2 = GroupTop(1, 4); b2 = GroupTop(2, 1);
            a3 = GroupTop(1, 2); b3 = GroupTop(2, 3);
            a4 = GroupTop(1, 3); b4 = GroupTop(2, 2);
            return;
        }

        if (tournament.GroupCount == 4)
        {
            a1 = GroupTop(1, 1); b1 = GroupTop(4, 2);
            a2 = GroupTop(2, 2); b2 = GroupTop(3, 1);
            a3 = GroupTop(1, 2); b3 = GroupTop(4, 1);
            a4 = GroupTop(2, 1); b4 = GroupTop(3, 2);
            return;
        }

        a1 = SeedSlot(1); b1 = SeedSlot(8);
        a2 = SeedSlot(4); b2 = SeedSlot(5);
        a3 = SeedSlot(2); b3 = SeedSlot(7);
        a4 = SeedSlot(3); b4 = SeedSlot(6);
    }

    /// <summary>
    /// Circle method — mirrors SchedulerEngine so Day 1/2 grouping lines up on the client.
    /// </summary>
    public static List<List<(Guid A, Guid B)>> BuildCircleRounds(IReadOnlyList<Guid> teams)
    {
        var result = new List<List<(Guid A, Guid B)>>();
        if (teams.Count < 2)
            return result;

        var list = teams.ToList();
        if (list.Count % 2 == 1)
            list.Add(Guid.Empty);

        var n = list.Count;
        var roundCount = n - 1;
        var half = n / 2;
        for (var r = 0; r < roundCount; r++)
        {
            var round = new List<(Guid A, Guid B)>();
            for (var i = 0; i < half; i++)
            {
                var a = list[i];
                var b = list[n - 1 - i];
                if (a == Guid.Empty || b == Guid.Empty)
                    continue;
                if ((r & 1) == 1)
                    (a, b) = (b, a);
                round.Add((a, b));
            }

            result.Add(round);
            var last = list[n - 1];
            for (var i = n - 1; i > 1; i--)
                list[i] = list[i - 1];
            list[1] = last;
        }

        return result;
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
            if (tournament.KnockoutFormat == KnockoutNone)
            {
                var top = standings.FirstOrDefault(s => s.GroupNumber == 1)
                          ?? standings.FirstOrDefault();
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

        if (tournament.Status == TournamentStatus.Completed)
        {
            var completedMatchIds = matches
                .Where(m => m.Status == MatchStatus.Completed)
                .Select(m => m.Id)
                .ToHashSet();
            var deliveries = innings
                .Where(i => completedMatchIds.Contains(i.MatchId))
                .SelectMany(i => i.Deliveries)
                .ToList();
            if (EventAwardsCalculator.TryAssignTournament(tournament, deliveries))
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
        var seeds = FlattenSeeds(standings, KnockoutTeamCount(tournament.KnockoutFormat));

        Guid? Resolve(string? slot)
        {
            if (string.IsNullOrWhiteSpace(slot))
                return null;
            var s = slot.Trim();

            if (TrySeedSlot(s, out var seed))
                return seed <= seeds.Count ? seeds[seed - 1] : null;

            if (TryGroupTopSlot(s, out var group, out var rank))
                return standings.Where(r => r.GroupNumber == group).Skip(rank - 1).FirstOrDefault()?.TeamId;

            // Legacy "G1 #1"
            if (s.Length >= 4 && (s[0] is 'G' or 'g') && s.Contains('#'))
            {
                var hash = s.IndexOf('#');
                if (int.TryParse(s.AsSpan(1, hash - 1), out var g)
                    && int.TryParse(s.AsSpan(hash + 1), out var r))
                    return standings.Where(x => x.GroupNumber == g).Skip(r - 1).FirstOrDefault()?.TeamId;
            }

            if (s.EndsWith(" Winner", StringComparison.OrdinalIgnoreCase)
                || s.EndsWith(" Loser", StringComparison.OrdinalIgnoreCase))
            {
                var wantLoser = s.EndsWith(" Loser", StringComparison.OrdinalIgnoreCase);
                var prefix = s[..s.LastIndexOf(' ')].Trim();
                var source = FindByLabelPrefix(fixtures, prefix);
                if (source is null)
                    return null;
                var winner = WinnerOf(source, matchById, innsByMatch, standings);
                if (winner is null)
                    return null;
                if (!wantLoser)
                    return winner;
                if (source.TeamAId == winner)
                    return source.TeamBId;
                if (source.TeamBId == winner)
                    return source.TeamAId;
                return null;
            }

            return null;
        }

        foreach (var f in fixtures.Where(x => x.Stage != TournamentFixtureStage.Group).OrderBy(x => x.Sequence))
        {
            var a = Resolve(f.TeamASlot);
            var b = Resolve(f.TeamBSlot);
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

    private static List<Guid> FlattenSeeds(List<TournamentStandingRow> standings, int need)
    {
        if (need <= 0)
            return new List<Guid>();
        // Seeds from group stage only (standings never include knockout results).
        return standings
            .OrderBy(s => s.GroupNumber)
            .ThenByDescending(s => s.Points)
            .ThenByDescending(s => s.NetRunRate)
            .ThenByDescending(s => s.Won)
            .ThenByDescending(s => s.RunsFor)
            .ThenBy(s => s.TeamName, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.TeamId)
            .Take(need)
            .ToList();
    }

    private static TournamentFixture? FindByLabelPrefix(List<TournamentFixture> fixtures, string prefix)
    {
        var p = prefix.Trim();
        var label = p.ToUpperInvariant() switch
        {
            "SF1" => "Semi-final 1",
            "SF2" => "Semi-final 2",
            "QF1" => "Quarter-final 1",
            "QF2" => "Quarter-final 2",
            "QF3" => "Quarter-final 3",
            "QF4" => "Quarter-final 4",
            "SEMI" => "Semi-final",
            _ => p
        };
        return fixtures.FirstOrDefault(f =>
                   string.Equals(f.Label, label, StringComparison.OrdinalIgnoreCase))
               ?? fixtures.FirstOrDefault(f =>
                   f.Label.StartsWith(label, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TrySeedSlot(string slot, out int seed)
    {
        seed = 0;
        if (!slot.StartsWith("Seed ", StringComparison.OrdinalIgnoreCase))
            return false;
        return int.TryParse(slot.AsSpan("Seed ".Length), out seed);
    }

    private static bool TryGroupTopSlot(string slot, out int group, out int rank)
    {
        group = 0;
        rank = 0;
        if (!slot.StartsWith("Group ", StringComparison.OrdinalIgnoreCase))
            return false;
        var open = slot.IndexOf('(');
        var close = slot.IndexOf(')');
        if (open < 0 || close < open)
            return false;
        if (!int.TryParse(slot.AsSpan("Group ".Length, open - "Group ".Length).Trim(), out group))
            return false;
        var inner = slot.AsSpan(open + 1, close - open - 1).Trim();
        if (!inner.StartsWith("Top ", StringComparison.OrdinalIgnoreCase))
            return false;
        return int.TryParse(inner["Top ".Length..].Trim(), out rank) && group > 0 && rank > 0;
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
