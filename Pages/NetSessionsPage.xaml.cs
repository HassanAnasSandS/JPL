using JPL.Services;
using JPL.ViewModels;

namespace JPL.Pages;

public partial class NetSessionsPage : ContentPage
{
    private readonly NetSessionsViewModel _vm;

    public NetSessionsPage(NetSessionsViewModel vm)
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

    private void OnScoreClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: NetSessionListItem session })
            return;
        PageSafe.Run(() => _vm.OpenCommand.ExecuteAsync(session));
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite)
            return;
        if (sender is not Button { CommandParameter: NetSessionListItem session })
            return;
        PageSafe.Run(() => _vm.DeleteCommand.ExecuteAsync(session));
    }

    private void OnSyncClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: NetSessionListItem session })
            return;
        PageSafe.Run(() => _vm.SyncCommand.ExecuteAsync(session));
    }
}
