using JPL.Services;
using JPL.ViewModels;

namespace JPL.Pages;

public partial class AuctionsPage : ContentPage
{
    private readonly AuctionsViewModel _vm;
    private double _lastWidth;

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

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || Math.Abs(width - _lastWidth) < 1)
            return;
        _lastWidth = width;
        // Fill wide desktop: 1 / 2 / 3 columns by available width.
        var span = width >= 1100 ? 3 : width >= 720 ? 2 : 1;
        if (AuctionsGridLayout.Span != span)
            AuctionsGridLayout.Span = span;
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

    void OnEnterClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: AuctionListItem item })
            PageSafe.Run(() => _vm.EnterCommand.ExecuteAsync(item));
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
