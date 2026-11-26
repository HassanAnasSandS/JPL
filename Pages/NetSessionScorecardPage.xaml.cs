using JPL.Services;
using JPL.ViewModels;

namespace JPL.Pages;

public partial class NetSessionScorecardPage : ContentPage
{
    private readonly NetSessionScorecardViewModel _vm;

    public NetSessionScorecardPage(NetSessionScorecardViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        (Shell.Current as AppShell)?.EnsureDesktopSidebar();
        PageSafe.Run(() => _vm.LoadCommand.ExecuteAsync(null));
    }

    protected override bool OnBackButtonPressed()
    {
        if (_vm.IsSelectedPlayerPointsOpen)
        {
            _vm.CloseSelectedPlayerPointsCommand.Execute(null);
            return true;
        }

        if (_vm.IsPointsListOpen)
        {
            _vm.CloseAwardPointsCommand.Execute(null);
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void OnPlayerPointsRowClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: MatchPointsRow row })
            _vm.SelectPlayerPointsCommand.Execute(row);
    }
}
