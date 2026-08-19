using JPL.ViewModels;

namespace JPL.Pages;

public partial class LeaguesPage : ContentPage
{
    private readonly LeaguesViewModel _vm;

    public LeaguesPage(LeaguesViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.LoadCommand.ExecuteAsync(null));
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        await Shell.Current.GoToAsync(nameof(LeagueEditPage));
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        if (sender is Button { CommandParameter: LeagueRow row })
            _vm.EditCommand.Execute(row);
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        if (sender is Button { CommandParameter: LeagueRow row })
            _vm.DeleteCommand.Execute(row);
    }
}
