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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.RefreshAuthState();
        if (_vm.IsUnlocked)
            PageSafe.Run(() => _vm.LoadCommand.ExecuteAsync(null));
    }

    private void OnAddClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite || !_vm.IsUnlocked)
            return;
        PageSafe.Run(() => _vm.AddCommand.ExecuteAsync(null));
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: UserRow user })
            return;
        PageSafe.Run(() => _vm.EditCommand.ExecuteAsync(user));
    }

    private void OnToggleActiveClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: UserRow user })
            return;
        PageSafe.Run(() => _vm.ToggleActiveCommand.ExecuteAsync(user));
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: UserRow user })
            return;
        PageSafe.Run(() => _vm.DeleteCommand.ExecuteAsync(user));
    }
}
