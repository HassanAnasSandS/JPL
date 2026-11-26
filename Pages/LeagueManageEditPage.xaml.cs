using JPL.ViewModels;

namespace JPL.Pages;

public partial class LeagueManageEditPage : ContentPage
{
    private readonly LeagueManageEditViewModel _vm;

    public LeagueManageEditPage(LeagueManageEditViewModel vm)
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
