using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class RankingsViewModel : ObservableObject
{
    private readonly IStatsService _stats;
    private readonly StatsViewModel _statsVm;
    public FormatFilterViewModel FormatFilter { get; }
    public EventScopeFilterViewModel EventScope { get; }
    private int _loadBusy;
    private int _filterGen;

    public ObservableCollection<RankingRow> VisibleRows { get; } = new();

    [ObservableProperty] private int selectedCategory;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string categoryTitle = "Batting Ranking";
    [ObservableProperty] private string categoryHint = string.Empty;
    [ObservableProperty] private string nameColumnTitle = "Player";
    [ObservableProperty] private string searchPlaceholder = "Filter by player name";
    [ObservableProperty] private string nameFilter = string.Empty;
    [ObservableProperty] private bool filterByDate;
    [ObservableProperty] private DateTime fromDate = new(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private string dateRangeText = "All completed matches";
    [ObservableProperty] private bool isBreakdownVisible;
    [ObservableProperty] private string breakdownName = string.Empty;
    [ObservableProperty] private string breakdownRank = string.Empty;
    [ObservableProperty] private string breakdownCareerLabel = "Full career";
    [ObservableProperty] private RankingBreakdown? breakdown;
    private RankingRow? _openedRow;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(NameFilter) || FilterByDate || FormatFilter.HasSelection || EventScope.HasSelection;
    public bool IsBatting => SelectedCategory == 0;
    public bool IsBowling => SelectedCategory == 1;
    public bool IsAllRounder => SelectedCategory == 2;
    public bool IsFielder => SelectedCategory == 3;
    public bool IsCaptain => SelectedCategory == 4;
    public bool IsTeam => SelectedCategory == 5;
    public bool HasPositiveFactors => Breakdown?.HasPositive == true;
    public bool HasNegativeFactors => Breakdown?.HasNegative == true;
    public bool HasBreakdownNote => Breakdown?.HasNote == true;
    public bool CanOpenCareer => _openedRow is { PlayerId: var id } && id != Guid.Empty;
    public string CountLabel => TotalCount <= 0 ? "No rankings" : $"{VisibleRows.Count} of {TotalCount}";
    private string CategoryKey => SelectedCategory switch
    {
        1 => "bowling",
        2 => "allrounder",
        3 => "fielder",
        4 => "captain",
        5 => "team",
        _ => "batting"
    };

    public RankingsViewModel(IStatsService stats, StatsViewModel statsVm, IFormatService formats, ILeagueContext league)
    {
        _stats = stats;
        _statsVm = statsVm;
        FormatFilter = new FormatFilterViewModel(formats);
        EventScope = new EventScopeFilterViewModel(stats, league);
        FormatFilter.Committed += (_, _) =>
        {
            OnPropertyChanged(nameof(HasActiveFilters));
            _ = LoadAsync();
        };
        FormatFilter.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FormatFilterViewModel.HasSelection) or null)
                OnPropertyChanged(nameof(HasActiveFilters));
        };
        EventScope.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(HasActiveFilters));
            _ = LoadAsync();
        };
        EventScope.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EventScopeFilterViewModel.HasSelection) or null)
                OnPropertyChanged(nameof(HasActiveFilters));
        };
    }

    partial void OnErrorMessageChanged(string? value)
        => OnPropertyChanged(nameof(HasError));

    partial void OnNameFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        _ = ReloadDebouncedAsync();
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));

    partial void OnFilterByDateChanged(bool value)
        => OnPropertyChanged(nameof(HasActiveFilters));

    partial void OnSelectedCategoryChanged(int value)
    {
        OnPropertyChanged(nameof(IsBatting));
        OnPropertyChanged(nameof(IsBowling));
        OnPropertyChanged(nameof(IsAllRounder));
        OnPropertyChanged(nameof(IsFielder));
        OnPropertyChanged(nameof(IsCaptain));
        OnPropertyChanged(nameof(IsTeam));
        ApplyCategoryTitles();
        _ = ReloadAsync();
    }

    partial void OnBreakdownChanged(RankingBreakdown? value)
    {
        OnPropertyChanged(nameof(HasPositiveFactors));
        OnPropertyChanged(nameof(HasNegativeFactors));
        OnPropertyChanged(nameof(HasBreakdownNote));
        OnPropertyChanged(nameof(CanOpenCareer));
    }

    [RelayCommand]
    public Task LoadAsync() => ReloadAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        HasMore = true;
        VisibleRows.Clear();
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
            if (VisibleRows.Count == 0)
                IsBusy = true;
            else
                IsLoadingMore = true;
            ErrorMessage = null;
            await FormatFilter.EnsureLoadedAsync().ConfigureAwait(true);
            await EventScope.EnsureLoadedAsync().ConfigureAwait(true);
            DateTime? from = null;
            DateTime? toExclusive = null;
            if (FilterByDate)
            {
                if (FromDate.Date > ToDate.Date)
                    throw new ArgumentException("From date cannot be after To date.");

                from = FromDate.Date;
                toExclusive = ToDate.Date.AddDays(1);
                DateRangeText = $"{FromDate:dd MMM yyyy} — {ToDate:dd MMM yyyy}";
            }
            else
            {
                DateRangeText = "All completed matches";
            }

            var page = await _stats.GetRankingsPageAsync(
                    VisibleRows.Count,
                    CategoryKey,
                    NameFilter,
                    from,
                    toExclusive,
                    FormatFilter.SelectedIds,
                    EventScope.TournamentIds,
                    EventScope.SeriesIds,
                    EventScope.MatchIds)
                .ConfigureAwait(true);
            TotalCount = page.TotalCount;
            ListPaging.Append(VisibleRows, page.Items, r => r.PlayerId);
            RankingBars.Apply(VisibleRows);
            HasMore = page.HasMore;
            OnPropertyChanged(nameof(CountLabel));
        }
        catch (Exception ex)
        {
            if (VisibleRows.Count == 0)
            {
                VisibleRows.Clear();
                TotalCount = 0;
            }
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — rankings need internet."
                : $"Rankings require API (server DB). {ex.Message}";
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            IsLoadingMore = false;
            Interlocked.Exchange(ref _loadBusy, 0);
        }
    }

    [RelayCommand]
    private void SelectCategory(string? category)
    {
        SelectedCategory = category switch
        {
            "bowling" => 1,
            "allrounder" => 2,
            "fielder" => 3,
            "captain" => 4,
            "team" => 5,
            _ => 0
        };
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        NameFilter = string.Empty;
        FilterByDate = false;
        FromDate = new DateTime(DateTime.Today.Year, 1, 1);
        ToDate = DateTime.Today;
        FormatFilter.ClearSelection();
        EventScope.Clear();
        OnPropertyChanged(nameof(HasActiveFilters));
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ApplyDateRangeAsync()
    {
        FilterByDate = true;
        OnPropertyChanged(nameof(HasActiveFilters));
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenBreakdownAsync(RankingRow? row)
    {
        if (row is null)
            return;

        _openedRow = row;
        BreakdownName = row.PlayerName;
        BreakdownRank = row.RankText;
        BreakdownCareerLabel = IsTeam ? "View team" : "Full career";
        Breakdown = RankingExplain.ForRow(SelectedCategory, row);
        if (!IsTeam && row.PlayerId != Guid.Empty && row.Breakdown is null)
        {
            try
            {
                var career = await _stats.GetCareerAsync(row.PlayerId).ConfigureAwait(true);
                if (career is not null)
                {
                    Breakdown = SelectedCategory switch
                    {
                        1 => RankingExplain.Bowling(career),
                        2 => RankingExplain.AllRounder(career),
                        3 => RankingExplain.Fielding(career),
                        4 => RankingExplain.Captaincy(career),
                        _ => RankingExplain.Batting(career)
                    };
                }
            }
            catch
            {
                /* keep the compact row breakdown */
            }
        }
        IsBreakdownVisible = true;
        OnPropertyChanged(nameof(CanOpenCareer));
    }

    [RelayCommand]
    private void CloseBreakdown()
    {
        IsBreakdownVisible = false;
    }

    [RelayCommand]
    private async Task OpenCareerAsync()
    {
        var row = _openedRow;
        CloseBreakdown();
        await OpenPlayerAsync(row).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenPlayerAsync(RankingRow? row)
    {
        if (row is null || row.PlayerId == Guid.Empty)
            return;

        if (IsTeam)
        {
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync($"TeamEditPage?id={row.PlayerId}&mode=view")
                    .ConfigureAwait(true);
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await _statsVm.OpenPlayerByIdAsync(row.PlayerId, returnRoute: "//rankings").ConfigureAwait(true);
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync("//stats").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — player stats need internet."
                : ex.Message;
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyCategoryTitles()
    {
        SearchPlaceholder = IsTeam ? "Filter by team name" : "Filter by player name";
        NameColumnTitle = IsTeam ? "Team" : "Player";
        switch (SelectedCategory)
        {
            case 1:
                CategoryTitle = "Bowling Ranking";
                break;
            case 2:
                CategoryTitle = "All-Rounder Ranking";
                break;
            case 3:
                CategoryTitle = "Fielder Ranking";
                break;
            case 4:
                CategoryTitle = "Captain Ranking";
                break;
            case 5:
                CategoryTitle = "Team Ranking";
                break;
            default:
                CategoryTitle = "Batting Ranking";
                break;
        }
        CategoryHint = string.Empty;
    }
}
