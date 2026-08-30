using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class LeagueRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public ImageSource Image { get; init; } = "cric360_logo.png";
}

public partial class LeaguesViewModel : ObservableObject
{
    private readonly ILeagueService _leagues;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly IAppNavigator _navigator;
    private readonly MasterDataCache _cache;
    private int _loadBusy;
    private int _filterGen;

    public ObservableCollection<LeagueRow> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private string? nameFilter;
    [ObservableProperty] private string? errorMessage;

    public bool CanWrite => _auth.Current?.CanManageLeagues == true;
    public bool HasFilter => !string.IsNullOrWhiteSpace(NameFilter);
    public string CountLabel => TotalCount <= 0 ? "No leagues" : $"{Items.Count} of {TotalCount}";

    public LeaguesViewModel(
        ILeagueService leagues,
        IAuthService auth,
        ILeagueContext league,
        IAppNavigator navigator,
        MasterDataCache cache)
    {
        _leagues = leagues;
        _auth = auth;
        _league = league;
        _navigator = navigator;
        _cache = cache;
    }

    partial void OnNameFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasFilter));
        _ = ReloadDebouncedAsync();
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
            var page = await _leagues.GetPageAsync(Items.Count, NameFilter).ConfigureAwait(true);
            TotalCount = page.TotalCount;
            ListPaging.Append(Items, page.Items.Select(v => new LeagueRow
            {
                Id = v.Id,
                Name = v.Name,
                Image = LeagueImages.ToImageSource(v.ImageBytes)
            }), r => r.Id);
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

    [RelayCommand]
    private void ClearFilter() => NameFilter = null;

    [RelayCommand]
    private async Task EditAsync(LeagueRow? row)
    {
        if (row is null || !CanWrite)
            return;
        await Shell.Current.GoToAsync($"{nameof(Pages.LeagueEditPage)}?id={row.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(LeagueRow? row)
    {
        if (row is null || !CanWrite)
            return;

        var ok = await Shell.Current.DisplayAlert(
            "Delete league",
            $"Delete \"{row.Name}\"? The league will be hidden. Match data is kept.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await _leagues.DeleteAsync(row.Id).ConfigureAwait(true);
            if (_league.CurrentLeagueId == row.Id)
            {
                _league.Clear();
                _cache.Invalidate();
                await _navigator.ShowLeaguePickerAsync().ConfigureAwait(true);
                return;
            }

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await Shell.Current.DisplayAlert("Delete failed", ex.Message, "OK").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
