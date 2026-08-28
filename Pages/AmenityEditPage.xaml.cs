using JPL.ViewModels;

namespace JPL.Pages;

public partial class AmenityEditPage : ContentPage
{
    private readonly AmenityEditViewModel _vm;

    public AmenityEditPage(AmenityEditViewModel vm)
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
