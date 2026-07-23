using JPL.ViewModels;

namespace JPL.Pages;

public partial class TeamsPage : ContentPage
{
    private readonly TeamsViewModel _vm;

    public TeamsPage(TeamsViewModel vm)
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
