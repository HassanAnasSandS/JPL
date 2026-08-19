using JPL.ViewModels;

namespace JPL.Pages;

public partial class NetSessionEditPage : ContentPage
{
    private readonly NetSessionEditViewModel _vm;

    public NetSessionEditPage(NetSessionEditViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.InitializeAsync());
    }

    private void OnTogglePlayerClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: SelectablePlayer player })
            return;
        _vm.TogglePlayerCommand.Execute(player);
    }
}
