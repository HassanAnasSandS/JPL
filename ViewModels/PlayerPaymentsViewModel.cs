using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class PlayerPaymentsViewModel : ObservableObject
{
    private readonly IStatsService _stats;
    private int _loadBusy;
    private int _searchGeneration;
    private bool _sortAscending;

    public ObservableCollection<PlayerCareerSummary> Players { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string playerNameFilter = string.Empty;
    [ObservableProperty] private string sortColumn = "PaymentTotal";
    [ObservableProperty] private string sortDirectionText = "Total ↓";

    public string CountText => TotalCount == 0 ? "No players" : $"{Players.Count} of {TotalCount} players";

    public PlayerPaymentsViewModel(IStatsService stats) => _stats = stats;

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(CountText));

    partial void OnPlayerNameFilterChanged(string value)
    {
        OnPropertyChanged(nameof(CountText));
        _ = ReloadDebouncedAsync();
    }

    partial void OnSortColumnChanged(string value) => OnPropertyChanged(nameof(SortDirectionText));

    [RelayCommand]
    public Task LoadAsync() => ReloadAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        Interlocked.Increment(ref _searchGeneration);
        while (Volatile.Read(ref _loadBusy) == 1)
            await Task.Delay(50).ConfigureAwait(true);

        HasMore = true;
        Players.Clear();
        TotalCount = 0;
        OnPropertyChanged(nameof(CountText));
        await LoadMoreAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || Interlocked.Exchange(ref _loadBusy, 1) == 1)
            return;

        var generation = Volatile.Read(ref _searchGeneration);
        try
        {
            if (Players.Count == 0)
                IsBusy = true;
            else
                IsLoadingMore = true;
            ErrorMessage = null;

            var page = await _stats.GetLeaderboardPageAsync(
                    Players.Count,
                    string.IsNullOrWhiteSpace(PlayerNameFilter) ? null : PlayerNameFilter.Trim(),
                    SortColumn,
                    _sortAscending,
                    source: "matches")
                .ConfigureAwait(true);

            if (generation != Volatile.Read(ref _searchGeneration))
                return;

            foreach (var row in page.Items)
                row.IsAltRow = row.Rank % 2 == 0;
            ListPaging.Append(Players, page.Items, player => player.PlayerId);
            TotalCount = page.TotalCount;
            HasMore = page.HasMore;
            OnPropertyChanged(nameof(CountText));
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — player payments need internet."
                : $"Could not load player payments. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            IsLoadingMore = false;
            Interlocked.Exchange(ref _loadBusy, 0);
        }
    }

    [RelayCommand]
    private async Task SortAsync(string column)
    {
        if (string.Equals(SortColumn, column, StringComparison.Ordinal))
            _sortAscending = !_sortAscending;
        else
        {
            SortColumn = column;
            _sortAscending = column == "PlayerName";
        }

        SortDirectionText = $"{column switch { "PlayerName" => "Player", "PaymentTotal" => "Total", "PaymentPaid" => "Paid", _ => "Remaining" }} {(_sortAscending ? "↑" : "↓")}";
        await ReloadAsync().ConfigureAwait(true);
    }

    private async Task ReloadDebouncedAsync()
    {
        var generation = Interlocked.Increment(ref _searchGeneration);
        await Task.Delay(300).ConfigureAwait(true);
        if (generation == Volatile.Read(ref _searchGeneration))
            await ReloadAsync().ConfigureAwait(true);
    }
}
