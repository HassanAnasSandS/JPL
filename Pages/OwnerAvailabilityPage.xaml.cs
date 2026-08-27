using JPL.ViewModels;

namespace JPL.Pages;

public partial class OwnerAvailabilityPage : ContentPage
{
    private readonly OwnerAvailabilityViewModel _vm;
    public OwnerAvailabilityPage(OwnerAvailabilityViewModel vm)
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
