using JPL.ViewModels;

namespace JPL.Pages;

public partial class ServiceEditPage : ContentPage
{
    private readonly ServiceEditViewModel _vm;

    public ServiceEditPage(ServiceEditViewModel vm)
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
