using JPL.ViewModels;

namespace JPL.Pages;

public partial class SeriesDetailPage : ContentPage
{
    private readonly SeriesDetailViewModel _vm;

    public SeriesDetailPage(SeriesDetailViewModel vm)
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
