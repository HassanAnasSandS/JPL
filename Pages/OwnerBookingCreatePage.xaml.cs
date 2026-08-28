using JPL.ViewModels;

namespace JPL.Pages;

public partial class OwnerBookingCreatePage : ContentPage
{
    private readonly OwnerBookingCreateViewModel _vm;
    public OwnerBookingCreatePage(OwnerBookingCreateViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(_vm.LoadAsync);
    }
}
