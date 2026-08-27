using JPL.ViewModels;

namespace JPL.Pages;

public partial class OwnerBookingsPage : ContentPage
{
    private readonly OwnerBookingsViewModel _vm;
    public OwnerBookingsPage(OwnerBookingsViewModel vm)
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
