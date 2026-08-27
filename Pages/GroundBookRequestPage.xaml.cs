using JPL.ViewModels;

namespace JPL.Pages;

public partial class GroundBookRequestPage : ContentPage
{
    private readonly GroundBookRequestViewModel _vm;
    public GroundBookRequestPage(GroundBookRequestViewModel vm)
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
