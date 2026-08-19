using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class AuctionCaptainEditor : ObservableObject
{
    public Guid PlayerId { get; init; }
    public string PlayerName { get; init; } = string.Empty;

    [ObservableProperty] private int walletAmount;
    [ObservableProperty] private int spentAmount;

    public int Remaining => WalletAmount - SpentAmount;
    public bool IsOverBudget => Remaining < 0;
    public string BudgetLine => $"Spent {SpentAmount:N0} · Left {Remaining:N0}";
    public string WalletSummary => $"Wallet {WalletAmount:N0} · Spent {SpentAmount:N0} · Left {Remaining:N0}";
    public string PickerLabel => $"{PlayerName}  ({Remaining:N0} left)";

    partial void OnWalletAmountChanged(int value) => NotifyBudget();
    partial void OnSpentAmountChanged(int value) => NotifyBudget();

    private void NotifyBudget()
    {
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(IsOverBudget));
        OnPropertyChanged(nameof(BudgetLine));
        OnPropertyChanged(nameof(WalletSummary));
        OnPropertyChanged(nameof(PickerLabel));
    }
}

public partial class AuctionLotEditor : ObservableObject
{
    public Guid PlayerId { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public Action? Changed { get; set; }

    [ObservableProperty] private int basePrice;
    [ObservableProperty] private bool isSold;
    [ObservableProperty] private int soldPrice;
    [ObservableProperty] private AuctionCaptainEditor? soldToCaptain;
    [ObservableProperty] private ObservableCollection<AuctionCaptainEditor> captainChoices = new();

    public bool ShowSoldFields => IsSold;
    public string BasePriceLine => $"Base {BasePrice:N0}";
    public string SoldPriceLine => $"Sold {SoldPrice:N0}";
    public string SoldLine => IsSold
        ? $"Sold {SoldPrice:N0} → {SoldToCaptain?.PlayerName ?? "?"}"
        : "Unsold";

    partial void OnIsSoldChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSoldFields));
        OnPropertyChanged(nameof(SoldLine));
        OnPropertyChanged(nameof(SoldPriceLine));
        Changed?.Invoke();
    }

    partial void OnBasePriceChanged(int value) => OnPropertyChanged(nameof(BasePriceLine));
    partial void OnSoldPriceChanged(int value)
    {
        OnPropertyChanged(nameof(SoldLine));
        OnPropertyChanged(nameof(SoldPriceLine));
        Changed?.Invoke();
    }

    partial void OnSoldToCaptainChanged(AuctionCaptainEditor? value)
    {
        OnPropertyChanged(nameof(SoldLine));
        Changed?.Invoke();
    }
}

/// <summary>View-mode grouping: sold players under one captain.</summary>
public sealed class AuctionCaptainPurchaseGroup
{
    public required string CaptainName { get; init; }
    public required string HeaderLine { get; init; }
    public required int SpentTotal { get; init; }
    public ObservableCollection<AuctionLotEditor> Players { get; init; } = new();
    public bool HasPlayers => Players.Count > 0;
}

[QueryProperty(nameof(AuctionId), "id")]
[QueryProperty(nameof(Mode), "mode")]
public partial class AuctionEditViewModel : ObservableObject
{
    private readonly IAuctionService _auctions;
    private readonly IPlayerService _players;
    private readonly IAuthService _auth;
    private readonly IAuctionsModuleGate _gate;
    private Guid? _existingId;

    public string? AuctionId { get; set; }
    public string? Mode { get; set; }

    public ObservableCollection<SelectablePlayer> CaptainCandidates { get; } = new();
    public ObservableCollection<SelectablePlayer> PlayerCandidates { get; } = new();
    public ObservableCollection<AuctionCaptainEditor> SelectedCaptains { get; } = new();
    public ObservableCollection<AuctionLotEditor> Lots { get; } = new();
    public ObservableCollection<AuctionLotEditor> SoldLots { get; } = new();
    public ObservableCollection<AuctionLotEditor> UnsoldLots { get; } = new();
    public ObservableCollection<AuctionCaptainPurchaseGroup> SoldByCaptain { get; } = new();

    [ObservableProperty] private ObservableCollection<AuctionCaptainEditor> captainOptions = new();

    public bool HasLots => Lots.Count > 0;
    public bool HasSelectedCaptains => SelectedCaptains.Count > 0;
    public bool HasNoSelectedCaptains => !HasSelectedCaptains;
    public bool HasSoldLots => SoldLots.Count > 0;
    public bool HasNoSoldLots => !HasSoldLots;
    public bool HasUnsoldLots => UnsoldLots.Count > 0;
    public bool HasNoUnsoldLots => !HasUnsoldLots;
    public bool CanRefresh => IsViewMode && !IsBusy;
    public int SoldCount => SoldLots.Count;
    public int UnsoldCount => UnsoldLots.Count;
    public int TotalSoldAmount => SoldLots.Sum(l => Math.Max(0, l.SoldPrice));
    public string ViewSummaryText =>
        $"{SelectedCaptains.Count} captains · {Lots.Count} players · Sold {SoldCount} · Unsold {UnsoldCount} · Total {TotalSoldAmount:N0}";

    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private DateTime auctionDate = DateTime.Today;
    [ObservableProperty] private string pageTitle = "New auction";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private bool isViewMode;
    [ObservableProperty] private bool canEdit;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));
    partial void OnIsViewModeChanged(bool value) => OnPropertyChanged(nameof(CanRefresh));

    public AuctionEditViewModel(
        IAuctionService auctions,
        IPlayerService players,
        IAuthService auth,
        IAuctionsModuleGate gate)
    {
        _auctions = auctions;
        _players = players;
        _auth = auth;
        _gate = gate;
        CanWrite = _auth.CanWrite;
        CanEdit = CanWrite;
    }

    public async Task InitializeAsync()
    {
        CanWrite = _auth.CanWrite;
        IsViewMode = string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase);
        CanEdit = CanWrite && !IsViewMode && _gate.IsUnlocked;

        if (!IsViewMode && !_gate.IsUnlocked)
        {
            ErrorMessage = "Password required. Use Add / Edit from the list.";
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        if (!CanWrite && !IsViewMode)
        {
            ErrorMessage = "Read-only user cannot edit auctions.";
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        await LoadAuctionAsync(refreshFromApi: IsViewMode).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsViewMode || IsBusy) return;
        await LoadAuctionAsync(refreshFromApi: true).ConfigureAwait(true);
    }

    private async Task LoadAuctionAsync(bool refreshFromApi)
    {
        _ = refreshFromApi;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);

            var allPlayers = await _players.GetActiveAsync().ConfigureAwait(true);

            CaptainCandidates.Clear();
            PlayerCandidates.Clear();
            SelectedCaptains.Clear();
            Lots.Clear();
            foreach (var p in allPlayers)
            {
                CaptainCandidates.Add(new SelectablePlayer { Id = p.Id, FullName = p.FullName });
                PlayerCandidates.Add(new SelectablePlayer { Id = p.Id, FullName = p.FullName });
            }

            NotifyLotsChanged();
            NotifyCaptainsChanged();
            RebuildCaptainOptions();

            if (Guid.TryParse(AuctionId, out var id))
            {
                _existingId = id;
                PageTitle = IsViewMode ? "View auction" : "Edit auction";
                var detail = await _auctions.GetAsync(id).ConfigureAwait(true);
                if (detail is null)
                {
                    ErrorMessage = "Auction not found.";
                    return;
                }

                Title = detail.Title;
                AuctionDate = detail.AuctionDateUtc.Date;

                var captainMap = detail.Captains.ToDictionary(c => c.PlayerId);
                foreach (var c in CaptainCandidates)
                    c.IsSelected = captainMap.ContainsKey(c.Id);

                foreach (var cap in detail.Captains.OrderBy(c => c.PlayerName))
                {
                    SelectedCaptains.Add(new AuctionCaptainEditor
                    {
                        PlayerId = cap.PlayerId,
                        PlayerName = cap.PlayerName,
                        WalletAmount = cap.WalletAmount
                    });
                }

                NotifyCaptainsChanged();
                RebuildCaptainOptions();

                var lotIds = detail.Lots.Select(l => l.PlayerId).ToHashSet();
                foreach (var p in PlayerCandidates)
                    p.IsSelected = lotIds.Contains(p.Id);

                foreach (var lot in detail.Lots)
                {
                    var editor = new AuctionLotEditor
                    {
                        PlayerId = lot.PlayerId,
                        PlayerName = lot.PlayerName,
                        BasePrice = lot.BasePrice,
                        IsSold = lot.IsSold,
                        SoldPrice = lot.SoldPrice ?? 0,
                        CaptainChoices = CaptainOptions,
                        SoldToCaptain = lot.SoldToCaptainPlayerId is Guid cid
                            ? CaptainOptions.FirstOrDefault(c => c.PlayerId == cid)
                            : null,
                        Changed = RefreshCaptainBudgets
                    };
                    Lots.Add(editor);
                }

                NotifyLotsChanged();
                RefreshCaptainBudgets();
                RebuildViewLists();
            }
            else
            {
                _existingId = null;
                PageTitle = "New auction";
                Title = string.Empty;
                AuctionDate = DateTime.Today;
                RebuildViewLists();
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
    private void ToggleCaptain(SelectablePlayer? player)
    {
        if (!CanEdit || player is null) return;
        player.IsSelected = !player.IsSelected;

        if (player.IsSelected)
        {
            if (SelectedCaptains.Any(c => c.PlayerId == player.Id)) return;
            SelectedCaptains.Add(new AuctionCaptainEditor
            {
                PlayerId = player.Id,
                PlayerName = player.FullName,
                WalletAmount = 0
            });
        }
        else
        {
            var existing = SelectedCaptains.FirstOrDefault(c => c.PlayerId == player.Id);
            if (existing is not null)
                SelectedCaptains.Remove(existing);
        }

        NotifyCaptainsChanged();
        RebuildCaptainOptions();
        RefreshCaptainBudgets();
        RebuildViewLists();
    }

    [RelayCommand]
    private void TogglePlayer(SelectablePlayer? player)
    {
        if (!CanEdit || player is null) return;
        player.IsSelected = !player.IsSelected;
        if (player.IsSelected)
        {
            if (Lots.Any(l => l.PlayerId == player.Id)) return;
            Lots.Add(new AuctionLotEditor
            {
                PlayerId = player.Id,
                PlayerName = player.FullName,
                BasePrice = 0,
                IsSold = false,
                SoldPrice = 0,
                CaptainChoices = CaptainOptions,
                Changed = RefreshCaptainBudgets
            });
        }
        else
        {
            var existing = Lots.FirstOrDefault(l => l.PlayerId == player.Id);
            if (existing is not null)
                Lots.Remove(existing);
        }

        NotifyLotsChanged();
        RefreshCaptainBudgets();
        RebuildViewLists();
    }

    private void NotifyLotsChanged() => OnPropertyChanged(nameof(HasLots));
    private void NotifyCaptainsChanged()
    {
        OnPropertyChanged(nameof(HasSelectedCaptains));
        OnPropertyChanged(nameof(HasNoSelectedCaptains));
    }

    private void RebuildCaptainOptions()
    {
        var next = new ObservableCollection<AuctionCaptainEditor>(
            SelectedCaptains.OrderBy(c => c.PlayerName));
        CaptainOptions = next;

        foreach (var lot in Lots)
        {
            var keepId = lot.SoldToCaptain?.PlayerId;
            lot.CaptainChoices = next;
            lot.SoldToCaptain = keepId is Guid id
                ? next.FirstOrDefault(c => c.PlayerId == id)
                : null;
            lot.Changed = RefreshCaptainBudgets;
        }
    }

    private void RefreshCaptainBudgets()
    {
        foreach (var cap in SelectedCaptains)
        {
            cap.SpentAmount = Lots
                .Where(l => l.IsSold && l.SoldToCaptain?.PlayerId == cap.PlayerId)
                .Sum(l => Math.Max(0, l.SoldPrice));
        }

        RebuildViewLists();
    }

    private void RebuildViewLists()
    {
        SoldLots.Clear();
        UnsoldLots.Clear();
        SoldByCaptain.Clear();

        foreach (var lot in Lots.OrderBy(l => l.PlayerName))
        {
            if (lot.IsSold)
                SoldLots.Add(lot);
            else
                UnsoldLots.Add(lot);
        }

        // One section per captain (even if empty purchases — only show those with buys).
        foreach (var cap in SelectedCaptains.OrderBy(c => c.PlayerName))
        {
            var bought = SoldLots
                .Where(l => l.SoldToCaptain?.PlayerId == cap.PlayerId)
                .OrderBy(l => l.PlayerName)
                .ToList();
            if (bought.Count == 0)
                continue;

            var spent = bought.Sum(l => Math.Max(0, l.SoldPrice));
            var group = new AuctionCaptainPurchaseGroup
            {
                CaptainName = cap.PlayerName,
                SpentTotal = spent,
                HeaderLine = $"{bought.Count} player{(bought.Count == 1 ? "" : "s")} · Spent {spent:N0}"
            };
            foreach (var p in bought)
                group.Players.Add(p);
            SoldByCaptain.Add(group);
        }

        // Sold lots without a captain assignment (edge case)
        var orphaned = SoldLots
            .Where(l => l.SoldToCaptain is null ||
                        SelectedCaptains.All(c => c.PlayerId != l.SoldToCaptain.PlayerId))
            .OrderBy(l => l.PlayerName)
            .ToList();
        if (orphaned.Count > 0)
        {
            var spent = orphaned.Sum(l => Math.Max(0, l.SoldPrice));
            var group = new AuctionCaptainPurchaseGroup
            {
                CaptainName = "Unassigned",
                SpentTotal = spent,
                HeaderLine = $"{orphaned.Count} player{(orphaned.Count == 1 ? "" : "s")} · Spent {spent:N0}"
            };
            foreach (var p in orphaned)
                group.Players.Add(p);
            SoldByCaptain.Add(group);
        }

        OnPropertyChanged(nameof(HasSoldLots));
        OnPropertyChanged(nameof(HasNoSoldLots));
        OnPropertyChanged(nameof(HasUnsoldLots));
        OnPropertyChanged(nameof(HasNoUnsoldLots));
        OnPropertyChanged(nameof(SoldCount));
        OnPropertyChanged(nameof(UnsoldCount));
        OnPropertyChanged(nameof(TotalSoldAmount));
        OnPropertyChanged(nameof(ViewSummaryText));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanEdit || IsBusy) return;
        if (!_gate.IsUnlocked)
        {
            ErrorMessage = "Password required for saving. Go back and use Edit again.";
            return;
        }

        RefreshCaptainBudgets();
        try
        {
            IsBusy = true;
            ErrorMessage = null;

            if (SelectedCaptains.Count < 2)
                throw new InvalidOperationException("Select at least 2 captains.");
            if (Lots.Count == 0)
                throw new InvalidOperationException("Select at least 1 player for the auction.");

            var draft = new AuctionDraft(
                Title?.Trim() ?? string.Empty,
                AuctionDate.Date,
                Notes: null,
                SelectedCaptains.Select(c => new AuctionCaptainDraft(c.PlayerId, c.WalletAmount)).ToList(),
                Lots.Select(l => new AuctionLotDraft(
                    l.PlayerId,
                    l.BasePrice,
                    l.IsSold,
                    l.IsSold ? l.SoldPrice : null,
                    l.IsSold ? l.SoldToCaptain?.PlayerId : null)).ToList());

            if (_existingId is Guid id)
                await _auctions.UpdateAsync(id, draft).ConfigureAwait(true);
            else
                await _auctions.CreateAsync(draft).ConfigureAwait(true);

            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
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
}
