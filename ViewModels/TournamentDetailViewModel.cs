using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class TournamentStandingGroup
{
    public string Title { get; init; } = string.Empty;
    public bool ShowTitle { get; init; }
    public ObservableCollection<TournamentStandingItem> Rows { get; } = new();
}

public sealed class TournamentFixtureGroup
{
    public string Title { get; init; } = string.Empty;
    public ObservableCollection<TournamentFixtureItem> Fixtures { get; } = new();
}

public partial class TournamentDetailViewModel : ObservableObject, IQueryAttributable
{
    private readonly ITournamentService _tournaments;
    private readonly IMatchService _matches;
    private readonly IAuthService _auth;

    private Guid _id;
    private Guid? _teamScheduleTeamId;
    private IReadOnlyList<TournamentFixtureItem> _allFixtures = Array.Empty<TournamentFixtureItem>();

    public ObservableCollection<TournamentStandingGroup> StandingGroups { get; } = new();
    public ObservableCollection<TournamentFixtureGroup> FixtureGroups { get; } = new();
    public ObservableCollection<TournamentTeamItem> Teams { get; } = new();
    public ObservableCollection<TeamScheduleMatchItem> TeamScheduleMatches { get; } = new();

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string metaLine = string.Empty;
    [ObservableProperty] private string matchDefaultsLine = string.Empty;
    [ObservableProperty] private string winnerLine = string.Empty;
    [ObservableProperty] private bool hasWinner;
    [ObservableProperty] private bool hasAwards;
    [ObservableProperty] private bool hasPlayerOfTournament;
    [ObservableProperty] private bool hasBestBatsman;
    [ObservableProperty] private bool hasBestBowler;
    [ObservableProperty] private bool hasBestFielder;
    [ObservableProperty] private string playerOfTournamentLine = string.Empty;
    [ObservableProperty] private string bestBatsmanLine = string.Empty;
    [ObservableProperty] private string bestBowlerLine = string.Empty;
    [ObservableProperty] private string bestFielderLine = string.Empty;
    [ObservableProperty] private bool isDraft;
    [ObservableProperty] private bool canSave;
    [ObservableProperty] private bool hasStandings;
    [ObservableProperty] private bool hasFixtures;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private byte status;
    [ObservableProperty] private string tournamentMatchesLine = string.Empty;
    [ObservableProperty] private string groupRoundMatchesLine = string.Empty;
    [ObservableProperty] private string groupMatchesLine = string.Empty;
    [ObservableProperty] private bool showTeamScheduleModal;
    [ObservableProperty] private string teamScheduleTitle = string.Empty;
    [ObservableProperty] private string teamScheduleSubtitle = string.Empty;
    [ObservableProperty] private bool teamScheduleEmpty;

    private Guid _venueId;
    private Guid? _formatId;
    private byte _format;
    private int _oversLimit = 8;
    private byte _oversMode;
    private bool _lastBatsmanCanBat;
    private int _bowlerMaxOvers;

    public bool CanWrite => _auth.CanWrite;
    public bool ShowNoFixtures => !HasFixtures;
    public bool ShowFixtureHint => HasFixtures && !IsDraft;

    public TournamentDetailViewModel(ITournamentService tournaments, IMatchService matches, IAuthService auth)
    {
        _tournaments = tournaments;
        _matches = matches;
        _auth = auth;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var raw) && Guid.TryParse($"{raw}", out var id))
            _id = id;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_id == Guid.Empty || IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var detail = await _tournaments.GetAsync(_id).ConfigureAwait(true)
                         ?? throw new InvalidOperationException("Tournament not found.");
            Bind(detail);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    private void Bind(TournamentDetail detail)
    {
        Name = detail.Name;
        Status = detail.Status;
        StatusText = detail.StatusText;
        var rrLabel = detail.RoundRobinType switch
        {
            TournamentRoundRobinType.Double => "Double Round Robin",
            TournamentRoundRobinType.Custom => detail.MatchesPerTeam > 0
                ? $"Custom ({detail.MatchesPerTeam} matches/team)"
                : "Custom",
            _ => "Single Round Robin"
        };
        MetaLine =
            $"{rrLabel} · {SchedulerEngine.GroupsTeamsLabel(detail.GroupCount, detail.Teams.Count)}"
            + $" · top {detail.QualifyPerGroup}/group. {KnockoutPathLabel(detail.GroupCount, detail.QualifyPerGroup)}";
        if (detail.Status == 0
            && detail.RoundRobinType == TournamentRoundRobinType.Custom
            && !detail.CustomFixturesReady)
        {
            MetaLine += " · Finish custom fixtures in Edit before Save.";
        }

        _venueId = detail.VenueId;
        _formatId = detail.FormatId;
        _format = detail.Format;
        _oversLimit = detail.OversLimit;
        _oversMode = (byte)detail.OversMode;
        _lastBatsmanCanBat = detail.LastBatsmanCanBatAsSingleWicket;
        _bowlerMaxOvers = detail.BowlerMaxOvers;
        var lastBat = detail.LastBatsmanCanBatAsSingleWicket ? "Last bat ON" : "Last bat OFF";
        var bowlerMax = detail.BowlerMaxOvers <= 0 ? "no bowler max" : $"bowler max {detail.BowlerMaxOvers}";
        MatchDefaultsLine =
            $"{detail.VenueName} · {detail.FormatText} · {OversRules.ShortLabel(detail.OversMode, detail.OversLimit)} · {lastBat} · {bowlerMax}";
        HasWinner = !string.IsNullOrWhiteSpace(detail.WinnerTeamName);
        WinnerLine = HasWinner ? $"Champion — {detail.WinnerTeamName}" : string.Empty;
        HasPlayerOfTournament = !string.IsNullOrWhiteSpace(detail.PlayerOfTheTournamentName);
        PlayerOfTournamentLine = HasPlayerOfTournament
            ? $"Player of the Tournament — {detail.PlayerOfTheTournamentName}"
            : string.Empty;
        HasBestBatsman = !string.IsNullOrWhiteSpace(detail.BestBatsmanName);
        BestBatsmanLine = HasBestBatsman
            ? $"Best Batsman — {detail.BestBatsmanName}"
            : string.Empty;
        HasBestBowler = !string.IsNullOrWhiteSpace(detail.BestBowlerName);
        BestBowlerLine = HasBestBowler
            ? $"Best Bowler — {detail.BestBowlerName}"
            : string.Empty;
        HasBestFielder = !string.IsNullOrWhiteSpace(detail.BestFielderName);
        BestFielderLine = HasBestFielder
            ? $"Best Fielder — {detail.BestFielderName}"
            : string.Empty;
        HasAwards = HasPlayerOfTournament || HasBestBatsman || HasBestBowler || HasBestFielder;
        IsDraft = detail.Status == 0;
        CanSave = CanWrite && IsDraft && detail.CustomFixturesReady;

        Teams.Clear();
        foreach (var t in detail.Teams) Teams.Add(t);

        StandingGroups.Clear();
        foreach (var g in detail.Standings
                     .GroupBy(s => s.GroupNumber)
                     .OrderBy(g => g.Key))
        {
            var section = new TournamentStandingGroup
            {
                Title = detail.GroupCount == 1 ? "Points table" : $"Group {g.Key}",
                ShowTitle = detail.GroupCount > 1
            };
            foreach (var row in g.OrderBy(s => s.Rank).ThenBy(s => s.TeamName))
                section.Rows.Add(row);
            StandingGroups.Add(section);
        }
        HasStandings = StandingGroups.Count > 0;

        FixtureGroups.Clear();
        var fixtures = detail.Fixtures.OrderBy(f => f.Sequence).ToList();
        _allFixtures = fixtures;
        var groupFixtures = fixtures.Where(IsGroupFixture).ToList();
        var knockoutFixtures = fixtures.Where(f => !IsGroupFixture(f)).ToList();

        var total = fixtures.Count;
        var groupRound = groupFixtures.Count;
        TournamentMatchesLine = total == 1 ? "Total tournament matches: 1" : $"Total tournament matches: {total}";
        GroupRoundMatchesLine = groupRound == 1 ? "Group round matches: 1" : $"Group round matches: {groupRound}";
        if (detail.GroupCount <= 1)
        {
            GroupMatchesLine = groupRound == 1 ? "Group matches: 1" : $"Group matches: {groupRound}";
        }
        else
        {
            GroupMatchesLine = string.Join(" · ",
                groupFixtures.GroupBy(f => f.GroupNumber ?? 0).Where(g => g.Key > 0).OrderBy(g => g.Key)
                    .Select(g =>
                    {
                        var n = g.Count();
                        return n == 1 ? $"Group {g.Key}: 1 match" : $"Group {g.Key}: {n} matches";
                    }));
        }

        // Day-wise like Tournament Scheduler (circle method: each team ≤1 group match/day).
        var schedulerTeams = detail.Teams
            .OrderBy(t => t.GroupNumber)
            .ThenBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase)
            .Select((t, i) => new SchedulerTeam
            {
                Id = t.TeamId,
                Name = t.TeamName,
                GroupNumber = t.GroupNumber,
                SortOrder = i
            })
            .ToList();
        var playDays = SchedulerEngine.AssignGroupPlayDays(
            schedulerTeams,
            groupFixtures.Select(f => (f.Id, f.GroupNumber ?? 0, f.Sequence, f.TeamAId, f.TeamBId)).ToList());

        foreach (var day in groupFixtures
                     .GroupBy(f => playDays.TryGetValue(f.Id, out var d) ? d : 1)
                     .OrderBy(g => g.Key))
        {
            var section = new TournamentFixtureGroup { Title = $"Day {day.Key}" };
            foreach (var f in day.OrderBy(x => x.GroupNumber ?? 0).ThenBy(x => x.Sequence))
                section.Fixtures.Add(f);
            FixtureGroups.Add(section);
        }

        foreach (var stage in knockoutFixtures
                     .GroupBy(KnockoutStageTitle)
                     .OrderBy(g => g.Min(x => x.Sequence)))
        {
            var section = new TournamentFixtureGroup { Title = stage.Key };
            foreach (var f in stage.OrderBy(x => x.Sequence))
                section.Fixtures.Add(f);
            FixtureGroups.Add(section);
        }

        HasFixtures = fixtures.Count > 0;

        if (!CanWrite)
        {
            foreach (var f in FixtureGroups.SelectMany(g => g.Fixtures))
            {
                f.CanStart = false;
                f.CanResume = false;
            }
        }

        // Keep team schedule modal open after refresh (e.g. returning to page).
        if (_teamScheduleTeamId is Guid teamId)
        {
            var row = detail.Standings.FirstOrDefault(s => s.TeamId == teamId);
            if (row is not null)
                FillTeamSchedule(row);
            else
                CloseTeamSchedule();
        }
    }

    partial void OnHasFixturesChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNoFixtures));
        OnPropertyChanged(nameof(ShowFixtureHint));
    }

    partial void OnIsDraftChanged(bool value)
        => OnPropertyChanged(nameof(ShowFixtureHint));

    /// <summary>Group-stage fixture. Everything else (semis, final, …) is a knockout.</summary>
    private static bool IsGroupFixture(TournamentFixtureItem f)
        => string.Equals(f.StageText, "Group", StringComparison.OrdinalIgnoreCase)
           || f.GroupNumber is > 0;

    /// <summary>Section titles match Tournament Scheduler: Quarter-finals / Playoffs / Semi-finals / Final.</summary>
    private static string KnockoutStageTitle(TournamentFixtureItem f)
    {
        var stage = string.IsNullOrWhiteSpace(f.StageText) ? f.Label : f.StageText;
        if (stage.Contains("Quarter", StringComparison.OrdinalIgnoreCase)) return "Quarter-finals";
        if (stage.Contains("Playoff", StringComparison.OrdinalIgnoreCase)) return "Playoffs";
        if (stage.Contains("Semi", StringComparison.OrdinalIgnoreCase)) return "Semi-finals";
        if (stage.Contains("Final", StringComparison.OrdinalIgnoreCase)) return "Final";
        return string.IsNullOrWhiteSpace(stage) ? "Knockouts" : stage.Trim();
    }

    private static string KnockoutPathLabel(int groupCount, int qualifyPerGroup)
        => (groupCount, qualifyPerGroup) switch
        {
            (1, 1) => "No knockout — the group leader is champion.",
            (2, 2) => "Semi Finals → Final.",
            _ => "Final."
        };

    private static bool IsFixtureFinished(TournamentFixtureItem f)
        => f.HasResult
           || string.Equals(f.MatchStatusText, "Completed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(f.MatchStatusText, "Abandoned", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void OpenTeamSchedule(TournamentStandingItem? row)
    {
        if (row is null)
            return;
        _teamScheduleTeamId = row.TeamId;
        FillTeamSchedule(row);
    }

    private void FillTeamSchedule(TournamentStandingItem row)
    {
        TeamScheduleMatches.Clear();
        var forTeam = _allFixtures
            .Where(f => f.TeamAId == row.TeamId || f.TeamBId == row.TeamId)
            // Finished first, then remaining / live.
            .OrderBy(f => IsFixtureFinished(f) ? 0 : 1)
            .ThenBy(f => f.Sequence)
            .ToList();

        foreach (var f in forTeam)
        {
            var isA = f.TeamAId == row.TeamId;
            var opponent = isA ? f.TeamBName : f.TeamAName;
            if (string.IsNullOrWhiteSpace(opponent) || opponent is "TBD" or "—")
                opponent = "TBD";

            var status = !string.IsNullOrWhiteSpace(f.MatchStatusText)
                ? f.MatchStatusText
                : f.HasResult
                    ? "Completed"
                    : f.TeamAId is not null && f.TeamBId is not null
                        ? "Upcoming"
                        : "Fixture TBD";

            TeamScheduleMatches.Add(new TeamScheduleMatchItem
            {
                StageLabel = string.IsNullOrWhiteSpace(f.Label) ? f.StageText : f.Label,
                OpponentName = opponent,
                StatusText = status,
                ResultText = f.ResultText,
                HasResult = f.HasResult,
                Fixture = f
            });
        }

        var played = TeamScheduleMatches.Count(x =>
            x.HasResult
            || string.Equals(x.StatusText, "Completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.StatusText, "Abandoned", StringComparison.OrdinalIgnoreCase));
        var total = TeamScheduleMatches.Count;
        TeamScheduleTitle = row.TeamName;
        TeamScheduleSubtitle = total == 0
            ? "No fixtures scheduled for this team yet."
            : $"{played} of {total} played · tap outside to close";
        TeamScheduleEmpty = total == 0;
        ShowTeamScheduleModal = true;
    }

    [RelayCommand]
    private void CloseTeamSchedule()
    {
        _teamScheduleTeamId = null;
        ShowTeamScheduleModal = false;
        TeamScheduleMatches.Clear();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanSave || IsBusy) return;
        var ok = await Shell.Current.DisplayAlert(
            "Save tournament",
            "Save locks this tournament: schedule is created, Edit/Delete stop, and fixtures can be started.\n\nContinue?",
            "Save",
            "Cancel").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            IsBusy = true;
            var detail = await _tournaments.GenerateAsync(_id).ConfigureAwait(true);
            Bind(detail);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await Shell.Current.DisplayAlert("Save failed", ex.Message, "OK").ConfigureAwait(true);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (!IsDraft || !CanWrite) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.TournamentEditPage)}?id={_id}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task StartFixtureAsync(TournamentFixtureItem? fixture)
    {
        if (fixture is null || !CanWrite) return;
        if (IsDraft)
        {
            await Shell.Current.DisplayAlert(
                "Draft tournament",
                "Save the tournament first. Matches cannot start while it is still a draft.",
                "OK").ConfigureAwait(true);
            return;
        }
        if (!fixture.CanStart || fixture.TeamAId is null || fixture.TeamBId is null)
        {
            await Shell.Current.DisplayAlert(
                "Not ready",
                "Both teams must be known before this fixture can start.",
                "OK").ConfigureAwait(true);
            return;
        }

        var active = await _matches.GetActiveMatchAsync().ConfigureAwait(true);
        if (active is not null)
        {
            await Shell.Current.DisplayAlert(
                "Match in progress",
                $"Only one match can run at a time in this league on this device. Finish or complete \"{active.Title}\" first.",
                "Continue").ConfigureAwait(true);
            await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={active.Id}")
                .ConfigureAwait(true);
            return;
        }

        var stage = string.IsNullOrWhiteSpace(fixture.Label) ? fixture.StageText : fixture.Label.Trim();
        var matchTitle = string.IsNullOrWhiteSpace(stage)
            ? $"{Name.Trim()} : {fixture.TeamAName} vs {fixture.TeamBName}"
            : $"{Name.Trim()} : {fixture.TeamAName} vs {fixture.TeamBName} ({stage})";
        var title = Uri.EscapeDataString(matchTitle);
        await Shell.Current.GoToAsync(
                $"{nameof(Pages.MatchEditPage)}" +
                $"?tournamentId={_id}&fixtureId={fixture.Id}" +
                $"&teamAId={fixture.TeamAId}&teamBId={fixture.TeamBId}&title={title}" +
                $"&venueId={_venueId}&formatId={_formatId}&format={_format}&overs={_oversLimit}&oversMode={_oversMode}" +
                $"&lastBat={(_lastBatsmanCanBat ? 1 : 0)}&bowlerMax={_bowlerMaxOvers}" +
                $"&lockSettings=1")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResumeFixtureAsync(TournamentFixtureItem? fixture)
    {
        if (fixture?.MatchId is not Guid matchId) return;

        // After a reinstall SQLite is empty — pull the match back down before deciding
        // whether this device can score it.
        await _matches.EnsureLocalMatchAsync(matchId).ConfigureAwait(true);

        if (!await _matches.IsScoredOnThisDeviceAsync(matchId).ConfigureAwait(true))
        {
            await Shell.Current.GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={matchId}")
                .ConfigureAwait(true);
            return;
        }

        var active = await _matches.GetActiveMatchAsync().ConfigureAwait(true);
        if (active is not null && active.Id != matchId)
        {
            await Shell.Current.DisplayAlert(
                "Match in progress",
                $"Only one match can run at a time in this league on this device. Finish or complete \"{active.Title}\" first.",
                "Continue").ConfigureAwait(true);
            await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={active.Id}")
                .ConfigureAwait(true);
            return;
        }

        await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={matchId}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenScorecardAsync(TournamentFixtureItem? fixture)
    {
        if (fixture?.MatchId is not Guid matchId) return;
        await _matches.EnsureLocalMatchAsync(matchId, reclaimDeviceIfOwner: false).ConfigureAwait(true);
        await Shell.Current.GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={matchId}")
            .ConfigureAwait(true);
    }
}
