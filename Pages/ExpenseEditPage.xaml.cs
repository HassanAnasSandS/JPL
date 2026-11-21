using JPL.ViewModels;

namespace JPL.Pages;

public partial class ExpenseEditPage : ContentPage, IQueryAttributable
{
    private readonly ExpenseEditViewModel _vm;
    private string? _id, _mode;
    public ExpenseEditPage(ExpenseEditViewModel vm) { InitializeComponent(); BindingContext = _vm = vm; }
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _id = Get(query, "id"); _mode = Get(query, "mode"); _vm.ApplyQuery(_id, _mode);
    }
    protected override void OnAppearing() { base.OnAppearing(); _vm.ApplyQuery(_id, _mode); PageSafe.Run(_vm.InitializeAsync); }
    private void OnTogglePlayer(object? sender, EventArgs e)
    { if (sender is Button { CommandParameter: ExpenseContributionEditor player }) _vm.TogglePlayerCommand.Execute(player); }
    private void OnToggleStatus(object? sender, EventArgs e)
    { if (sender is Button { CommandParameter: ExpenseContributionEditor player }) _vm.ToggleStatusCommand.Execute(player); }
    private static string? Get(IDictionary<string, object> query, string key)
        => query.TryGetValue(key, out var raw) && raw?.ToString() is string text && !string.IsNullOrWhiteSpace(text) ? Uri.UnescapeDataString(text) : null;
}
