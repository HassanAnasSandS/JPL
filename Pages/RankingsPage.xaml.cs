using JPL.ViewModels;

namespace JPL.Pages;

public partial class RankingsPage : ContentPage
{
    private readonly RankingsViewModel _vm;

    public RankingsPage(RankingsViewModel vm)
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
