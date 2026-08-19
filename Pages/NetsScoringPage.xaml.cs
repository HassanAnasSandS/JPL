using JPL.ViewModels;

namespace JPL.Pages;

public partial class NetsScoringPage : ContentPage
{
    private readonly NetsScoringViewModel _vm;

    public NetsScoringPage(NetsScoringViewModel vm)
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
