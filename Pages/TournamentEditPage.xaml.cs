using JPL.ViewModels;

namespace JPL.Pages;

public partial class TournamentEditPage : ContentPage
{
    private readonly TournamentEditViewModel _vm;

    public TournamentEditPage(TournamentEditViewModel vm)
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
