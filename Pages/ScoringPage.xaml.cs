using JPL.ViewModels;

namespace JPL.Pages;

public partial class ScoringPage : ContentPage
{
    private readonly ScoringViewModel _vm;
    public ScoringPage(ScoringViewModel vm)
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
