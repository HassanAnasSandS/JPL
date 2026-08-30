using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class SchedulerRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string MetaLine { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string WinnerLine { get; init; } = string.Empty;
    public string OwnerLine { get; init; } = string.Empty;
    public bool CanEdit { get; init; }
    public bool HasWinner => !string.IsNullOrWhiteSpace(WinnerLine);
    public bool HasOwner => !string.IsNullOrWhiteSpace(OwnerLine);
}

public sealed class SchedulerUserOption
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public partial class TournamentSchedulerViewModel : ObservableObject
{
    private static readonly SchedulerUserOption AllUsersOption = new() { Id = Guid.Empty, Name = "All users" };

    private readonly ITournamentSchedulerService _scheduler;
    private readonly IAuthService _auth;
    private int _loadBusy;
    private int _filterGen;
    private int _suppressFilter;

    public ObservableCollection<SchedulerRow> Items { get; } = new();
    public ObservableCollection<SchedulerUserOption> UserFilterOptions { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private string? nameFilter;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private SchedulerUserOption? selectedUserFilter;

    public bool CanWrite => _auth.Current is { IsGuest: false };
    public bool ShowUserFilter => _auth.Current?.CanManageLeagues == true;
    public bool HasFilter =>
        !string.IsNullOrWhiteSpace(NameFilter)
        || (ShowUserFilter && SelectedUserFilter is { } user && user.Id != Guid.Empty);
    public string CountLabel => TotalCount <= 0 ? "No schedules" : $"{Items.Count} of {TotalCount}";

    public TournamentSchedulerViewModel(ITournamentSchedulerService scheduler, IAuthService auth)
    {
        _scheduler = scheduler;
        _auth = auth;
        UserFilterOptions.Add(AllUsersOption);
        SelectedUserFilter = AllUsersOption;
    }

    partial void OnNameFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasFilter));
        if (_suppressFilter == 0)
            _ = ReloadDebouncedAsync();
    }

    partial void OnSelectedUserFilterChanged(SchedulerUserOption? value)
    {
        OnPropertyChanged(nameof(HasFilter));
        if (_suppressFilter == 0)
            _ = ReloadAsync();
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));

    [RelayCommand]
    private Task LoadAsync() => ReloadAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        HasMore = true;
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
            OnPropertyChanged(nameof(CanWrite));
            OnPropertyChanged(nameof(ShowUserFilter));

            var createdBy = ShowUserFilter && SelectedUserFilter is { Id: var userId } && userId != Guid.Empty
                ? userId
                : (Guid?)null;
            var page = await _scheduler.GetPageAsync(Items.Count, NameFilter, createdBy).ConfigureAwait(true);
            TotalCount = page.TotalCount;
            var rows = page.Items.Select(ToRow).ToList();
            ListPaging.Append(Items, rows, r => r.Id);
            MergeUserFilter(page.Items);
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

    private SchedulerRow ToRow(SchedulerListItem s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        MetaLine = s.MetaLine,
        StatusText = s.StatusText,
        WinnerLine = s.WinnerLine,
        OwnerLine = ShowUserFilter && !string.IsNullOrWhiteSpace(s.CreatedByUsername)
            ? $"Owner: {s.CreatedByUsername}"
            : string.Empty,
        CanEdit = CanWrite && s.CanEdit
    };

    private void MergeUserFilter(IReadOnlyList<SchedulerListItem> rows)
    {
        if (!ShowUserFilter)
            return;

        var have = UserFilterOptions.Select(x => x.Id).ToHashSet();
        foreach (var row in rows)
        {
            if (!have.Add(row.CreatedByUserId))
                continue;
            var name = string.IsNullOrWhiteSpace(row.CreatedByUsername) ? "Unknown user" : row.CreatedByUsername;
            UserFilterOptions.Add(new SchedulerUserOption { Id = row.CreatedByUserId, Name = name });
        }
    }

    [RelayCommand]
    private void ClearFilter()
    {
        Interlocked.Exchange(ref _suppressFilter, 1);
        NameFilter = null;
        SelectedUserFilter = AllUsersOption;
        Interlocked.Exchange(ref _suppressFilter, 0);
        _ = ReloadAsync();
    }

    [RelayCommand]
    private async Task OpenAsync(SchedulerRow? row)
    {
        if (row is null) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.TournamentSchedulerDetailPage)}?id={row.Id}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync(SchedulerRow? row)
    {
        if (row is null || !row.CanEdit) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.TournamentSchedulerEditPage)}?id={row.Id}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(SchedulerRow? row)
    {
        if (row is null || !row.CanEdit) return;
        var ok = await Shell.Current.DisplayAlert(
            "Delete scheduler",
            $"Delete \"{row.Name}\"? This only removes the manual schedule.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            await _scheduler.DeleteAsync(row.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Delete failed", ex.Message, "OK").ConfigureAwait(true);
        }
    }
}
