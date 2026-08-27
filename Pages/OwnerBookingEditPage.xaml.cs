using JPL.ViewModels;

namespace JPL.Pages;

public partial class OwnerBookingEditPage : ContentPage
{
    private readonly OwnerBookingEditViewModel _vm;
    public OwnerBookingEditPage(OwnerBookingEditViewModel vm)
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
