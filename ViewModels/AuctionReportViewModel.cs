using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class AuctionReportViewModel : ObservableObject
{
    private readonly IAuctionService _auctions;
    private List<AuctionReportRow> _all = new();
    private string _sortColumn = "PlayerName";
    private bool _sortAscending = true;

    public ObservableCollection<AuctionReportRow> Rows { get; } = new();
    public ObservableCollection<AuctionPlayerLotEvent> DetailLots { get; } = new();
    public ObservableCollection<AuctionPlayerCaptainEvent> DetailCaptainEvents { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isSorting;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string summaryText = string.Empty;
    [ObservableProperty] private string sortedByText = "Sorted by Player (asc) — tap a column header to sort";
    [ObservableProperty] private bool showDetail;
    [ObservableProperty] private AuctionReportRow? selected;
    [ObservableProperty] private string detailTitle = "Player auction details";
    [ObservableProperty] private string playerNameFilter = string.Empty;

    public bool CanLoad => !IsBusy && !IsSorting;
    public bool ShowTable => !ShowDetail;
    public bool ShowLoading => IsBusy || IsSorting;
    public string LoadingMessage => IsSorting ? "Sorting…" : "Loading…";
    public bool HasDetailLots => DetailLots.Count > 0;
    public bool HasNoDetailLots => !HasDetailLots;
    public bool HasDetailCaptainEvents => DetailCaptainEvents.Count > 0;
    public bool HasNoDetailCaptainEvents => !HasDetailCaptainEvents;
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(PlayerNameFilter);

    public AuctionReportViewModel(IAuctionService auctions) => _auctions = auctions;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLoad));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(LoadingMessage));
        SortCommand.NotifyCanExecuteChanged();
        LoadCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSortingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLoad));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(LoadingMessage));
        SortCommand.NotifyCanExecuteChanged();
        LoadCommand.NotifyCanExecuteChanged();
    }

    partial void OnShowDetailChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowTable));
        SortCommand.NotifyCanExecuteChanged();
    }

    partial void OnPlayerNameFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplySort();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);

            var list = await _auctions.GetAllTimeReportAsync().ConfigureAwait(true);
            _all = list.ToList();
            ApplySort();

            if (ShowDetail && Selected is not null)
            {
                var refreshed = list.FirstOrDefault(r => r.PlayerId == Selected.PlayerId);
                if (refreshed is not null)
                    Selected = refreshed;
                await LoadPlayerDetailAsync(Selected.PlayerId).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        PlayerNameFilter = string.Empty;
    }

    /// <summary>Tap column header: first click sorts desc for numbers / asc for name; tap again toggles.</summary>
    [RelayCommand(CanExecute = nameof(CanSort))]
    private async Task Sort(string? column)
    {
        if (string.IsNullOrWhiteSpace(column) || !ShowTable)
            return;

        if (string.Equals(_sortColumn, column, StringComparison.Ordinal))
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = column;
            // Name: A→Z first; numeric stats: high→low first.
            _sortAscending = column is "PlayerName";
        }

        IsSorting = true;
        try
        {
            // Let the spinner paint so the tap is visibly acknowledged.
            await Task.Delay(50).ConfigureAwait(true);

            var sortColumn = _sortColumn;
            var ascending = _sortAscending;
            var source = FilteredRows();
            var ordered = await Task.Run(() => BuildSortedRows(source, sortColumn, ascending)).ConfigureAwait(true);

            Rows.Clear();
            foreach (var row in ordered)
                Rows.Add(row);

            UpdateSummary(source);
            var dir = _sortAscending ? "asc ↑" : "desc ↓";
            SortedByText = $"Sorted by {DisplayName(_sortColumn)} ({dir}) — tap a column header to sort";
        }
        finally
        {
            IsSorting = false;
        }
    }

    private bool CanSort(string? column) => !IsSorting && !IsBusy && ShowTable;

    private void ApplySort()
    {
        var filtered = FilteredRows();
        var ordered = BuildSortedRows(filtered, _sortColumn, _sortAscending);
        Rows.Clear();
        foreach (var row in ordered)
            Rows.Add(row);

        UpdateSummary(filtered);
        var dir = _sortAscending ? "asc ↑" : "desc ↓";
        SortedByText = $"Sorted by {DisplayName(_sortColumn)} ({dir}) — tap a column header to sort";
    }

    private List<AuctionReportRow> FilteredRows()
    {
        IEnumerable<AuctionReportRow> q = _all;
        if (!string.IsNullOrWhiteSpace(PlayerNameFilter))
        {
            var f = PlayerNameFilter.Trim();
            q = q.Where(r => r.PlayerName.Contains(f, StringComparison.OrdinalIgnoreCase));
        }
        return q.ToList();
    }

    private void UpdateSummary(IReadOnlyList<AuctionReportRow> rows)
    {
        if (_all.Count == 0)
        {
            SummaryText = "No auction data yet.";
            return;
        }

        var players = rows.Count;
        var wallets = rows.Sum(r => r.TotalWalletAssigned);
        var spent = rows.Sum(r => r.TotalSpentAsCaptain);
        var earned = rows.Sum(r => r.TotalSoldAmount);
        var prefix = HasActiveFilters ? $"{players} of {_all.Count} players" : $"{players} players";
        SummaryText = players == 0
            ? "No players match the search."
            : $"{prefix} · Sold {earned:N0} · Wallets {wallets:N0} · Spent {spent:N0}";
    }

    private static List<AuctionReportRow> BuildSortedRows(
        IReadOnlyList<AuctionReportRow> source,
        string sortColumn,
        bool ascending)
    {
        IOrderedEnumerable<AuctionReportRow> ordered = ascending
            ? source.OrderBy(r => SortKey(r, sortColumn), Comparer<object?>.Create(CompareKeys))
            : source.OrderByDescending(r => SortKey(r, sortColumn), Comparer<object?>.Create(CompareKeys));

        return ordered.ThenBy(r => r.PlayerName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int CompareKeys(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        if (a is IComparable ca && a.GetType() == b.GetType())
            return ca.CompareTo(b);
        return string.CompareOrdinal(a.ToString(), b.ToString());
    }

    private static object SortKey(AuctionReportRow r, string column) => column switch
    {
        "PlayerName" => r.PlayerName,
        "AuctionsParticipated" => r.AuctionsParticipated,
        "TimesSold" => r.TimesSold,
        "TimesUnsold" => r.TimesUnsold,
        "TotalBasePrice" => r.TotalBasePrice,
        "TotalSoldAmount" => r.TotalSoldAmount,
        "AverageSoldPrice" => r.TimesSold == 0 ? double.MinValue : r.AverageSoldPrice,
        "HighestSoldPrice" => r.HighestSoldPrice,
        "TimesAsCaptain" => r.TimesAsCaptain,
        "TotalWalletAssigned" => r.TotalWalletAssigned,
        "TotalSpentAsCaptain" => r.TotalSpentAsCaptain,
        "WalletRemaining" => r.WalletRemaining,
        _ => r.PlayerName
    };

    private static string DisplayName(string column) => column switch
    {
        "PlayerName" => "Player",
        "AuctionsParticipated" => "Auc",
        "TimesSold" => "Sold",
        "TimesUnsold" => "Unsold",
        "TotalBasePrice" => "B.Tot",
        "TotalSoldAmount" => "S.Tot",
        "AverageSoldPrice" => "Avg",
        "HighestSoldPrice" => "High",
        "TimesAsCaptain" => "×Cap",
        "TotalWalletAssigned" => "Wallet",
        "TotalSpentAsCaptain" => "Spent",
        "WalletRemaining" => "Left",
        _ => column
    };

    [RelayCommand]
    private async Task OpenPlayerAsync(AuctionReportRow? row)
    {
        if (row is null) return;
        Selected = row;
        DetailTitle = row.PlayerName;
        ShowDetail = true;
        await LoadPlayerDetailAsync(row.PlayerId).ConfigureAwait(true);
    }

    [RelayCommand]
    private void BackToTable()
    {
        ShowDetail = false;
        Selected = null;
        DetailLots.Clear();
        DetailCaptainEvents.Clear();
        NotifyDetailFlags();
    }

    private async Task LoadPlayerDetailAsync(Guid playerId)
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var detail = await _auctions.GetPlayerDetailAsync(playerId).ConfigureAwait(true);
            DetailLots.Clear();
            DetailCaptainEvents.Clear();
            if (detail is null)
            {
                ErrorMessage = "No auction history for this player.";
                NotifyDetailFlags();
                return;
            }

            Selected = detail.Summary;
            DetailTitle = detail.Summary.PlayerName;
            foreach (var lot in detail.Lots)
                DetailLots.Add(lot);
            foreach (var cap in detail.CaptainEvents)
                DetailCaptainEvents.Add(cap);
            NotifyDetailFlags();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyDetailFlags()
    {
        OnPropertyChanged(nameof(HasDetailLots));
        OnPropertyChanged(nameof(HasNoDetailLots));
        OnPropertyChanged(nameof(HasDetailCaptainEvents));
        OnPropertyChanged(nameof(HasNoDetailCaptainEvents));
    }
}
