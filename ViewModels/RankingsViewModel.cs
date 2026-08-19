using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class RankingsViewModel : ObservableObject
{
    private readonly IStatsService _stats;
    private readonly StatsViewModel _statsVm;
    private RankingsBundle _bundle = new();
    private List<RankingRow> _categoryRows = new();

    public ObservableCollection<RankingRow> VisibleRows { get; } = new();

    [ObservableProperty] private int selectedCategory;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string categoryTitle = "Batting Ranking";
    [ObservableProperty] private string categoryHint = "ICC-style pts (0–1000) from Avg, SR, Runs";
    [ObservableProperty] private string nameFilter = string.Empty;
    [ObservableProperty] private bool filterByDate;
    [ObservableProperty] private DateTime fromDate = new(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private string dateRangeText = "All completed matches";

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(NameFilter) || FilterByDate;
    public bool IsBatting => SelectedCategory == 0;
    public bool IsBowling => SelectedCategory == 1;
    public bool IsAllRounder => SelectedCategory == 2;
    public bool IsFielder => SelectedCategory == 3;
    public bool IsCaptain => SelectedCategory == 4;

    public RankingsViewModel(IStatsService stats, StatsViewModel statsVm)
    {
        _stats = stats;
        _statsVm = statsVm;
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
        ApplyCategory();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
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

            _bundle = await _stats.GetRankingsAsync(from, toExclusive).ConfigureAwait(true);
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
    private async Task OpenPlayerAsync(RankingRow? row)
    {
        if (row is null || row.PlayerId == Guid.Empty)
            return;

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
        IReadOnlyList<RankingRow> rows;
        switch (SelectedCategory)
        {
            case 1:
                CategoryTitle = "Bowling Ranking";
                CategoryHint = "ICC-style pts (0–1000) from Wickets, Avg, Econ, Dots";
                rows = _bundle.Bowling;
                break;
            case 2:
                CategoryTitle = "All-Rounder Ranking";
                CategoryHint = "Pts (0–1000) from batting + bowling: √(Bat×Bowl) + balance (must have batted & bowled)";
                rows = _bundle.AllRounder;
                break;
            case 3:
                CategoryTitle = "Fielder Ranking";
                CategoryHint = "ICC-style pts (0–1000): Ct×8, RO×8, St×12 (no ICC fielder table)";
                rows = _bundle.Fielder;
                break;
            case 4:
                CategoryTitle = "Captain Ranking";
                CategoryHint = "Pts (0–1000) from Win% (tie=½), wins, and games as captain";
                rows = _bundle.Captain;
                break;
            default:
                CategoryTitle = "Batting Ranking";
                CategoryHint = "ICC-style pts (0–1000) from Avg, SR, Runs (+ not-out bonus)";
                rows = _bundle.Batting;
                break;
        }

        _categoryRows = rows.ToList();
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
