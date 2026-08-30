using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class TeamsViewModel : ObservableObject
{
    private readonly ITeamService _teams;
    private readonly ITeamImageService _images;
    private readonly IAuthService _auth;
    private int _loadBusy;
    private int _filterGen;
    private int _suppressFilter;

    public ObservableCollection<TeamListItem> Items { get; } = new();

    /// <summary>0 = All, 1 = Active, 2 = Deactive</summary>
    [ObservableProperty] private int statusFilter;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private string nameFilter = string.Empty;
    [ObservableProperty] private string captainFilter = string.Empty;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private int activeCount;
    [ObservableProperty] private int inactiveCount;

    public string CountLabel => TotalCount <= 0 ? "No teams" : $"{Items.Count} of {TotalCount}";

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(NameFilter)
        || !string.IsNullOrWhiteSpace(CaptainFilter)
        || StatusFilter != 0;

    public bool IsAllFilter => StatusFilter == 0;
    public bool IsActiveFilter => StatusFilter == 1;
    public bool IsInactiveFilter => StatusFilter == 2;

    public string AllFilterLabel => $"All ({TotalCount})";
    public string ActiveFilterLabel => $"Active ({ActiveCount})";
    public string InactiveFilterLabel => $"Deactive ({InactiveCount})";

    public TeamsViewModel(ITeamService teams, ITeamImageService images, IAuthService auth)
    {
        _teams = teams;
        _images = images;
        _auth = auth;
        CanWrite = _auth.CanWrite;
    }

    public void RefreshAuthState() => CanWrite = _auth.CanWrite;

    partial void OnNameFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        if (_suppressFilter == 0)
            _ = ReloadDebouncedAsync();
    }

    partial void OnCaptainFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        if (_suppressFilter == 0)
            _ = ReloadDebouncedAsync();
    }

    partial void OnStatusFilterChanged(int value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsActiveFilter));
        OnPropertyChanged(nameof(IsInactiveFilter));
        if (_suppressFilter == 0)
            _ = ReloadAsync();
    }

    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(AllFilterLabel));
        OnPropertyChanged(nameof(CountLabel));
    }

    partial void OnActiveCountChanged(int value) => OnPropertyChanged(nameof(ActiveFilterLabel));
    partial void OnInactiveCountChanged(int value) => OnPropertyChanged(nameof(InactiveFilterLabel));

    [RelayCommand]
    public Task LoadAsync() => ReloadAsync();

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
            RefreshAuthState();
            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);

            var page = await _teams.GetPageAsync(Items.Count, NameFilter, CaptainFilter, StatusFilter)
                .ConfigureAwait(true);
            TotalCount = page.TotalCount;
            ActiveCount = page.ActiveCount ?? ActiveCount;
            InactiveCount = page.InactiveCount ?? InactiveCount;

            IReadOnlyDictionary<Guid, ImageSource> photos = new Dictionary<Guid, ImageSource>();
            try
            {
                photos = await _images.GetPhotosAsync(page.Items.Select(t => t.Id)).ConfigureAwait(true);
            }
            catch
            {
                // List still works if logos fail.
            }

            ListPaging.Append(Items, page.Items.Select(t => t with
            {
                Photo = photos.GetValueOrDefault(t.Id),
                Initials = PlayerPhotos.Initials(t.Name)
            }), t => t.Id);
            HasMore = page.HasMore;
            OnPropertyChanged(nameof(CountLabel));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            if (Items.Count == 0)
            {
                TotalCount = 0;
                ActiveCount = 0;
                InactiveCount = 0;
            }
        }
        finally
        {
            IsBusy = false;
            IsLoadingMore = false;
            Interlocked.Exchange(ref _loadBusy, 0);
        }
    }

    [RelayCommand]
    private void SelectStatus(string? status)
    {
        StatusFilter = status switch
        {
            "active" => 1,
            "inactive" or "deactive" => 2,
            _ => 0
        };
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Interlocked.Exchange(ref _suppressFilter, 1);
        NameFilter = string.Empty;
        CaptainFilter = string.Empty;
        StatusFilter = 0;
        Interlocked.Exchange(ref _suppressFilter, 0);
        _ = ReloadAsync();
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanWrite)
            return;
        await Shell.Current.GoToAsync(nameof(Pages.TeamEditPage)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ViewAsync(TeamListItem? team)
    {
        if (team is null)
            return;

        await Shell.Current.GoToAsync($"{nameof(Pages.TeamEditPage)}?id={team.Id}&mode=view").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync(TeamListItem? team)
    {
        if (!CanWrite || team is null)
            return;

        await Shell.Current.GoToAsync($"{nameof(Pages.TeamEditPage)}?id={team.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(TeamListItem? team)
    {
        if (!CanWrite || team is null)
            return;

        var next = !team.IsActive;
        var verb = next ? "Activate" : "Deactivate";
        var ok = await Shell.Current.DisplayAlert(
            $"{verb} team",
            $"{verb} {team.Name}? Inactive teams will not appear in new match dropdowns.",
            verb,
            "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            await _teams.SetActiveAsync(team.Id, next).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(TeamListItem? team)
    {
        if (!CanWrite || team is null)
            return;

        var ok = await Shell.Current.DisplayAlert(
            "Delete team",
            $"Remove team {team.Name}?",
            "Delete",
            "Cancel").ConfigureAwait(true);

        if (!ok)
            return;

        try
        {
            await _teams.SoftDeleteAsync(team.Id).ConfigureAwait(true);
            _images.Invalidate(team.Id);

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.UserMessage(ex);
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
    }
}
