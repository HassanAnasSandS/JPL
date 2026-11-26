using JPL.ViewModels;

namespace JPL.Pages;

/// <summary>SuperAdmin League management (league records only). Lives next to Users in the Users workspace.</summary>
public partial class LeagueManagementPage : ContentPage
{
    private readonly LeagueManagementViewModel _vm;

    public LeagueManagementPage(LeagueManagementViewModel vm)
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
        if (sender is not Button { CommandParameter: ManagedLeagueRow row })
            return;
        PageSafe.Run(() => _vm.EditCommand.ExecuteAsync(row));
    }

    private void OnToggleActiveClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: ManagedLeagueRow row })
            return;
        PageSafe.Run(() => _vm.ToggleActiveCommand.ExecuteAsync(row));
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: ManagedLeagueRow row })
            return;
        PageSafe.Run(() => _vm.DeleteCommand.ExecuteAsync(row));
    }
}
