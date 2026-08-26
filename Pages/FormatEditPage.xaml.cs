using JPL.ViewModels;

namespace JPL.Pages;

public partial class FormatEditPage : ContentPage
{
    private readonly FormatEditViewModel _vm;

    public FormatEditPage(FormatEditViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.InitializeAsync());
    }
}
