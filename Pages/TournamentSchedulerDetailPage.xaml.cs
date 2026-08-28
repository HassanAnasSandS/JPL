using JPL.Services;
using JPL.ViewModels;

namespace JPL.Pages;

public partial class TournamentSchedulerDetailPage : ContentPage
{
    private readonly TournamentSchedulerDetailViewModel _vm;

    public TournamentSchedulerDetailPage(TournamentSchedulerDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.LoadCommand.ExecuteAsync(null));
    }

    private void OnResultClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SchedulerMatchView match })
            _vm.SetResultCommand.Execute(match);
    }
}
