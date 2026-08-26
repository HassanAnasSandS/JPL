using JPL.ViewModels;

namespace JPL.Pages;

public partial class FormatsPage : ContentPage
{
    private readonly FormatsViewModel _vm;

    public FormatsPage(FormatsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.LoadCommand.ExecuteAsync(null));
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        await Shell.Current.GoToAsync(nameof(FormatEditPage));
    }

    private void OnEditClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        if (sender is Button { CommandParameter: FormatRow row })
            _vm.EditCommand.Execute(row);
    }

    private void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (!_vm.CanWrite) return;
        if (sender is Button { CommandParameter: FormatRow row })
            _vm.DeleteCommand.Execute(row);
    }
}
