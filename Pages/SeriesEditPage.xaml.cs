using JPL.ViewModels;

namespace JPL.Pages;

public partial class SeriesEditPage : ContentPage
{
    private readonly SeriesEditViewModel _vm;

    public SeriesEditPage(SeriesEditViewModel vm)
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
