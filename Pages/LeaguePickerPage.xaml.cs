using JPL.ViewModels;

namespace JPL.Pages;

public partial class LeaguePickerPage : ContentPage
{
    private readonly LeaguePickerViewModel _vm;

    public LeaguePickerPage(LeaguePickerViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.LoadCommand.ExecuteAsync(null));
    }
}
