using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class ExpenseRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string PurchasedByLine { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public string AmountText { get; init; } = string.Empty;
    public string ContributedText { get; init; } = string.Empty;
    public string OutstandingText { get; init; } = string.Empty;
    public string ContributorsLine { get; init; } = string.Empty;
    public string OwnerLine { get; init; } = string.Empty;
    public bool CanEdit { get; init; }
}

public partial class ExpensesViewModel(IExpenseService expenses, IAuthService auth) : ObservableObject
{
    private int _loadBusy;
    private int _filterGen;
    public ObservableCollection<ExpenseRow> Items { get; } = new();
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string nameFilter = string.Empty;
    public string CountLabel => TotalCount == 0 ? "No expenses" : $"{Items.Count} of {TotalCount}";
    public bool HasFilter => !string.IsNullOrWhiteSpace(NameFilter);

    partial void OnNameFilterChanged(string value) { OnPropertyChanged(nameof(HasFilter)); _ = ReloadDebouncedAsync(); }
    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));

    [RelayCommand] public Task LoadAsync() => ReloadAsync();
    [RelayCommand] private async Task ReloadAsync()
    {
        HasMore = true; Items.Clear(); OnPropertyChanged(nameof(CountLabel)); await LoadMoreAsync().ConfigureAwait(true);
    }
    private async Task ReloadDebouncedAsync()
    {
        var id = Interlocked.Increment(ref _filterGen); await Task.Delay(350).ConfigureAwait(true);
        if (id == _filterGen) await ReloadAsync().ConfigureAwait(true);
    }
    [RelayCommand] private async Task LoadMoreAsync()
    {
        if (!HasMore || Interlocked.Exchange(ref _loadBusy, 1) == 1) return;
        try
        {
            IsBusy = Items.Count == 0; IsLoadingMore = Items.Count > 0; ErrorMessage = null; CanWrite = auth.CanWrite;
            var page = await expenses.GetPageAsync(Items.Count, NameFilter).ConfigureAwait(true);
            TotalCount = page.TotalCount;
            ListPaging.Append(Items, page.Items.Select(x => new ExpenseRow
            {
                Id = x.Id, Name = x.Name, PurchasedByLine = $"Purchased by {x.PurchasedByPlayerName}",
                DateText = x.DateText, AmountText = x.AmountText, ContributedText = x.ContributedText,
                OutstandingText = x.OutstandingText, ContributorsLine = x.ContributorsLine, OwnerLine = x.OwnerLine, CanEdit = x.CanEdit
            }), x => x.Id);
            HasMore = page.HasMore; OnPropertyChanged(nameof(CountLabel));
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex) ? "Offline — Expenses need internet / API." : ex.Message;
            if (NetworkUi.IsNetworkFailure(ex)) await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
        finally { IsBusy = false; IsLoadingMore = false; Interlocked.Exchange(ref _loadBusy, 0); }
    }
    [RelayCommand] private void ClearFilter() => NameFilter = string.Empty;
    [RelayCommand] private Task AddAsync() => CanWrite ? Shell.Current.GoToAsync(nameof(Pages.ExpenseEditPage)) : Task.CompletedTask;
    [RelayCommand] private Task ViewAsync(ExpenseRow? row) => row is null ? Task.CompletedTask : Shell.Current.GoToAsync($"{nameof(Pages.ExpenseEditPage)}?id={row.Id}&mode=view");
    [RelayCommand] private Task EditAsync(ExpenseRow? row) => row is null || !row.CanEdit ? Task.CompletedTask : Shell.Current.GoToAsync($"{nameof(Pages.ExpenseEditPage)}?id={row.Id}");
    [RelayCommand] private async Task DeleteAsync(ExpenseRow? row)
    {
        if (row is null || !row.CanEdit || !await Shell.Current.DisplayAlert("Delete expense", $"Delete ‘{row.Name}’ and its player contributions?", "Delete", "Cancel").ConfigureAwait(true)) return;
        try { await expenses.DeleteAsync(row.Id).ConfigureAwait(true); await LoadAsync().ConfigureAwait(true); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }
}
