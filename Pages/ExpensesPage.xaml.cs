using JPL.ViewModels;

namespace JPL.Pages;

public partial class ExpensesPage : ContentPage
{
    private readonly ExpensesViewModel _vm;
    public ExpensesPage(ExpensesViewModel vm) { InitializeComponent(); BindingContext = _vm = vm; }
    protected override void OnAppearing() { base.OnAppearing(); if (_vm.Items.Count == 0) PageSafe.Run(_vm.LoadAsync); }
    private void OnLoadMore(object? sender, EventArgs e) => _vm.LoadMoreCommand.Execute(null);
    private void OnViewClicked(object? sender, EventArgs e) { if (sender is Button { CommandParameter: ExpenseRow row }) _vm.ViewCommand.Execute(row); }
    private void OnEditClicked(object? sender, EventArgs e) { if (sender is Button { CommandParameter: ExpenseRow row }) _vm.EditCommand.Execute(row); }
    private void OnDeleteClicked(object? sender, EventArgs e) { if (sender is Button { CommandParameter: ExpenseRow row }) _vm.DeleteCommand.Execute(row); }
}
