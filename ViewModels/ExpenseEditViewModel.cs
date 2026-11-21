using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public partial class ExpenseContributionEditor : ObservableObject
{
    public Guid PlayerId { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string amountText = "0";
    [ObservableProperty] private string status = ExpenseContributionStatus.Pending;
    public bool IsPaid => string.Equals(Status, ExpenseContributionStatus.Paid, StringComparison.OrdinalIgnoreCase);
    public string StatusToggleLabel => IsPaid ? ExpenseContributionStatus.Paid : ExpenseContributionStatus.Pending;
    public string AmountDisplay => decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount.ToString("N0", CultureInfo.InvariantCulture) : "0";
    partial void OnStatusChanged(string value) { OnPropertyChanged(nameof(IsPaid)); OnPropertyChanged(nameof(StatusToggleLabel)); }
    partial void OnAmountTextChanged(string value) => OnPropertyChanged(nameof(AmountDisplay));
    public void ToggleStatus() => Status = IsPaid ? ExpenseContributionStatus.Pending : ExpenseContributionStatus.Paid;
}

public sealed record ExpensePlayerOption(Guid Id, string FullName);

public partial class ExpenseEditViewModel(IExpenseService expenses, IPlayerService players, IAuthService auth) : ObservableObject
{
    private Guid? _id;
    private int _gate;
    public ObservableCollection<ExpensePlayerOption> PlayerOptions { get; } = new();
    public ObservableCollection<ExpenseContributionEditor> ContributionCandidates { get; } = new();
    public ObservableCollection<ExpenseContributionEditor> AddedContributions { get; } = new();
    [ObservableProperty] private string expenseName = string.Empty;
    [ObservableProperty] private string amountText = "0";
    [ObservableProperty] private ExpensePlayerOption? selectedPurchaser;
    [ObservableProperty] private string title = "New expense";
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string ownerLine = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isViewMode;
    [ObservableProperty] private bool canEdit;
    public bool ShowEditContributions => !IsViewMode;
    public bool ShowViewContributions => IsViewMode;

    partial void OnIsViewModeChanged(bool value) { OnPropertyChanged(nameof(ShowEditContributions)); OnPropertyChanged(nameof(ShowViewContributions)); }
    public void ApplyQuery(string? id, string? mode) { _id = Guid.TryParse(id, out var parsed) && parsed != Guid.Empty ? parsed : null; IsViewMode = string.Equals(mode, "view", StringComparison.OrdinalIgnoreCase); }

    public async Task InitializeAsync()
    {
        var gate = Interlocked.Increment(ref _gate); CanEdit = auth.CanWrite;
        if (!CanEdit && !IsViewMode) { ErrorMessage = "Read-only user cannot edit expenses."; await Shell.Current.GoToAsync("..").ConfigureAwait(true); return; }
        var all = await players.GetActiveAsync().ConfigureAwait(true); if (gate != _gate) return;
        PlayerOptions.Clear(); ContributionCandidates.Clear(); AddedContributions.Clear();
        foreach (var p in all) PlayerOptions.Add(new ExpensePlayerOption(p.Id, p.FullName));
        if (_id is null) { Title = "New expense"; IsViewMode = false; SelectedPurchaser = PlayerOptions.FirstOrDefault(); OwnerLine = auth.Current?.Username is string u ? $"Added by {u}" : string.Empty; foreach (var p in all) ContributionCandidates.Add(new ExpenseContributionEditor { PlayerId = p.Id, PlayerName = p.FullName }); return; }
        var detail = await expenses.GetAsync(_id.Value).ConfigureAwait(true); if (gate != _gate) return;
        if (detail is null) { ErrorMessage = "Expense not found."; return; }
        if (IsViewMode || !detail.CanEdit) { if (!IsViewMode && !detail.CanEdit) ErrorMessage = "Only the admin who created this expense can edit it."; IsViewMode = true; }
        CanEdit = detail.CanEdit && !IsViewMode; Title = IsViewMode ? "View expense" : "Edit expense";
        ExpenseName = detail.Name; AmountText = detail.Amount.ToString("0.##", CultureInfo.InvariantCulture); OwnerLine = $"Added by {detail.CreatedByUsername}";
        SelectedPurchaser = PlayerOptions.FirstOrDefault(p => p.Id == detail.PurchasedByPlayerId);
        if (SelectedPurchaser is null) { SelectedPurchaser = new ExpensePlayerOption(detail.PurchasedByPlayerId, detail.PurchasedByPlayerName); PlayerOptions.Insert(0, SelectedPurchaser); }
        var byId = detail.Contributions.ToDictionary(x => x.PlayerId);
        foreach (var p in detail.Contributions.OrderBy(x => x.PlayerName, StringComparer.OrdinalIgnoreCase))
            AddedContributions.Add(new ExpenseContributionEditor
            {
                PlayerId = p.PlayerId, PlayerName = p.PlayerName, IsSelected = true,
                AmountText = p.Amount.ToString("0.##", CultureInfo.InvariantCulture), Status = ExpenseContributionStatus.Normalize(p.Status)
            });
        if (!IsViewMode)
        {
            foreach (var p in all)
            {
                var editor = new ExpenseContributionEditor { PlayerId = p.Id, PlayerName = p.FullName };
                if (byId.TryGetValue(p.Id, out var c))
                {
                    editor.IsSelected = true; editor.AmountText = c.Amount.ToString("0.##", CultureInfo.InvariantCulture);
                    editor.Status = ExpenseContributionStatus.Normalize(c.Status);
                }
                ContributionCandidates.Add(editor);
            }
            foreach (var c in detail.Contributions.Where(c => all.All(p => p.Id != c.PlayerId)))
                ContributionCandidates.Add(new ExpenseContributionEditor
                {
                    PlayerId = c.PlayerId, PlayerName = c.PlayerName, IsSelected = true,
                    AmountText = c.Amount.ToString("0.##", CultureInfo.InvariantCulture), Status = ExpenseContributionStatus.Normalize(c.Status)
                });
        }
    }

    [RelayCommand] private void TogglePlayer(ExpenseContributionEditor? player)
    {
        if (!CanEdit || player is null) return;
        player.IsSelected = !player.IsSelected;
        if (!player.IsSelected) { player.AmountText = "0"; player.Status = ExpenseContributionStatus.Pending; }
    }

    [RelayCommand] private void ToggleStatus(ExpenseContributionEditor? player)
    { if (!CanEdit || player is null || !player.IsSelected) return; player.ToggleStatus(); }

    [RelayCommand] private async Task SaveAsync()
    {
        if (IsBusy || !CanEdit) return;
        try
        {
            IsBusy = true; ErrorMessage = null;
            if (string.IsNullOrWhiteSpace(ExpenseName)) throw new ArgumentException("Enter the expense name.");
            if (SelectedPurchaser is null) throw new ArgumentException("Select who purchased this expense.");
            if (!TryAmount(AmountText, out var total)) throw new ArgumentException("Enter a valid amount.");
            var contributions = ContributionCandidates.Where(x => x.IsSelected).Select(x => new ExpenseContributionDraft(x.PlayerId,
                TryAmount(x.AmountText, out var amount) ? amount : throw new ArgumentException($"Enter a valid contribution for {x.PlayerName}."),
                ExpenseContributionStatus.Normalize(x.Status))).ToList();
            var draft = new ExpenseDraft(ExpenseName.Trim(), SelectedPurchaser.Id, total, contributions);
            if (_id is Guid id) await expenses.UpdateAsync(id, draft).ConfigureAwait(true); else await expenses.CreateAsync(draft).ConfigureAwait(true);
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private static bool TryAmount(string? text, out decimal value)
        => decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value)
           || decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value);
}
