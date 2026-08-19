using JPL.ViewModels;

namespace JPL.Pages;

public partial class VenueEditPage : ContentPage
{
    private readonly VenueEditViewModel _vm;

    public VenueEditPage(VenueEditViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.InitializeAsync());
    }
}
