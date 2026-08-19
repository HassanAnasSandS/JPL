using JPL.ViewModels;

namespace JPL.Pages;

public partial class TournamentsPage : ContentPage
{
    private readonly TournamentsViewModel _vm;

    public TournamentsPage(TournamentsViewModel vm)
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
        await Shell.Current.GoToAsync(nameof(TournamentEditPage));
    }

    private void OnOpenClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: TournamentRow row })
            _vm.OpenCommand.Execute(row);
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: TournamentRow row })
            _vm.EditCommand.Execute(row);
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: TournamentRow row })
            _vm.DeleteCommand.Execute(row);
    }

    private void OnSyncClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: TournamentRow row })
            PageSafe.Run(() => _vm.SyncCommand.ExecuteAsync(row));
    }
}
