using JPL.ViewModels;

namespace JPL.Pages;

public partial class OwnerGroundsPage : ContentPage
{
    private readonly OwnerGroundsViewModel _vm;
    public OwnerGroundsPage(OwnerGroundsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.ReloadCommand.ExecuteAsync(null));
    }
}
