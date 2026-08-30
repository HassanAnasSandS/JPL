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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.LoadCommand.ExecuteAsync(null));
    }

    private void OnAddClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        PageSafe.Run(() => _vm.AddCommand.ExecuteAsync(null));
    }

    private void OnViewClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: VenueBookingRow row })
            return;
        PageSafe.Run(() => _vm.ViewCommand.ExecuteAsync(row));
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        if (sender is not Button { CommandParameter: VenueBookingRow row })
            return;
        PageSafe.Run(() => _vm.EditCommand.ExecuteAsync(row));
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        if (sender is not Button { CommandParameter: VenueBookingRow row })
            return;
        PageSafe.Run(() => _vm.DeleteCommand.ExecuteAsync(row));
    }
}
