using JPL.ViewModels;

namespace JPL.Pages;

public partial class VenuesPage : ContentPage
{
    private readonly VenuesViewModel _vm;

    public VenuesPage(VenuesViewModel vm)
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
        await Shell.Current.GoToAsync(nameof(VenueEditPage));
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        if (sender is Button { CommandParameter: VenueRow row })
            _vm.EditCommand.Execute(row);
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        if (sender is Button { CommandParameter: VenueRow row })
            _vm.DeleteCommand.Execute(row);
    }
}
