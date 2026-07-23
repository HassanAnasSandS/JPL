using JPL.ViewModels;

namespace JPL.Pages;

public partial class PlayerEditPage : ContentPage
{
    private readonly PlayerEditViewModel _vm;

    public PlayerEditPage(PlayerEditViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.InitializeAsync();
    }
}
