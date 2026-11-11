using JPL.ViewModels;

namespace JPL.Pages;

public partial class AuctionEditPage : ContentPage
{
    private readonly AuctionEditViewModel _vm;

    public AuctionEditPage(AuctionEditViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.InitializeAsync());
    }

    protected override void OnDisappearing()
    {
        _vm.StopLiveWatch();
        base.OnDisappearing();
    }

    void OnToggleCaptainClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: AuctionCaptainCandidate player })
            _vm.ToggleCaptainCommand.Execute(player);
    }

    void OnTogglePlayerClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SelectablePlayer player })
            _vm.TogglePlayerCommand.Execute(player);
    }

    void OnOpenRoundClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: AuctionLotEditor lot })
            PageSafe.Run(() => _vm.OpenRoundCommand.ExecuteAsync(lot));
    }

    void OnDraftPickClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: AuctionLotEditor lot })
            PageSafe.Run(() => _vm.DraftPickCommand.ExecuteAsync(lot));
    }

    void OnMoveCaptainUpClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: AuctionCaptainEditor captain })
            _vm.MoveCaptainUpCommand.Execute(captain);
    }

    void OnMoveCaptainDownClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: AuctionCaptainEditor captain })
            _vm.MoveCaptainDownCommand.Execute(captain);
    }

    void OnRemoveDraftGroupClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: AuctionDraftGroupEditor group })
            _vm.RemoveDraftGroupCommand.Execute(group);
    }

    void OnRemoveBlindRoundClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: AuctionBlindRoundEditor row })
            _vm.RemoveBlindRoundCommand.Execute(row);
    }

    void OnDraftGroupNameCompleted(object? sender, EventArgs e)
        => _vm.RefreshGroupNamesFromUi();
}
