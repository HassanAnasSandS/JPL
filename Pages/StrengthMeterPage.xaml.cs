using JPL.ViewModels;

namespace JPL.Pages;

public partial class StrengthMeterPage : ContentPage
{
    private readonly StrengthMeterViewModel _vm;

    public StrengthMeterPage(StrengthMeterViewModel vm)
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
