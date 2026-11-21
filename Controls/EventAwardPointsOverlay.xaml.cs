using JPL.Services;
using JPL.ViewModels;
using Microsoft.Maui.Controls;

namespace JPL.Controls;

public partial class EventAwardPointsOverlay : ContentView
{
    public EventAwardPointsOverlay()
    {
        InitializeComponent();
    }

    private void OnPlayerRowClicked(object? sender, EventArgs e)
    {
        if (BindingContext is EventAwardPointsViewModel vm
            && sender is Button { BindingContext: MatchPointsRow row })
        {
            vm.SelectPlayerCommand.Execute(row);
        }
    }
}
