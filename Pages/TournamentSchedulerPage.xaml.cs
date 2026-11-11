using JPL.ViewModels;

namespace JPL.Pages;

public partial class TournamentSchedulerPage : ContentPage
{
    private readonly TournamentSchedulerViewModel _vm;
    private double _lastWidth;

    public TournamentSchedulerPage(TournamentSchedulerViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.LoadCommand.ExecuteAsync(null));
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || Math.Abs(width - _lastWidth) < 1)
            return;
        _lastWidth = width;
        // Wide desktop: more columns so list fills the window.
        var span = width >= 1200 ? 3 : width >= 780 ? 2 : 1;
        if (SchedulerGridLayout.Span != span)
            SchedulerGridLayout.Span = span;
    }

    private void OnAddClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        PageSafe.Run(() => Shell.Current.GoToAsync(nameof(TournamentSchedulerEditPage)));
    }

    private void OnViewClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SchedulerRow row })
            PageSafe.Run(() => _vm.ViewCommand.ExecuteAsync(row));
    }

    private void OnOpenClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SchedulerRow row })
            PageSafe.Run(() => _vm.OpenCommand.ExecuteAsync(row));
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SchedulerRow row })
            PageSafe.Run(() => _vm.EditCommand.ExecuteAsync(row));
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SchedulerRow row })
            PageSafe.Run(() => _vm.DeleteCommand.ExecuteAsync(row));
    }
}
