using JPL.ViewModels;

namespace JPL.Pages;

public partial class TournamentDetailPage : ContentPage
{
    private readonly TournamentDetailViewModel _vm;

    public TournamentDetailPage(TournamentDetailViewModel vm)
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
