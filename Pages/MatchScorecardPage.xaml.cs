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
        (Shell.Current as AppShell)?.EnsureDesktopSidebar();
        PageSafe.Run(() => _vm.StartLiveWatchAsync());
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0 && height > 0)
            _vm.StreamHeight = Math.Clamp(height - 250, 320, height * 0.72);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        PageSafe.Run(() => _vm.StopLiveWatchAsync());
    }
}
