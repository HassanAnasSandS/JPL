using JPL.ViewModels;

namespace JPL.Pages;

public partial class HallOfFamePage : ContentPage
{
    private readonly HallOfFameViewModel _vm;

    public HallOfFamePage(HallOfFameViewModel vm)
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
