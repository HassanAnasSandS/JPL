using JPL.ViewModels;

namespace JPL.Pages;

public partial class MatchScorecardPage : ContentPage
{
    private readonly MatchScorecardViewModel _vm;

    public MatchScorecardPage(MatchScorecardViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.EnsureLoadedAsync());
    }
}
