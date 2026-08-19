using JPL.ViewModels;

namespace JPL.Pages;

public partial class VenueBookingsPage : ContentPage
{
    private readonly VenueBookingsViewModel _vm;

    public VenueBookingsPage(VenueBookingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadCommand.ExecuteAsync(null);
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        await _vm.AddCommand.ExecuteAsync(null);
    }

    private async void OnViewClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: VenueBookingRow row })
            return;
        await _vm.ViewCommand.ExecuteAsync(row);
    }

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        if (sender is not Button { CommandParameter: VenueBookingRow row })
            return;
        await _vm.EditCommand.ExecuteAsync(row);
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        if (sender is not Button { CommandParameter: VenueBookingRow row })
            return;
        await _vm.DeleteCommand.ExecuteAsync(row);
    }
}
