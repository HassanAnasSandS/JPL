using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class MatchesViewModel : ObservableObject
{
    private readonly IMatchService _matches;
    private readonly IAuthService _auth;
    private readonly ISyncService _sync;
    private List<MatchListItem> _all = new();

    public ObservableCollection<MatchListItem> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private bool hasActiveMatch;
    [ObservableProperty] private string addButtonText = "Add";
    [ObservableProperty] private string? activeMatchHint;
    [ObservableProperty] private string teamFilter = string.Empty;
    [ObservableProperty] private string captainFilter = string.Empty;
    [ObservableProperty] private bool filterByDate;
    [ObservableProperty] private DateTime filterDate = DateTime.Today;

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(TeamFilter)
        || !string.IsNullOrWhiteSpace(CaptainFilter)
        || FilterByDate;

    private MatchListItem? _activeMatch;

    public MatchesViewModel(IMatchService matches, IAuthService auth, ISyncService sync)
    {
        _matches = matches;
        _auth = auth;
        _sync = sync;
        CanWrite = _auth.CanWrite;
    }

    partial void OnTeamFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnCaptainFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnFilterByDateChanged(bool value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnFilterDateChanged(DateTime value)
    {
        if (FilterByDate)
            ApplyFilters();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            CanWrite = _auth.CanWrite;
            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);
            _all = (await _matches.GetAllAsync().ConfigureAwait(true)).ToList();
            ApplyFilters();

            _activeMatch = await _matches.GetActiveMatchAsync().ConfigureAwait(true);
            HasActiveMatch = _activeMatch is not null;
            var owned = _activeMatch?.IsScoringOwner == true;
            AddButtonText = owned ? "Continue" : "+ Add";
            ActiveMatchHint = _activeMatch is null
                ? null
                : owned
                    ? $"LIVE on this device: {_activeMatch.Title}. Score goes to the server as you play. If internet drops, balls stay here and auto-send when it returns."
                    : $"LIVE on this device: {_activeMatch.Title}. Another admin started this match — view only. You cannot continue or edit.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        TeamFilter = string.Empty;
        CaptainFilter = string.Empty;
        FilterByDate = false;
        FilterDate = DateTime.Today;
    }

    private void ApplyFilters()
    {
        IEnumerable<MatchListItem> q = _all;
        var team = TeamFilter?.Trim();
        var captain = CaptainFilter?.Trim();

        if (!string.IsNullOrWhiteSpace(team))
        {
            q = q.Where(m =>
                m.TeamAName.Contains(team, StringComparison.OrdinalIgnoreCase)
                || m.TeamBName.Contains(team, StringComparison.OrdinalIgnoreCase)
                || m.Title.Contains(team, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(captain))
        {
            q = q.Where(m =>
                (m.TeamACaptainName ?? string.Empty).Contains(captain, StringComparison.OrdinalIgnoreCase)
                || (m.TeamBCaptainName ?? string.Empty).Contains(captain, StringComparison.OrdinalIgnoreCase));
        }

        if (FilterByDate)
        {
            var key = FilterDate.Date.ToString("yyyy-MM-dd");
            q = q.Where(m => m.DateKey == key);
        }

        Items.Clear();
        foreach (var m in q)
            Items.Add(m with { AllowMutate = CanWrite, NeedsSync = CanWrite && m.NeedsSync });
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanWrite)
            return;

        var active = _activeMatch ?? await _matches.GetActiveMatchAsync().ConfigureAwait(true);
        if (active is not null)
        {
            if (!active.IsScoringOwner)
            {
                await Shell.Current.DisplayAlert(
                    "View only",
                    ScoringOwnership.OtherAccountMessage,
                    "OK").ConfigureAwait(true);
                await Shell.Current.GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={active.Id}").ConfigureAwait(true);
                return;
            }

            await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={active.Id}").ConfigureAwait(true);
            return;
        }

        await Shell.Current.GoToAsync(nameof(Pages.MatchEditPage)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenAsync(MatchListItem? match)
    {
        if (match is null) return;

        if (!match.CanScore)
        {
            await Shell.Current.GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={match.Id}").ConfigureAwait(true);
            return;
        }

        var active = _activeMatch ?? await _matches.GetActiveMatchAsync().ConfigureAwait(true);
        if (active is not null && active.Id != match.Id)
        {
            if (!active.IsScoringOwner)
            {
                await Shell.Current.DisplayAlert(
                    "View only",
                    ScoringOwnership.OtherAccountMessage,
                    "OK").ConfigureAwait(true);
                await Shell.Current.GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={active.Id}").ConfigureAwait(true);
                return;
            }

            await Shell.Current.DisplayAlert(
                "Match in progress",
                $"Only one match can run at a time in this league on this device. Finish or complete \"{active.Title}\" first.",
                "Continue active").ConfigureAwait(true);
            await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={active.Id}").ConfigureAwait(true);
            return;
        }

        await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={match.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SyncAsync(MatchListItem? match)
    {
        if (match is null || !match.NeedsSync)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await _sync.FlushMatchAsync(match.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            await Shell.Current.DisplayAlert("Sync", "Match synced.", "OK").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — cannot sync right now."
                : ex.Message;
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(MatchListItem? match)
    {
        if (!CanWrite || match is null)
            return;

        if (!match.CanDelete)
        {
            await Shell.Current.DisplayAlert(
                "Cannot delete",
                "Completed matches cannot be deleted.",
                "OK").ConfigureAwait(true);
            return;
        }

        var ok = await Shell.Current.DisplayAlert(
            "Delete match",
            $"Delete ongoing match \"{match.Title}\"? Scoring data for this match will be removed from this device.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            await _matches.DeleteOngoingAsync(match.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    partial void OnCanWriteChanged(bool value) => AddCommand.NotifyCanExecuteChanged();
}
