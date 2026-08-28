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

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        await Shell.Current.GoToAsync(nameof(TournamentSchedulerEditPage));
    }

    private void OnOpenClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SchedulerRow row })
            _vm.OpenCommand.Execute(row);
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SchedulerRow row })
            _vm.EditCommand.Execute(row);
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SchedulerRow row })
            _vm.DeleteCommand.Execute(row);
    }
}
