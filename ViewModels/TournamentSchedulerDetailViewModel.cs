using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class SchedulerStandingGroup
{
    public string Title { get; init; } = string.Empty;
    public bool ShowTitle { get; init; }
    public ObservableCollection<SchedulerStandingRow> Rows { get; } = new();
}

public sealed class SchedulerMatchGroup
{
    public string Title { get; init; } = string.Empty;
    public ObservableCollection<SchedulerMatchView> Matches { get; } = new();
}

/// <summary>One fixture row in the points-table team schedule modal.</summary>
public sealed class TeamScheduleMatchItem
{
    public string StageLabel { get; init; } = string.Empty;
    public string OpponentName { get; init; } = string.Empty;
    public string OpponentLine => string.IsNullOrWhiteSpace(OpponentName) ? "vs TBD" : $"vs {OpponentName}";
    public string StatusText { get; init; } = string.Empty;
    public string ResultText { get; init; } = string.Empty;
    public bool HasResult { get; init; }
    public bool ShowStatus => !string.IsNullOrWhiteSpace(StatusText);

    /// <summary>Scheduler fixture — Result/Change uses this.</summary>
    public SchedulerMatchView? SchedulerMatch { get; init; }
    public bool ShowResultAction => SchedulerMatch?.CanRecord == true;
    public string ResultActionLabel => HasResult ? "Change" : "Result";

    /// <summary>League fixture — Start/Resume/Scorecard use this.</summary>
    public TournamentFixtureItem? Fixture { get; init; }
    public bool CanStart => Fixture?.CanStart == true;
    public bool CanResume => Fixture?.CanResume == true;
    public bool CanOpenScorecard => Fixture?.CanOpenScorecard == true;
    public string ScorecardLabel => Fixture?.ScorecardLabel ?? "Scorecard";
}

public partial class SchedulerTieBreakPick : ObservableObject
{
    public Guid TeamId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Meta { get; init; } = string.Empty;
    public bool IsLocked { get; init; }
    public bool CanToggle => !IsLocked;
    public Action<SchedulerTieBreakPick>? SelectionChanged { get; set; }

    [ObservableProperty] private bool isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (IsLocked && !value)
        {
            IsSelected = true;
            return;
        }

        SelectionChanged?.Invoke(this);
    }
}

public partial class TournamentSchedulerDetailViewModel : ObservableObject, IQueryAttributable
{
    private readonly ITournamentSchedulerService _scheduler;
    private readonly IAuthService _auth;

    private Guid _id;
    private Guid? _teamScheduleTeamId;
    private IReadOnlyList<SchedulerMatchView> _allMatches = Array.Empty<SchedulerMatchView>();

    public ObservableCollection<SchedulerStandingGroup> StandingGroups { get; } = new();
    public ObservableCollection<SchedulerMatchGroup> MatchGroups { get; } = new();
    public ObservableCollection<SchedulerTieBreakPick> TieBreakPicks { get; } = new();
    public ObservableCollection<TeamScheduleMatchItem> TeamScheduleMatches { get; } = new();

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string metaLine = string.Empty;
    [ObservableProperty] private string winnerLine = string.Empty;
    [ObservableProperty] private bool hasWinner;
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private bool hasStandings;
    [ObservableProperty] private bool hasMatches;
    [ObservableProperty] private bool showTieBreakModal;
    [ObservableProperty] private string tieBreakTitle = "Tied on points";
    [ObservableProperty] private string tieBreakMessage = string.Empty;
    [ObservableProperty] private string tieBreakCountLine = string.Empty;
    [ObservableProperty] private string tieBreakConfirmText = "Confirm knockout teams";
    [ObservableProperty] private bool canConfirmTieBreak;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string tournamentMatchesLine = string.Empty;
    [ObservableProperty] private string groupRoundMatchesLine = string.Empty;
    [ObservableProperty] private string groupMatchesLine = string.Empty;
    [ObservableProperty] private bool showTeamScheduleModal;
    [ObservableProperty] private string teamScheduleTitle = string.Empty;
    [ObservableProperty] private string teamScheduleSubtitle = string.Empty;
    [ObservableProperty] private bool teamScheduleEmpty;
    [ObservableProperty] private bool isReadOnlyView;
    [ObservableProperty] private bool canChangeResults;
    [ObservableProperty] private bool showResultHint;
    [ObservableProperty] private bool showViewOnlyBanner;
    [ObservableProperty] private string viewOnlyBanner = string.Empty;

    private int _tieBreakNeed;
    private int _tieBreakExtra;
    private bool _suppressTieBreak;

    public bool CanWrite => _auth.Current is { IsGuest: false };
    public bool ShowNoMatches => !HasMatches;

    partial void OnHasMatchesChanged(bool value) => OnPropertyChanged(nameof(ShowNoMatches));

    public TournamentSchedulerDetailViewModel(ITournamentSchedulerService scheduler, IAuthService auth)
    {
        _scheduler = scheduler;
        _auth = auth;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var raw) && Guid.TryParse($"{raw}", out var id))
            _id = id;

        IsReadOnlyView = false;
        if (query.TryGetValue("view", out var viewRaw))
        {
            var text = $"{viewRaw}".Trim();
            IsReadOnlyView = text is "1" or "true" or "yes";
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_id == Guid.Empty || IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var detail = await _scheduler.GetAsync(_id).ConfigureAwait(true);
            Bind(detail);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    private void Bind(SchedulerDetail detail)
    {
        Name = detail.Name;
        StatusText = detail.StatusText;
        MetaLine = detail.MetaLine;
        WinnerLine = detail.WinnerLine;
        HasWinner = detail.HasWinner;
        CanEdit = CanWrite && detail.CanEdit && !IsReadOnlyView;
        var isDraft = detail.Status == SchedulerStatus.Draft;
        CanChangeResults = CanWrite && detail.CanEdit && !IsReadOnlyView && !isDraft;
        ShowResultHint = CanChangeResults;
        ShowViewOnlyBanner = IsReadOnlyView || !detail.CanEdit || isDraft;
        ViewOnlyBanner = IsReadOnlyView
            ? "View only — browse standings and fixtures. Results cannot be changed here."
            : isDraft
                ? detail.RoundRobinType == SchedulerRoundRobinType.Custom
                    ? "Draft — finish Custom fixtures in Edit setup (matches per team + who vs who). Schedule starts after that."
                    : "Draft — open Edit setup to finish and start the schedule."
                : detail.CanEdit
                    ? string.Empty
                    : "Completed — this schedule is locked. Edit and delete are disabled.";
        HasStandings = detail.Standings.Count > 0;
        HasMatches = detail.Matches.Count > 0;
        _allMatches = detail.Matches.ToList();

        if (!CanChangeResults)
        {
            foreach (var m in _allMatches)
                m.CanRecord = false;
        }

        var groupMatches = detail.Matches.Where(m => !m.IsKnockout).ToList();
        var knockoutMatches = detail.Matches.Where(m => m.IsKnockout).ToList();
        var total = detail.Matches.Count;
        var groupRound = groupMatches.Count;
        TournamentMatchesLine = total == 1 ? "Total tournament matches: 1" : $"Total tournament matches: {total}";
        GroupRoundMatchesLine = groupRound == 1 ? "Group round matches: 1" : $"Group round matches: {groupRound}";
        if (detail.GroupCount <= 1)
        {
            GroupMatchesLine = groupRound == 1 ? "Group matches: 1" : $"Group matches: {groupRound}";
        }
        else
        {
            GroupMatchesLine = string.Join(" · ",
                groupMatches.GroupBy(m => m.GroupNumber).OrderBy(g => g.Key)
                    .Select(g =>
                    {
                        var n = g.Count();
                        return n == 1 ? $"Group {g.Key}: 1 match" : $"Group {g.Key}: {n} matches";
                    }));
        }

        StandingGroups.Clear();
        foreach (var group in detail.Standings.GroupBy(s => s.GroupNumber).OrderBy(g => g.Key))
        {
            var block = new SchedulerStandingGroup
            {
                Title = detail.GroupCount == 1 ? "Points table" : $"Group {group.Key}",
                ShowTitle = detail.GroupCount > 1
            };
            foreach (var row in group.OrderBy(r => r.Rank))
                block.Rows.Add(row);
            StandingGroups.Add(block);
        }

        MatchGroups.Clear();
        var playDays = SchedulerEngine.AssignGroupPlayDays(
            detail.Teams,
            groupMatches.Select(m => (m.Id, m.GroupNumber, m.Sequence, m.TeamAId, m.TeamBId)).ToList());
        foreach (var day in groupMatches
                     .GroupBy(m => playDays.TryGetValue(m.Id, out var d) ? d : 1)
                     .OrderBy(g => g.Key))
        {
            var block = new SchedulerMatchGroup { Title = $"Day {day.Key}" };
            foreach (var match in day.OrderBy(m => m.GroupNumber).ThenBy(m => m.Sequence))
                block.Matches.Add(match);
            MatchGroups.Add(block);
        }

        foreach (var group in knockoutMatches.GroupBy(m => m.StageTitle))
        {
            var block = new SchedulerMatchGroup { Title = group.Key };
            foreach (var match in group.OrderBy(m => m.Sequence))
                block.Matches.Add(match);
            MatchGroups.Add(block);
        }

        var q = detail.Qualification;
        if (CanChangeResults && detail.GroupStageComplete && q.NeedsManualPick)
        {
            var isChampionPick = detail.KnockoutFormat == SchedulerKnockoutFormat.None;
            TieBreakTitle = isChampionPick ? "Champion tie" : "Tied on points";
            TieBreakMessage = q.Message;
            _tieBreakNeed = isChampionPick ? 1 : SchedulerEngine.KnockoutTeamCount(detail.KnockoutFormat);
            _tieBreakExtra = q.PickCount;
            TieBreakCountLine = isChampionPick
                ? "Pick 1 champion from the tied teams."
                : (_tieBreakExtra == 1
                    ? $"Already through teams stay locked. Pick 1 more team ({_tieBreakNeed} qualify in total)."
                    : $"Already through teams stay locked. Pick {_tieBreakExtra} more teams ({_tieBreakNeed} qualify in total).");
            TieBreakConfirmText = isChampionPick ? "Confirm champion" : "Confirm knockout teams";
            TieBreakPicks.Clear();
            var locked = q.AutoQualifiedIds.ToHashSet();
            foreach (var team in detail.Teams.OrderBy(t => t.GroupNumber).ThenBy(t => t.Name))
            {
                var isLocked = locked.Contains(team.Id);
                var standing = detail.Standings.FirstOrDefault(s => s.TeamId == team.Id);
                var allowed = isLocked || q.PickFrom.Any(p => p.Id == team.Id);
                if (!allowed)
                    continue;
                var pick = new SchedulerTieBreakPick
                {
                    TeamId = team.Id,
                    Name = isLocked
                        ? $"{team.Name} (already through)"
                        : team.Name,
                    Meta = standing is null
                        ? $"Group {team.GroupNumber}"
                        : $"Group {team.GroupNumber} · {standing.Points} pts · {standing.Won}W {standing.Lost}L {standing.Tied}T",
                    IsLocked = isLocked,
                    SelectionChanged = OnTieBreakSelectionChanged
                };
                pick.IsSelected = isLocked;
                TieBreakPicks.Add(pick);
            }

            RefreshTieBreakConfirm();
            ShowTieBreakModal = true;
        }
        else
        {
            ShowTieBreakModal = false;
            CanConfirmTieBreak = false;
            TieBreakPicks.Clear();
        }

        // Keep team schedule modal open after result refresh.
        if (_teamScheduleTeamId is Guid teamId)
        {
            var row = detail.Standings.FirstOrDefault(s => s.TeamId == teamId);
            if (row is not null)
                FillTeamSchedule(row);
            else
                CloseTeamSchedule();
        }
    }

    [RelayCommand]
    private void OpenTeamSchedule(SchedulerStandingRow? row)
    {
        if (row is null)
            return;
        _teamScheduleTeamId = row.TeamId;
        FillTeamSchedule(row);
    }

    private void FillTeamSchedule(SchedulerStandingRow row)
    {
        TeamScheduleMatches.Clear();
        var forTeam = _allMatches
            .Where(m => m.TeamAId == row.TeamId || m.TeamBId == row.TeamId)
            // Played first, then remaining; keep stage/sequence within each bucket.
            .OrderBy(m => m.HasResult ? 0 : 1)
            .ThenBy(m => m.IsKnockout)
            .ThenBy(m => m.GroupNumber)
            .ThenBy(m => m.Sequence)
            .ToList();

        foreach (var m in forTeam)
        {
            var isA = m.TeamAId == row.TeamId;
            var opponent = isA ? m.TeamBName : m.TeamAName;
            if (string.IsNullOrWhiteSpace(opponent) || opponent is "TBD" or "—")
                opponent = "TBD";

            var status = m.HasResult
                ? "Played"
                : m.BothTeamsKnown
                    ? "Upcoming"
                    : "Fixture TBD";

            TeamScheduleMatches.Add(new TeamScheduleMatchItem
            {
                StageLabel = string.IsNullOrWhiteSpace(m.Label) ? m.StageTitle : m.Label,
                OpponentName = opponent,
                StatusText = status,
                ResultText = m.ResultText,
                HasResult = m.HasResult,
                SchedulerMatch = m
            });
        }

        var played = TeamScheduleMatches.Count(x => x.HasResult);
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
    private async Task EditAsync()
    {
        if (!CanEdit) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.TournamentSchedulerEditPage)}?id={_id}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SetResultAsync(SchedulerMatchView? match)
    {
        if (match is null || !CanChangeResults) return;
        try
        {
            string[] buttons;
            if (match.IsKnockout)
            {
                buttons = [match.TeamAName, match.TeamBName, "Clear result"];
            }
            else
            {
                buttons = [match.TeamAName, "Tied", match.TeamBName, "Clear result"];
            }

            var choice = await Shell.Current.DisplayActionSheet(
                match.Label,
                "Cancel",
                null,
                buttons).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(choice) || choice == "Cancel")
                return;

            var result = choice switch
            {
                "Tied" => SchedulerMatchResult.Tied,
                "Clear result" => SchedulerMatchResult.None,
                _ when string.Equals(choice, match.TeamAName, StringComparison.Ordinal) => SchedulerMatchResult.TeamAWon,
                _ when string.Equals(choice, match.TeamBName, StringComparison.Ordinal) => SchedulerMatchResult.TeamBWon,
                _ => (SchedulerMatchResult?)null
            };
            if (result is null)
                return;

            await _scheduler.SetMatchResultAsync(_id, match.Id, result.Value).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await Shell.Current.DisplayAlert("Result not saved", ex.Message, "OK").ConfigureAwait(true);
        }
    }

    private void OnTieBreakSelectionChanged(SchedulerTieBreakPick pick)
    {
        if (_suppressTieBreak || pick.IsLocked)
            return;

        if (pick.IsSelected)
        {
            var extra = TieBreakPicks.Count(p => !p.IsLocked && p.IsSelected);
            if (extra > _tieBreakExtra)
            {
                _suppressTieBreak = true;
                pick.IsSelected = false;
                _suppressTieBreak = false;
            }
        }

        RefreshTieBreakConfirm();
    }

    private void RefreshTieBreakConfirm()
    {
        var extra = TieBreakPicks.Count(p => !p.IsLocked && p.IsSelected);
        var total = TieBreakPicks.Count(p => p.IsSelected);
        CanConfirmTieBreak = extra == _tieBreakExtra && total == _tieBreakNeed;
    }

    [RelayCommand]
    private async Task ConfirmTieBreakAsync()
    {
        if (!CanConfirmTieBreak)
            return;
        try
        {
            var ids = TieBreakPicks.Where(p => p.IsSelected).Select(p => p.TeamId).ToList();
            await _scheduler.SaveQualificationPicksAsync(_id, ids).ConfigureAwait(true);
            ShowTieBreakModal = false;
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await Shell.Current.DisplayAlert(
                TieBreakConfirmText.Contains("champion", StringComparison.OrdinalIgnoreCase)
                    ? "Could not set champion"
                    : "Could not qualify teams",
                ex.Message,
                "OK").ConfigureAwait(true);
        }
    }
}
