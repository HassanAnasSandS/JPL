using JPL.Services;
using JPL.ViewModels;

namespace JPL.Pages;

public partial class TeamsPage : ContentPage
{
    private readonly TeamsViewModel _vm;

    public TeamsPage(TeamsViewModel vm)
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
        if (sender is not Button { CommandParameter: TeamListItem team })
            return;
        PageSafe.Run(() => _vm.ViewCommand.ExecuteAsync(team));
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite)
            return;
        if (sender is not Button { CommandParameter: TeamListItem team })
            return;
        PageSafe.Run(() => _vm.EditCommand.ExecuteAsync(team));
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite)
            return;
        if (sender is not Button { CommandParameter: TeamListItem team })
            return;
        PageSafe.Run(() => _vm.DeleteCommand.ExecuteAsync(team));
    }

    private void OnToggleActiveClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite)
            return;
        if (sender is not Button { CommandParameter: TeamListItem team })
            return;
        PageSafe.Run(() => _vm.ToggleActiveCommand.ExecuteAsync(team));
    }
}
