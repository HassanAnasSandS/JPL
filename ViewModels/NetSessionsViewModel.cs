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
    private int _loadBusy;
    private int _filterGen;
    private int _suppressFilter;
    private int _pinnedCount;

    public ObservableCollection<NetSessionListItem> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private bool hasActiveSession;
    [ObservableProperty] private string addButtonText = "Add";
    [ObservableProperty] private string? activeSessionHint;
    [ObservableProperty] private string nameFilter = string.Empty;
    [ObservableProperty] private bool filterByDate;
    [ObservableProperty] private DateTime filterDate = DateTime.Today;

    public string CountLabel => TotalCount <= 0 ? "No sessions" : $"{Items.Count} of {TotalCount}";

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
        if (_suppressFilter == 0)
            _ = ReloadDebouncedAsync();
    }

    partial void OnFilterByDateChanged(bool value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        if (_suppressFilter == 0)
            _ = ReloadAsync();
    }

    partial void OnFilterDateChanged(DateTime value)
    {
        if (FilterByDate && _suppressFilter == 0)
            _ = ReloadAsync();
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));

    [RelayCommand]
    private void ClearFilters()
    {
        Interlocked.Exchange(ref _suppressFilter, 1);
        NameFilter = string.Empty;
        FilterByDate = false;
        FilterDate = DateTime.Today;
        Interlocked.Exchange(ref _suppressFilter, 0);
        _ = ReloadAsync();
    }

    [RelayCommand]
    public Task LoadAsync() => ReloadAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        HasMore = true;
        _pinnedCount = 0;
        Items.Clear();
        OnPropertyChanged(nameof(CountLabel));
        await LoadMoreAsync().ConfigureAwait(true);
    }

    private async Task ReloadDebouncedAsync()
    {
        var gen = Interlocked.Increment(ref _filterGen);
        await Task.Delay(350).ConfigureAwait(true);
        if (gen != _filterGen)
            return;
        await ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || Interlocked.Exchange(ref _loadBusy, 1) == 1)
            return;

        try
        {
            if (Items.Count == 0)
                IsBusy = true;
            else
                IsLoadingMore = true;
            ErrorMessage = null;
            CanWrite = _auth.CanWrite;

            if (Items.Count == 0)
            {
                _active = await _sessions.GetActiveAsync().ConfigureAwait(true);
                HasActiveSession = _active is not null;
                AddButtonText = HasActiveSession ? "Continue" : "+ Add";
                ActiveSessionHint = _active is null
                    ? null
                    : $"In progress: {_active.Name}. Saved on this phone until Complete + Sync.";

                var pinned = SessionFilter(await _sessions.GetPinnedListAsync().ConfigureAwait(true));
                ListPaging.Append(Items, pinned, s => s.Id);
                _pinnedCount = Items.Count;
            }

            var skip = Math.Max(0, Items.Count - _pinnedCount);
            var date = FilterByDate ? FilterDate.ToString("yyyy-MM-dd") : null;
            var page = await _sessions.GetCompletedPageAsync(skip, NameFilter, date).ConfigureAwait(true);
            ListPaging.Append(Items, page.Items, s => s.Id);
            TotalCount = _pinnedCount + page.TotalCount;
            HasMore = page.HasMore;
            OnPropertyChanged(nameof(CountLabel));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            if (Items.Count == 0)
                TotalCount = 0;
        }
        finally
        {
            IsBusy = false;
            IsLoadingMore = false;
            Interlocked.Exchange(ref _loadBusy, 0);
        }
    }

    private IEnumerable<NetSessionListItem> SessionFilter(IEnumerable<NetSessionListItem> source)
    {
        IEnumerable<NetSessionListItem> q = source;
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

        return q;
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
