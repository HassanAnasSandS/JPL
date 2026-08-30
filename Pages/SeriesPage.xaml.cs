using JPL.ViewModels;

namespace JPL.Pages;

public partial class SeriesPage : ContentPage
{
    private readonly SeriesListViewModel _vm;

    public SeriesPage(SeriesListViewModel vm)
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
        PageSafe.Run(() => Shell.Current.GoToAsync(nameof(SeriesEditPage)));
    }

    private void OnOpenClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SeriesRow row })
            PageSafe.Run(() => _vm.OpenCommand.ExecuteAsync(row));
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SeriesRow row })
            PageSafe.Run(() => _vm.EditCommand.ExecuteAsync(row));
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SeriesRow row })
            PageSafe.Run(() => _vm.DeleteCommand.ExecuteAsync(row));
    }

    private void OnSyncClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SeriesRow row })
            PageSafe.Run(() => _vm.SyncCommand.ExecuteAsync(row));
    }
}
