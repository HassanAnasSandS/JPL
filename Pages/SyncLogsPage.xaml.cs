using JPL.ViewModels;

namespace JPL.Pages;

public partial class SyncLogsPage : ContentPage
{
    private readonly SyncLogsViewModel _vm;

    public SyncLogsPage(SyncLogsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.OnAppearing();
        PageSafe.Run(() => _vm.LoadCommand.ExecuteAsync(null));
    }

    protected override void OnDisappearing()
    {
        _vm.OnDisappearing();
        base.OnDisappearing();
    }
}
