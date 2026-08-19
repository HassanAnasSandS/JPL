using JPL.Services;
using JPL.ViewModels;

namespace JPL.Pages;

public partial class MatchesPage : ContentPage
{
    private readonly MatchesViewModel _vm;

    public MatchesPage(MatchesViewModel vm)
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
        if (sender is not Button { CommandParameter: MatchListItem match })
            return;
        PageSafe.Run(() => _vm.OpenCommand.ExecuteAsync(match));
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite)
            return;
        if (sender is not Button { CommandParameter: MatchListItem match })
            return;
        PageSafe.Run(() => _vm.DeleteCommand.ExecuteAsync(match));
    }

    private void OnSyncClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: MatchListItem match })
            return;
        PageSafe.Run(() => _vm.SyncCommand.ExecuteAsync(match));
    }
}
