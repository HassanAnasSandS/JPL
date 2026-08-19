using JPL.Services;
using JPL.ViewModels;

namespace JPL.Pages;

public partial class AuctionsPage : ContentPage
{
    private readonly AuctionsViewModel _vm;

    public AuctionsPage(AuctionsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.RefreshAuthState();
        PageSafe.Run(() => _vm.LoadCommand.ExecuteAsync(null));
    }

    void OnAddClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        PageSafe.Run(() => _vm.AddCommand.ExecuteAsync(null));
    }

    void OnReportClicked(object? sender, EventArgs e)
        => PageSafe.Run(() => _vm.OpenReportCommand.ExecuteAsync(null));

    void OnViewClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: AuctionListItem item })
            PageSafe.Run(() => _vm.ViewCommand.ExecuteAsync(item));
    }

    void OnEditClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        if (sender is Button { CommandParameter: AuctionListItem item })
            PageSafe.Run(() => _vm.EditCommand.ExecuteAsync(item));
    }

    void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        if (sender is Button { CommandParameter: AuctionListItem item })
            PageSafe.Run(() => _vm.DeleteCommand.ExecuteAsync(item));
    }
}
