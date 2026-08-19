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
        PageSafe.Run(() => _vm.RefreshCommand.ExecuteAsync(null));
    }
}
