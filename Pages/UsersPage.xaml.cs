using JPL.ViewModels;

namespace JPL.Pages;

public partial class UsersPage : ContentPage
{
    private readonly UsersViewModel _vm;

    public UsersPage(UsersViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.RefreshAuthState();
        if (_vm.IsUnlocked)
            await _vm.LoadCommand.ExecuteAsync(null);
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite || !_vm.IsUnlocked)
            return;
        await _vm.AddCommand.ExecuteAsync(null);
    }

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: UserRow user })
            return;
        await _vm.EditCommand.ExecuteAsync(user);
    }

    private async void OnToggleActiveClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: UserRow user })
            return;
        await _vm.ToggleActiveCommand.ExecuteAsync(user);
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: UserRow user })
            return;
        await _vm.DeleteCommand.ExecuteAsync(user);
    }
}
