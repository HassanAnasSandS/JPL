using JPL.ViewModels;

namespace JPL.Pages;

public partial class PlayersPage : ContentPage
{
    private readonly PlayersViewModel _vm;

    public PlayersPage(PlayersViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadCommand.ExecuteAsync(null);
    }
}
