using JPL.ViewModels;

namespace JPL.Pages;

public partial class CustomerBookingsPage : ContentPage
{
    private readonly CustomerBookingsViewModel _vm;
    public CustomerBookingsPage(CustomerBookingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.ReloadCommand.ExecuteAsync(null));
    }
}
