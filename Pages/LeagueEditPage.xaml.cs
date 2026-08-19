using JPL.ViewModels;

namespace JPL.Pages;

public partial class LeagueEditPage : ContentPage
{
    private readonly LeagueEditViewModel _vm;

    public LeagueEditPage(LeagueEditViewModel vm)
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
