using JPL.ViewModels;

namespace JPL.Pages;

public partial class GroundSettingsPage : ContentPage
{
    private readonly GroundSettingsViewModel _vm;

    public GroundSettingsPage(GroundSettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(_vm.LoadAsync);
    }
}
