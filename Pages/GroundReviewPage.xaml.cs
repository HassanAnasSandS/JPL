using JPL.ViewModels;

namespace JPL.Pages;

public partial class GroundReviewPage : ContentPage
{
    private readonly GroundReviewViewModel _vm;
    public GroundReviewPage(GroundReviewViewModel vm)
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
