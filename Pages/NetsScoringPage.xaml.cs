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
        (Shell.Current as AppShell)?.EnsureDesktopSidebar();
        PageSafe.Run(() => _vm.InitializeAsync());
    }
}
