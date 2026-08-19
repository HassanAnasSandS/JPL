using JPL.ViewModels;

namespace JPL.Pages;

public partial class AuctionReportPage : ContentPage
{
    private readonly AuctionReportViewModel _vm;

    public AuctionReportPage(AuctionReportViewModel vm)
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
