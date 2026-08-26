using JPL.Domain;
using JPL.Services;
using JPL.ViewModels;

namespace JPL.Pages;

public partial class PlayersPage : ContentPage
{
    private readonly PlayersViewModel _vm;

    public PlayersPage(PlayersViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.LoadCommand.ExecuteAsync(null));
    }

    private void OnAddClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite)
            return;
        PageSafe.Run(() => _vm.AddCommand.ExecuteAsync(null));
    }

    private void OnViewClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Player player })
            return;
        PageSafe.Run(() => _vm.ViewCommand.ExecuteAsync(player));
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite)
            return;
        if (sender is not Button { CommandParameter: Player player })
            return;
        PageSafe.Run(() => _vm.EditCommand.ExecuteAsync(player));
    }

    private void OnToggleActiveClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite)
            return;
        if (sender is not Button { CommandParameter: Player player })
            return;
        PageSafe.Run(() => _vm.ToggleActiveCommand.ExecuteAsync(player));
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite)
            return;
        if (sender is not Button { CommandParameter: Player player })
            return;
        PageSafe.Run(() => _vm.DeleteCommand.ExecuteAsync(player));
    }
}
