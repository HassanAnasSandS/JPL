using JPL.ViewModels;

namespace JPL.Pages;

public partial class TournamentSchedulerEditPage : ContentPage
{
    private readonly TournamentSchedulerEditViewModel _vm;

    public TournamentSchedulerEditPage(TournamentSchedulerEditViewModel vm)
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
