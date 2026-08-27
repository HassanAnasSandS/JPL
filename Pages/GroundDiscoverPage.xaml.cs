using JPL.ViewModels;

namespace JPL.Pages;

public partial class GroundDiscoverPage : ContentPage
{
    private readonly GroundDiscoverViewModel _vm;
    public GroundDiscoverPage(GroundDiscoverViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.InitializeCommand.ExecuteAsync(null));
    }
}
