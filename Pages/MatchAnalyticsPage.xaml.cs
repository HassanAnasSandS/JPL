using JPL.ViewModels;

namespace JPL.Pages;

public partial class MatchAnalyticsPage : ContentPage
{
    public MatchAnalyticsPage(MatchAnalyticsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        (Shell.Current as AppShell)?.EnsureDesktopSidebar();
    }
}
