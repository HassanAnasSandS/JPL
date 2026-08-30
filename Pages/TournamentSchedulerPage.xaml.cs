using JPL.ViewModels;

namespace JPL.Pages;

public partial class TournamentSchedulerPage : ContentPage
{
    private readonly TournamentSchedulerViewModel _vm;

    public TournamentSchedulerPage(TournamentSchedulerViewModel vm)
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
        if (!_vm.CanWrite) return;
        PageSafe.Run(() => Shell.Current.GoToAsync(nameof(TournamentSchedulerEditPage)));
    }

    private void OnOpenClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SchedulerRow row })
            PageSafe.Run(() => _vm.OpenCommand.ExecuteAsync(row));
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SchedulerRow row })
            PageSafe.Run(() => _vm.EditCommand.ExecuteAsync(row));
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SchedulerRow row })
            PageSafe.Run(() => _vm.DeleteCommand.ExecuteAsync(row));
    }
}
