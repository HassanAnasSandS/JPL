using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class NetSessionsViewModel : ObservableObject
{
    private readonly INetSessionService _sessions;
    private readonly IAuthService _auth;
    private readonly ISyncService _sync;
    private List<NetSessionListItem> _all = new();

    public ObservableCollection<NetSessionListItem> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private bool hasActiveSession;
    [ObservableProperty] private string addButtonText = "Add";
    [ObservableProperty] private string? activeSessionHint;
    [ObservableProperty] private string nameFilter = string.Empty;
    [ObservableProperty] private bool filterByDate;
    [ObservableProperty] private DateTime filterDate = DateTime.Today;

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(NameFilter) || FilterByDate;

    private NetSessionListItem? _active;

    public NetSessionsViewModel(INetSessionService sessions, IAuthService auth, ISyncService sync)
    {
        _sessions = sessions;
        _auth = auth;
        _sync = sync;
        CanWrite = _auth.CanWrite;
    }

    partial void OnNameFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnFilterByDateChanged(bool value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnFilterDateChanged(DateTime value) => ApplyFilters();

    [RelayCommand]
    private void ClearFilters()
    {
        NameFilter = string.Empty;
        FilterByDate = false;
        FilterDate = DateTime.Today;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            CanWrite = _auth.CanWrite;
            _all = (await _sessions.GetAllAsync().ConfigureAwait(true)).ToList();
            _active = await _sessions.GetActiveAsync().ConfigureAwait(true);
            HasActiveSession = _active is not null;
            AddButtonText = HasActiveSession ? "Continue" : "+ Add";
            ActiveSessionHint = _active is null
                ? null
                : $"In progress: {_active.Name}. Saved on this phone until Complete + Sync.";
            ApplyFilters();
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

    private void ApplyFilters()
    {
        IEnumerable<NetSessionListItem> q = _all;
        if (!string.IsNullOrWhiteSpace(NameFilter))
        {
            var f = NameFilter.Trim();
            q = q.Where(s => s.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        if (FilterByDate)
        {
            var key = FilterDate.ToString("yyyy-MM-dd");
            q = q.Where(s => s.SessionDateUtc.ToString("yyyy-MM-dd") == key);
        }

        Items.Clear();
        foreach (var item in q)
            Items.Add(item);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task AddAsync()
    {
        if (!CanWrite)
            return;

        if (_active is not null)
        {
            await OpenAsync(_active).ConfigureAwait(true);
            return;
        }

        await Shell.Current.GoToAsync(nameof(Pages.NetSessionEditPage)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenAsync(NetSessionListItem? session)
    {
        if (session is null)
            return;

        if (!session.CanScore)
        {
            await Shell.Current.GoToAsync($"{nameof(Pages.NetSessionScorecardPage)}?id={session.Id}")
                .ConfigureAwait(true);
            return;
        }

        var active = _active ?? await _sessions.GetActiveAsync().ConfigureAwait(true);
        if (active is not null && active.Id != session.Id)
        {
            await Shell.Current.DisplayAlert(
                "Session in progress",
                $"Only one nets session can run at a time. Finish or complete \"{active.Name}\" first.",
                "Continue active").ConfigureAwait(true);
            await Shell.Current.GoToAsync($"{nameof(Pages.NetsScoringPage)}?id={active.Id}").ConfigureAwait(true);
            return;
        }

        if (session.Status == Domain.NetSessionStatus.Setup && session.PlayerCount == 0)
        {
            await Shell.Current.GoToAsync($"{nameof(Pages.NetSessionEditPage)}?id={session.Id}").ConfigureAwait(true);
            return;
        }

        await Shell.Current.GoToAsync($"{nameof(Pages.NetsScoringPage)}?id={session.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SyncAsync(NetSessionListItem? session)
    {
        if (session is null || !session.NeedsSync)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await _sync.FlushNetSessionAsync(session.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            await Shell.Current.DisplayAlert("Sync", "Session synced.", "OK").ConfigureAwait(true);
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
    private async Task DeleteAsync(NetSessionListItem? session)
    {
        if (!CanWrite || session is null)
            return;

        if (!session.CanDelete)
        {
            await Shell.Current.DisplayAlert(
                "Cannot delete",
                "Completed sessions cannot be deleted.",
                "OK").ConfigureAwait(true);
            return;
        }

        var ok = await Shell.Current.DisplayAlert(
            "Delete session",
            $"Delete ongoing session \"{session.Name}\"? Scoring for this session will be removed from this device.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            await _sessions.DeleteOngoingAsync(session.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    partial void OnCanWriteChanged(bool value) => AddCommand.NotifyCanExecuteChanged();
}
