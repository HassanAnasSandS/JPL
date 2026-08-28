using JPL.ViewModels;

namespace JPL.Pages;

public partial class ShopCatalogEditPage : ContentPage
{
    private readonly ShopCatalogEditViewModel _vm;

    public ShopCatalogEditPage(ShopCatalogEditViewModel vm)
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
