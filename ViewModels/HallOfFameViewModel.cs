using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class HallOfFameViewModel : ObservableObject
{
    private readonly IApiClient _api;
    private int _loadBusy;

    public ObservableCollection<TrophyRow> Items { get; } = new();

    public FormatFilterViewModel FormatFilter { get; }
    public EventScopeFilterViewModel EventScope { get; }

    [ObservableProperty] private int selectedCategory;
    [ObservableProperty] private string categoryTitle = "Player of the Match";
    [ObservableProperty] private string categoryHint = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private string? errorMessage;

    public bool HasActiveFilters => FormatFilter.HasSelection || EventScope.HasSelection;
    public bool IsPotm => SelectedCategory == 0;
    public bool IsCaptain => SelectedCategory == 1;
    public bool IsTourChamp => SelectedCategory == 2;
    public bool IsSeriesChamp => SelectedCategory == 3;
    public bool IsPoTour => SelectedCategory == 4;
    public bool IsPoSeries => SelectedCategory == 5;
    public bool IsBestBat => SelectedCategory == 6;
    public bool IsBestBowl => SelectedCategory == 7;
    public bool IsBestField => SelectedCategory == 8;
    public string CountLabel => TotalCount <= 0 ? "No trophies" : $"{Items.Count} of {TotalCount}";
    private string CategoryKey => SelectedCategory switch
    {
        1 => "captain",
        2 => "tourchamp",
        3 => "serieschamp",
        4 => "potour",
        5 => "poseries",
        6 => "bestbat",
        7 => "bestbowl",
        8 => "bestfield",
        _ => "potm"
    };

    public HallOfFameViewModel(IApiClient api, IFormatService formats, IStatsService stats, ILeagueContext league)
    {
        _api = api;
        FormatFilter = new FormatFilterViewModel(formats);
        EventScope = new EventScopeFilterViewModel(stats, league);
        FormatFilter.Committed += (_, _) =>
        {
            OnPropertyChanged(nameof(HasActiveFilters));
            _ = ReloadAsync();
        };
        FormatFilter.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FormatFilterViewModel.HasSelection) or null)
                OnPropertyChanged(nameof(HasActiveFilters));
        };
        EventScope.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(HasActiveFilters));
            _ = ReloadAsync();
        };
        EventScope.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EventScopeFilterViewModel.HasSelection) or null)
                OnPropertyChanged(nameof(HasActiveFilters));
        };
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));

    partial void OnSelectedCategoryChanged(int value)
    {
        OnPropertyChanged(nameof(IsPotm));
        OnPropertyChanged(nameof(IsCaptain));
        OnPropertyChanged(nameof(IsTourChamp));
        OnPropertyChanged(nameof(IsSeriesChamp));
        OnPropertyChanged(nameof(IsPoTour));
        OnPropertyChanged(nameof(IsPoSeries));
        OnPropertyChanged(nameof(IsBestBat));
        OnPropertyChanged(nameof(IsBestBowl));
        OnPropertyChanged(nameof(IsBestField));
        ApplyCategoryTitles();
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void SelectCategory(string? key)
    {
        SelectedCategory = key?.ToLowerInvariant() switch
        {
            "captain" => 1,
            "tourchamp" => 2,
            "serieschamp" => 3,
            "potour" => 4,
            "poseries" => 5,
            "bestbat" => 6,
            "bestbowl" => 7,
            "bestfield" => 8,
            _ => 0
        };
    }

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
            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);
            await FormatFilter.EnsureLoadedAsync().ConfigureAwait(true);
            await EventScope.EnsureLoadedAsync().ConfigureAwait(true);
            var page = await _api.GetHallOfFamePageAsync(
                    Items.Count,
                    CategoryKey,
                    q: null,
                    FormatFilter.SelectedIds,
                    EventScope.TournamentIds,
                    EventScope.SeriesIds,
                    EventScope.MatchIds)
                .ConfigureAwait(true);
            TotalCount = page.TotalCount;
            ListPaging.Append(Items, page.Items, r => r.PlayerId);
            HasMore = page.HasMore;
            OnPropertyChanged(nameof(CountLabel));
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — Hall of Fame needs internet."
                : ex.Message;
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
            if (Items.Count == 0)
            {
                Items.Clear();
                TotalCount = 0;
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
    private async Task ClearFiltersAsync()
    {
        FormatFilter.ClearSelection();
        EventScope.Clear();
        OnPropertyChanged(nameof(HasActiveFilters));
        await ReloadAsync().ConfigureAwait(true);
    }

    private void ApplyCategoryTitles()
    {
        CategoryTitle = SelectedCategory switch
        {
            1 => "Captain Win Trophies",
            2 => "Tournament Champions",
            3 => "Series Champions",
            4 => "Player of the Tournament",
            5 => "Player of the Series",
            6 => "Best Batsman",
            7 => "Best Bowler",
            8 => "Best Fielder",
            _ => "Player of the Match"
        };
        CategoryHint = string.Empty;
    }
}
