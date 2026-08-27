using JPL.ViewModels;

namespace JPL.Pages;

public partial class OwnerGroundEditPage : ContentPage
{
    private readonly OwnerGroundEditViewModel _vm;
    public OwnerGroundEditPage(OwnerGroundEditViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(_vm.InitializeAsync);
    }
}
