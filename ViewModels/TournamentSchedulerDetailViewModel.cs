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

    public ObservableCollection<SchedulerStandingGroup> StandingGroups { get; } = new();
    public ObservableCollection<SchedulerMatchGroup> MatchGroups { get; } = new();
    public ObservableCollection<SchedulerTieBreakPick> TieBreakPicks { get; } = new();

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string metaLine = string.Empty;
    [ObservableProperty] private string winnerLine = string.Empty;
    [ObservableProperty] private bool hasWinner;
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private bool hasStandings;
    [ObservableProperty] private bool hasMatches;
    [ObservableProperty] private bool showTieBreakModal;
    [ObservableProperty] private string tieBreakMessage = string.Empty;
    [ObservableProperty] private string tieBreakCountLine = string.Empty;
    [ObservableProperty] private bool canConfirmTieBreak;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;

    private int _tieBreakNeed;
    private int _tieBreakExtra;
    private bool _suppressTieBreak;

    public bool CanWrite => _auth.Current is { IsGuest: false };
    public bool ShowNoMatches => !HasMatches;

    public TournamentSchedulerDetailViewModel(ITournamentSchedulerService scheduler, IAuthService auth)
    {
        _scheduler = scheduler;
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
        CanEdit = CanWrite && detail.CanEdit;
        HasStandings = detail.Standings.Count > 0;
        HasMatches = detail.Matches.Count > 0;

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
        foreach (var group in detail.Matches.GroupBy(m => m.StageTitle))
        {
            var block = new SchedulerMatchGroup { Title = group.Key };
            foreach (var match in group.OrderBy(m => m.Sequence))
                block.Matches.Add(match);
            MatchGroups.Add(block);
        }

        var q = detail.Qualification;
        if (detail.GroupStageComplete && q.NeedsManualPick)
        {
            TieBreakMessage = q.Message;
            _tieBreakNeed = SchedulerEngine.KnockoutTeamCount(detail.KnockoutFormat);
            _tieBreakExtra = q.PickCount;
            TieBreakCountLine = _tieBreakExtra == 1
                ? $"Already through teams stay locked. Pick 1 more team ({_tieBreakNeed} qualify in total)."
                : $"Already through teams stay locked. Pick {_tieBreakExtra} more teams ({_tieBreakNeed} qualify in total).";
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
                    Name = isLocked ? $"{team.Name} (already through)" : team.Name,
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
        if (match is null || !CanWrite) return;
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
            await Shell.Current.DisplayAlert("Could not qualify teams", ex.Message, "OK").ConfigureAwait(true);
        }
    }
}
