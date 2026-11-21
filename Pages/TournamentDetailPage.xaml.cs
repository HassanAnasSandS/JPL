using JPL.ViewModels;
using JPL.Services;
using Microsoft.Maui.Controls;

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

    protected override bool OnBackButtonPressed()
    {
        if (_vm.AwardPoints.IsDetailOpen)
        {
            _vm.AwardPoints.CloseDetailCommand.Execute(null);
            return true;
        }
        if (_vm.AwardPoints.IsOpen)
        {
            _vm.AwardPoints.CloseCommand.Execute(null);
            return true;
        }
        if (_vm.ShowTeamScheduleModal)
        {
            _vm.CloseTeamScheduleCommand.Execute(null);
            return true;
        }
        return base.OnBackButtonPressed();
    }

    private void OnAwardPointsClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: EventAwardPointsCategory category })
            _vm.AwardPoints.OpenCommand.Execute(category);
    }
}
