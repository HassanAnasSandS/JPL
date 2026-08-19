using JPL.ViewModels;

namespace JPL.Pages;

public partial class UserEditPage : ContentPage
{
    private readonly UserEditViewModel _vm;

    public UserEditPage(UserEditViewModel vm)
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
