using JPL.ViewModels;

namespace JPL.Pages;

public partial class PlayerPaymentsPage : ContentPage
{
    private readonly PlayerPaymentsViewModel _vm;

    public PlayerPaymentsPage(PlayerPaymentsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_vm.Players.Count == 0)
            PageSafe.Run(() => _vm.LoadCommand.ExecuteAsync(null));
    }

    private void OnRemainingItemsThresholdReached(object? sender, EventArgs e)
    {
        if (_vm.HasMore && !_vm.IsBusy && !_vm.IsLoadingMore)
            PageSafe.Run(() => _vm.LoadMoreCommand.ExecuteAsync(null));
    }
}
