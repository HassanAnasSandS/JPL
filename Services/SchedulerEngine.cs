using System.Text.Json;
using JPL.Domain;

namespace JPL.Services;

public static class SchedulerEngine
{
    public const int PointsWin = 2;
    public const int PointsLoss = 0;
    public const int PointsTie = 1;

    public static int KnockoutTeamCount(SchedulerKnockoutFormat format) => format switch
    {
        SchedulerKnockoutFormat.None => 0,
        SchedulerKnockoutFormat.QuarterFinals => 8,
        SchedulerKnockoutFormat.Playoff => 4,
        SchedulerKnockoutFormat.SemiFinals => 4,
        SchedulerKnockoutFormat.FinalOnly => 2,
        _ => 2
    };

    public static string KnockoutLabel(SchedulerKnockoutFormat format) => format switch
    {
        SchedulerKnockoutFormat.None => "No",
        SchedulerKnockoutFormat.QuarterFinals => "Quarter Finals → Semi Finals → Final",
        SchedulerKnockoutFormat.Playoff => "Playoff → Semi Final → Final",
        SchedulerKnockoutFormat.SemiFinals => "Semi Finals → Final",
        SchedulerKnockoutFormat.FinalOnly => "Final",
        _ => format.ToString()
    };

    public static string RoundRobinLabel(SchedulerRoundRobinType type) => type switch
    {
        SchedulerRoundRobinType.Double => "Double Round Robin",
        SchedulerRoundRobinType.Custom => "Custom",
        _ => "Single Round Robin"
    };

    public static string StatusText(SchedulerStatus status) => status switch
    {
        SchedulerStatus.GroupStage => "Group stage",
        SchedulerStatus.Knockout => "Knockouts",
        SchedulerStatus.Completed => "Completed",
        _ => "Draft"
    };

    public static IReadOnlyList<int> QualifyOptions(int groupCount, SchedulerKnockoutFormat knockout)
    {
        if (groupCount is not (1 or 2 or 4))
            return Array.Empty<int>();

        if (knockout == SchedulerKnockoutFormat.None)
            return new[] { 0 };

        var need = KnockoutTeamCount(knockout);
        if (need % groupCount != 0)
            return Array.Empty<int>();

        var qualify = need / groupCount;
        return qualify >= 1 ? new[] { qualify } : Array.Empty<int>();
    }

    public static string GroupsTeamsLabel(int groupCount, int teamCount)
    {
        var groups = groupCount == 1 ? "1 group" : $"{groupCount} groups";
        var teams = teamCount == 1 ? "1 team" : $"{teamCount} teams";
        return $"{groups} {teams}";
    }

    public static string SetupHint(
        int groupCount,
        int qualifyPerGroup,
        SchedulerKnockoutFormat knockout,
        SchedulerRoundRobinType rr,
        int? teamCount = null,
        int? matchesPerTeam = null)
    {
        var groups = teamCount is int n
            ? GroupsTeamsLabel(groupCount, n)
            : $"{groupCount} group{(groupCount == 1 ? "" : "s")}";

        var typePart = rr == SchedulerRoundRobinType.Custom && matchesPerTeam is int mpt && mpt > 0
            ? $"{RoundRobinLabel(rr)} ({mpt} matches/team)"
            : RoundRobinLabel(rr);

        if (knockout == SchedulerKnockoutFormat.None)
            return $"{typePart} · {groups} · round matches only — no knockout.";

        var need = KnockoutTeamCount(knockout);
        var guaranteed = groupCount * qualifyPerGroup;
        var fit = guaranteed == need
            ? $"{guaranteed} teams qualify — one per knockout slot."
            : $"{groupCount} × {qualifyPerGroup} = {guaranteed}, but {KnockoutLabel(knockout)} needs exactly {need}.";
        return $"{typePart} · {groups} · top {qualifyPerGroup}/group. {fit}";
    }

    /// <summary>Valid matches-per-team values for Custom (unique opponents, even total edges).</summary>
    public static IReadOnlyList<int> CustomMatchesPerTeamOptions(int teamCount)
    {
        if (teamCount < 2)
            return Array.Empty<int>();

        var list = new List<int>();
        var max = teamCount - 1;
        for (var n = 1; n <= max; n++)
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

    public static void ValidateCustomPairings(
        IReadOnlyList<Guid> teamIds,
        int matchesPerTeam,
        IReadOnlyList<(Guid A, Guid B)> pairs)
    {
        if (teamIds.Count < 2)
            throw new InvalidOperationException("Custom needs at least two teams.");
        if (matchesPerTeam < 1)
            throw new InvalidOperationException("Matches per team must be at least 1.");
        if (matchesPerTeam >= teamIds.Count)
            throw new InvalidOperationException(
                $"With {teamIds.Count} teams, matches per team can be at most {teamIds.Count - 1} (unique opponents).");

        var options = CustomMatchesPerTeamOptions(teamIds.Count);
        if (!options.Contains(matchesPerTeam))
            throw new InvalidOperationException(
                $"Matches per team {matchesPerTeam} is not possible with {teamIds.Count} teams (total match slots must be even).");

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
                    $"Every team must have exactly {matchesPerTeam} match{(matchesPerTeam == 1 ? "" : "es")} (one team has {degree[id]}).");
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

    /// <summary>
    /// Builds a random Custom schedule: each team plays exactly <paramref name="matchesPerTeam"/>
    /// unique opponents. Safe to call repeatedly — each successful run reshuffles.
    /// When <paramref name="avoidSameAs"/> is set, prefers a different pairing set if one exists.
    /// </summary>
    public static IReadOnlyList<(Guid A, Guid B)> GenerateCustomPairings(
        IReadOnlyList<Guid> teamIds,
        int matchesPerTeam,
        IReadOnlyList<(Guid A, Guid B)>? avoidSameAs = null,
        Random? random = null)
    {
        if (teamIds.Count < 2)
            throw new InvalidOperationException("Custom needs at least two teams.");

        var options = CustomMatchesPerTeamOptions(teamIds.Count);
        if (matchesPerTeam < 1 || !options.Contains(matchesPerTeam))
            throw new InvalidOperationException(
                options.Count == 0
                    ? "Not enough teams for a custom schedule."
                    : $"Matches per team must be one of: {string.Join(", ", options)}.");

        var expected = ExpectedCustomMatchCount(teamIds.Count, matchesPerTeam);
        if (expected < 1)
            throw new InvalidOperationException("Cannot build fixtures for this setup.");

        var rng = random ?? Random.Shared;
        var teams = teamIds.Distinct().ToList();
        if (teams.Count != teamIds.Count)
            throw new InvalidOperationException("Duplicate team ids in custom setup.");

        string? avoidKey = avoidSameAs is { Count: > 0 }
            ? CanonicalPairingKey(avoidSameAs)
            : null;

        IReadOnlyList<(Guid A, Guid B)>? firstOk = null;

        // 1) Random greedy draws (fast, often unique).
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (!TryGreedyCustomPairings(teams, matchesPerTeam, expected, rng, out var pairs))
                continue;

            ValidateCustomPairings(teams, matchesPerTeam, pairs);
            firstOk ??= pairs;
            if (avoidKey is null || !string.Equals(CanonicalPairingKey(pairs), avoidKey, StringComparison.Ordinal))
                return pairs;
        }

        // 2) Guaranteed k-regular circle construction + random team permutation.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var pairs = BuildCircleCustomPairings(teams, matchesPerTeam, rng);
            ValidateCustomPairings(teams, matchesPerTeam, pairs);
            firstOk ??= pairs;
            if (avoidKey is null || !string.Equals(CanonicalPairingKey(pairs), avoidKey, StringComparison.Ordinal))
                return pairs;
        }

        if (firstOk is not null)
            return firstOk;

        throw new InvalidOperationException(
            $"Could not auto-build a custom schedule ({matchesPerTeam} matches per team). Try Manual, or change matches per team.");
    }

    private static bool TryGreedyCustomPairings(
        IReadOnlyList<Guid> teams,
        int matchesPerTeam,
        int expected,
        Random rng,
        out List<(Guid A, Guid B)> pairs)
    {
        pairs = new List<(Guid A, Guid B)>(expected);
        var order = teams.OrderBy(_ => rng.Next()).ToList();
        var degree = order.ToDictionary(id => id, _ => 0);
        var edges = new List<(Guid A, Guid B)>();
        for (var i = 0; i < order.Count; i++)
        {
            for (var j = i + 1; j < order.Count; j++)
            {
                var a = order[i];
                var b = order[j];
                edges.Add(a.CompareTo(b) <= 0 ? (a, b) : (b, a));
            }
        }

        for (var i = edges.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (edges[i], edges[j]) = (edges[j], edges[i]);
        }

        foreach (var (a, b) in edges)
        {
            if (degree[a] >= matchesPerTeam || degree[b] >= matchesPerTeam)
                continue;
            pairs.Add((a, b));
            degree[a]++;
            degree[b]++;
            if (pairs.Count == expected)
                break;
        }

        return pairs.Count == expected && degree.Values.All(d => d == matchesPerTeam);
    }

    /// <summary>
    /// Classic circulant k-regular graph on n vertices (requires n·k even), then maps
    /// vertices onto a random team permutation so each Auto run can differ.
    /// </summary>
    private static List<(Guid A, Guid B)> BuildCircleCustomPairings(
        IReadOnlyList<Guid> teams,
        int matchesPerTeam,
        Random rng)
    {
        var n = teams.Count;
        var order = teams.OrderBy(_ => rng.Next()).ToList();
        var pairs = new List<(Guid A, Guid B)>();
        var seen = new HashSet<(Guid, Guid)>();

        void Add(int i, int j)
        {
            var a = order[i];
            var b = order[j];
            var key = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
            if (seen.Add(key))
                pairs.Add(key);
        }

        var half = matchesPerTeam / 2;
        for (var i = 0; i < n; i++)
        {
            for (var step = 1; step <= half; step++)
                Add(i, (i + step) % n);
        }

        if (matchesPerTeam % 2 == 1)
        {
            // Odd degree needs even n (already enforced by CustomMatchesPerTeamOptions).
            for (var i = 0; i < n / 2; i++)
                Add(i, (i + n / 2) % n);
        }

        return pairs;
    }

    private static string CanonicalPairingKey(IReadOnlyList<(Guid A, Guid B)> pairs)
    {
        var keys = pairs
            .Select(p => p.A.CompareTo(p.B) <= 0 ? $"{p.A:D}|{p.B:D}" : $"{p.B:D}|{p.A:D}")
            .OrderBy(s => s, StringComparer.Ordinal);
        return string.Join(";", keys);
    }

    public static int? InferMatchesPerTeam(IReadOnlyList<SchedulerMatch> matches, int teamCount)
    {
        if (teamCount < 2)
            return null;
        var group = matches
            .Where(m => m.Stage == SchedulerMatchStage.Group && m.TeamAId is Guid && m.TeamBId is Guid)
            .ToList();
        if (group.Count == 0)
            return null;
        var slots = group.Count * 2;
        if (slots % teamCount != 0)
            return null;
        var degree = slots / teamCount;
        return degree >= 1 ? degree : null;
    }

    public static int? TryReadCustomMatchesPerTeam(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("mpt", out var mpt)
                && mpt.TryGetInt32(out var n)
                && n > 0)
                return n;
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    public static string WriteCustomMatchesPerTeam(int matchesPerTeam)
        => JsonSerializer.Serialize(new Dictionary<string, int> { ["mpt"] = matchesPerTeam });

    public static void ValidateSetup(
        int groupCount,
        SchedulerRoundRobinType rr,
        int qualifyPerGroup,
        SchedulerKnockoutFormat knockout,
        IReadOnlyList<(string Name, int GroupNumber)> teams)
    {
        if (rr == SchedulerRoundRobinType.Custom)
        {
            if (groupCount != 1)
                throw new InvalidOperationException("Custom type allows only 1 group.");
            if (teams.Any(t => t.GroupNumber != 1))
                throw new InvalidOperationException("Custom type: all teams must be in group 1.");
        }
        else if (groupCount is not (1 or 2 or 4))
            throw new InvalidOperationException("Groups must be 1, 2, or 4.");

        if (knockout == SchedulerKnockoutFormat.None)
        {
            if (qualifyPerGroup != 0)
                throw new InvalidOperationException("With Knockout = No, qualification is not used (set to 0).");
        }
        else if (qualifyPerGroup < 1)
            throw new InvalidOperationException("Qualification must be at least 1 team per group.");
        if (teams.Count < 2)
            throw new InvalidOperationException("Add at least two teams.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var team in teams)
        {
            var name = team.Name.Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Every team needs a name.");
            if (!names.Add(name))
                throw new InvalidOperationException($"Duplicate team name: {name}.");
            if (team.GroupNumber < 1 || team.GroupNumber > groupCount)
                throw new InvalidOperationException($"Assign {name} to a group between 1 and {groupCount}.");
        }

        for (var g = 1; g <= groupCount; g++)
        {
            var count = teams.Count(t => t.GroupNumber == g);
            if (count < 2)
                throw new InvalidOperationException(
                    rr == SchedulerRoundRobinType.Custom
                        ? $"Custom needs at least 2 teams (currently {count})."
                        : $"Group {g} needs at least 2 teams for round robin (currently {count}).");
            if (knockout != SchedulerKnockoutFormat.None && count < qualifyPerGroup)
                throw new InvalidOperationException(
                    $"Group {g} needs at least {qualifyPerGroup} team{(qualifyPerGroup == 1 ? "" : "s")} to qualify that many (currently {count}).");
        }

        if (knockout == SchedulerKnockoutFormat.None)
            return;

        var need = KnockoutTeamCount(knockout);
        var guaranteed = groupCount * qualifyPerGroup;
        if (guaranteed != need)
            throw new InvalidOperationException(
                $"{KnockoutLabel(knockout)} needs exactly {need} teams. With {groupCount} groups, Qualification must be {need / groupCount} per group.");

        if (teams.Count < need)
            throw new InvalidOperationException(
                $"{KnockoutLabel(knockout)} needs {need} teams in the tournament (currently {teams.Count}).");
    }

    public static List<SchedulerMatch> BuildMatches(
        SchedulerTournament tournament,
        IReadOnlyList<(Guid A, Guid B)>? customPairs = null)
    {
        var matches = new List<SchedulerMatch>();
        var seq = 1;

        if (tournament.RoundRobinType == SchedulerRoundRobinType.Custom)
        {
            if (customPairs is { Count: > 0 })
            {
                foreach (var (a, b) in customPairs)
                {
                    matches.Add(new SchedulerMatch
                    {
                        Id = Guid.NewGuid(),
                        TournamentId = tournament.Id,
                        Stage = SchedulerMatchStage.Group,
                        GroupNumber = 1,
                        Sequence = seq++,
                        Label = "Custom",
                        TeamAId = a,
                        TeamBId = b
                    });
                }
            }
        }
        else
        {
            var rrLegs = tournament.RoundRobinType == SchedulerRoundRobinType.Double ? 2 : 1;

            for (var g = 1; g <= tournament.GroupCount; g++)
            {
                var groupTeams = tournament.Teams
                    .Where(t => t.GroupNumber == g)
                    .OrderBy(t => t.SortOrder)
                    .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(t => t.Id)
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
                            matches.Add(new SchedulerMatch
                            {
                                Id = Guid.NewGuid(),
                                TournamentId = tournament.Id,
                                Stage = SchedulerMatchStage.Group,
                                GroupNumber = g,
                                Sequence = seq++,
                                Label = tournament.GroupCount == 1 ? $"Round robin{suffix}" : $"Group {g}{suffix}",
                                TeamAId = a,
                                TeamBId = b
                            });
                        }
                    }
                }
            }
        }

        // Draft Custom (no complete pairings yet) — do not create knockout skeleton.
        if (tournament.RoundRobinType == SchedulerRoundRobinType.Custom
            && (customPairs is null || customPairs.Count == 0))
            return matches;

        matches.AddRange(BuildKnockoutSkeleton(tournament, ref seq));
        return matches;
    }

    /// <summary>
    /// Rebuilds fixtures after roster/setup changes. Surviving group pairings keep their results
    /// (including swapped home/away). Knockouts are kept only when <paramref name="preserveKnockout"/> is true.
    /// </summary>
    public static List<SchedulerMatch> RebuildMatches(
        SchedulerTournament tournament,
        IReadOnlyList<SchedulerMatch> previous,
        bool preserveKnockout,
        IReadOnlyList<(Guid A, Guid B)>? customPairs = null)
    {
        var built = BuildMatches(tournament, customPairs);
        CopyGroupResults(previous, built);
        if (!preserveKnockout)
            return built;

        var group = built.Where(m => m.Stage == SchedulerMatchStage.Group).OrderBy(m => m.Sequence).ToList();
        var oldKo = previous.Where(m => m.Stage != SchedulerMatchStage.Group).OrderBy(m => m.Sequence).ToList();
        if (oldKo.Count == 0)
            return built;

        var seq = group.Count == 0 ? 1 : group[^1].Sequence + 1;
        foreach (var ko in oldKo)
            ko.Sequence = seq++;
        return group.Concat(oldKo).ToList();
    }

    private static void CopyGroupResults(IReadOnlyList<SchedulerMatch> previous, List<SchedulerMatch> built)
    {
        var unused = previous
            .Where(m => m.Stage == SchedulerMatchStage.Group && m.TeamAId is Guid && m.TeamBId is Guid)
            .OrderBy(m => m.Sequence)
            .ToList();
        var used = new HashSet<Guid>();

        foreach (var next in built.Where(m => m.Stage == SchedulerMatchStage.Group))
        {
            if (next.TeamAId is not Guid a || next.TeamBId is not Guid b)
                continue;

            var old = unused.FirstOrDefault(m =>
                !used.Contains(m.Id)
                && m.GroupNumber == next.GroupNumber
                && SameUnorderedPair(m.TeamAId!.Value, m.TeamBId!.Value, a, b));
            if (old is null)
                continue;

            used.Add(old.Id);
            next.Id = old.Id;
            next.Result = SameSides(old, next) ? old.Result : FlipResult(old.Result);
        }
    }

    private static bool SameUnorderedPair(Guid a1, Guid b1, Guid a2, Guid b2)
        => (a1 == a2 && b1 == b2) || (a1 == b2 && b1 == a2);

    private static bool SameSides(SchedulerMatch old, SchedulerMatch next)
        => old.TeamAId == next.TeamAId && old.TeamBId == next.TeamBId;

    private static SchedulerMatchResult FlipResult(SchedulerMatchResult result) => result switch
    {
        SchedulerMatchResult.TeamAWon => SchedulerMatchResult.TeamBWon,
        SchedulerMatchResult.TeamBWon => SchedulerMatchResult.TeamAWon,
        _ => result
    };

    /// <summary>
    /// 1-based play day so each team appears in at most one group match per day (circle method).
    /// Same day number is shared across groups (all groups' round 1 = Day 1).
    /// </summary>
    public static Dictionary<Guid, int> AssignGroupPlayDays(
        IReadOnlyList<SchedulerTeam> teams,
        IReadOnlyList<(Guid Id, int GroupNumber, int Sequence, Guid? TeamAId, Guid? TeamBId)> groupMatches)
    {
        var days = new Dictionary<Guid, int>();
        if (groupMatches.Count == 0)
            return days;

        foreach (var grp in groupMatches.GroupBy(m => m.GroupNumber).OrderBy(g => g.Key))
        {
            var groupTeams = teams
                .Where(t => t.GroupNumber == grp.Key)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.Id)
                .ToList();
            var rounds = BuildCircleRounds(groupTeams);
            var pairLeg = new Dictionary<(Guid, Guid), int>();
            var ordered = grp.OrderBy(m => m.Sequence).ToList();

            foreach (var match in ordered)
            {
                if (match.TeamAId is not Guid a || match.TeamBId is not Guid b)
                {
                    days[match.Id] = 1;
                    continue;
                }

                var pair = NormalizePair(a, b);
                var round = IndexOfPair(rounds, pair);
                if (round < 0)
                {
                    days[match.Id] = NextFreeDay(days, ordered, a, b);
                    continue;
                }

                pairLeg.TryGetValue(pair, out var leg);
                pairLeg[pair] = leg + 1;
                days[match.Id] = round + 1 + (leg * Math.Max(rounds.Count, 1));
            }
        }

        return days;
    }

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

    private static (Guid, Guid) NormalizePair(Guid a, Guid b)
        => a.CompareTo(b) <= 0 ? (a, b) : (b, a);

    private static int IndexOfPair(List<List<(Guid A, Guid B)>> rounds, (Guid, Guid) pair)
    {
        for (var i = 0; i < rounds.Count; i++)
        {
            foreach (var (a, b) in rounds[i])
            {
                if (NormalizePair(a, b).Equals(pair))
                    return i;
            }
        }

        return -1;
    }

    private static int NextFreeDay(
        IReadOnlyDictionary<Guid, int> assigned,
        IReadOnlyList<(Guid Id, int GroupNumber, int Sequence, Guid? TeamAId, Guid? TeamBId)> groupMatches,
        Guid teamA,
        Guid teamB)
    {
        var busy = new HashSet<int>();
        foreach (var m in groupMatches)
        {
            if (!assigned.TryGetValue(m.Id, out var day))
                continue;
            if (m.TeamAId == teamA || m.TeamBId == teamA || m.TeamAId == teamB || m.TeamBId == teamB)
                busy.Add(day);
        }

        var d = 1;
        while (busy.Contains(d))
            d++;
        return d;
    }

    private static List<SchedulerMatch> BuildKnockoutSkeleton(SchedulerTournament tournament, ref int seq)
    {
        var list = new List<SchedulerMatch>();
        switch (tournament.KnockoutFormat)
        {
            case SchedulerKnockoutFormat.None:
                return list;
            case SchedulerKnockoutFormat.QuarterFinals:
            {
                OpeningQuarterSlots(tournament, out var q1a, out var q1b, out var q2a, out var q2b, out var q3a, out var q3b, out var q4a, out var q4b);
                var qf1 = Knockout(tournament, SchedulerMatchStage.QuarterFinal, ref seq, "Quarter-final 1", q1a, q1b);
                var qf2 = Knockout(tournament, SchedulerMatchStage.QuarterFinal, ref seq, "Quarter-final 2", q2a, q2b);
                var qf3 = Knockout(tournament, SchedulerMatchStage.QuarterFinal, ref seq, "Quarter-final 3", q3a, q3b);
                var qf4 = Knockout(tournament, SchedulerMatchStage.QuarterFinal, ref seq, "Quarter-final 4", q4a, q4b);
                var sf1 = Knockout(tournament, SchedulerMatchStage.SemiFinal, ref seq, "Semi-final 1", "QF1 Winner", "QF2 Winner");
                var sf2 = Knockout(tournament, SchedulerMatchStage.SemiFinal, ref seq, "Semi-final 2", "QF3 Winner", "QF4 Winner");
                var final = Knockout(tournament, SchedulerMatchStage.Final, ref seq, "Final", "SF1 Winner", "SF2 Winner");
                FeedWinner(qf1, sf1, true);
                FeedWinner(qf2, sf1, false);
                FeedWinner(qf3, sf2, true);
                FeedWinner(qf4, sf2, false);
                FeedWinner(sf1, final, true);
                FeedWinner(sf2, final, false);
                list.AddRange([qf1, qf2, qf3, qf4, sf1, sf2, final]);
                break;
            }
            case SchedulerKnockoutFormat.Playoff:
            {
                OpeningPlayoffSlots(tournament, out var p1a, out var p1b, out var p2a, out var p2b);
                var p1 = Knockout(tournament, SchedulerMatchStage.Playoff, ref seq, "Playoff 1", p1a, p1b);
                var p2 = Knockout(tournament, SchedulerMatchStage.Playoff, ref seq, "Playoff 2", p2a, p2b);
                var semi = Knockout(tournament, SchedulerMatchStage.SemiFinal, ref seq, "Semi-final", "Playoff 1 Loser", "Playoff 2 Winner");
                var final = Knockout(tournament, SchedulerMatchStage.Final, ref seq, "Final", "Playoff 1 Winner", "Semi Winner");
                FeedWinner(p1, final, true);
                FeedLoser(p1, semi, true);
                FeedWinner(p2, semi, false);
                FeedWinner(semi, final, false);
                list.AddRange([p1, p2, semi, final]);
                break;
            }
            case SchedulerKnockoutFormat.SemiFinals:
            {
                OpeningSemiSlots(tournament, out var a1, out var b1, out var a2, out var b2);
                var sf1 = Knockout(tournament, SchedulerMatchStage.SemiFinal, ref seq, "Semi-final 1", a1, b1);
                var sf2 = Knockout(tournament, SchedulerMatchStage.SemiFinal, ref seq, "Semi-final 2", a2, b2);
                var final = Knockout(tournament, SchedulerMatchStage.Final, ref seq, "Final", "SF1 Winner", "SF2 Winner");
                FeedWinner(sf1, final, true);
                FeedWinner(sf2, final, false);
                list.AddRange([sf1, sf2, final]);
                break;
            }
            default:
            {
                OpeningFinalSlots(tournament, out var a, out var b);
                list.Add(Knockout(tournament, SchedulerMatchStage.Final, ref seq, "Final", a, b));
                break;
            }
        }

        return list;
    }

    private static string GroupTop(int group, int rank) => $"Group {group} (Top {rank})";

    private static string SeedSlot(int seed) => $"Seed {seed}";

    /// <summary>
    /// Two groups: Group 1 Top 1 vs Group 2 Top 2, Group 1 Top 2 vs Group 2 Top 1.
    /// Four groups: Group 1 vs Group 4, Group 2 vs Group 3.
    /// One group: overall 1 vs 4 and 2 vs 3.
    /// </summary>
    private static void OpeningSemiSlots(
        SchedulerTournament tournament,
        out string a1, out string b1, out string a2, out string b2)
    {
        if (tournament.GroupCount == 2)
        {
            a1 = GroupTop(1, 1);
            b1 = GroupTop(2, 2);
            a2 = GroupTop(1, 2);
            b2 = GroupTop(2, 1);
            return;
        }

        if (tournament.GroupCount == 4)
        {
            a1 = GroupTop(1, 1);
            b1 = GroupTop(4, 1);
            a2 = GroupTop(2, 1);
            b2 = GroupTop(3, 1);
            return;
        }

        a1 = SeedSlot(1);
        b1 = SeedSlot(4);
        a2 = SeedSlot(2);
        b2 = SeedSlot(3);
    }

    private static void OpeningPlayoffSlots(
        SchedulerTournament tournament,
        out string a1, out string b1, out string a2, out string b2)
    {
        if (tournament.GroupCount == 2)
        {
            a1 = GroupTop(1, 1);
            b1 = GroupTop(2, 2);
            a2 = GroupTop(1, 2);
            b2 = GroupTop(2, 1);
            return;
        }

        a1 = SeedSlot(1);
        b1 = SeedSlot(2);
        a2 = SeedSlot(3);
        b2 = SeedSlot(4);
    }

    private static void OpeningFinalSlots(SchedulerTournament tournament, out string a, out string b)
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
        SchedulerTournament tournament,
        out string a1, out string b1, out string a2, out string b2,
        out string a3, out string b3, out string a4, out string b4)
    {
        if (tournament.GroupCount == 2)
        {
            a1 = GroupTop(1, 1);
            b1 = GroupTop(2, 4);
            a2 = GroupTop(1, 4);
            b2 = GroupTop(2, 1);
            a3 = GroupTop(1, 2);
            b3 = GroupTop(2, 3);
            a4 = GroupTop(1, 3);
            b4 = GroupTop(2, 2);
            return;
        }

        if (tournament.GroupCount == 4)
        {
            a1 = GroupTop(1, 1);
            b1 = GroupTop(4, 2);
            a2 = GroupTop(2, 2);
            b2 = GroupTop(3, 1);
            a3 = GroupTop(1, 2);
            b3 = GroupTop(4, 1);
            a4 = GroupTop(2, 1);
            b4 = GroupTop(3, 2);
            return;
        }

        a1 = SeedSlot(1);
        b1 = SeedSlot(8);
        a2 = SeedSlot(4);
        b2 = SeedSlot(5);
        a3 = SeedSlot(2);
        b3 = SeedSlot(7);
        a4 = SeedSlot(3);
        b4 = SeedSlot(6);
    }

    private static SchedulerMatch Knockout(
        SchedulerTournament tournament,
        SchedulerMatchStage stage,
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

    private static void FeedWinner(SchedulerMatch source, SchedulerMatch target, bool isTeamA)
    {
        source.WinnerFeedsMatchId = target.Id;
        source.WinnerFeedsIsTeamA = isTeamA;
    }

    private static void FeedLoser(SchedulerMatch source, SchedulerMatch target, bool isTeamA)
    {
        source.LoserFeedsMatchId = target.Id;
        source.LoserFeedsIsTeamA = isTeamA;
    }

    public static List<SchedulerStandingRow> BuildStandings(
        IReadOnlyList<SchedulerTeam> teams,
        IReadOnlyList<SchedulerMatch> matches)
    {
        var rows = teams.Select(t => new SchedulerStandingRow
        {
            TeamId = t.Id,
            TeamName = t.Name,
            GroupNumber = t.GroupNumber
        }).ToDictionary(r => r.TeamId);

        foreach (var match in matches.Where(m => m.Stage == SchedulerMatchStage.Group && m.Result != SchedulerMatchResult.None))
        {
            if (match.TeamAId is not Guid a || match.TeamBId is not Guid b)
                continue;
            if (!rows.TryGetValue(a, out var rowA) || !rows.TryGetValue(b, out var rowB))
                continue;

            rowA.Played++;
            rowB.Played++;
            switch (match.Result)
            {
                case SchedulerMatchResult.TeamAWon:
                    rowA.Won++;
                    rowA.Points += PointsWin;
                    rowB.Lost++;
                    break;
                case SchedulerMatchResult.TeamBWon:
                    rowB.Won++;
                    rowB.Points += PointsWin;
                    rowA.Lost++;
                    break;
                case SchedulerMatchResult.Tied:
                    rowA.Tied++;
                    rowB.Tied++;
                    rowA.Points += PointsTie;
                    rowB.Points += PointsTie;
                    break;
            }
        }

        return rows.Values
            .OrderBy(r => r.GroupNumber)
            .ThenByDescending(r => r.Points)
            .ThenByDescending(r => r.Won)
            .ThenBy(r => r.TeamName, StringComparer.OrdinalIgnoreCase)
            .GroupBy(r => r.GroupNumber)
            .SelectMany(g => g.Select((r, i) =>
            {
                r.Rank = i + 1;
                return r;
            }))
            .ToList();
    }

    public static bool GroupStageComplete(IReadOnlyList<SchedulerMatch> matches)
    {
        var group = matches.Where(m => m.Stage == SchedulerMatchStage.Group).ToList();
        return group.Count > 0 && group.All(m => m.Result != SchedulerMatchResult.None);
    }

    /// <summary>
    /// Remaining unfinished group matches per team (fixture-based, not schedule theory).
    /// </summary>
    public static Dictionary<Guid, int> CountRemainingGroupMatches(
        IReadOnlyList<SchedulerMatch> matches,
        IEnumerable<Guid> teamIds)
    {
        var rem = teamIds.Distinct().ToDictionary(id => id, _ => 0);
        foreach (var m in matches.Where(m =>
                     m.Stage == SchedulerMatchStage.Group && m.Result == SchedulerMatchResult.None))
        {
            if (m.TeamAId is Guid a && rem.ContainsKey(a))
                rem[a]++;
            if (m.TeamBId is Guid b && rem.ContainsKey(b))
                rem[b]++;
        }
        return rem;
    }

    /// <summary>
    /// Marks Q / E on standings. Uses locked qualification when the group stage is done;
    /// otherwise a conservative points envelope (win=2) that never claims Q/E unless certain.
    /// </summary>
    public static void ApplyQualificationMarks(
        IReadOnlyList<SchedulerStandingRow> standings,
        IReadOnlyList<SchedulerMatch> matches,
        int qualifyPerGroup,
        bool groupStageComplete,
        QualificationDecision? qualification = null)
    {
        if (standings.Count == 0 || qualifyPerGroup <= 0)
            return;

        var remaining = CountRemainingGroupMatches(matches, standings.Select(s => s.TeamId));
        qualification ??= new QualificationDecision();

        foreach (var group in standings.GroupBy(s => s.GroupNumber))
        {
            var rows = group.ToList();
            var k = Math.Min(qualifyPerGroup, rows.Count);
            if (k <= 0)
                continue;

            if (k >= rows.Count)
            {
                foreach (var r in rows)
                    r.QualMark = "Q";
                continue;
            }

            if (groupStageComplete)
            {
                if (!qualification.NeedsManualPick && qualification.QualifiedInSeedOrder.Count > 0)
                {
                    var qset = qualification.QualifiedInSeedOrder.ToHashSet();
                    foreach (var r in rows)
                        r.QualMark = qset.Contains(r.TeamId) ? "Q" : "E";
                    continue;
                }

                if (qualification.NeedsManualPick)
                {
                    var auto = qualification.AutoQualifiedIds.ToHashSet();
                    var pick = qualification.PickFrom.Select(t => t.Id).ToHashSet();
                    foreach (var r in rows)
                    {
                        if (auto.Contains(r.TeamId))
                            r.QualMark = "Q";
                        else if (pick.Contains(r.TeamId))
                            r.QualMark = string.Empty; // cutoff tie — not safe to mark
                        else
                            r.QualMark = "E";
                    }
                    continue;
                }
            }

            // During round robin: only after at least one group result (avoid noise on day 0).
            var anyGroupResult = matches.Any(m =>
                m.Stage == SchedulerMatchStage.Group
                && m.Result != SchedulerMatchResult.None
                && rows.Any(r => r.TeamId == m.TeamAId || r.TeamId == m.TeamBId));
            if (!anyGroupResult)
            {
                foreach (var r in rows)
                    r.QualMark = string.Empty;
                continue;
            }

            ApplyEnvelopeQualificationMarks(rows, id => remaining.GetValueOrDefault(id), k);
        }
    }

    /// <summary>
    /// Conservative mid-table math (points only, ignores H2H):
    /// Q if even at worst points for T, at most K teams can still reach T's floor;
    /// E if at least K other teams already have a points floor strictly above T's ceiling.
    /// </summary>
    public static void ApplyEnvelopeQualificationMarks(
        IReadOnlyList<SchedulerStandingRow> groupRows,
        Func<Guid, int> remainingMatches,
        int qualifySlots)
    {
        if (groupRows.Count == 0 || qualifySlots <= 0)
            return;

        var k = Math.Min(qualifySlots, groupRows.Count);
        if (k >= groupRows.Count)
        {
            foreach (var r in groupRows)
                r.QualMark = "Q";
            return;
        }

        var env = groupRows.Select(r =>
        {
            var rem = Math.Max(0, remainingMatches(r.TeamId));
            return (Row: r, Min: r.Points, Max: r.Points + rem * PointsWin);
        }).ToList();

        foreach (var t in env)
        {
            // Clinched: even if T loses all, ≤ K teams (incl. T) can finish at/above T's floor.
            var canReachFloor = env.Count(u => u.Max >= t.Min);
            if (canReachFloor <= k)
            {
                t.Row.QualMark = "Q";
                continue;
            }

            // Eliminated: ≥ K other teams already cannot finish at or below T's best.
            var lockedStrictlyAbove = env.Count(u =>
                u.Row.TeamId != t.Row.TeamId && u.Min > t.Max);
            t.Row.QualMark = lockedStrictlyAbove >= k ? "E" : string.Empty;
        }
    }

    public static QualificationDecision DecideQualification(
        SchedulerTournament tournament,
        IReadOnlyList<SchedulerTeam> teams,
        IReadOnlyList<SchedulerStandingRow> standings)
    {
        if (tournament.KnockoutFormat == SchedulerKnockoutFormat.None)
            return DecideRoundRobinChampion(tournament, teams, standings);

        var need = KnockoutTeamCount(tournament.KnockoutFormat);
        var qualify = tournament.QualifyPerGroup;
        var teamById = teams.ToDictionary(t => t.Id);
        var auto = new List<Guid>();
        var pickFrom = new List<SchedulerTeam>();
        var messages = new List<string>();

        for (var g = 1; g <= tournament.GroupCount; g++)
        {
            var groupRows = standings.Where(s => s.GroupNumber == g).ToList();
            if (groupRows.Count == 0)
                continue;

            var guaranteed = groupRows
                .Where(r => groupRows.Count(o => o.Points >= r.Points) <= qualify)
                .Select(r => r.TeamId)
                .ToList();
            auto.AddRange(guaranteed);

            var cutIndex = Math.Min(qualify, groupRows.Count) - 1;
            var cutPoints = groupRows.OrderBy(r => r.Rank).ToList()[cutIndex].Points;
            var band = groupRows.Where(r => r.Points == cutPoints && !guaranteed.Contains(r.TeamId)).ToList();
            if (band.Count > 0)
            {
                messages.Add($"Group {g}: teams on {cutPoints} pts are tied at the cutoff.");
                foreach (var row in band)
                {
                    if (teamById.TryGetValue(row.TeamId, out var team))
                        pickFrom.Add(team);
                }
            }
        }

        var pickCount = Math.Max(0, need - auto.Count);
        if (pickCount > 0)
        {
            if (pickFrom.Count < pickCount)
            {
                var taken = auto.Concat(pickFrom.Select(t => t.Id)).ToHashSet();
                foreach (var row in standings.Where(s => !taken.Contains(s.TeamId)))
                {
                    if (!teamById.TryGetValue(row.TeamId, out var team))
                        continue;
                    pickFrom.Add(team);
                    taken.Add(team.Id);
                    if (pickFrom.Count >= pickCount)
                        break;
                }
            }

            if (pickFrom.Count == pickCount)
            {
                auto.AddRange(pickFrom.Select(t => t.Id));
                pickFrom.Clear();
                pickCount = 0;
                messages.Clear();
            }
            else if (pickFrom.Count > pickCount)
            {
                messages.Add($"Pick {pickCount} of these tied teams for the remaining knockout slot{(pickCount == 1 ? "" : "s")}.");
            }
        }

        var stored = ReadPicks(tournament.QualificationPicksJson);
        var needsPick = pickCount > 0;
        IReadOnlyList<Guid> seeded = Array.Empty<Guid>();
        if (!needsPick)
        {
            seeded = OrderSeeds(auto, standings, need, tournament.KnockoutFormat);
        }
        else if (TryAcceptPicks(stored, auto, pickFrom, teams, qualify, tournament.GroupCount, need, out var accepted))
        {
            needsPick = false;
            seeded = OrderSeeds(accepted, standings, need, tournament.KnockoutFormat);
        }

        return new QualificationDecision
        {
            NeedsManualPick = needsPick,
            Message = messages.Count == 0
                ? "Tied teams must be picked for the knockouts."
                : string.Join(" ", messages),
            AutoQualifiedIds = auto,
            PickFrom = pickFrom,
            PickCount = pickCount,
            QualifiedInSeedOrder = seeded
        };
    }

    /// <summary>
    /// Knockout = No: table leader is champion. If 2+ teams share top points, ask which is champion.
    /// </summary>
    public static QualificationDecision DecideRoundRobinChampion(
        SchedulerTournament tournament,
        IReadOnlyList<SchedulerTeam> teams,
        IReadOnlyList<SchedulerStandingRow> standings)
    {
        if (standings.Count == 0)
        {
            return new QualificationDecision
            {
                NeedsManualPick = false,
                Message = string.Empty,
                AutoQualifiedIds = Array.Empty<Guid>(),
                PickFrom = Array.Empty<SchedulerTeam>(),
                PickCount = 0,
                QualifiedInSeedOrder = Array.Empty<Guid>()
            };
        }

        var topPoints = standings.Max(s => s.Points);
        var teamById = teams.ToDictionary(t => t.Id);
        var contenders = standings
            .Where(s => s.Points == topPoints)
            .OrderBy(s => s.GroupNumber)
            .ThenBy(s => s.TeamName, StringComparer.OrdinalIgnoreCase)
            .Select(s => teamById.GetValueOrDefault(s.TeamId))
            .Where(t => t is not null)
            .Cast<SchedulerTeam>()
            .ToList();

        if (contenders.Count == 1)
        {
            return new QualificationDecision
            {
                NeedsManualPick = false,
                Message = string.Empty,
                AutoQualifiedIds = Array.Empty<Guid>(),
                PickFrom = Array.Empty<SchedulerTeam>(),
                PickCount = 0,
                QualifiedInSeedOrder = new[] { contenders[0].Id }
            };
        }

        var stored = ReadPicks(tournament.QualificationPicksJson);
        if (stored.Count == 1
            && contenders.Any(t => t.Id == stored[0])
            && TryAcceptPicks(stored, Array.Empty<Guid>(), contenders, teams, qualifyPerGroup: 0, tournament.GroupCount, need: 1, out var accepted))
        {
            return new QualificationDecision
            {
                NeedsManualPick = false,
                Message = string.Empty,
                AutoQualifiedIds = Array.Empty<Guid>(),
                PickFrom = Array.Empty<SchedulerTeam>(),
                PickCount = 0,
                QualifiedInSeedOrder = accepted
            };
        }

        return new QualificationDecision
        {
            NeedsManualPick = true,
            Message = contenders.Count == 2
                ? $"2 teams are tied on {topPoints} pts at the top of the table. Pick the champion."
                : $"{contenders.Count} teams are tied on {topPoints} pts at the top of the table. Pick the champion.",
            AutoQualifiedIds = Array.Empty<Guid>(),
            PickFrom = contenders,
            PickCount = 1,
            QualifiedInSeedOrder = Array.Empty<Guid>()
        };
    }

    public static void EnsurePicksValid(
        IReadOnlyList<Guid> picks,
        IReadOnlyList<Guid> auto,
        IReadOnlyList<SchedulerTeam> pickFrom,
        IReadOnlyList<SchedulerTeam> allTeams,
        int qualifyPerGroup,
        int groupCount,
        int need)
    {
        if (!TryAcceptPicks(picks, auto, pickFrom, allTeams, qualifyPerGroup, groupCount, need, out _))
            throw new InvalidOperationException(
                $"Select exactly {need} knockout teams, including locked qualifiers, with at least {qualifyPerGroup} from each group.");
    }

    public static bool TryAcceptPicks(
        IReadOnlyList<Guid> picks,
        IReadOnlyList<Guid> auto,
        IReadOnlyList<SchedulerTeam> pickFrom,
        IReadOnlyList<SchedulerTeam> allTeams,
        int qualifyPerGroup,
        int groupCount,
        int need,
        out List<Guid> accepted)
    {
        accepted = [];
        if (picks.Count != need || auto.Any(id => !picks.Contains(id)))
            return false;
        var allowed = auto.Concat(pickFrom.Select(t => t.Id)).ToHashSet();
        if (picks.Any(id => !allowed.Contains(id)))
            return false;
        var byId = allTeams.ToDictionary(t => t.Id);
        for (var g = 1; g <= groupCount; g++)
        {
            var n = picks.Count(id => byId.TryGetValue(id, out var team) && team.GroupNumber == g);
            if (n < qualifyPerGroup)
                return false;
        }

        accepted = picks.Distinct().ToList();
        return accepted.Count == need;
    }

    public static List<Guid> ReadPicks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string WritePicks(IEnumerable<Guid> ids)
        => JsonSerializer.Serialize(ids.ToList());

    public static IReadOnlyList<Guid> OrderSeeds(
        IReadOnlyList<Guid> qualified,
        IReadOnlyList<SchedulerStandingRow> standings,
        int need,
        SchedulerKnockoutFormat knockout = SchedulerKnockoutFormat.SemiFinals)
    {
        var byId = standings.ToDictionary(s => s.TeamId);
        var rows = qualified
            .Distinct()
            .Select(id => byId.TryGetValue(id, out var row) ? row : null)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        var byGroup = rows
            .GroupBy(r => r.GroupNumber)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(r => r.Rank)
                .ThenByDescending(r => r.Points)
                .ThenByDescending(r => r.Won)
                .ThenBy(r => r.TeamName, StringComparer.OrdinalIgnoreCase)
                .ToList())
            .ToList();

        if (byGroup.Count <= 1)
        {
            return rows
                .OrderBy(r => r.Rank)
                .ThenByDescending(r => r.Points)
                .ThenByDescending(r => r.Won)
                .ThenBy(r => r.TeamName, StringComparer.OrdinalIgnoreCase)
                .Take(need)
                .Select(r => r.TeamId)
                .ToList();
        }

        // Existing playoff brackets are Seed 1 vs Seed 2 / Seed 3 vs Seed 4.
        // Interleave so those pairs are G1 Top 1 vs G2 Top 2 and G1 Top 2 vs G2 Top 1.
        if (knockout == SchedulerKnockoutFormat.Playoff && byGroup.Count == 2)
        {
            var g1 = byGroup[0];
            var g2 = byGroup[1];
            var list = new List<Guid>(need);
            void Add(List<SchedulerStandingRow> group, int index)
            {
                if (index < group.Count)
                    list.Add(group[index].TeamId);
            }

            Add(g1, 0);
            Add(g2, 1);
            Add(g1, 1);
            Add(g2, 0);
            return list.Take(need).ToList();
        }

        // Semi / quarter / final: G1 tops then G2 tops so Seed 1 vs last is cross-group
        // (G1 Top 1 vs G2 Top 2, G1 Top 2 vs G2 Top 1).
        return byGroup.SelectMany(g => g).Take(need).Select(r => r.TeamId).ToList();
    }

    public static void AssignOpeningKnockoutSeeds(
        IReadOnlyList<SchedulerMatch> matches,
        IReadOnlyList<Guid> seeds,
        IReadOnlyList<SchedulerStandingRow> standings)
    {
        Guid? Resolve(string? slot)
        {
            if (TrySeedSlot(slot, out var seed))
                return seed <= seeds.Count ? seeds[seed - 1] : null;
            if (TryGroupTopSlot(slot, out var group, out var rank))
                return PickQualifiedByGroupRank(seeds, standings, group, rank);
            return null;
        }

        foreach (var match in matches.Where(m => m.Stage != SchedulerMatchStage.Group))
        {
            var a = Resolve(match.TeamASlot);
            var b = Resolve(match.TeamBSlot);
            if (a is Guid)
                match.TeamAId = a;
            if (b is Guid)
                match.TeamBId = b;
        }
    }

    private static Guid? PickQualifiedByGroupRank(
        IReadOnlyList<Guid> qualified,
        IReadOnlyList<SchedulerStandingRow> standings,
        int group,
        int rank)
    {
        var allowed = qualified.ToHashSet();
        return standings
            .Where(s => s.GroupNumber == group && allowed.Contains(s.TeamId))
            .OrderBy(s => s.Rank)
            .ThenByDescending(s => s.Points)
            .ThenByDescending(s => s.Won)
            .ThenBy(s => s.TeamName, StringComparer.OrdinalIgnoreCase)
            .Skip(Math.Max(0, rank - 1))
            .Select(s => (Guid?)s.TeamId)
            .FirstOrDefault();
    }

    private static bool TrySeedSlot(string? slot, out int seed)
    {
        seed = 0;
        if (string.IsNullOrWhiteSpace(slot) || !slot.StartsWith("Seed ", StringComparison.OrdinalIgnoreCase))
            return false;
        return int.TryParse(slot.AsSpan("Seed ".Length), out seed);
    }

    private static bool TryGroupTopSlot(string? slot, out int group, out int rank)
    {
        group = 0;
        rank = 0;
        if (string.IsNullOrWhiteSpace(slot))
            return false;

        var s = slot.Trim();
        if (s.StartsWith("Group ", StringComparison.OrdinalIgnoreCase))
        {
            var open = s.IndexOf('(');
            if (open < 0)
                return false;
            var groupSpan = s.AsSpan("Group ".Length, open - "Group ".Length).Trim();
            var close = s.IndexOf(')', open);
            if (close < 0 || !int.TryParse(groupSpan, out group))
                return false;
            var inner = s.AsSpan(open + 1, close - open - 1).Trim();
            if (!inner.StartsWith("Top ", StringComparison.OrdinalIgnoreCase))
                return false;
            return int.TryParse(inner.Slice("Top ".Length).Trim(), out rank) && group > 0 && rank > 0;
        }

        if (s.Length >= 4 && (s[0] is 'G' or 'g'))
        {
            var hash = s.IndexOf('#');
            if (hash < 2)
                return false;
            return int.TryParse(s.AsSpan(1, hash - 1).Trim(), out group)
                && int.TryParse(s.AsSpan(hash + 1).Trim(), out rank)
                && group > 0 && rank > 0;
        }

        return false;
    }

    public static void ClearKnockoutProgress(IReadOnlyList<SchedulerMatch> matches)
    {
        foreach (var match in matches.Where(m => m.Stage != SchedulerMatchStage.Group))
        {
            match.Result = SchedulerMatchResult.None;
            match.TeamAId = null;
            match.TeamBId = null;
        }
    }

    public static void ApplyMatchResult(
        SchedulerMatch match,
        IReadOnlyList<SchedulerMatch> all,
        SchedulerMatchResult result)
    {
        if (match.TeamAId is not Guid teamA || match.TeamBId is not Guid teamB)
            throw new InvalidOperationException("Both teams must be known before recording a result.");
        if (teamA == teamB)
            throw new InvalidOperationException("A match cannot have the same team on both sides.");
        if (match.Stage != SchedulerMatchStage.Group && result == SchedulerMatchResult.Tied)
            throw new InvalidOperationException("Knockout matches cannot be tied. Pick a winner.");
        if (result is not (SchedulerMatchResult.None or SchedulerMatchResult.TeamAWon
            or SchedulerMatchResult.TeamBWon or SchedulerMatchResult.Tied))
            throw new InvalidOperationException("Invalid match result.");

        if (match.Stage != SchedulerMatchStage.Group)
            ValidateKnockoutReady(match, all);

        ClearFeedsOf(match, all);
        match.Result = result;
        if (result is SchedulerMatchResult.TeamAWon or SchedulerMatchResult.TeamBWon)
            PushFeeds(match, all);
    }

    public static void ValidateKnockoutReady(SchedulerMatch match, IReadOnlyList<SchedulerMatch> all)
    {
        if (match.TeamAId is not Guid a || match.TeamBId is not Guid b)
            throw new InvalidOperationException($"{match.Label} is waiting for both teams.");
        if (a == b)
            throw new InvalidOperationException($"{match.Label}: the same team cannot play both sides.");

        var sameRound = all.Where(m =>
            m.Id != match.Id
            && m.Stage == match.Stage
            && m.Stage != SchedulerMatchStage.Group).ToList();
        foreach (var other in sameRound)
        {
            var others = new[] { other.TeamAId, other.TeamBId };
            if (others.Contains(a) || others.Contains(b))
                throw new InvalidOperationException(
                    $"{match.Label} conflicts with {other.Label}: a team is already in this knockout round.");
        }

        var feeders = all.Where(m =>
            m.WinnerFeedsMatchId == match.Id || m.LoserFeedsMatchId == match.Id).ToList();
        foreach (var feeder in feeders)
        {
            if (feeder.Result == SchedulerMatchResult.None)
                throw new InvalidOperationException(
                    $"Finish {feeder.Label} before playing {match.Label}.");
        }
    }

    public static Guid? WinnerOf(SchedulerMatch match) => match.Result switch
    {
        SchedulerMatchResult.TeamAWon => match.TeamAId,
        SchedulerMatchResult.TeamBWon => match.TeamBId,
        _ => null
    };

    public static Guid? LoserOf(SchedulerMatch match) => match.Result switch
    {
        SchedulerMatchResult.TeamAWon => match.TeamBId,
        SchedulerMatchResult.TeamBWon => match.TeamAId,
        _ => null
    };

    private static void ClearFeedsOf(SchedulerMatch source, IReadOnlyList<SchedulerMatch> all)
    {
        var previousWinner = WinnerOf(source);
        var previousLoser = LoserOf(source);
        ClearSlot(all, source.WinnerFeedsMatchId, source.WinnerFeedsIsTeamA, previousWinner);
        ClearSlot(all, source.LoserFeedsMatchId, source.LoserFeedsIsTeamA, previousLoser);
    }

    private static void ClearSlot(IReadOnlyList<SchedulerMatch> all, Guid? targetId, bool isTeamA, Guid? teamId)
    {
        if (targetId is not Guid id)
            return;
        var target = all.FirstOrDefault(m => m.Id == id);
        if (target is null)
            return;
        if (target.Result != SchedulerMatchResult.None)
        {
            ClearFeedsOf(target, all);
            target.Result = SchedulerMatchResult.None;
        }

        if (isTeamA && (teamId is null || target.TeamAId == teamId))
            target.TeamAId = null;
        if (!isTeamA && (teamId is null || target.TeamBId == teamId))
            target.TeamBId = null;
    }

    private static void PushFeeds(SchedulerMatch source, IReadOnlyList<SchedulerMatch> all)
    {
        var winner = WinnerOf(source);
        var loser = LoserOf(source);
        SetSlot(all, source.WinnerFeedsMatchId, source.WinnerFeedsIsTeamA, winner, source.Label);
        SetSlot(all, source.LoserFeedsMatchId, source.LoserFeedsIsTeamA, loser, source.Label);
    }

    private static void SetSlot(
        IReadOnlyList<SchedulerMatch> all,
        Guid? targetId,
        bool isTeamA,
        Guid? teamId,
        string sourceLabel)
    {
        if (targetId is not Guid id || teamId is not Guid team)
            return;
        var target = all.FirstOrDefault(m => m.Id == id)
            ?? throw new InvalidOperationException($"Knockout feed from {sourceLabel} is missing.");
        if (target.Result != SchedulerMatchResult.None)
            throw new InvalidOperationException(
                $"{target.Label} already has a result. Change it first if you need to update {sourceLabel}.");

        var other = isTeamA ? target.TeamBId : target.TeamAId;
        if (other == team)
            throw new InvalidOperationException(
                $"{target.Label} would have the same team on both sides after {sourceLabel}.");

        if (isTeamA)
            target.TeamAId = team;
        else
            target.TeamBId = team;
    }

    public static SchedulerStatus ComputeStatus(IReadOnlyList<SchedulerMatch> matches, Guid? winnerId)
    {
        if (winnerId is not null)
            return SchedulerStatus.Completed;
        var knockout = matches.Where(m => m.Stage != SchedulerMatchStage.Group).ToList();
        if (knockout.Count == 0 && GroupStageComplete(matches))
            return SchedulerStatus.Completed;
        if (knockout.Any(m => m.Result != SchedulerMatchResult.None || m.TeamAId is not null || m.TeamBId is not null)
            && GroupStageComplete(matches))
            return SchedulerStatus.Knockout;
        return SchedulerStatus.GroupStage;
    }
}

public sealed class SchedulerStandingRow
{
    public Guid TeamId { get; init; }
    public string TeamName { get; init; } = string.Empty;
    public int GroupNumber { get; init; }
    public int Rank { get; set; }
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Tied { get; set; }
    public int Points { get; set; }
    /// <summary>"Q" qualified, "E" eliminated, empty = still undecided.</summary>
    public string QualMark { get; set; } = string.Empty;
    public bool IsQualified => QualMark == "Q";
    public bool IsEliminated => QualMark == "E";
}

public sealed class QualificationDecision
{
    public bool NeedsManualPick { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<Guid> AutoQualifiedIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<SchedulerTeam> PickFrom { get; init; } = Array.Empty<SchedulerTeam>();
    public int PickCount { get; init; }
    public IReadOnlyList<Guid> QualifiedInSeedOrder { get; init; } = Array.Empty<Guid>();
}
