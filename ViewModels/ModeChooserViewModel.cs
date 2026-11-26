using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class ModeChooserViewModel : ObservableObject
{
    private readonly IAppNavigator _navigator;
    private readonly IAppWorkspace _workspace;
    private readonly IAuthService _auth;

    private bool IsPlatformAdmin => _auth.Current?.CanManagePlatform == true;

    /// <summary>SuperAdmin: Users only. Everyone else: operational modules.</summary>
    public bool ShowUsersOption => IsPlatformAdmin;
    public bool ShowSettingsOption => false;
    public bool ShowLeaguesOption => !IsPlatformAdmin;
    public bool ShowGroundsOption => !IsPlatformAdmin;
    public bool ShowSchedulerOption => !IsPlatformAdmin;
    public string LeaguesTitle => "Leagues";
    public string LeaguesButton => "Open Leagues";
    public string SignedInLabel => _auth.Current?.SignedInLabel ?? string.Empty;
    public string GroundsSubtitle => _auth.Current?.IsGroundOwner == true
        ? $"{BrandSlogans.Grounds}\nYour grounds, bookings, and availability."
        : _auth.Current?.IsGuest == true
            ? $"{BrandSlogans.Grounds}\nBrowse cricket grounds across Pakistan. Login to book."
            : BrandSlogans.Grounds;
    public string GroundsButton => _auth.Current?.IsGroundOwner == true
        ? "Open My Grounds"
        : "Browse Grounds";
    public string LeaguesSubtitle => _auth.Current?.CanViewLeagueCatalog == true
        ? $"{BrandSlogans.Catalog}\n{BrandSlogans.LiveScore}"
        : $"{BrandSlogans.LiveScore}\n{BrandSlogans.Statistics}";

    public ModeChooserViewModel(IAppNavigator navigator, IAppWorkspace workspace, IAuthService auth)
    {
        _navigator = navigator;
        _workspace = workspace;
        _auth = auth;
    }

    [RelayCommand]
    private async Task ChooseLeaguesAsync()
    {
        if (IsPlatformAdmin)
            return;

        _workspace.Set(AppWorkspace.Leagues);
        await _navigator.ShowLeaguePickerAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ChooseGroundsAsync()
    {
        if (IsPlatformAdmin)
            return;

        if (_auth.Current?.IsGroundOwner == true)
        {
            if (!await SchedulerGate.EnsureCanOpenAsync(_auth, _navigator, "My Grounds").ConfigureAwait(true))
                return;
            _workspace.Set(AppWorkspace.GroundOwner);
            await _navigator.ShowMainAsync().ConfigureAwait(true);
            return;
        }

        _workspace.Set(AppWorkspace.Grounds);
        await _navigator.ShowMainAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ChooseSchedulerAsync()
    {
        if (IsPlatformAdmin)
            return;
        if (!await SchedulerGate.EnsureCanOpenAsync(_auth, _navigator).ConfigureAwait(true))
            return;
        _workspace.Set(AppWorkspace.Scheduler);
        await _navigator.ShowMainAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ChooseUsersAsync()
    {
        if (!IsPlatformAdmin)
            return;
        _workspace.Set(AppWorkspace.Users);
        await _navigator.ShowMainAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private Task ChooseSettingsAsync() => Task.CompletedTask;

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _auth.LogoutAsync().ConfigureAwait(true);
        await _navigator.ShowLoginAsync().ConfigureAwait(true);
    }
}
