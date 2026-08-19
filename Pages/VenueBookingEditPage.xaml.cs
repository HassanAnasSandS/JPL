using JPL.ViewModels;

namespace JPL.Pages;

public partial class VenueBookingEditPage : ContentPage, IQueryAttributable
{
    private readonly VenueBookingEditViewModel _vm;
    private string? _bookingId;
    private string? _mode;

    public VenueBookingEditPage(VenueBookingEditViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _bookingId = GetQuery(query, "id");
        _mode = GetQuery(query, "mode");
        _vm.ApplyQuery(_bookingId, _mode);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Re-apply in case Shell set query after construction.
        _vm.ApplyQuery(_bookingId, _mode);
        PageSafe.Run(() => _vm.InitializeAsync());
    }

    private static string? GetQuery(IDictionary<string, object> query, string key)
    {
        if (!query.TryGetValue(key, out var raw) || raw is null)
            return null;
        var text = raw.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : Uri.UnescapeDataString(text);
    }

    private void OnToggleParticipantClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: VenueParticipantEditor player })
            return;
        _vm.ToggleParticipantCommand.Execute(player);
    }

    private void OnToggleStatusClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: VenueParticipantEditor player })
            return;
        _vm.TogglePaymentStatusCommand.Execute(player);
    }
}
