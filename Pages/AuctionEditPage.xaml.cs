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

    void OnToggleCaptainClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SelectablePlayer player })
            _vm.ToggleCaptainCommand.Execute(player);
    }

    void OnTogglePlayerClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: SelectablePlayer player })
            _vm.TogglePlayerCommand.Execute(player);
    }
}
