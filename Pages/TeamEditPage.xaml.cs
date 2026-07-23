using JPL.ViewModels;

namespace JPL.Pages;

public partial class TeamEditPage : ContentPage
{
    private readonly TeamEditViewModel _vm;

    public TeamEditPage(TeamEditViewModel vm)
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
