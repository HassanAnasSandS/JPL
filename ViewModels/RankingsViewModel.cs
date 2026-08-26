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
    private RankingsBundle _bundle = new();
    private List<RankingRow> _categoryRows = new();

    public ObservableCollection<RankingRow> VisibleRows { get; } = new();

    [ObservableProperty] private int selectedCategory;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string categoryTitle = "Batting Ranking";
    [ObservableProperty] private string categoryHint = "Runs, boundaries, SR, avg  ·  dots/ducks/dismissals (−)";
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
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(NameFilter) || FilterByDate || FormatFilter.HasSelection;
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

    public RankingsViewModel(IStatsService stats, StatsViewModel statsVm, IFormatService formats)
    {
        _stats = stats;
        _statsVm = statsVm;
        FormatFilter = new FormatFilterViewModel(formats);
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
    }

    partial void OnErrorMessageChanged(string? value)
        => OnPropertyChanged(nameof(HasError));

    partial void OnNameFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyNameFilter();
    }

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
        ApplyCategory();
    }

    partial void OnBreakdownChanged(RankingBreakdown? value)
    {
        OnPropertyChanged(nameof(HasPositiveFactors));
        OnPropertyChanged(nameof(HasNegativeFactors));
        OnPropertyChanged(nameof(HasBreakdownNote));
        OnPropertyChanged(nameof(CanOpenCareer));
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await FormatFilter.EnsureLoadedAsync().ConfigureAwait(true);
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

            _bundle = await _stats.GetRankingsAsync(from, toExclusive, FormatFilter.SelectedIds).ConfigureAwait(true);
            ApplyCategory();
        }
        catch (Exception ex)
        {
            VisibleRows.Clear();
            _categoryRows.Clear();
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — rankings need internet."
                : $"Rankings require API (server DB). {ex.Message}";
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
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
    private void OpenBreakdown(RankingRow? row)
    {
        if (row is null)
            return;

        _openedRow = row;
        BreakdownName = row.PlayerName;
        BreakdownRank = row.RankText;
        BreakdownCareerLabel = IsTeam ? "View team" : "Full career";
        Breakdown = RankingExplain.ForRow(SelectedCategory, row);
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

    private void ApplyCategory()
    {
        SearchPlaceholder = IsTeam ? "Filter by team name" : "Filter by player name";
        IReadOnlyList<RankingRow> rows;
        switch (SelectedCategory)
        {
            case 1:
                CategoryTitle = "Bowling Ranking";
                CategoryHint = "Wkts, maidens, dots, economy vs 8, extras (−)";
                rows = _bundle.Bowling;
                break;
            case 2:
                CategoryTitle = "All-Rounder Ranking";
                CategoryHint = "Batting pts + bowling pts (must have batted & bowled)";
                rows = _bundle.AllRounder;
                break;
            case 3:
                CategoryTitle = "Fielder Ranking";
                CategoryHint = "Catch×10, RO×12, St×15  ·  low rate after 4 matches (−)";
                rows = _bundle.Fielder;
                break;
            case 4:
                CategoryTitle = "Captain Ranking";
                CategoryHint = "Win×4 + tie×1 − loss×2";
                rows = _bundle.Captain;
                break;
            case 5:
                CategoryTitle = "Team Ranking";
                CategoryHint = "Win×2 + tie×1 − loss×1 + NRR×10";
                rows = _bundle.Team;
                break;
            default:
                CategoryTitle = "Batting Ranking";
                CategoryHint = "Runs, boundaries, SR, avg  ·  dots/ducks/dismissals (−)";
                rows = _bundle.Batting;
                break;
        }

        _categoryRows = rows.ToList();
        RankingBars.Apply(_categoryRows);
        ApplyNameFilter();
    }

    private void ApplyNameFilter()
    {
        IEnumerable<RankingRow> q = _categoryRows;
        var name = NameFilter?.Trim();
        if (!string.IsNullOrWhiteSpace(name))
            q = q.Where(r => r.PlayerName.Contains(name, StringComparison.OrdinalIgnoreCase));

        VisibleRows.Clear();
        foreach (var row in q)
            VisibleRows.Add(row);
    }
}
