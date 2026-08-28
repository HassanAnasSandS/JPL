using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class ModeChooserViewModel : ObservableObject
{
    private readonly IAppNavigator _navigator;
    private readonly IAppWorkspace _workspace;
    private readonly IAuthService _auth;

    public bool ShowUsersOption => _auth.Current?.CanManagePlatform == true;
    public bool ShowSettingsOption => _auth.Current?.CanManagePlatform == true;
    public string SignedInLabel => _auth.Current?.SignedInLabel ?? string.Empty;
    public string GroundsSubtitle => _auth.Current?.IsGroundOwner == true
        ? "Your grounds, bookings, and availability."
        : _auth.Current?.IsGuest == true
            ? "Browse cricket grounds across Pakistan. Sign up to book."
            : "Search cricket grounds across Pakistan and request a booking.";
    public string GroundsButton => _auth.Current?.IsGroundOwner == true
        ? "Open My Grounds"
        : "Browse Grounds";
    public string LeaguesSubtitle => _auth.Current?.CanViewLeagueCatalog == true
        ? "Live scoring, tournaments, auctions, and your league catalog."
        : "Watch live scores, stats, and public league pages.";

    public ModeChooserViewModel(IAppNavigator navigator, IAppWorkspace workspace, IAuthService auth)
    {
        _navigator = navigator;
        _workspace = workspace;
        _auth = auth;
    }

    [RelayCommand]
    private async Task ChooseLeaguesAsync()
    {
        _workspace.Set(AppWorkspace.Leagues);
        await _navigator.ShowLeaguePickerAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ChooseGroundsAsync()
    {
        if (!await SchedulerGate.EnsureCanOpenAsync(_auth, _navigator, "Grounds").ConfigureAwait(true))
            return;
        _workspace.Set(_auth.Current?.IsGroundOwner == true ? AppWorkspace.GroundOwner : AppWorkspace.Grounds);
        await _navigator.ShowMainAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ChooseSchedulerAsync()
    {
        if (!await SchedulerGate.EnsureCanOpenAsync(_auth, _navigator).ConfigureAwait(true))
            return;
        _workspace.Set(AppWorkspace.Scheduler);
        await _navigator.ShowMainAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ChooseUsersAsync()
    {
        if (_auth.Current?.CanManagePlatform != true)
            return;
        _workspace.Set(AppWorkspace.Users);
        await _navigator.ShowMainAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ChooseSettingsAsync()
    {
        if (_auth.Current?.CanManagePlatform != true)
            return;
        _workspace.Set(AppWorkspace.Settings);
        await _navigator.ShowMainAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _auth.LogoutAsync().ConfigureAwait(true);
        await _navigator.ShowLoginAsync().ConfigureAwait(true);
    }
}
