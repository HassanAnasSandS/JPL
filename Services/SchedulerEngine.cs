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
        SchedulerKnockoutFormat.QuarterFinals => 8,
        SchedulerKnockoutFormat.Playoff => 4,
        SchedulerKnockoutFormat.SemiFinals => 4,
        SchedulerKnockoutFormat.FinalOnly => 2,
        _ => 2
    };

    public static string KnockoutLabel(SchedulerKnockoutFormat format) => format switch
    {
        SchedulerKnockoutFormat.QuarterFinals => "Quarter Finals → Semi Finals → Final",
        SchedulerKnockoutFormat.Playoff => "Playoff → Semi Final → Final",
        SchedulerKnockoutFormat.SemiFinals => "Semi Finals → Final",
        SchedulerKnockoutFormat.FinalOnly => "Final",
        _ => format.ToString()
    };

    public static string RoundRobinLabel(SchedulerRoundRobinType type) => type switch
    {
        SchedulerRoundRobinType.Double => "Double Round Robin",
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

    public static string SetupHint(int groupCount, int qualifyPerGroup, SchedulerKnockoutFormat knockout, SchedulerRoundRobinType rr, int? teamCount = null)
    {
        var need = KnockoutTeamCount(knockout);
        var guaranteed = groupCount * qualifyPerGroup;
        var fit = guaranteed == need
            ? $"{guaranteed} teams qualify — one per knockout slot."
            : $"{groupCount} × {qualifyPerGroup} = {guaranteed}, but {KnockoutLabel(knockout)} needs exactly {need}.";
        var groups = teamCount is int n
            ? GroupsTeamsLabel(groupCount, n)
            : $"{groupCount} group{(groupCount == 1 ? "" : "s")}";
        return $"{RoundRobinLabel(rr)} · {groups} · top {qualifyPerGroup}/group. {fit}";
    }

    public static void ValidateSetup(
        int groupCount,
        SchedulerRoundRobinType rr,
        int qualifyPerGroup,
        SchedulerKnockoutFormat knockout,
        IReadOnlyList<(string Name, int GroupNumber)> teams)
    {
        if (groupCount is not (1 or 2 or 4))
            throw new InvalidOperationException("Groups must be 1, 2, or 4.");
        if (qualifyPerGroup < 1)
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

        var need = KnockoutTeamCount(knockout);
        var guaranteed = groupCount * qualifyPerGroup;
        if (guaranteed != need)
            throw new InvalidOperationException(
                $"{KnockoutLabel(knockout)} needs exactly {need} teams. With {groupCount} groups, Qualification must be {need / groupCount} per group.");

        for (var g = 1; g <= groupCount; g++)
        {
            var count = teams.Count(t => t.GroupNumber == g);
            if (count < 2)
                throw new InvalidOperationException($"Group {g} needs at least 2 teams for round robin (currently {count}).");
            if (count < qualifyPerGroup)
                throw new InvalidOperationException(
                    $"Group {g} needs at least {qualifyPerGroup} team{(qualifyPerGroup == 1 ? "" : "s")} to qualify that many (currently {count}).");
        }

        if (teams.Count < need)
            throw new InvalidOperationException(
                $"{KnockoutLabel(knockout)} needs {need} teams in the tournament (currently {teams.Count}).");

        _ = rr;
    }

    public static List<SchedulerMatch> BuildMatches(SchedulerTournament tournament)
    {
        var matches = new List<SchedulerMatch>();
        var seq = 1;
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
                for (var i = 0; i < groupTeams.Count; i++)
                {
                    for (var j = i + 1; j < groupTeams.Count; j++)
                    {
                        var a = leg == 0 ? groupTeams[i] : groupTeams[j];
                        var b = leg == 0 ? groupTeams[j] : groupTeams[i];
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

        matches.AddRange(BuildKnockoutSkeleton(tournament, ref seq));
        return matches;
    }

    private static List<SchedulerMatch> BuildKnockoutSkeleton(SchedulerTournament tournament, ref int seq)
    {
        var list = new List<SchedulerMatch>();
        switch (tournament.KnockoutFormat)
        {
            case SchedulerKnockoutFormat.QuarterFinals:
            {
                var qf1 = Knockout(tournament, SchedulerMatchStage.QuarterFinal, ref seq, "Quarter-final 1", "Seed 1", "Seed 8");
                var qf2 = Knockout(tournament, SchedulerMatchStage.QuarterFinal, ref seq, "Quarter-final 2", "Seed 4", "Seed 5");
                var qf3 = Knockout(tournament, SchedulerMatchStage.QuarterFinal, ref seq, "Quarter-final 3", "Seed 2", "Seed 7");
                var qf4 = Knockout(tournament, SchedulerMatchStage.QuarterFinal, ref seq, "Quarter-final 4", "Seed 3", "Seed 6");
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
                var p1 = Knockout(tournament, SchedulerMatchStage.Playoff, ref seq, "Playoff 1", "Seed 1", "Seed 2");
                var p2 = Knockout(tournament, SchedulerMatchStage.Playoff, ref seq, "Playoff 2", "Seed 3", "Seed 4");
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
                var sf1 = Knockout(tournament, SchedulerMatchStage.SemiFinal, ref seq, "Semi-final 1", "Seed 1", "Seed 4");
                var sf2 = Knockout(tournament, SchedulerMatchStage.SemiFinal, ref seq, "Semi-final 2", "Seed 2", "Seed 3");
                var final = Knockout(tournament, SchedulerMatchStage.Final, ref seq, "Final", "SF1 Winner", "SF2 Winner");
                FeedWinner(sf1, final, true);
                FeedWinner(sf2, final, false);
                list.AddRange([sf1, sf2, final]);
                break;
            }
            default:
            {
                list.Add(Knockout(tournament, SchedulerMatchStage.Final, ref seq, "Final", "Seed 1", "Seed 2"));
                break;
            }
        }

        return list;
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

    public static QualificationDecision DecideQualification(
        SchedulerTournament tournament,
        IReadOnlyList<SchedulerTeam> teams,
        IReadOnlyList<SchedulerStandingRow> standings)
    {
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
            seeded = OrderSeeds(auto, standings, need);
        }
        else if (TryAcceptPicks(stored, auto, pickFrom, teams, qualify, tournament.GroupCount, need, out var accepted))
        {
            needsPick = false;
            seeded = OrderSeeds(accepted, standings, need);
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
        int need)
    {
        var byId = standings.ToDictionary(s => s.TeamId);
        return qualified
            .Distinct()
            .Select(id => byId.TryGetValue(id, out var row) ? row : null)
            .Where(r => r is not null)
            .Select(r => r!)
            .OrderByDescending(r => r.Points)
            .ThenByDescending(r => r.Won)
            .ThenBy(r => r.TeamName, StringComparer.OrdinalIgnoreCase)
            .Take(need)
            .Select(r => r.TeamId)
            .ToList();
    }

    public static void AssignOpeningKnockoutSeeds(IReadOnlyList<SchedulerMatch> matches, IReadOnlyList<Guid> seeds)
    {
        Guid? Seed(int oneBased) => oneBased <= seeds.Count ? seeds[oneBased - 1] : null;

        foreach (var match in matches.Where(m => m.Stage != SchedulerMatchStage.Group))
        {
            if (TrySeedSlot(match.TeamASlot, out var a))
                match.TeamAId = Seed(a);
            if (TrySeedSlot(match.TeamBSlot, out var b))
                match.TeamBId = Seed(b);
        }
    }

    private static bool TrySeedSlot(string? slot, out int seed)
    {
        seed = 0;
        if (string.IsNullOrWhiteSpace(slot) || !slot.StartsWith("Seed ", StringComparison.OrdinalIgnoreCase))
            return false;
        return int.TryParse(slot.AsSpan("Seed ".Length), out seed);
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
