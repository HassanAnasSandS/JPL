using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
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

    public ObservableCollection<SchedulerRow> Items { get; } = new();
    public ObservableCollection<SchedulerUserOption> UserFilterOptions { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? nameFilter;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private SchedulerUserOption? selectedUserFilter;

    private List<SchedulerListItem> _all = new();

    public bool CanWrite => _auth.Current is { IsGuest: false };
    public bool ShowUserFilter => _auth.Current?.CanManageLeagues == true;
    public bool HasFilter =>
        !string.IsNullOrWhiteSpace(NameFilter)
        || (ShowUserFilter && SelectedUserFilter is { } user && user.Id != Guid.Empty);

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
        ApplyFilter();
    }

    partial void OnSelectedUserFilterChanged(SchedulerUserOption? value)
    {
        OnPropertyChanged(nameof(HasFilter));
        ApplyFilter();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            OnPropertyChanged(nameof(CanWrite));
            OnPropertyChanged(nameof(ShowUserFilter));
            _all = (await _scheduler.GetAllAsync().ConfigureAwait(true)).ToList();
            RebuildUserFilter();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _all = [];
            Items.Clear();
        }
        finally { IsBusy = false; }
    }

    private void RebuildUserFilter()
    {
        var keepId = SelectedUserFilter?.Id ?? Guid.Empty;
        UserFilterOptions.Clear();
        UserFilterOptions.Add(AllUsersOption);

        if (ShowUserFilter)
        {
            foreach (var group in _all
                .GroupBy(s => s.CreatedByUserId)
                .OrderBy(g => g.First().CreatedByUsername, StringComparer.OrdinalIgnoreCase))
            {
                var name = string.IsNullOrWhiteSpace(group.First().CreatedByUsername)
                    ? "Unknown user"
                    : group.First().CreatedByUsername;
                UserFilterOptions.Add(new SchedulerUserOption { Id = group.Key, Name = name });
            }
        }

        SelectedUserFilter = UserFilterOptions.FirstOrDefault(x => x.Id == keepId) ?? AllUsersOption;
        OnPropertyChanged(nameof(HasFilter));
    }

    private void ApplyFilter()
    {
        Items.Clear();
        IEnumerable<SchedulerListItem> q = _all;
        if (ShowUserFilter && SelectedUserFilter is { Id: var userId } && userId != Guid.Empty)
            q = q.Where(s => s.CreatedByUserId == userId);

        if (!string.IsNullOrWhiteSpace(NameFilter))
        {
            var f = NameFilter.Trim();
            q = q.Where(s =>
                s.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                || s.StatusText.Contains(f, StringComparison.OrdinalIgnoreCase)
                || s.MetaLine.Contains(f, StringComparison.OrdinalIgnoreCase)
                || s.CreatedByUsername.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var s in q)
        {
            Items.Add(new SchedulerRow
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
            });
        }
    }

    [RelayCommand]
    private void ClearFilter()
    {
        NameFilter = null;
        SelectedUserFilter = AllUsersOption;
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
