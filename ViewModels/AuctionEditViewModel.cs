using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public partial class AuctionCaptainEditor : ObservableObject
{
    public Guid PlayerId { get; init; }
    public string PlayerName { get; init; } = string.Empty;

    [ObservableProperty] private string teamName = string.Empty;
    [ObservableProperty] private int walletAmount;
    [ObservableProperty] private int spentAmount;
    [ObservableProperty] private bool isClaimedByOther;
    [ObservableProperty] private bool isClaimedOnThisDevice;
    [ObservableProperty] private string operatorUsername = string.Empty;
    [ObservableProperty] private int jokersLeft;
    [ObservableProperty] private int remainingSlots;
    [ObservableProperty] private int maxBidThisRound;
    [ObservableProperty] private int boughtCount;
    [ObservableProperty] private bool showOperator;
    [ObservableProperty] private bool hideWallet;
    [ObservableProperty] private bool isDraftTurn;
    [ObservableProperty] private bool showPickOrderButtons;

    public bool ShowWallet => !HideWallet;
    public int Remaining => WalletAmount - SpentAmount;
    public bool IsOverBudget => Remaining < 0;
    public string BudgetLine => $"Spent {SpentAmount:N0} · Left {Remaining:N0}";
    public string WalletSummary => $"Wallet {WalletAmount:N0} · Spent {SpentAmount:N0} · Left {Remaining:N0}";
    public string SquadLine => HideWallet
        ? $"Squad {BoughtCount}/{BoughtCount + RemainingSlots}"
        : $"Squad {BoughtCount}/{BoughtCount + RemainingSlots} · Max bid {MaxBidThisRound:N0}";
    public string MaxBidLine => $"Max this round: {MaxBidThisRound:N0}";
    public ObservableCollection<AuctionGroupSlot> GroupSlots { get; } = new();
    [ObservableProperty] private bool showGroupSlots;
    public string DisplayName => string.IsNullOrWhiteSpace(TeamName)
        ? PlayerName
        : $"{PlayerName}  ·  {TeamName}";
    public string PickerLabel => $"{DisplayName}  ({Remaining:N0} left)";
    public string ClaimHint => IsClaimedOnThisDevice
        ? "This device"
        : IsClaimedByOther
            ? "Taken on another device"
            : "Available";

    partial void OnWalletAmountChanged(int value) => NotifyBudget();
    partial void OnSpentAmountChanged(int value) => NotifyBudget();
    partial void OnTeamNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(PickerLabel));
    }

    private void NotifyBudget()
    {
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(IsOverBudget));
        OnPropertyChanged(nameof(BudgetLine));
        OnPropertyChanged(nameof(WalletSummary));
        OnPropertyChanged(nameof(PickerLabel));
        OnPropertyChanged(nameof(SquadLine));
        OnPropertyChanged(nameof(MaxBidLine));
    }

    partial void OnHideWalletChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWallet));
        NotifyBudget();
    }

    partial void OnRemainingSlotsChanged(int value) => NotifyBudget();
    partial void OnMaxBidThisRoundChanged(int value) => NotifyBudget();
    partial void OnBoughtCountChanged(int value) => NotifyBudget();
}

public partial class AuctionCaptainCandidate : ObservableObject
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private AuctionCaptainEditor? editor;

    public bool ShowEditor => IsSelected && Editor is not null;

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(ShowEditor));
    partial void OnEditorChanged(AuctionCaptainEditor? value) => OnPropertyChanged(nameof(ShowEditor));
}

public partial class AuctionUpdateAdminEditor : ObservableObject
{
    [ObservableProperty] private string username = string.Empty;
}

public partial class AuctionLotEditor : ObservableObject
{
    public Guid PlayerId { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public Action? Changed { get; set; }
    public Action<AuctionLotEditor, string, string>? GroupMoved { get; set; }
    internal long IgnorePickerUntil { get; set; }

    [ObservableProperty] private int basePrice;
    [ObservableProperty] private bool isSold;
    [ObservableProperty] private int soldPrice;
    [ObservableProperty] private AuctionCaptainEditor? soldToCaptain;
    [ObservableProperty] private ObservableCollection<AuctionCaptainEditor> captainChoices = new();
    [ObservableProperty] private bool canEditLot = true;
    [ObservableProperty] private string groupName = string.Empty;
    [ObservableProperty] private bool canPickNow;
    [ObservableProperty] private bool isDraftingLot;
    [ObservableProperty] private ObservableCollection<string> groupChoices = new();

    public bool ShowSoldFields => IsSold;
    public bool IsLockedByOther => !CanEditLot;
    public bool HasGroup => !string.IsNullOrWhiteSpace(GroupName);
    public string GroupLine => HasGroup ? GroupName : "";
    public string BasePriceLine => $"Base {BasePrice:N0}";
    public string SoldPriceLine => $"Sold {SoldPrice:N0}";
    public string SoldLine => IsSold
        ? (IsDraftingLot
            ? $"Picked → {SoldToCaptain?.DisplayName ?? "?"}"
            : $"Sold {SoldPrice:N0} → {SoldToCaptain?.DisplayName ?? "?"}")
        : (IsDraftingLot ? "Available" : "Unsold");
    public string SquadPlayerLine => IsDraftingLot
        ? (HasGroup ? $"{PlayerName}  ·  {GroupName}" : PlayerName)
        : $"{PlayerName}  ·  {SoldPrice:N0}";

    [ObservableProperty] private bool showPickAction;
    [ObservableProperty] private bool showStartAction;

    partial void OnIsSoldChanged(bool value)
    {
        if (value && SoldPrice <= 0 && BasePrice > 0)
            SoldPrice = BasePrice;
        OnPropertyChanged(nameof(ShowSoldFields));
        OnPropertyChanged(nameof(SoldLine));
        OnPropertyChanged(nameof(SoldPriceLine));
        OnPropertyChanged(nameof(SquadPlayerLine));
        Changed?.Invoke();
    }

    partial void OnBasePriceChanged(int value) => OnPropertyChanged(nameof(BasePriceLine));
    partial void OnSoldPriceChanged(int value)
    {
        OnPropertyChanged(nameof(SoldLine));
        OnPropertyChanged(nameof(SoldPriceLine));
        OnPropertyChanged(nameof(SquadPlayerLine));
        Changed?.Invoke();
    }

    partial void OnSoldToCaptainChanged(AuctionCaptainEditor? value)
    {
        OnPropertyChanged(nameof(SoldLine));
        Changed?.Invoke();
    }

    partial void OnCanEditLotChanged(bool value) => OnPropertyChanged(nameof(IsLockedByOther));
    partial void OnGroupChoicesChanged(ObservableCollection<string> value)
        => OnPropertyChanged(nameof(GroupChoiceIndex));
    partial void OnGroupNameChanged(string? oldValue, string newValue)
    {
        OnPropertyChanged(nameof(HasGroup));
        OnPropertyChanged(nameof(GroupLine));
        OnPropertyChanged(nameof(SquadPlayerLine));
        OnPropertyChanged(nameof(GroupChoiceIndex));
        GroupMoved?.Invoke(this, oldValue ?? string.Empty, newValue ?? string.Empty);
        Changed?.Invoke();
    }

    public int GroupChoiceIndex
    {
        get
        {
            if (GroupChoices is null || GroupChoices.Count == 0)
                return -1;
            for (var i = 0; i < GroupChoices.Count; i++)
            {
                if (string.Equals(GroupChoices[i], GroupName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }
        set
        {
            if (Environment.TickCount64 < IgnorePickerUntil)
                return;
            if (value < 0 || GroupChoices is null || value >= GroupChoices.Count)
                return;
            var next = GroupChoices[value];
            if (!string.Equals(GroupName, next, StringComparison.Ordinal))
                GroupName = next;
        }
    }

    internal void NotifyGroupIndex() => OnPropertyChanged(nameof(GroupChoiceIndex));

    partial void OnIsDraftingLotChanged(bool value)
    {
        OnPropertyChanged(nameof(SoldLine));
        OnPropertyChanged(nameof(SquadPlayerLine));
    }
}

/// <summary>One draft group’s selected / max for a captain.</summary>
public sealed class AuctionGroupSlot
{
    public required string Name { get; init; }
    public required string CountLine { get; init; }
    public bool IsFull { get; init; }
}

public enum AuctionGroupKind
{
    Setup,
    Pool,
    Squad
}

/// <summary>Players in one draft group (Platinum / Gold / Silver, etc.).</summary>
public partial class AuctionPoolGroup : ObservableObject
{
    public required string Name { get; init; }
    public AuctionGroupKind Kind { get; set; }
    public int Max { get; set; }
    public int? CaptainTaken { get; set; }
    [ObservableProperty] private string header = string.Empty;
    [ObservableProperty] private string countLine = string.Empty;
    [ObservableProperty] private string hintLine = string.Empty;
    public ObservableCollection<AuctionLotEditor> Players { get; } = new();
    public bool HasPlayers => Players.Count > 0;
    public bool HasNoPlayers => Players.Count == 0;
    public bool ShowCount => !string.IsNullOrWhiteSpace(CountLine);
    public bool ShowHint => !string.IsNullOrWhiteSpace(HintLine);

    public void RefreshCounts()
    {
        Header = Name;
        var n = Players.Count;
        switch (Kind)
        {
            case AuctionGroupKind.Squad:
                CountLine = Max > 0 ? $"{n} / {Max}" : n.ToString();
                HintLine = Max > 0
                    ? (n == 0 ? "None yet · selected / max per captain" : "Selected / max per captain")
                    : string.Empty;
                break;
            case AuctionGroupKind.Pool:
                CountLine = Max > 0
                    ? (CaptainTaken is int taken ? $"{taken} / {Max}" : $"max {Max}")
                    : string.Empty;
                HintLine = Max > 0
                    ? (CaptainTaken is int
                        ? $"{n} left in pool · this captain selected / max"
                        : $"{n} left in pool · max {Max} per captain")
                    : $"{n} left in pool";
                break;
            default:
                CountLine = Max > 0 ? $"max {Max} each" : string.Empty;
                HintLine = n == 0
                    ? "No players in this group yet."
                    : $"{n} player{(n == 1 ? "" : "s")} in this group";
                break;
        }
        OnPropertyChanged(nameof(HasPlayers));
        OnPropertyChanged(nameof(HasNoPlayers));
        OnPropertyChanged(nameof(ShowCount));
        OnPropertyChanged(nameof(ShowHint));
    }
}

/// <summary>View-mode grouping: sold players under one captain.</summary>
public sealed class AuctionCaptainPurchaseGroup
{
    public required string CaptainName { get; init; }
    public required string HeaderLine { get; init; }
    public string WalletLine { get; init; } = string.Empty;
    public bool ShowWalletLine => !string.IsNullOrWhiteSpace(WalletLine);
    public required int SpentTotal { get; init; }
    public ObservableCollection<AuctionLotEditor> Players { get; init; } = new();
    public ObservableCollection<AuctionPoolGroup> GroupedPlayers { get; init; } = new();
    public bool ShowGroupedPlayers { get; init; }
    public bool ShowFlatPlayers => !ShowGroupedPlayers;
    public bool HasPlayers => Players.Count > 0;
}

public partial class AuctionDraftGroupEditor : ObservableObject
{
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private int maxPerCaptain;
}

public partial class AuctionBlindRoundEditor : ObservableObject
{
    [ObservableProperty] private int roundNumber;
    public Action? Changed { get; set; }

    partial void OnRoundNumberChanged(int value) => Changed?.Invoke();
}

[QueryProperty(nameof(AuctionId), "id")]
[QueryProperty(nameof(Mode), "mode")]
public partial class AuctionEditViewModel : ObservableObject
{
    private readonly IAuctionService _auctions;
    private readonly IPlayerService _players;
    private readonly IAuthService _auth;
    private Guid? _existingId;
    private CancellationTokenSource? _watchCts;
    private AuctionAccess _access = new(false, false, false, false, false, null);
    private string? _liveStamp;
    private string? _soldViewStamp;
    private readonly List<int> _blindRoundNumbers = new();

    public string? AuctionId { get; set; }
    public string? Mode { get; set; }

    public IReadOnlyList<string> KindOptions { get; } = AuctionKindText.CreateLabels;

    public ObservableCollection<AuctionCaptainCandidate> CaptainCandidates { get; } = new();
    public ObservableCollection<SelectablePlayer> PlayerCandidates { get; } = new();
    public ObservableCollection<AuctionCaptainCandidate> VisibleCaptainCandidates { get; } = new();
    public ObservableCollection<SelectablePlayer> VisiblePlayerCandidates { get; } = new();
    public ObservableCollection<AuctionCaptainEditor> SelectedCaptains { get; } = new();
    public ObservableCollection<AuctionUpdateAdminEditor> UpdateAdmins { get; } = new();
    public ObservableCollection<AuctionLotEditor> Lots { get; } = new();
    public ObservableCollection<AuctionLotEditor> SoldLots { get; } = new();
    public ObservableCollection<AuctionLotEditor> UnsoldLots { get; } = new();
    public ObservableCollection<AuctionPoolGroup> GroupedUnsoldLots { get; } = new();
    public ObservableCollection<AuctionPoolGroup> GroupedSetupLots { get; } = new();
    public ObservableCollection<AuctionCaptainPurchaseGroup> SoldByCaptain { get; } = new();
    public ObservableCollection<AuctionCaptainEditor> ClaimChoices { get; } = new();
    public ObservableCollection<AuctionDraftGroupEditor> DraftGroups { get; } = new();
    public ObservableCollection<AuctionBlindRoundEditor> BlindRoundRows { get; } = new();
    public ObservableCollection<string> GroupNameChoices { get; } = new();

    [ObservableProperty] private ObservableCollection<AuctionCaptainEditor> captainOptions = new();

    public bool HasLots => Lots.Count > 0;
    public bool HasSelectedCaptains => SelectedCaptains.Count > 0;
    public bool HasNoSelectedCaptains => !HasSelectedCaptains;
    public bool HasSoldLots => IsBasic ? Lots.Any(l => l.IsSold) : SoldLots.Count > 0;
    public bool HasNoSoldLots => !HasSoldLots;
    public bool HasUnsoldLots => IsBasic ? Lots.Any(l => !l.IsSold) : UnsoldLots.Count > 0;
    public bool HasNoUnsoldLots => !HasUnsoldLots;
    public bool CanRefresh => !IsBasic && (IsViewMode || IsCaptainOperateMode || IsLiveRun) && !IsBusy;
    public int SoldCount => IsBasic ? Lots.Count(l => l.IsSold) : SoldLots.Count;
    public int UnsoldCount => IsBasic ? Lots.Count(l => !l.IsSold) : UnsoldLots.Count;
    public int TotalSoldAmount => IsBasic
        ? Lots.Where(l => l.IsSold).Sum(l => Math.Max(0, l.SoldPrice))
        : SoldLots.Sum(l => Math.Max(0, l.SoldPrice));
    public string ViewSummaryText =>
        $"{KindLabel} · {LiveStatusWord} · {SelectedCaptains.Count} captains · {Lots.Count} players · Sold {SoldCount} · Unsold {UnsoldCount}";
    public string BannerText => $"{LiveStatusWord} · {KindLabel}";
    public string LiveStatusWord => IsBasic
        ? (IsEventComplete ? "COMPLETE" : "LOCAL")
        : IsEventComplete ? "COMPLETE" : "LIVE";
    public string PlayersEachLabel => IsDrafting
        ? "Players each captain must get *"
        : "Players each captain must buy *";
    public string KindHelpText => !AuctionKindText.TryParse(SelectedKindLabel, out _)
        ? "Pick Basic Auction, Simple Auction, Advance Auction, Simple Drafting, or Advance Drafting."
        : IsBasic
            ? "Runs on this device only. Mark Sold, set price and captain in memory — tap Save Players to store. No live rounds, no cloud until you use another auction type."
            : IsAdvanceDrafting
            ? "No wallet. Set a code per captain. They enter and pick only on their turn. Groups cap how many each captain can take. You can pick for the captain on the clock and change settings."
            : IsDrafting
                ? "No wallet, no bids. Set a code per captain. They enter and pick only on their turn. You can pick for the captain on the clock and change settings."
                : IsAdvance
                    ? "Always live. Up/Down sets who brings a player first. Captains enter with a code to bid. Add which round numbers are Blind — those open as Blind automatically. Creator picks the player; highest bid wins when every captain has bid. Joker cannot be used on a Blind round."
                    : "Always live. Up/Down sets who brings a player first. You run every round; captains do not bid themselves.";
    public string TurnBannerText => IsEventComplete
        ? (IsDrafting ? "Draft complete" : "Auction complete")
        : TurnCaptainId is null
            ? (IsDrafting ? "Draft complete" : "No captain left to bring a player.")
            : IsDrafting
                ? $"Turn: {TurnCaptainName}"
                : CurrentRound is { IsBlind: true, IsRevealed: false }
                    ? $"BLIND BID — {CurrentRound.PlayerName}"
                    : NextRoundIsBlind && (HasNoOpenRound || IsBlindAwarded)
                    ? $"Round {DraftTurnIndex + 1} is BLIND — creator selects the player"
                    : $"{TurnCaptainName} brings the next player";
    public string OperateHint => ClaimedCaptain is null
        ? "Enter the captain code the creator gave you. One captain cannot be used on two devices."
        : IsDrafting
            ? $"You are {ClaimedCaptain.DisplayName}. You can pick only when it is your turn."
            : $"You are {ClaimedCaptain.DisplayName}. Max bid this round: {ClaimedCaptain.MaxBidThisRound:N0}.";
    public string CaptainWaitText => IsDrafting
        ? "Waiting for your turn to pick."
        : NextRoundIsBlind && (HasNoOpenRound || IsBlindAwarded)
            ? "Waiting — this is a scheduled Blind round. The creator selects the player."
            : "Waiting for the next captain to bring a player.";
    public string SetupHint
    {
        get
        {
            var n = Math.Max(1, PlayersPerCaptain);
            var need = SelectedCaptains.Count * n;
            if (SelectedCaptains.Count < 2)
                return "Add at least 2 captains, each with a unique team name.";
            if (Lots.Count < need)
                return $"Need {need} players ({SelectedCaptains.Count} × {n}). Added {Lots.Count}.";
            return $"Pool OK · {Lots.Count} players for {SelectedCaptains.Count} captains × {n} each.";
        }
    }

    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private DateTime auctionDate = DateTime.Today;
    [ObservableProperty] private string pageTitle = "New Auction/Drafting";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private bool isViewMode;
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private bool isSetupEdit;
    [ObservableProperty] private bool isCaptainOperateMode;
    [ObservableProperty] private bool needsCaptainPick;
    [ObservableProperty] private bool isLive = true;
    [ObservableProperty] private string selectedKindLabel = string.Empty;
    [ObservableProperty] private string kindLabel = string.Empty;
    [ObservableProperty] private string? titleError;
    [ObservableProperty] private string? kindError;
    [ObservableProperty] private string? playersPerCaptainError;
    [ObservableProperty] private string modeLabel = "LIVE";
    [ObservableProperty] private bool isLiveWatching;
    [ObservableProperty] private bool isLiveRun;
    [ObservableProperty] private bool isAdvance;
    [ObservableProperty] private bool isDrafting;
    [ObservableProperty] private bool isAdvanceDrafting;
    [ObservableProperty] private bool isBasic;
    [ObservableProperty] private int playersPerCaptain = 11;
    [ObservableProperty] private int jokersPerCaptain;
    [ObservableProperty] private int blindBidRoundsAllowed;
    [ObservableProperty] private int blindRoundsUsed;
    [ObservableProperty] private int draftTurnIndex;
    [ObservableProperty] private bool hasLegacyBlindCount;
    [ObservableProperty] private AuctionLotEditor? currentLot;
    [ObservableProperty] private AuctionCaptainEditor? simpleWinner;
    [ObservableProperty] private int simplePrice;
    [ObservableProperty] private int bidAmount;
    [ObservableProperty] private bool useJoker;
    [ObservableProperty] private bool openAsBlind;
    [ObservableProperty] private string roundStatus = string.Empty;
    [ObservableProperty] private AuctionRoundInfo? currentRound;
    [ObservableProperty] private AuctionCaptainEditor? claimedCaptain;
    [ObservableProperty] private string captainCode = string.Empty;
    [ObservableProperty] private Guid? turnCaptainId;
    [ObservableProperty] private string turnCaptainName = string.Empty;
    [ObservableProperty] private string selectedTab = TabGeneral;
    [ObservableProperty] private int unlockedCount = 1;
    [ObservableProperty] private string captainNameFilter = string.Empty;
    [ObservableProperty] private string playerNameFilter = string.Empty;

    private const string TabGeneral = "general";
    private const string TabCaptains = "captains";
    private const string TabPlayers = "players";

    public bool IsGeneralTab => SelectedTab == TabGeneral;
    public bool IsCaptainsTab => SelectedTab == TabCaptains;
    public bool IsPlayersTab => SelectedTab == TabPlayers;
    public bool CanOpenGeneralTab => true;
    public bool CanOpenCaptainsTab => UnlockedCount >= 2;
    public bool CanOpenPlayersTab => UnlockedCount >= 3;
    public string TabLockHint => UnlockedCount >= 3
        ? string.Empty
        : UnlockedCount < 2
            ? "Save General to open Captains."
            : "Save Captains to open Players.";
    public bool HasTabLockHint => IsSetupEdit && UnlockedCount < 3;
    public bool HasTitleError => !string.IsNullOrWhiteSpace(TitleError);
    public bool HasKindError => !string.IsNullOrWhiteSpace(KindError);
    public bool HasPlayersPerCaptainError => !string.IsNullOrWhiteSpace(PlayersPerCaptainError);
    public bool CanBackToLive => IsSetupEdit
        && !IsBasic
        && _existingId is not null
        && UnlockedCount >= 3
        && SelectedCaptains.Count >= 2
        && Lots.Count > 0;

    public bool ShowLiveBanner => true;
    public bool ShowUpdateAdmins => false;
    public bool ShowAdvanceSettings => IsSetupEdit && IsAdvance;
    public bool ShowOperateHint => IsCaptainOperateMode || NeedsCaptainPick;
    public bool ShowLiveChrome => !IsSetupEdit;
    public bool CanSave => IsSetupEdit && !IsBusy;
    public string SaveButtonText => !IsSetupEdit
        ? "Save"
        : IsGeneralTab
            ? "Save General"
            : IsCaptainsTab
                ? "Save Captains"
                : "Save Players";
    public bool ShowLiveRun => IsLiveRun && !IsSetupEdit && !IsViewMode && !NeedsCaptainPick;
    public bool ShowAuctionRun => ShowLiveRun && !IsDrafting;
    public bool ShowDraftRun => ShowLiveRun && IsDrafting;
    public bool ShowSettingsButton => ShowLiveRun && (SoldCount == 0 || IsEventComplete);
    public string SettingsButtonText => IsEventComplete ? "Edit" : "Settings";
    public bool ShowUndoLastPick => ShowLiveRun && SoldCount > 0;
    public string CreatorRunHint => IsEventComplete
        ? "This Auction/Drafting is complete. You can undo the last pick or edit settings."
        : ShowUndoLastPick
            ? "You can pick for the captain on the clock and undo the last pick. Captains can only pick on their own turn."
            : "You can pick for the captain on the clock, change settings, and update remaining players. Captains can only pick on their own turn.";
    public bool ShowDraftTurnBanner => !IsSetupEdit && !NeedsCaptainPick
        && (IsViewMode || ShowDraftRun || ShowAuctionRun || IsCaptainOperateMode);
    public string LiveCaptainsTitle => IsDrafting ? "Pick order" : "Captains";
    public string LiveAvailableTitle => IsEventComplete ? "Unsold" : "Available players";
    public string LiveTeamsTitle => IsDrafting ? "Teams" : "Sold";
    public string LiveAuctionPoolTitle => IsEventComplete ? "Unsold" : "Open next round";
    public string LivePoolEmptyText => IsEventComplete
        ? "No unsold players."
        : (IsDrafting ? "All players picked." : "All players sold.");
    public bool IsEventComplete =>
        !IsSetupEdit
        && !NeedsCaptainPick
        && SelectedCaptains.Count >= 2
        && Lots.Count > 0
        && TurnCaptainId is null
        && (CurrentRound is null || IsBlindAwarded);
    public bool ShowDraftGroupsSetup => IsSetupEdit && IsAdvanceDrafting;
    public bool ShowLotGroups => IsSetupEdit && IsAdvanceDrafting && HasLots;
    public bool ShowGroupedPool => IsAdvanceDrafting;
    public bool ShowFlatPool => !IsAdvanceDrafting;
    public bool ShowGroupQuotaHint => IsAdvanceDrafting;
    public string LivePoolGroupHint => IsCaptainOperateMode
        ? "Each group shows your selected / max. Pick is off when that group is full."
        : "Each group shows selected / max for the captain on the clock.";
    public bool ShowBasePrices => IsSetupEdit && !IsDrafting && !IsBasic && HasLots;
    public bool ShowManualSaleLots => IsSetupEdit && IsBasic && HasLots;
    public bool ShowBasicSoldByCaptain => IsSetupEdit && IsBasic && SoldByCaptain.Count > 0;
    public string BasicSoldSummaryTitle =>
        $"Sold by captain · {Lots.Count(l => l.IsSold)} sold · {Lots.Count(l => !l.IsSold)} left";
    public bool ShowPickOrder => IsSetupEdit && HasSelectedCaptains && !IsBasic;
    public bool RequiresCaptainCode => IsAdvance || IsDrafting;
    public bool CanCaptainNominate => IsCaptainOperateMode && IsAdvance && HasNoOpenRound
        && !NextRoundIsBlind
        && ClaimedCaptain is { IsDraftTurn: true };
    public bool CanCaptainDraftPick => IsCaptainOperateMode && IsDrafting
        && ClaimedCaptain is { IsDraftTurn: true };
    public bool ShowCaptainWaitForTurn => IsCaptainOperateMode
        && !CanCaptainNominate
        && !CanCaptainDraftPick
        && !IsEventComplete
        && (IsDrafting || HasNoOpenRound);
    public bool HasOpenRound => CurrentRound is not null;
    public bool HasNoOpenRound => !HasOpenRound;
    public bool IsBlindAwarded => CurrentRound?.IsBlind == true && CurrentRound.IsRevealed;
    public bool ShowAuctionPool => HasNoOpenRound || IsBlindAwarded;
    public bool HasScheduledBlindRounds => _blindRoundNumbers.Count > 0;
    public bool NextRoundIsBlind => IsAdvance && _blindRoundNumbers.Contains(DraftTurnIndex + 1);
    public bool ShowLegacyBlindCount => IsAdvance && HasLegacyBlindCount && BlindRoundRows.Count == 0;
    public bool HasNoBlindRoundRows => BlindRoundRows.Count == 0;
    public bool CanDeclareBlind => IsAdvance && ShowAuctionRun && HasNoOpenRound
        && !HasScheduledBlindRounds
        && BlindRoundsUsed < BlindBidRoundsAllowed
        && !IsEventComplete;
    public bool ShowReveal => false;
    public bool ShowSimpleClose => ShowAuctionRun && !IsAdvance && HasOpenRound;
    public bool ShowAdvanceClose => ShowAuctionRun && IsAdvance && HasOpenRound && CurrentRound?.IsBlind != true;
    public bool ShowUnsoldSkip => ShowAuctionRun && HasOpenRound && !IsBlindAwarded;
    public bool ShowJokerUse => IsAdvance && HasOpenRound && CurrentRound?.IsBlind != true;
    public bool ShowBidEntry => IsAdvance && HasOpenRound && !IsBlindAwarded && !AlreadySubmittedBlindBid;
    public bool AlreadySubmittedBlindBid =>
        IsCaptainOperateMode
        && CurrentRound is { IsBlind: true, IsRevealed: false }
        && ClaimedCaptain is { } cap
        && CurrentRound.Bids.Any(b => b.CaptainPlayerId == cap.PlayerId);
    public string BlindLeftText
    {
        get
        {
            if (HasScheduledBlindRounds)
            {
                var listed = string.Join(", ", _blindRoundNumbers);
                var next = DraftTurnIndex + 1;
                if (CurrentRound is { IsBlind: true, IsRevealed: false })
                    return $"Blind rounds: {listed} · this round is BLIND";
                var upcoming = _blindRoundNumbers.Where(n => n >= next).ToList();
                if (upcoming.Count == 0)
                    return $"Blind rounds: {listed} · none left";
                if (upcoming[0] == next && (HasNoOpenRound || IsBlindAwarded))
                    return $"Blind rounds: {listed} · round {next} is BLIND — creator selects the player";
                return $"Blind rounds: {listed} · next blind is round {upcoming[0]}";
            }

            return $"Blind rounds left: {Math.Max(0, BlindBidRoundsAllowed - BlindRoundsUsed)}";
        }
    }
    public string RoundTitle => CurrentRound is null
        ? "No round open — pick a player"
        : CurrentRound.IsBlind
            ? (CurrentRound.IsRevealed ? "Blind round — awarded" : "BLIND BID — waiting")
            : "Live bidding";

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanSave));
    }

    partial void OnIsViewModeChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(ShowLiveBanner));
        OnPropertyChanged(nameof(ShowDraftTurnBanner));
        OnPropertyChanged(nameof(ShowAuctionRun));
        OnPropertyChanged(nameof(ShowDraftRun));
        OnPropertyChanged(nameof(ShowLiveRun));
    }

    partial void OnIsLiveChanged(bool value)
    {
        ModeLabel = value ? "LIVE" : "LOCAL";
        OnPropertyChanged(nameof(ShowUpdateAdmins));
        OnPropertyChanged(nameof(ShowLiveBanner));
        if (value && UpdateAdmins.Count == 0)
            UpdateAdmins.Add(new AuctionUpdateAdminEditor());
    }

    partial void OnTitleChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(TitleError))
            TitleError = null;
    }

    partial void OnTitleErrorChanged(string? value) => OnPropertyChanged(nameof(HasTitleError));
    partial void OnKindErrorChanged(string? value) => OnPropertyChanged(nameof(HasKindError));
    partial void OnPlayersPerCaptainErrorChanged(string? value) => OnPropertyChanged(nameof(HasPlayersPerCaptainError));
    partial void OnPlayersPerCaptainChanged(int value)
    {
        if (!string.IsNullOrWhiteSpace(PlayersPerCaptainError))
            PlayersPerCaptainError = null;
        OnPropertyChanged(nameof(SetupHint));
        OnPropertyChanged(nameof(PlayersEachLabel));
    }

    partial void OnSelectedKindLabelChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(KindError))
            KindError = null;
        if (!AuctionKindText.TryParse(value, out var kind))
        {
            KindLabel = string.Empty;
            IsAdvance = false;
            IsDrafting = false;
            IsAdvanceDrafting = false;
            IsBasic = false;
            foreach (var c in SelectedCaptains)
            {
                c.ShowOperator = false;
                c.HideWallet = false;
            }
            NotifyKindUi();
            return;
        }
        KindLabel = AuctionKindText.Of(kind);
        IsAdvance = AuctionKindText.IsAdvanceAuction(kind);
        IsDrafting = AuctionKindText.IsDrafting(kind);
        IsAdvanceDrafting = AuctionKindText.IsAdvanceDrafting(kind);
        IsBasic = AuctionKindText.IsBasic(kind);
        foreach (var c in SelectedCaptains)
        {
            c.ShowOperator = AuctionKindText.RequiresCaptainCode(kind);
            c.HideWallet = IsDrafting;
            c.ShowPickOrderButtons = IsSetupEdit && !IsBasic;
        }
        foreach (var lot in Lots)
            lot.IsDraftingLot = IsDrafting;
        if (IsAdvanceDrafting)
            EnsureDefaultDraftGroups();
        RebuildGroupChoices();
        OnPropertyChanged(nameof(ShowAdvanceSettings));
        NotifyKindUi();
    }

    private void NotifyKindUi()
    {
        OnPropertyChanged(nameof(ShowAuctionRun));
        OnPropertyChanged(nameof(ShowDraftRun));
        OnPropertyChanged(nameof(ShowSettingsButton));
        OnPropertyChanged(nameof(SettingsButtonText));
        OnPropertyChanged(nameof(ShowUndoLastPick));
        OnPropertyChanged(nameof(CreatorRunHint));
        OnPropertyChanged(nameof(ShowDraftTurnBanner));
        OnPropertyChanged(nameof(LiveCaptainsTitle));
        OnPropertyChanged(nameof(LiveAvailableTitle));
        OnPropertyChanged(nameof(LiveAuctionPoolTitle));
        OnPropertyChanged(nameof(LivePoolEmptyText));
        OnPropertyChanged(nameof(IsEventComplete));
        OnPropertyChanged(nameof(LiveTeamsTitle));
        OnPropertyChanged(nameof(ShowDraftGroupsSetup));
        OnPropertyChanged(nameof(ShowLotGroups));
        OnPropertyChanged(nameof(ShowGroupedPool));
        OnPropertyChanged(nameof(ShowFlatPool));
        OnPropertyChanged(nameof(ShowGroupQuotaHint));
        OnPropertyChanged(nameof(LivePoolGroupHint));
        OnPropertyChanged(nameof(ShowBasePrices));
        OnPropertyChanged(nameof(ShowManualSaleLots));
        OnPropertyChanged(nameof(ShowBasicSoldByCaptain));
        OnPropertyChanged(nameof(BasicSoldSummaryTitle));
        OnPropertyChanged(nameof(ShowPickOrder));
        OnPropertyChanged(nameof(BannerText));
        OnPropertyChanged(nameof(LiveStatusWord));
        OnPropertyChanged(nameof(PlayersEachLabel));
        OnPropertyChanged(nameof(KindHelpText));
        OnPropertyChanged(nameof(CanBackToLive));
        OnPropertyChanged(nameof(TurnBannerText));
        OnPropertyChanged(nameof(ShowSimpleClose));
        OnPropertyChanged(nameof(ShowAdvanceClose));
        OnPropertyChanged(nameof(CanDeclareBlind));
        OnPropertyChanged(nameof(ShowReveal));
        OnPropertyChanged(nameof(ShowUnsoldSkip));
        OnPropertyChanged(nameof(ShowJokerUse));
        OnPropertyChanged(nameof(ShowBidEntry));
        OnPropertyChanged(nameof(AlreadySubmittedBlindBid));
        OnPropertyChanged(nameof(IsBlindAwarded));
        OnPropertyChanged(nameof(ShowAuctionPool));
        OnPropertyChanged(nameof(HasScheduledBlindRounds));
        OnPropertyChanged(nameof(NextRoundIsBlind));
        OnPropertyChanged(nameof(ShowLegacyBlindCount));
        OnPropertyChanged(nameof(HasNoBlindRoundRows));
        OnPropertyChanged(nameof(BlindLeftText));
        OnPropertyChanged(nameof(RoundTitle));
        OnPropertyChanged(nameof(ViewSummaryText));
        OnPropertyChanged(nameof(SetupHint));
        OnPropertyChanged(nameof(ShowLiveChrome));
        OnPropertyChanged(nameof(RequiresCaptainCode));
        OnPropertyChanged(nameof(CanCaptainNominate));
        OnPropertyChanged(nameof(CanCaptainDraftPick));
        OnPropertyChanged(nameof(ShowCaptainWaitForTurn));
        OnPropertyChanged(nameof(OperateHint));
        OnPropertyChanged(nameof(CaptainWaitText));
    }

    partial void OnIsSetupEditChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAdvanceSettings));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ShowLiveRun));
        OnPropertyChanged(nameof(ShowLiveChrome));
        OnPropertyChanged(nameof(HasTabLockHint));
        OnPropertyChanged(nameof(CanBackToLive));
        OnPropertyChanged(nameof(SaveButtonText));
        foreach (var c in SelectedCaptains)
            c.ShowPickOrderButtons = value;
        NotifyKindUi();
    }

    partial void OnIsLiveRunChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLiveRun));
        OnPropertyChanged(nameof(ShowSimpleClose));
        OnPropertyChanged(nameof(ShowAdvanceClose));
        OnPropertyChanged(nameof(CanDeclareBlind));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(ShowSettingsButton));
        OnPropertyChanged(nameof(SettingsButtonText));
        OnPropertyChanged(nameof(ShowUndoLastPick));
        OnPropertyChanged(nameof(CreatorRunHint));
        NotifyKindUi();
    }

    partial void OnCurrentRoundChanged(AuctionRoundInfo? value)
    {
        OnPropertyChanged(nameof(HasOpenRound));
        OnPropertyChanged(nameof(HasNoOpenRound));
        OnPropertyChanged(nameof(IsBlindAwarded));
        OnPropertyChanged(nameof(ShowAuctionPool));
        OnPropertyChanged(nameof(ShowReveal));
        OnPropertyChanged(nameof(ShowSimpleClose));
        OnPropertyChanged(nameof(ShowAdvanceClose));
        OnPropertyChanged(nameof(ShowUnsoldSkip));
        OnPropertyChanged(nameof(ShowJokerUse));
        OnPropertyChanged(nameof(ShowBidEntry));
        OnPropertyChanged(nameof(AlreadySubmittedBlindBid));
        OnPropertyChanged(nameof(CanDeclareBlind));
        OnPropertyChanged(nameof(CanCaptainNominate));
        OnPropertyChanged(nameof(CanCaptainDraftPick));
        OnPropertyChanged(nameof(ShowCaptainWaitForTurn));
        OnPropertyChanged(nameof(BlindLeftText));
        OnPropertyChanged(nameof(RoundTitle));
        OnPropertyChanged(nameof(TurnBannerText));
        OnPropertyChanged(nameof(CaptainWaitText));
        OnPropertyChanged(nameof(NextRoundIsBlind));
        OnPropertyChanged(nameof(IsEventComplete));
        OnPropertyChanged(nameof(LiveAvailableTitle));
        OnPropertyChanged(nameof(LiveAuctionPoolTitle));
        OnPropertyChanged(nameof(LivePoolEmptyText));
        RefreshPickableLots();
    }

    partial void OnIsCaptainOperateModeChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(ShowOperateHint));
        OnPropertyChanged(nameof(CanCaptainNominate));
        OnPropertyChanged(nameof(CanCaptainDraftPick));
        OnPropertyChanged(nameof(ShowCaptainWaitForTurn));
        OnPropertyChanged(nameof(ShowDraftTurnBanner));
        OnPropertyChanged(nameof(LivePoolGroupHint));
        RefreshPickableLots();
    }

    partial void OnNeedsCaptainPickChanged(bool value) => OnPropertyChanged(nameof(ShowOperateHint));
    partial void OnIsLiveWatchingChanged(bool value) => OnPropertyChanged(nameof(ShowLiveBanner));
    partial void OnClaimedCaptainChanged(AuctionCaptainEditor? value)
    {
        OnPropertyChanged(nameof(OperateHint));
        OnPropertyChanged(nameof(CanCaptainNominate));
        OnPropertyChanged(nameof(CanCaptainDraftPick));
        OnPropertyChanged(nameof(ShowCaptainWaitForTurn));
    }
    partial void OnTurnCaptainIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(TurnBannerText));
        OnPropertyChanged(nameof(ShowDraftTurnBanner));
        OnPropertyChanged(nameof(CanCaptainNominate));
        OnPropertyChanged(nameof(CanCaptainDraftPick));
        OnPropertyChanged(nameof(ShowCaptainWaitForTurn));
        OnPropertyChanged(nameof(IsEventComplete));
        OnPropertyChanged(nameof(LiveAvailableTitle));
        OnPropertyChanged(nameof(LiveAuctionPoolTitle));
        OnPropertyChanged(nameof(LivePoolEmptyText));
        OnPropertyChanged(nameof(LiveStatusWord));
        OnPropertyChanged(nameof(BannerText));
        OnPropertyChanged(nameof(ViewSummaryText));
        OnPropertyChanged(nameof(ShowSettingsButton));
        OnPropertyChanged(nameof(SettingsButtonText));
        OnPropertyChanged(nameof(CreatorRunHint));
        RefreshPickableLots();
    }
    partial void OnTurnCaptainNameChanged(string value) => OnPropertyChanged(nameof(TurnBannerText));
    partial void OnSelectedTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsGeneralTab));
        OnPropertyChanged(nameof(IsCaptainsTab));
        OnPropertyChanged(nameof(IsPlayersTab));
        OnPropertyChanged(nameof(SaveButtonText));
    }
    partial void OnUnlockedCountChanged(int value)
    {
        OnPropertyChanged(nameof(CanOpenCaptainsTab));
        OnPropertyChanged(nameof(CanOpenPlayersTab));
        OnPropertyChanged(nameof(TabLockHint));
        OnPropertyChanged(nameof(HasTabLockHint));
    }
    partial void OnCaptainNameFilterChanged(string value) => RebuildVisibleCaptains();
    partial void OnPlayerNameFilterChanged(string value) => RebuildVisiblePlayers();

    [RelayCommand]
    private void SelectGeneralTab() => SelectedTab = TabGeneral;

    [RelayCommand]
    private void SelectCaptainsTab()
    {
        if (!CanOpenCaptainsTab) return;
        SelectedTab = TabCaptains;
    }

    [RelayCommand]
    private void SelectPlayersTab()
    {
        if (!CanOpenPlayersTab) return;
        SelectedTab = TabPlayers;
    }

    public AuctionEditViewModel(
        IAuctionService auctions,
        IPlayerService players,
        IAuthService auth)
    {
        _auctions = auctions;
        _players = players;
        _auth = auth;
        CanWrite = _auth.CanWrite;
        CanEdit = CanWrite;
    }

    public async Task InitializeAsync()
    {
        CanWrite = _auth.CanWrite;
        IsViewMode = string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase);
        var enterMode = string.Equals(Mode, "enter", StringComparison.OrdinalIgnoreCase);
        var isNew = string.IsNullOrWhiteSpace(AuctionId);

        if (!IsViewMode && !enterMode && !CanWrite)
        {
            ErrorMessage = isNew
                ? "Read-only user cannot create Auction/Drafting."
                : "Read-only user cannot edit Auction/Drafting.";
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        await LoadAuctionAsync(refreshFromApi: IsViewMode || enterMode).ConfigureAwait(true);

        if (!IsViewMode && !enterMode && _existingId is not null && !_access.CanFullEdit)
        {
            ErrorMessage = "Only the creator admin can edit this Auction/Drafting.";
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        if (!IsBasic && (IsViewMode || IsCaptainOperateMode || IsLiveRun || NeedsCaptainPick))
            StartLiveWatch();
    }

    public void StopLiveWatch()
    {
        IsLiveWatching = false;
        var cts = _watchCts;
        _watchCts = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    private void StartLiveWatch()
    {
        StopLiveWatch();
        _watchCts = new CancellationTokenSource();
        var ct = _watchCts.Token;
        IsLiveWatching = true;
        _ = PollWhileOpenAsync(ct);
    }

    private async Task PollWhileOpenAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(true);
                if (IsBusy || IsSetupEdit) continue;
                await RefreshLiveAsync(ct).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            /* page closed */
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if ((!IsViewMode && !IsCaptainOperateMode && !IsLiveRun) || IsBusy) return;
        _liveStamp = null;
        await RefreshLiveAsync().ConfigureAwait(true);
    }

    private async Task RefreshLiveAsync(CancellationToken ct = default)
    {
        if (IsBusy || IsSetupEdit) return;
        if (!Guid.TryParse(AuctionId, out var id)) return;
        try
        {
            var detail = await _auctions.GetAsync(id, ct).ConfigureAwait(true);
            if (detail is null) return;
            var stamp = LiveStamp(detail);
            if (string.Equals(stamp, _liveStamp, StringComparison.Ordinal))
                return;
            ApplyDetail(detail, livePatch: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            /* keep the last stable frame */
        }
    }

    private async Task LoadAuctionAsync(bool refreshFromApi, bool silent = false)
    {
        _ = refreshFromApi;
        if (silent)
        {
            await RefreshLiveAsync().ConfigureAwait(true);
            return;
        }
        try
        {
            if (!silent)
                IsBusy = true;
            if (!silent)
                ErrorMessage = null;
            TitleError = null;
            KindError = null;
            PlayersPerCaptainError = null;
            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);

            var allPlayers = await _players.GetActiveAsync().ConfigureAwait(true);

            CaptainCandidates.Clear();
            PlayerCandidates.Clear();
            ClaimChoices.Clear();
            DraftGroups.Clear();
            GroupNameChoices.Clear();
            foreach (var p in allPlayers)
            {
                CaptainCandidates.Add(new AuctionCaptainCandidate { Id = p.Id, FullName = p.FullName });
                PlayerCandidates.Add(new SelectablePlayer { Id = p.Id, FullName = p.FullName });
            }
            RebuildVisibleCaptains();
            RebuildVisiblePlayers();

            NotifyLotsChanged();
            NotifyCaptainsChanged();
            RebuildCaptainOptions();

            if (Guid.TryParse(AuctionId, out var id))
            {
                _existingId = id;
                var detail = await _auctions.GetAsync(id).ConfigureAwait(true);
                if (detail is null)
                {
                    ErrorMessage = "Auction/Drafting not found.";
                    return;
                }

                ApplyDetail(detail);
            }
            else
            {
                _existingId = null;
                PageTitle = "New Auction/Drafting";
                Title = string.Empty;
                AuctionDate = DateTime.Today;
                IsLive = true;
                SelectedKindLabel = string.Empty;
                KindLabel = string.Empty;
                TitleError = null;
                KindError = null;
                PlayersPerCaptainError = null;
                IsAdvance = false;
                IsDrafting = false;
                IsAdvanceDrafting = false;
                IsBasic = false;
                ModeLabel = "LIVE";
                PlayersPerCaptain = 11;
                JokersPerCaptain = 0;
                BlindBidRoundsAllowed = 0;
                BlindRoundsUsed = 0;
                DraftTurnIndex = 0;
                HasLegacyBlindCount = false;
                _blindRoundNumbers.Clear();
                BlindRoundRows.Clear();
                TurnCaptainId = null;
                TurnCaptainName = string.Empty;
                SelectedTab = TabGeneral;
                UnlockedCount = 1;
                _access = new AuctionAccess(true, false, true, false, true, null);
                IsSetupEdit = CanWrite && !IsViewMode;
                IsLiveRun = false;
                CanEdit = IsSetupEdit;
                IsCaptainOperateMode = false;
                NeedsCaptainPick = false;
                ClaimedCaptain = null;
                SelectedCaptains.Clear();
                Lots.Clear();
                SoldLots.Clear();
                UnsoldLots.Clear();
                GroupedUnsoldLots.Clear();
                GroupedSetupLots.Clear();
                SoldByCaptain.Clear();
                _liveStamp = null;
                _soldViewStamp = null;
                UpdateAdmins.Clear();
                UpdateAdmins.Add(new AuctionUpdateAdminEditor());
                RebuildViewLists();
            }
        }
        catch (Exception ex)
        {
            if (!silent)
                ErrorMessage = ex.Message;
        }
        finally
        {
            if (!silent)
                IsBusy = false;
        }
    }

    private void ApplyDetail(AuctionDetail detail, bool livePatch = false)
    {
        _liveStamp = LiveStamp(detail);
        _access = detail.Access;
        Title = detail.Title;
        AuctionDate = detail.AuctionDateUtc.Date;
        IsLive = true;
        if (!livePatch)
        {
            SelectedKindLabel = AuctionKindText.Of(detail.Kind);
            if (!AuctionKindText.CreateLabels.Contains(SelectedKindLabel))
                SelectedKindLabel = AuctionKindText.CreateLabels[0];
            KindLabel = AuctionKindText.Of(detail.Kind);
            IsAdvance = AuctionKindText.IsAdvanceAuction(detail.Kind);
            IsDrafting = AuctionKindText.IsDrafting(detail.Kind);
            IsAdvanceDrafting = AuctionKindText.IsAdvanceDrafting(detail.Kind);
            IsBasic = AuctionKindText.IsBasic(detail.Kind);
        }
        var eventDone = detail.TurnCaptainId is null
            && detail.Round is null
            && detail.Captains.Count >= 2
            && detail.Lots.Count > 0;
        ModeLabel = eventDone
            ? "COMPLETE"
            : AuctionKindText.IsBasic(detail.Kind) ? "LOCAL" : "LIVE";
        PlayersPerCaptain = Math.Max(1, detail.PlayersPerCaptain);
        JokersPerCaptain = detail.JokersPerCaptain;
        BlindBidRoundsAllowed = detail.BlindBidRoundsAllowed;
        BlindRoundsUsed = detail.BlindRoundsUsed;
        DraftTurnIndex = detail.DraftTurnIndex;
        _blindRoundNumbers.Clear();
        if (detail.BlindRoundNumbers is { Count: > 0 } scheduled)
            _blindRoundNumbers.AddRange(scheduled.Where(n => n > 0).Distinct().OrderBy(n => n));
        if (!livePatch)
        {
            RebuildBlindRoundRows(_blindRoundNumbers);
            HasLegacyBlindCount = _blindRoundNumbers.Count == 0 && detail.BlindBidRoundsAllowed > 0;
        }
        if (detail.Round?.IsBlind == true)
            UseJoker = false;
        CurrentRound = detail.Round;
        TurnCaptainId = detail.TurnCaptainId;
        TurnCaptainName = detail.TurnCaptainName ?? string.Empty;

        if (!livePatch)
        {
            DraftGroups.Clear();
            foreach (var g in detail.DraftGroups)
                DraftGroups.Add(new AuctionDraftGroupEditor { Name = g.Name, MaxPerCaptain = g.MaxPerCaptain });
            if (IsAdvanceDrafting && DraftGroups.Count == 0)
                EnsureDefaultDraftGroups();
            RebuildGroupChoices();
            UpdateAdmins.Clear();
        }

        var enterRequested = string.Equals(Mode, "enter", StringComparison.OrdinalIgnoreCase);
        var complete = detail.TurnCaptainId is null
            && detail.Round is null
            && detail.Captains.Count >= 2
            && detail.Lots.Count > 0;
        var enterMode = !complete && enterRequested && _access.CanEnterAsCaptain;
        var settingsMode = string.Equals(Mode, "setup", StringComparison.OrdinalIgnoreCase)
            || AuctionKindText.IsBasic(detail.Kind);
        IsViewMode = string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase)
            || (complete && !_access.CanFullEdit)
            || (enterRequested && !enterMode && !_access.CanFullEdit);
        IsSetupEdit = !IsViewMode && _access.CanFullEdit && (settingsMode || _existingId is null);
        IsLiveRun = !IsViewMode && _access.CanFullEdit && !enterMode && !IsSetupEdit
            && !AuctionKindText.IsBasic(detail.Kind);
        NeedsCaptainPick = enterMode
            && _access.DeviceCaptainPlayerId is null
            && AuctionKindText.RequiresCaptainCode(detail.Kind);
        IsCaptainOperateMode = !complete
            && !IsViewMode
            && !IsSetupEdit
            && !_access.CanFullEdit
            && _access.CanOperate
            && _access.DeviceCaptainPlayerId is Guid;
        CanEdit = IsSetupEdit || IsLiveRun;

        if (!livePatch || complete)
        {
            PageTitle = IsViewMode
                ? KindLabel
                : NeedsCaptainPick
                    ? "Enter as captain"
                    : IsCaptainOperateMode
                        ? (IsDrafting ? "My pick" : "My bidding")
                        : IsLiveRun
                            ? KindLabel
                            : _existingId is null
                                ? "New Auction/Drafting"
                                : IsBasic
                                    ? KindLabel
                                    : $"{KindLabel} settings";
        }

        var captainMap = detail.Captains.ToDictionary(c => c.PlayerId);

        SyncCaptainsFromDetail(detail);
        BindCaptainEditors();
        NotifyCaptainsChanged();
        if (!livePatch)
            RebuildCaptainOptions();

        ClaimedCaptain = _access.DeviceCaptainPlayerId is Guid claimedId
            ? SelectedCaptains.FirstOrDefault(c => c.PlayerId == claimedId)
            : null;

        if (!livePatch)
            ClaimChoices.Clear();

        var lotIds = detail.Lots.Select(l => l.PlayerId).ToHashSet();
        if (!livePatch)
        {
            foreach (var p in PlayerCandidates)
                p.IsSelected = lotIds.Contains(p.Id) && !captainMap.ContainsKey(p.Id);
        }

        SyncLotsFromDetail(detail);
        if (!livePatch && IsSetupEdit)
        {
            foreach (var id in SelectedCaptains.Select(c => c.PlayerId).ToList())
                DropFromPlayerPool(id);
        }
        NotifyLotsChanged();
        RefreshCaptainBudgets();
        CurrentLot = detail.Round is { } rnd
            ? Lots.FirstOrDefault(l => l.PlayerId == rnd.PlayerId)
            : null;
        SimpleWinner ??= SelectedCaptains.FirstOrDefault();
        if (CurrentLot is not null && SimplePrice < CurrentLot.BasePrice)
            SimplePrice = CurrentLot.BasePrice;
        var minBid = CurrentRound?.MinNextBid ?? 0;
        if (BidAmount < minBid)
            BidAmount = minBid;
        if (IsDrafting)
        {
            RoundStatus = TurnCaptainId is null
                ? "Every captain has a full squad (or no legal picks remain)."
                : $"It's {TurnCaptainName}'s turn. Tap an available player to pick.";
        }
        else
        {
            RoundStatus = CurrentRound is null
                ? (TurnCaptainId is Guid
                    ? (NextRoundIsBlind
                        ? $"Round {DraftTurnIndex + 1} is BLIND — creator selects the player."
                        : $"{TurnCaptainName} brings a player. Start that player to open bidding.")
                    : "Every captain has a full squad (or no player left to sell).")
                    : CurrentRound.IsBlind && !CurrentRound.IsRevealed
                    ? $"BLIND BID — waiting {CurrentRound.BidCount}/{CurrentRound.CaptainCount}. Highest bid wins automatically when all are in."
                    : CurrentRound.IsBlind
                        ? (CurrentLot is { IsSold: true } sold
                            ? $"Sold to {sold.SoldToCaptain?.DisplayName ?? "?"} for {sold.SoldPrice:N0}. All bids shown."
                            : "Blind round awarded. All bids shown.")
                    : $"Min next bid {CurrentRound.MinNextBid:N0}.";
        }
        RefreshPickableLots();
        OnPropertyChanged(nameof(SetupHint));
        OnPropertyChanged(nameof(ShowAdvanceSettings));
        if (livePatch)
        {
            OnPropertyChanged(nameof(TurnBannerText));
            OnPropertyChanged(nameof(CanCaptainNominate));
            OnPropertyChanged(nameof(CanCaptainDraftPick));
            OnPropertyChanged(nameof(ShowCaptainWaitForTurn));
            OnPropertyChanged(nameof(ViewSummaryText));
            OnPropertyChanged(nameof(IsBlindAwarded));
            OnPropertyChanged(nameof(ShowAuctionPool));
            OnPropertyChanged(nameof(ShowJokerUse));
            OnPropertyChanged(nameof(ShowBidEntry));
            OnPropertyChanged(nameof(AlreadySubmittedBlindBid));
            OnPropertyChanged(nameof(NextRoundIsBlind));
            OnPropertyChanged(nameof(ShowUnsoldSkip));
            OnPropertyChanged(nameof(HasScheduledBlindRounds));
            OnPropertyChanged(nameof(RoundTitle));
            OnPropertyChanged(nameof(ShowReveal));
            OnPropertyChanged(nameof(ShowAdvanceClose));
            OnPropertyChanged(nameof(CanDeclareBlind));
            OnPropertyChanged(nameof(BlindLeftText));
            OnPropertyChanged(nameof(IsEventComplete));
            OnPropertyChanged(nameof(CaptainWaitText));
        }
        else
            NotifyKindUi();
        if (IsSetupEdit)
            UnlockedCount = InferUnlockedCount();
        OnPropertyChanged(nameof(CanBackToLive));
    }

    private static string LiveStamp(AuctionDetail d)
    {
        var caps = string.Join(";", d.Captains.OrderBy(c => c.PlayerId).Select(c =>
            $"{c.PlayerId:N}:{c.BoughtCount}:{c.RemainingSlots}:{c.JokersLeft}:{c.RemainingWallet}:{c.MaxBidThisRound}"));
        var lots = string.Join(";", d.Lots.OrderBy(l => l.PlayerId).Select(l =>
            $"{l.PlayerId:N}:{(l.IsSold ? 1 : 0)}:{l.SoldToCaptainPlayerId?.ToString("N") ?? "-"}:{l.SoldPrice}"));
        var bids = d.Round is null
            ? "-"
            : string.Join(",", d.Round.Bids.Select(b => $"{b.CaptainPlayerId:N}-{b.Amount}-{(b.IsJoker ? 1 : 0)}"));
        var round = d.Round is null
            ? "-"
            : $"{d.Round.PlayerId:N}:{d.Round.IsBlind}:{(d.Round.IsRevealed ? 1 : 0)}:{d.Round.MinNextBid}:{d.Round.BidCount}:{bids}";
        return $"{d.TurnCaptainId?.ToString("N") ?? "-"}|{d.BlindRoundsUsed}|{d.Claims.Count}|{round}|{caps}|{lots}";
    }

    private static List<AuctionCaptainRow> OrderedCaptains(AuctionDetail detail)
    {
        var captainMap = detail.Captains.ToDictionary(c => c.PlayerId);
        if (detail.PickOrder.Count == 0)
            return detail.Captains.OrderBy(c => c.PlayerName).ToList();

        var seen = new HashSet<Guid>();
        var list = new List<AuctionCaptainRow>();
        foreach (var pid in detail.PickOrder)
        {
            if (!captainMap.TryGetValue(pid, out var row) || !seen.Add(pid))
                continue;
            list.Add(row);
        }
        foreach (var cap in detail.Captains)
        {
            if (seen.Add(cap.PlayerId))
                list.Add(cap);
        }
        return list;
    }

    private void SyncCaptainsFromDetail(AuctionDetail detail)
    {
        var ordered = OrderedCaptains(detail);
        var keep = ordered.Select(c => c.PlayerId).ToHashSet();
        for (var i = SelectedCaptains.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(SelectedCaptains[i].PlayerId))
                SelectedCaptains.RemoveAt(i);
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var cap = ordered[i];
            var claimed = detail.Claims.Any(x => x.CaptainPlayerId == cap.PlayerId);
            var mine = detail.Access.DeviceCaptainPlayerId == cap.PlayerId;
            var idx = -1;
            for (var j = 0; j < SelectedCaptains.Count; j++)
            {
                if (SelectedCaptains[j].PlayerId != cap.PlayerId)
                    continue;
                idx = j;
                break;
            }

            if (idx < 0)
            {
                SelectedCaptains.Insert(Math.Min(i, SelectedCaptains.Count), new AuctionCaptainEditor
                {
                    PlayerId = cap.PlayerId,
                    PlayerName = cap.PlayerName,
                    TeamName = cap.TeamName,
                    WalletAmount = cap.WalletAmount,
                    OperatorUsername = cap.OperatorUsername,
                    JokersLeft = cap.JokersLeft,
                    RemainingSlots = cap.RemainingSlots,
                    MaxBidThisRound = cap.MaxBidThisRound,
                    BoughtCount = cap.BoughtCount,
                    SpentAmount = cap.WalletAmount - cap.RemainingWallet,
                    IsClaimedOnThisDevice = mine,
                    IsClaimedByOther = claimed && !mine,
                    ShowOperator = RequiresCaptainCode,
                    HideWallet = IsDrafting,
                    IsDraftTurn = TurnCaptainId == cap.PlayerId,
                    ShowPickOrderButtons = IsSetupEdit
                });
                continue;
            }

            var row = SelectedCaptains[idx];
            row.TeamName = cap.TeamName;
            row.WalletAmount = cap.WalletAmount;
            row.OperatorUsername = cap.OperatorUsername;
            row.JokersLeft = cap.JokersLeft;
            row.RemainingSlots = cap.RemainingSlots;
            row.MaxBidThisRound = cap.MaxBidThisRound;
            row.BoughtCount = cap.BoughtCount;
            row.SpentAmount = cap.WalletAmount - cap.RemainingWallet;
            row.IsClaimedOnThisDevice = mine;
            row.IsClaimedByOther = claimed && !mine;
            row.ShowOperator = RequiresCaptainCode;
            row.HideWallet = IsDrafting;
            row.IsDraftTurn = TurnCaptainId == cap.PlayerId;
            row.ShowPickOrderButtons = IsSetupEdit;
            if (idx != i)
                SelectedCaptains.Move(idx, i);
        }
    }

    private void SyncLotsFromDetail(AuctionDetail detail)
    {
        var keep = detail.Lots.Select(l => l.PlayerId).ToHashSet();
        for (var i = Lots.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Lots[i].PlayerId))
                Lots.RemoveAt(i);
        }

        var ordered = detail.Lots.OrderBy(l => l.PlayerName).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var lot = ordered[i];
            var soldTo = lot.SoldToCaptainPlayerId is Guid cid
                ? CaptainOptions.FirstOrDefault(c => c.PlayerId == cid)
                : null;
            var idx = -1;
            for (var j = 0; j < Lots.Count; j++)
            {
                if (Lots[j].PlayerId != lot.PlayerId)
                    continue;
                idx = j;
                break;
            }

            if (idx < 0)
            {
                var row = new AuctionLotEditor
                {
                    PlayerId = lot.PlayerId,
                    PlayerName = lot.PlayerName,
                    BasePrice = lot.BasePrice,
                    IsSold = lot.IsSold,
                    SoldPrice = lot.SoldPrice ?? 0,
                    CaptainChoices = OperateChoices(),
                    SoldToCaptain = soldTo,
                    CanEditLot = IsSetupEdit,
                    GroupName = lot.GroupName ?? string.Empty,
                    GroupChoices = GroupNameChoices,
                    IsDraftingLot = IsDrafting
                };
                HookLot(row);
                Lots.Insert(Math.Min(i, Lots.Count), row);
                continue;
            }

            var editor = Lots[idx];
            var hook = editor.Changed;
            var moved = editor.GroupMoved;
            editor.Changed = null;
            editor.GroupMoved = null;
            editor.BasePrice = lot.BasePrice;
            editor.IsSold = lot.IsSold;
            editor.SoldPrice = lot.SoldPrice ?? 0;
            editor.SoldToCaptain = soldTo;
            editor.CanEditLot = IsSetupEdit;
            editor.GroupName = lot.GroupName ?? string.Empty;
            editor.GroupChoices = GroupNameChoices;
            editor.IsDraftingLot = IsDrafting;
            editor.Changed = hook;
            editor.GroupMoved = moved;
            HookLot(editor);
            if (idx != i)
                Lots.Move(idx, i);
        }

        RebuildSetupGroupLists();
    }

    private static void SyncLotCollection(
        ObservableCollection<AuctionLotEditor> dest,
        IReadOnlyList<AuctionLotEditor> wanted)
    {
        var ids = wanted.Select(l => l.PlayerId).ToHashSet();
        for (var i = dest.Count - 1; i >= 0; i--)
        {
            if (!ids.Contains(dest[i].PlayerId))
                dest.RemoveAt(i);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var item = wanted[i];
            var idx = -1;
            for (var j = 0; j < dest.Count; j++)
            {
                if (dest[j].PlayerId != item.PlayerId)
                    continue;
                idx = j;
                break;
            }

            if (idx < 0)
                dest.Insert(Math.Min(i, dest.Count), item);
            else if (idx != i)
                dest.Move(idx, i);
        }
    }

    private int InferUnlockedCount()
    {
        if (_existingId is null)
            return 1;
        return SelectedCaptains.Count < 2 ? 2 : 3;
    }

    private ObservableCollection<AuctionCaptainEditor> OperateChoices()
    {
        if (IsCaptainOperateMode && ClaimedCaptain is not null)
            return new ObservableCollection<AuctionCaptainEditor> { ClaimedCaptain };
        return CaptainOptions;
    }

    [RelayCommand]
    private void ToggleCaptain(AuctionCaptainCandidate? player)
    {
        if (!IsSetupEdit || player is null) return;
        player.IsSelected = !player.IsSelected;

        if (player.IsSelected)
        {
            if (SelectedCaptains.Any(c => c.PlayerId == player.Id))
            {
                player.IsSelected = true;
                ErrorMessage = "A captain cannot be selected twice.";
                BindCaptainEditors();
                return;
            }
            var editor = new AuctionCaptainEditor
            {
                PlayerId = player.Id,
                PlayerName = player.FullName,
                TeamName = string.Empty,
                WalletAmount = 0,
                OperatorUsername = string.Empty,
                ShowOperator = RequiresCaptainCode,
                HideWallet = IsDrafting,
                ShowPickOrderButtons = IsSetupEdit
            };
            SelectedCaptains.Add(editor);
            player.Editor = editor;
            DropFromPlayerPool(player.Id);
            ErrorMessage = null;
        }
        else
        {
            var existing = SelectedCaptains.FirstOrDefault(c => c.PlayerId == player.Id);
            if (existing is not null)
                SelectedCaptains.Remove(existing);
            player.Editor = null;
        }

        BindCaptainEditors();
        NotifyCaptainsChanged();
        RebuildCaptainOptions();
        RefreshCaptainBudgets();
        RebuildViewLists();
    }

    [RelayCommand]
    private void AddAllCaptains()
    {
        if (!IsSetupEdit) return;
        var added = 0;
        foreach (var player in VisibleCaptainCandidates.Where(p => !p.IsSelected).ToList())
        {
            if (SelectedCaptains.Any(c => c.PlayerId == player.Id))
                continue;
            player.IsSelected = true;
            var editor = new AuctionCaptainEditor
            {
                PlayerId = player.Id,
                PlayerName = player.FullName,
                TeamName = string.Empty,
                WalletAmount = 0,
                OperatorUsername = string.Empty,
                ShowOperator = RequiresCaptainCode,
                HideWallet = IsDrafting,
                ShowPickOrderButtons = IsSetupEdit
            };
            SelectedCaptains.Add(editor);
            player.Editor = editor;
            DropFromPlayerPool(player.Id);
            added++;
        }
        if (added == 0) return;
        ErrorMessage = null;
        BindCaptainEditors();
        NotifyCaptainsChanged();
        RebuildCaptainOptions();
        RefreshCaptainBudgets();
        RebuildViewLists();
    }

    [RelayCommand]
    private void TogglePlayer(SelectablePlayer? player)
    {
        if (!IsSetupEdit || player is null) return;
        if (SelectedCaptains.Any(c => c.PlayerId == player.Id))
            return;
        player.IsSelected = !player.IsSelected;
        if (player.IsSelected)
        {
            if (Lots.Any(l => l.PlayerId == player.Id)) return;
            var row = new AuctionLotEditor
            {
                PlayerId = player.Id,
                PlayerName = player.FullName,
                BasePrice = 0,
                IsSold = false,
                SoldPrice = 0,
                CaptainChoices = CaptainOptions,
                CanEditLot = true,
                GroupName = GroupNameChoices.FirstOrDefault() ?? string.Empty,
                GroupChoices = GroupNameChoices,
                IsDraftingLot = IsDrafting
            };
            HookLot(row);
            Lots.Add(row);
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

    [RelayCommand]
    private void AddAllPlayers()
    {
        if (!IsSetupEdit) return;
        var added = 0;
        foreach (var player in VisiblePlayerCandidates.Where(p => !p.IsSelected).ToList())
        {
            if (Lots.Any(l => l.PlayerId == player.Id))
                continue;
            player.IsSelected = true;
            var row = new AuctionLotEditor
            {
                PlayerId = player.Id,
                PlayerName = player.FullName,
                BasePrice = 0,
                IsSold = false,
                SoldPrice = 0,
                CaptainChoices = CaptainOptions,
                CanEditLot = true,
                GroupName = GroupNameChoices.FirstOrDefault() ?? string.Empty,
                GroupChoices = GroupNameChoices,
                IsDraftingLot = IsDrafting
            };
            HookLot(row);
            Lots.Add(row);
            added++;
        }
        if (added == 0) return;
        NotifyLotsChanged();
        RefreshCaptainBudgets();
        RebuildViewLists();
    }

    [RelayCommand]
    private void AddUpdateAdmin()
    {
        if (!IsSetupEdit) return;
        UpdateAdmins.Add(new AuctionUpdateAdminEditor());
    }

    [RelayCommand]
    private void RemoveUpdateAdmin(AuctionUpdateAdminEditor? row)
    {
        if (!IsSetupEdit || row is null) return;
        UpdateAdmins.Remove(row);
        if (UpdateAdmins.Count == 0)
            UpdateAdmins.Add(new AuctionUpdateAdminEditor());
    }

    [RelayCommand]
    private async Task ClaimByCodeAsync()
    {
        if (_existingId is not Guid id || IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var detail = await _auctions.ClaimCaptainByCodeAsync(id, CaptainCode).ConfigureAwait(true);
            CaptainCode = string.Empty;
            ApplyDetail(detail);
            if (IsCaptainOperateMode)
                StartLiveWatch();
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

    private void NotifyLotsChanged()
    {
        OnPropertyChanged(nameof(HasLots));
        OnPropertyChanged(nameof(SetupHint));
        OnPropertyChanged(nameof(ShowLotGroups));
        OnPropertyChanged(nameof(ShowGroupedPool));
        OnPropertyChanged(nameof(ShowFlatPool));
        OnPropertyChanged(nameof(ShowGroupQuotaHint));
        OnPropertyChanged(nameof(LivePoolGroupHint));
        OnPropertyChanged(nameof(ShowBasePrices));
        OnPropertyChanged(nameof(CanBackToLive));
        RebuildSetupGroupLists();
    }
    private void NotifyCaptainsChanged()
    {
        OnPropertyChanged(nameof(HasSelectedCaptains));
        OnPropertyChanged(nameof(HasNoSelectedCaptains));
        OnPropertyChanged(nameof(SetupHint));
        OnPropertyChanged(nameof(ShowPickOrder));
        OnPropertyChanged(nameof(CanBackToLive));
        RebuildVisiblePlayers();
    }

    private void BindCaptainEditors()
    {
        foreach (var row in CaptainCandidates)
        {
            var editor = SelectedCaptains.FirstOrDefault(c => c.PlayerId == row.Id);
            row.IsSelected = editor is not null;
            row.Editor = editor;
        }
        RebuildVisibleCaptains();
    }

    private void DropFromPlayerPool(Guid playerId)
    {
        var lot = Lots.FirstOrDefault(l => l.PlayerId == playerId);
        if (lot is not null)
            Lots.Remove(lot);
        var candidate = PlayerCandidates.FirstOrDefault(p => p.Id == playerId);
        if (candidate is not null)
            candidate.IsSelected = false;
        NotifyLotsChanged();
    }

    private static bool NameMatches(string name, string? filter)
        => string.IsNullOrWhiteSpace(filter)
            || name.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);

    private void RebuildVisibleCaptains()
    {
        VisibleCaptainCandidates.Clear();
        foreach (var cap in SelectedCaptains)
        {
            var row = CaptainCandidates.FirstOrDefault(p => p.Id == cap.PlayerId);
            if (row is null)
                continue;
            if (NameMatches(row.FullName, CaptainNameFilter))
                VisibleCaptainCandidates.Add(row);
        }
        foreach (var p in CaptainCandidates)
        {
            if (p.IsSelected)
                continue;
            if (NameMatches(p.FullName, CaptainNameFilter))
                VisibleCaptainCandidates.Add(p);
        }
    }

    private void RebuildVisiblePlayers()
    {
        var captainIds = SelectedCaptains.Select(c => c.PlayerId).ToHashSet();
        VisiblePlayerCandidates.Clear();
        foreach (var p in PlayerCandidates)
        {
            if (captainIds.Contains(p.Id))
                continue;
            if (NameMatches(p.FullName, PlayerNameFilter))
                VisiblePlayerCandidates.Add(p);
        }
    }

    private void RebuildCaptainOptions()
    {
        var next = new ObservableCollection<AuctionCaptainEditor>(
            SelectedCaptains.OrderBy(c => c.PlayerName));
        CaptainOptions = next;

        foreach (var lot in Lots)
        {
            var keepId = lot.SoldToCaptain?.PlayerId;
            lot.CaptainChoices = OperateChoices();
            lot.SoldToCaptain = keepId is Guid sid
                ? lot.CaptainChoices.FirstOrDefault(c => c.PlayerId == sid)
                    ?? next.FirstOrDefault(c => c.PlayerId == sid)
                : null;
            // Basic setup: in-memory only until Save — light captain sold summary, no full live rebuilds.
            lot.Changed = IsBasic && IsSetupEdit
                ? RefreshBasicSoldBudgets
                : RefreshCaptainBudgets;
        }

        if (IsBasic && IsSetupEdit)
            RebuildBasicSoldByCaptain();
    }

    /// <summary>Wallet spend + captain sold list — no Unsold/Sold/pool rebuild, no I/O.</summary>
    private void RefreshBasicSoldBudgets()
    {
        foreach (var cap in SelectedCaptains)
        {
            cap.SpentAmount = Lots
                .Where(l => l.IsSold && l.SoldToCaptain?.PlayerId == cap.PlayerId)
                .Sum(l => Math.Max(0, l.SoldPrice));
        }
        RebuildBasicSoldByCaptain();
    }

    private void RebuildBasicSoldByCaptain()
    {
        var sold = Lots.Where(l => l.IsSold).OrderBy(l => l.PlayerName).ToList();
        var soldStamp = "basic|"
            + string.Join("|", SelectedCaptains.Select(c => $"{c.PlayerId:N}:{c.WalletAmount}"))
            + ";" + string.Join(";", sold.Select(l =>
                $"{l.PlayerId:N}:{l.SoldToCaptain?.PlayerId:N}:{l.SoldPrice}"));
        if (!string.Equals(soldStamp, _soldViewStamp, StringComparison.Ordinal))
        {
            _soldViewStamp = soldStamp;
            SoldByCaptain.Clear();
            foreach (var cap in SelectedCaptains.OrderBy(c => c.PlayerName))
            {
                var bought = sold
                    .Where(l => l.SoldToCaptain?.PlayerId == cap.PlayerId)
                    .ToList();
                if (bought.Count == 0)
                    continue;
                SoldByCaptain.Add(BuildCaptainPurchaseGroup(cap.DisplayName, bought, cap));
            }

            var orphaned = sold
                .Where(l => l.SoldToCaptain is null ||
                            SelectedCaptains.All(c => c.PlayerId != l.SoldToCaptain.PlayerId))
                .ToList();
            if (orphaned.Count > 0)
                SoldByCaptain.Add(BuildCaptainPurchaseGroup("Unassigned", orphaned));
        }

        OnPropertyChanged(nameof(ShowBasicSoldByCaptain));
        OnPropertyChanged(nameof(BasicSoldSummaryTitle));
        OnPropertyChanged(nameof(HasSoldLots));
        OnPropertyChanged(nameof(HasNoSoldLots));
        OnPropertyChanged(nameof(SoldCount));
        OnPropertyChanged(nameof(UnsoldCount));
        OnPropertyChanged(nameof(TotalSoldAmount));
        OnPropertyChanged(nameof(ViewSummaryText));
        OnPropertyChanged(nameof(LiveStatusWord));
        OnPropertyChanged(nameof(BannerText));
    }

    private void RefreshCaptainBudgets()
    {
        foreach (var cap in SelectedCaptains)
        {
            cap.SpentAmount = Lots
                .Where(l => l.IsSold && l.SoldToCaptain?.PlayerId == cap.PlayerId)
                .Sum(l => Math.Max(0, l.SoldPrice));
        }

        if (IsBasic && IsSetupEdit)
        {
            RebuildBasicSoldByCaptain();
            return;
        }

        RebuildViewLists();
    }

    private void RebuildViewLists()
    {
        var unsold = Lots.Where(l => !l.IsSold).OrderBy(l => l.PlayerName).ToList();
        var sold = Lots.Where(l => l.IsSold).OrderBy(l => l.PlayerName).ToList();
        SyncLotCollection(UnsoldLots, unsold);
        SyncLotCollection(SoldLots, sold);
        RefreshCaptainGroupSlots();
        var poolFocus = IsCaptainOperateMode ? ClaimedCaptain?.PlayerId : TurnCaptainId;
        RebuildPoolGroups(GroupedUnsoldLots, unsold, includeEmpty: IsAdvanceDrafting,
            AuctionGroupKind.Pool, poolFocus);

        var soldStamp = string.Join("|", SelectedCaptains.Select(c => c.PlayerId.ToString("N")))
            + ";" + string.Join(";", sold.Select(l =>
            $"{l.PlayerId:N}:{l.SoldToCaptain?.PlayerId:N}:{l.SoldPrice}:{l.GroupName}"))
            + $"|{IsDrafting}|{IsAdvanceDrafting}|{string.Join(",", DraftGroups.Select(g => $"{g.Name}:{g.MaxPerCaptain}"))}";
        if (!string.Equals(soldStamp, _soldViewStamp, StringComparison.Ordinal))
        {
            _soldViewStamp = soldStamp;
            SoldByCaptain.Clear();

            foreach (var cap in SelectedCaptains.OrderBy(c => c.PlayerName))
            {
                var bought = SoldLots
                    .Where(l => l.SoldToCaptain?.PlayerId == cap.PlayerId)
                    .OrderBy(l => l.PlayerName)
                    .ToList();
                SoldByCaptain.Add(BuildCaptainPurchaseGroup(cap.DisplayName, bought, cap));
            }

            var orphaned = SoldLots
                .Where(l => l.SoldToCaptain is null ||
                            SelectedCaptains.All(c => c.PlayerId != l.SoldToCaptain.PlayerId))
                .OrderBy(l => l.PlayerName)
                .ToList();
            if (orphaned.Count > 0)
                SoldByCaptain.Add(BuildCaptainPurchaseGroup("Unassigned", orphaned));
        }

        OnPropertyChanged(nameof(HasSoldLots));
        OnPropertyChanged(nameof(HasNoSoldLots));
        OnPropertyChanged(nameof(HasUnsoldLots));
        OnPropertyChanged(nameof(HasNoUnsoldLots));
        OnPropertyChanged(nameof(SoldCount));
        OnPropertyChanged(nameof(UnsoldCount));
        OnPropertyChanged(nameof(TotalSoldAmount));
        OnPropertyChanged(nameof(ViewSummaryText));
        OnPropertyChanged(nameof(ShowSettingsButton));
        OnPropertyChanged(nameof(SettingsButtonText));
        OnPropertyChanged(nameof(ShowUndoLastPick));
        OnPropertyChanged(nameof(CreatorRunHint));
        OnPropertyChanged(nameof(IsEventComplete));
        OnPropertyChanged(nameof(LiveAvailableTitle));
        OnPropertyChanged(nameof(LiveAuctionPoolTitle));
        OnPropertyChanged(nameof(LivePoolEmptyText));
        OnPropertyChanged(nameof(LiveStatusWord));
        OnPropertyChanged(nameof(BannerText));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (!_access.CanFullEdit) return;
        if (SoldCount > 0 && !IsEventComplete) return;
        StopLiveWatch();
        Mode = "setup";
        IsSetupEdit = true;
        IsLiveRun = false;
        PageTitle = $"{KindLabel} settings";
        SelectedTab = TabGeneral;
        UnlockedCount = InferUnlockedCount();
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(CanBackToLive));
        NotifyKindUi();
    }

    [RelayCommand]
    private void BackToRun()
    {
        if (_existingId is null || IsBasic) return;
        Mode = null;
        IsSetupEdit = false;
        IsLiveRun = _access.CanFullEdit;
        PageTitle = KindLabel;
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ShowLiveRun));
        NotifyKindUi();
        StartLiveWatch();
    }

    [RelayCommand]
    private async Task UndoLastPickAsync()
    {
        if (_existingId is not Guid id || IsBusy || !ShowUndoLastPick) return;
        var ok = await Shell.Current.DisplayAlert(
            "Undo last pick",
            "Return the last confirmed pick to the pool?",
            "Undo",
            "Cancel").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var detail = await _auctions.UndoLastPickAsync(id).ConfigureAwait(true);
            ApplyDetail(detail);
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
    private async Task OpenRoundAsync(AuctionLotEditor? lot)
    {
        if (lot is null || _existingId is not Guid id || IsBusy) return;
        if (!ShowAuctionRun && !CanCaptainNominate) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var detail = await _auctions.OpenRoundAsync(
                id,
                lot.PlayerId,
                OpenAsBlind && IsAdvance && !HasScheduledBlindRounds).ConfigureAwait(true);
            ApplyDetail(detail);
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
    private async Task CloseSimpleRoundAsync()
    {
        if (_existingId is not Guid id || SimpleWinner is null || IsBusy) return;
        if (!ShowAuctionRun) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var detail = await _auctions.CloseRoundAsync(id, unsold: false, SimpleWinner.PlayerId, SimplePrice).ConfigureAwait(true);
            ApplyDetail(detail);
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
    private async Task CloseAdvanceRoundAsync()
    {
        if (_existingId is not Guid id || IsBusy) return;
        if (!ShowAuctionRun) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var detail = await _auctions.CloseRoundAsync(id, unsold: false, null, null).ConfigureAwait(true);
            ApplyDetail(detail);
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
    private async Task MarkUnsoldAsync()
    {
        if (_existingId is not Guid id || IsBusy) return;
        if (!ShowAuctionRun) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var detail = await _auctions.CloseRoundAsync(id, unsold: true, null, null).ConfigureAwait(true);
            ApplyDetail(detail);
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
    private async Task RevealBlindAsync()
    {
        if (_existingId is not Guid id || IsBusy) return;
        if (!ShowAuctionRun) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var detail = await _auctions.RevealBlindAsync(id).ConfigureAwait(true);
            ApplyDetail(detail);
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
    private async Task DraftPickAsync(AuctionLotEditor? lot)
    {
        if (lot is null || _existingId is not Guid id || IsBusy || !IsDrafting) return;
        if (!ShowDraftRun && !CanCaptainDraftPick) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var detail = await _auctions.DraftPickAsync(id, lot.PlayerId).ConfigureAwait(true);
            ApplyDetail(detail);
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
    private void MoveCaptainUp(AuctionCaptainEditor? captain)
    {
        if (!IsSetupEdit || captain is null) return;
        var i = SelectedCaptains.IndexOf(captain);
        if (i <= 0) return;
        SelectedCaptains.Move(i, i - 1);
        RebuildVisibleCaptains();
    }

    [RelayCommand]
    private void MoveCaptainDown(AuctionCaptainEditor? captain)
    {
        if (!IsSetupEdit || captain is null) return;
        var i = SelectedCaptains.IndexOf(captain);
        if (i < 0 || i >= SelectedCaptains.Count - 1) return;
        SelectedCaptains.Move(i, i + 1);
        RebuildVisibleCaptains();
    }

    [RelayCommand]
    private void AddDraftGroup()
    {
        if (!IsSetupEdit || !IsAdvanceDrafting) return;
        DraftGroups.Add(new AuctionDraftGroupEditor { Name = string.Empty, MaxPerCaptain = 1 });
        RebuildGroupChoices();
    }

    [RelayCommand]
    private void RemoveDraftGroup(AuctionDraftGroupEditor? group)
    {
        if (!IsSetupEdit || group is null) return;
        DraftGroups.Remove(group);
        RebuildGroupChoices();
    }

    public void RefreshGroupNamesFromUi() => RebuildGroupChoices();

    private void EnsureDefaultDraftGroups()
    {
        if (DraftGroups.Count > 0) return;
        DraftGroups.Add(new AuctionDraftGroupEditor { Name = "Platinum", MaxPerCaptain = 2 });
        DraftGroups.Add(new AuctionDraftGroupEditor { Name = "Gold", MaxPerCaptain = 4 });
        DraftGroups.Add(new AuctionDraftGroupEditor { Name = "Silver", MaxPerCaptain = 5 });
    }

    private void RebuildGroupChoices()
    {
        var kept = Lots.ToDictionary(l => l.PlayerId, l => (l.GroupName ?? string.Empty).Trim());
        var names = new List<string>();
        foreach (var g in DraftGroups)
        {
            var name = (g.Name ?? string.Empty).Trim();
            if (name.Length > 0 && !names.Exists(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                names.Add(name);
        }

        SyncStringCollection(GroupNameChoices, names);

        foreach (var lot in Lots)
        {
            var hook = lot.Changed;
            var moved = lot.GroupMoved;
            lot.Changed = null;
            lot.GroupMoved = null;
            lot.GroupChoices = GroupNameChoices;
            if (kept.TryGetValue(lot.PlayerId, out var g) && g.Length > 0)
            {
                var match = names.Find(n => string.Equals(n, g, StringComparison.OrdinalIgnoreCase));
                lot.GroupName = match ?? g;
            }
            lot.Changed = hook;
            lot.GroupMoved = moved;
            lot.NotifyGroupIndex();
        }
        OnPropertyChanged(nameof(ShowLotGroups));
        OnPropertyChanged(nameof(ShowGroupedPool));
        OnPropertyChanged(nameof(ShowFlatPool));
        OnPropertyChanged(nameof(ShowGroupQuotaHint));
        OnPropertyChanged(nameof(LivePoolGroupHint));
        RefreshPickableLots();
        RebuildViewLists();
        RebuildSetupGroupLists();
    }

    private void HookLot(AuctionLotEditor lot)
    {
        lot.Changed = IsBasic && IsSetupEdit
            ? RefreshBasicSoldBudgets
            : RefreshCaptainBudgets;
        lot.GroupMoved = RelocateSetupLot;
    }

    private void RelocateSetupLot(AuctionLotEditor lot, string from, string to)
    {
        if (!IsAdvanceDrafting || GroupedSetupLots.Count == 0)
            return;
        from = (from ?? string.Empty).Trim();
        to = (to ?? string.Empty).Trim();
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return;

        AuctionPoolGroup? source = null;
        foreach (var g in GroupedSetupLots)
        {
            if (!g.Players.Contains(lot))
                continue;
            source = g;
            break;
        }
        source?.Players.Remove(lot);
        source?.RefreshCounts();

        var destName = to.Length > 0 ? to : "Ungrouped";
        var dest = GroupedSetupLots.FirstOrDefault(g =>
            string.Equals(g.Name, destName, StringComparison.OrdinalIgnoreCase));
        if (dest is null)
        {
            dest = new AuctionPoolGroup
            {
                Name = destName,
                Kind = AuctionGroupKind.Setup,
                Max = MaxForGroup(destName)
            };
            GroupedSetupLots.Add(dest);
        }

        if (dest.Players.Contains(lot))
        {
            dest.RefreshCounts();
            return;
        }

        lot.IgnorePickerUntil = Environment.TickCount64 + 500;
        var insertAt = 0;
        while (insertAt < dest.Players.Count
               && string.Compare(dest.Players[insertAt].PlayerName, lot.PlayerName, StringComparison.OrdinalIgnoreCase) < 0)
            insertAt++;
        dest.Players.Insert(insertAt, lot);
        dest.RefreshCounts();
    }

    private void RebuildSetupGroupLists()
    {
        var until = Environment.TickCount64 + 500;
        foreach (var lot in Lots)
            lot.IgnorePickerUntil = until;
        RebuildPoolGroups(
            GroupedSetupLots,
            Lots.OrderBy(l => l.PlayerName).ToList(),
            includeEmpty: true,
            AuctionGroupKind.Setup,
            focusCaptainId: null);
    }

    private AuctionCaptainPurchaseGroup BuildCaptainPurchaseGroup(
        string captainName,
        List<AuctionLotEditor> bought,
        AuctionCaptainEditor? captain = null)
    {
        var spent = bought.Sum(l => Math.Max(0, l.SoldPrice));
        var playerHead = IsDrafting
            ? (bought.Count == 0
                ? "No players yet"
                : $"{bought.Count} player{(bought.Count == 1 ? "" : "s")}")
            : (bought.Count == 0
                ? "No players yet"
                : $"{bought.Count} player{(bought.Count == 1 ? "" : "s")} · Spent {spent:N0}");
        var groupHead = IsAdvanceDrafting ? FormatCaptainGroupSummary(bought) : string.Empty;
        var walletLine = string.Empty;
        if (!IsDrafting && captain is not null)
        {
            var wallet = Math.Max(0, captain.WalletAmount);
            var remaining = wallet - spent;
            walletLine = $"Total wallet {wallet:N0} · Remaining {remaining:N0}";
        }

        var group = new AuctionCaptainPurchaseGroup
        {
            CaptainName = captainName,
            SpentTotal = spent,
            ShowGroupedPlayers = IsAdvanceDrafting,
            HeaderLine = groupHead.Length == 0 ? playerHead : $"{playerHead} · {groupHead}",
            WalletLine = walletLine
        };
        foreach (var p in bought)
            group.Players.Add(p);
        if (IsAdvanceDrafting)
        {
            foreach (var pg in BuildPoolGroups(bought, includeEmpty: true, AuctionGroupKind.Squad, focusCaptainId: null))
                group.GroupedPlayers.Add(pg);
        }
        return group;
    }

    private void RefreshCaptainGroupSlots()
    {
        foreach (var cap in SelectedCaptains)
        {
            cap.GroupSlots.Clear();
            cap.ShowGroupSlots = IsAdvanceDrafting;
            if (!IsAdvanceDrafting)
                continue;
            foreach (var def in DraftGroups)
            {
                var name = (def.Name ?? string.Empty).Trim();
                if (name.Length == 0)
                    continue;
                var taken = TakenForCaptain(cap.PlayerId, name);
                var max = Math.Max(0, def.MaxPerCaptain);
                cap.GroupSlots.Add(new AuctionGroupSlot
                {
                    Name = name,
                    CountLine = $"{taken} / {max}",
                    IsFull = max > 0 && taken >= max
                });
            }
        }
    }

    private string FormatCaptainGroupSummary(IReadOnlyList<AuctionLotEditor> bought)
    {
        var parts = new List<string>();
        foreach (var def in DraftGroups)
        {
            var name = (def.Name ?? string.Empty).Trim();
            if (name.Length == 0)
                continue;
            var taken = bought.Count(l =>
                string.Equals((l.GroupName ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase));
            parts.Add($"{name} {taken}/{Math.Max(0, def.MaxPerCaptain)}");
        }
        return string.Join(" · ", parts);
    }

    private int MaxForGroup(string name)
    {
        var want = (name ?? string.Empty).Trim();
        if (want.Length == 0 || string.Equals(want, "Ungrouped", StringComparison.OrdinalIgnoreCase))
            return 0;
        return DraftGroups.FirstOrDefault(g =>
            string.Equals((g.Name ?? string.Empty).Trim(), want, StringComparison.OrdinalIgnoreCase))?.MaxPerCaptain ?? 0;
    }

    private int TakenForCaptain(Guid captainId, string name)
    {
        var want = (name ?? string.Empty).Trim();
        return Lots.Count(l =>
            l.IsSold
            && l.SoldToCaptain?.PlayerId == captainId
            && string.Equals((l.GroupName ?? string.Empty).Trim(), want, StringComparison.OrdinalIgnoreCase));
    }

    private void RebuildPoolGroups(
        ObservableCollection<AuctionPoolGroup> target,
        IReadOnlyList<AuctionLotEditor> lots,
        bool includeEmpty,
        AuctionGroupKind kind,
        Guid? focusCaptainId)
    {
        target.Clear();
        foreach (var g in BuildPoolGroups(lots, includeEmpty, kind, focusCaptainId))
            target.Add(g);
    }

    private List<AuctionPoolGroup> BuildPoolGroups(
        IEnumerable<AuctionLotEditor> lots,
        bool includeEmpty,
        AuctionGroupKind kind,
        Guid? focusCaptainId)
    {
        var list = lots.ToList();
        var result = new List<AuctionPoolGroup>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in DraftGroups)
        {
            var name = (g.Name ?? string.Empty).Trim();
            if (name.Length == 0) continue;
            used.Add(name);
            var players = list
                .Where(l => string.Equals((l.GroupName ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(l => l.PlayerName)
                .ToList();
            if (!includeEmpty && players.Count == 0)
                continue;
            result.Add(MakePoolGroup(name, players, kind, focusCaptainId));
        }

        foreach (var grp in list
            .Select(l => (l.GroupName ?? string.Empty).Trim())
            .Where(n => n.Length > 0 && !used.Contains(n))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            used.Add(grp);
            var players = list
                .Where(l => string.Equals((l.GroupName ?? string.Empty).Trim(), grp, StringComparison.OrdinalIgnoreCase))
                .OrderBy(l => l.PlayerName)
                .ToList();
            result.Add(MakePoolGroup(grp, players, kind, focusCaptainId));
        }

        var ungrouped = list
            .Where(l => string.IsNullOrWhiteSpace(l.GroupName))
            .OrderBy(l => l.PlayerName)
            .ToList();
        if (ungrouped.Count > 0)
            result.Add(MakePoolGroup("Ungrouped", ungrouped, kind, focusCaptainId));
        return result;
    }

    private AuctionPoolGroup MakePoolGroup(
        string name,
        List<AuctionLotEditor> players,
        AuctionGroupKind kind,
        Guid? focusCaptainId)
    {
        var g = new AuctionPoolGroup
        {
            Name = name,
            Kind = kind,
            Max = MaxForGroup(name),
            CaptainTaken = focusCaptainId is Guid id ? TakenForCaptain(id, name) : null
        };
        foreach (var p in players)
            g.Players.Add(p);
        g.RefreshCounts();
        return g;
    }

    private static void SyncStringCollection(ObservableCollection<string> target, IReadOnlyList<string> next)
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            var cur = target[i];
            if (!next.Any(n => string.Equals(n, cur, StringComparison.OrdinalIgnoreCase)))
                target.RemoveAt(i);
        }

        for (var i = 0; i < next.Count; i++)
        {
            var want = next[i];
            var idx = -1;
            for (var j = 0; j < target.Count; j++)
            {
                if (!string.Equals(target[j], want, StringComparison.OrdinalIgnoreCase))
                    continue;
                idx = j;
                break;
            }

            if (idx < 0)
                target.Insert(Math.Min(i, target.Count), want);
            else if (idx != i)
                target.Move(idx, i);
        }

        while (target.Count > next.Count)
            target.RemoveAt(target.Count - 1);
    }

    private void RefreshPickableLots()
    {
        var turn = TurnCaptainId;
        var showPick = !IsEventComplete && IsDrafting && (ShowDraftRun || CanCaptainDraftPick);
        var showStart = !IsEventComplete && !IsDrafting
            && ((ShowAuctionRun && (HasNoOpenRound || IsBlindAwarded)) || CanCaptainNominate);
        foreach (var lot in Lots)
        {
            lot.ShowPickAction = showPick && !lot.IsSold;
            lot.ShowStartAction = showStart && !lot.IsSold;
            if (lot.IsSold || turn is null)
            {
                lot.CanPickNow = false;
                continue;
            }
            if (!IsAdvanceDrafting)
            {
                lot.CanPickNow = true;
                continue;
            }
            var max = DraftGroups.FirstOrDefault(g =>
                string.Equals(g.Name, lot.GroupName, StringComparison.OrdinalIgnoreCase))?.MaxPerCaptain ?? 0;
            if (max <= 0)
            {
                lot.CanPickNow = false;
                continue;
            }
            var taken = Lots.Count(l =>
                l.IsSold
                && l.SoldToCaptain?.PlayerId == turn
                && string.Equals(l.GroupName, lot.GroupName, StringComparison.OrdinalIgnoreCase));
            lot.CanPickNow = taken < max;
        }
    }

    [RelayCommand]
    private async Task PlaceBidAsync()
    {
        if (_existingId is not Guid id || IsBusy) return;
        if (!IsCaptainOperateMode || IsDrafting) return;
        var capId = ClaimedCaptain?.PlayerId;
        if (capId is not Guid cid) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var detail = await _auctions.PlaceBidAsync(id, cid, BidAmount, UseJoker).ConfigureAwait(true);
            UseJoker = false;
            ApplyDetail(detail);
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
    private async Task SaveAsync()
    {
        if (!IsSetupEdit || IsBusy) return;

        try
        {
            ErrorMessage = null;
            if (IsGeneralTab)
            {
                ValidateGeneralTab();
                await PersistAuctionAsync(AuctionSaveStage.General, goLive: false).ConfigureAwait(true);
                UnlockedCount = Math.Max(UnlockedCount, 2);
                SelectedTab = TabCaptains;
                return;
            }

            if (IsCaptainsTab)
            {
                ValidateCaptainsTab();
                await PersistAuctionAsync(AuctionSaveStage.Captains, goLive: false).ConfigureAwait(true);
                UnlockedCount = Math.Max(UnlockedCount, 3);
                SelectedTab = TabPlayers;
                return;
            }

            ValidatePlayersTab();
            await PersistAuctionAsync(AuctionSaveStage.Complete, goLive: !IsBasic).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = FriendlyError(ex);
        }
    }

    private static string FriendlyError(Exception ex)
    {
        var m = ex.Message ?? string.Empty;
        const string prefix = "API ";
        if (!m.StartsWith(prefix, StringComparison.Ordinal))
            return m;
        var colon = m.IndexOf(':');
        return colon >= 0 ? m[(colon + 1)..].Trim() : m;
    }

    private void ValidateGeneralTab()
    {
        TitleError = null;
        KindError = null;
        PlayersPerCaptainError = null;

        if (string.IsNullOrWhiteSpace(Title))
            TitleError = "Auction/Drafting name is required.";
        if (!AuctionKindText.TryParse(SelectedKindLabel, out _))
            KindError = "Type is required.";
        if (PlayersPerCaptain < 1)
            PlayersPerCaptainError = "Each captain must get at least 1 player.";

        if (!string.IsNullOrWhiteSpace(TitleError))
            throw new InvalidOperationException(TitleError);
        if (!string.IsNullOrWhiteSpace(KindError))
            throw new InvalidOperationException(KindError);
        if (!string.IsNullOrWhiteSpace(PlayersPerCaptainError))
            throw new InvalidOperationException(PlayersPerCaptainError);
        if (IsAdvance && (JokersPerCaptain < 0 || BlindBidRoundsAllowed < 0))
            throw new InvalidOperationException("Joker cards and blind rounds cannot be negative.");
        if (IsAdvance)
            CollectBlindRoundNumbers(requireComplete: true);
        if (!IsAdvanceDrafting)
            return;
        var groups = DraftGroups.Where(g => !string.IsNullOrWhiteSpace(g.Name) && g.MaxPerCaptain > 0).ToList();
        if (groups.Count == 0)
            throw new InvalidOperationException("Add at least 1 group with a max per captain.");
        if (groups.Select(g => g.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != groups.Count)
            throw new InvalidOperationException("Group names must be unique.");
        if (groups.Sum(g => g.MaxPerCaptain) < PlayersPerCaptain)
            throw new InvalidOperationException($"Group maxes must cover {PlayersPerCaptain} players per captain.");
    }

    private List<int> CollectBlindRoundNumbers(bool requireComplete)
    {
        var list = new List<int>();
        foreach (var row in BlindRoundRows)
        {
            if (row.RoundNumber < 1)
            {
                if (requireComplete)
                    throw new InvalidOperationException("Set a round number on every blind row, or remove empty rows.");
                continue;
            }
            if (list.Contains(row.RoundNumber))
            {
                if (requireComplete)
                    throw new InvalidOperationException("Blind round numbers must be unique.");
                continue;
            }
            list.Add(row.RoundNumber);
        }
        list.Sort();
        return list;
    }

    private void SyncBlindRoundsFromRows()
    {
        _blindRoundNumbers.Clear();
        _blindRoundNumbers.AddRange(CollectBlindRoundNumbers(requireComplete: false));
        OnPropertyChanged(nameof(HasScheduledBlindRounds));
        OnPropertyChanged(nameof(NextRoundIsBlind));
        OnPropertyChanged(nameof(ShowLegacyBlindCount));
        OnPropertyChanged(nameof(HasNoBlindRoundRows));
        OnPropertyChanged(nameof(CanDeclareBlind));
        OnPropertyChanged(nameof(BlindLeftText));
        OnPropertyChanged(nameof(TurnBannerText));
    }

    private void RebuildBlindRoundRows(IEnumerable<int> numbers)
    {
        BlindRoundRows.Clear();
        foreach (var n in numbers.Where(x => x > 0).Distinct().OrderBy(x => x))
        {
            BlindRoundRows.Add(new AuctionBlindRoundEditor
            {
                RoundNumber = n,
                Changed = SyncBlindRoundsFromRows
            });
        }
        OnPropertyChanged(nameof(HasNoBlindRoundRows));
        OnPropertyChanged(nameof(ShowLegacyBlindCount));
    }

    [RelayCommand]
    private void AddBlindRound()
    {
        if (!IsSetupEdit || !IsAdvance) return;
        var next = BlindRoundRows.Count == 0
            ? 3
            : BlindRoundRows.Max(r => r.RoundNumber) + 3;
        if (next < 1)
            next = 1;
        while (BlindRoundRows.Any(r => r.RoundNumber == next))
            next++;
        BlindRoundRows.Add(new AuctionBlindRoundEditor
        {
            RoundNumber = next,
            Changed = SyncBlindRoundsFromRows
        });
        SyncBlindRoundsFromRows();
    }

    [RelayCommand]
    private void RemoveBlindRound(AuctionBlindRoundEditor? row)
    {
        if (!IsSetupEdit || row is null) return;
        BlindRoundRows.Remove(row);
        SyncBlindRoundsFromRows();
    }

    partial void OnDraftTurnIndexChanged(int value)
    {
        OnPropertyChanged(nameof(NextRoundIsBlind));
        OnPropertyChanged(nameof(TurnBannerText));
        OnPropertyChanged(nameof(CanCaptainNominate));
        OnPropertyChanged(nameof(BlindLeftText));
        OnPropertyChanged(nameof(CaptainWaitText));
        OnPropertyChanged(nameof(ShowCaptainWaitForTurn));
    }

    private void ValidateCaptainsTab()
    {
        if (SelectedCaptains.Count < 2)
            throw new InvalidOperationException("Select at least 2 captains.");
        if (SelectedCaptains.Select(c => c.PlayerId).Distinct().Count() != SelectedCaptains.Count)
            throw new InvalidOperationException("A captain cannot be selected twice.");
        if (SelectedCaptains.Any(c => string.IsNullOrWhiteSpace(c.TeamName)))
            throw new InvalidOperationException("Set a team name for every captain.");
        var teams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in SelectedCaptains)
        {
            if (!teams.Add(c.TeamName.Trim()))
                throw new InvalidOperationException("Each captain needs a unique team name.");
        }
        if (RequiresCaptainCode)
        {
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in SelectedCaptains)
            {
                var code = (c.OperatorUsername ?? string.Empty).Trim().Replace(" ", "");
                if (code.Length < 4 || code.Length > 12)
                    throw new InvalidOperationException("Set a unique captain code (4–12 characters) for every captain.");
                if (!codes.Add(code))
                    throw new InvalidOperationException("Each captain needs a different code.");
            }
        }
        if (!IsDrafting && SelectedCaptains.Any(c => c.WalletAmount < 0))
            throw new InvalidOperationException("Captain wallet cannot be negative.");
    }

    private void ValidatePlayersTab()
    {
        if (Lots.Count == 0)
            throw new InvalidOperationException("Select at least 1 player for this Auction/Drafting.");
        var need = SelectedCaptains.Count * Math.Max(1, PlayersPerCaptain);
        if (Lots.Count < need)
            throw new InvalidOperationException(
                $"Need at least {need} players ({SelectedCaptains.Count} captains × {PlayersPerCaptain} each). Added {Lots.Count}.");
        if (!IsAdvanceDrafting)
            return;
        var names = DraftGroups
            .Where(g => !string.IsNullOrWhiteSpace(g.Name) && g.MaxPerCaptain > 0)
            .Select(g => g.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Lots.Any(l => string.IsNullOrWhiteSpace(l.GroupName) || !names.Contains(l.GroupName.Trim())))
            throw new InvalidOperationException("Set a group for every player.");
    }

    private async Task PersistAuctionAsync(AuctionSaveStage stage, bool goLive)
    {
        if (!_access.CanFullEdit) return;
        RefreshCaptainBudgets();
        RebuildGroupChoices();
        ValidateGeneralTab();
        if (stage >= AuctionSaveStage.Captains)
            ValidateCaptainsTab();
        if (stage >= AuctionSaveStage.Complete)
            ValidatePlayersTab();

        var blindRounds = IsAdvance ? CollectBlindRoundNumbers(requireComplete: true) : new List<int>();
        var blindAllowed = blindRounds.Count > 0 ? blindRounds.Count : BlindBidRoundsAllowed;

        IsBusy = true;
        try
        {
            var draft = new AuctionDraft(
                Title?.Trim() ?? string.Empty,
                AuctionDate.Date,
                Notes: null,
                AuctionKindText.Parse(SelectedKindLabel),
                AuctionKindText.RunModeFor(AuctionKindText.Parse(SelectedKindLabel)),
                PlayersPerCaptain,
                JokersPerCaptain,
                blindAllowed,
                SelectedCaptains.Select(c => c.OperatorUsername).ToList(),
                SelectedCaptains.Select(c => new AuctionCaptainDraft(
                    c.PlayerId, c.WalletAmount, c.TeamName ?? string.Empty, c.OperatorUsername ?? string.Empty)).ToList(),
                Lots.Select(l => new AuctionLotDraft(
                    l.PlayerId,
                    l.BasePrice,
                    l.IsSold,
                    l.IsSold ? l.SoldPrice : null,
                    l.IsSold ? l.SoldToCaptain?.PlayerId : null,
                    l.GroupName ?? string.Empty)).ToList(),
                DraftGroups.Select(g => new AuctionDraftGroup
                {
                    Name = g.Name ?? string.Empty,
                    MaxPerCaptain = g.MaxPerCaptain
                }).ToList(),
                SelectedCaptains.Select(c => c.PlayerId).ToList(),
                Stage: stage,
                BlindRoundNumbers: blindRounds);

            if (_existingId is Guid id)
            {
                await _auctions.UpdateAsync(id, draft).ConfigureAwait(true);
            }
            else
            {
                var created = await _auctions.CreateAsync(draft).ConfigureAwait(true);
                AuctionId = created.Id.ToString("D");
                _existingId = created.Id;
            }

            OnPropertyChanged(nameof(CanBackToLive));
            OnPropertyChanged(nameof(SaveButtonText));

            if (!goLive || IsBasic)
            {
                Mode = "setup";
                IsSetupEdit = true;
                IsLiveRun = false;
                PageTitle = IsBasic ? KindLabel : $"{KindLabel} settings";
                ModeLabel = IsBasic ? "LOCAL" : ModeLabel;
                // Stay on in-memory editors — no reload after local Save.
                return;
            }

            Mode = null;
            IsSetupEdit = false;
            await LoadAuctionAsync(refreshFromApi: true).ConfigureAwait(true);
            StartLiveWatch();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
