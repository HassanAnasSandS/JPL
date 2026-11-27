using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly IAppNavigator _navigator;
    private readonly IAppStartupStatus _startup;
    private readonly ISyncService _sync;
    private readonly ILeagueContext _league;
    private string busyMessage = "Signing in…";

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordToggleIcon))]
    private bool isPasswordHidden = true;

    public string PasswordToggleIcon => IsPasswordHidden ? "👁" : "🙈";
    public bool ShowLoginForm => IsFormEnabled;

    public bool ShowBusyOverlay => IsBusy || _startup.IsBusy;
    public bool IsFormEnabled => !ShowBusyOverlay;
    public string BusyOverlayMessage =>
        IsBusy
            ? BusyMessage
            : string.IsNullOrWhiteSpace(_startup.Message) ? "Please wait…" : _startup.Message;

    public string BusyMessage
    {
        get => busyMessage;
        private set
        {
            if (SetProperty(ref busyMessage, value))
                OnPropertyChanged(nameof(BusyOverlayMessage));
        }
    }

    private bool CanSubmit => IsFormEnabled;

    public LoginViewModel(
        IAuthService auth,
        IAppNavigator navigator,
        IAppStartupStatus startup,
        ISyncService sync,
        ILeagueContext league)
    {
        _auth = auth;
        _navigator = navigator;
        _startup = startup;
        _sync = sync;
        _league = league;
        _startup.Changed += OnStartupChanged;
        Username = Preferences.Default.Get(AuthService.SessionLastUsernameKey, string.Empty);
        NotifyOverlay();
    }

    public void Detach() => _startup.Changed -= OnStartupChanged;

    private void OnStartupChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(NotifyOverlay);

    partial void OnIsBusyChanged(bool value) => NotifyOverlay();

    private void NotifyOverlay()
    {
        OnPropertyChanged(nameof(ShowBusyOverlay));
        OnPropertyChanged(nameof(IsFormEnabled));
        OnPropertyChanged(nameof(ShowLoginForm));
        OnPropertyChanged(nameof(BusyOverlayMessage));
        LoginCommand.NotifyCanExecuteChanged();
        ContinueWithoutLoginCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordHidden = !IsPasswordHidden;

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private Task ContinueWithoutLoginAsync()
    {
        Username = "user";
        Password = "user123";
        return LoginAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task LoginAsync()
    {
        if (!CanSubmit)
            return;

        try
        {
            IsBusy = true;
            BusyMessage = "Signing in…";
            ErrorMessage = null;

            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);

            var ok = await _auth.LoginAsync(Username, Password).ConfigureAwait(true);
            if (!ok)
            {
                ErrorMessage = AuthMessages.Generic;
                return;
            }

            if (_auth.Current?.Role == JPL.Domain.AppRole.Admin)
            {
                var session = _auth.Current;
                if (session.LeagueId is not Guid homeLeagueId
                    || homeLeagueId == Guid.Empty
                    || _league.CurrentLeagueId != homeLeagueId)
                    throw new InvalidOperationException(
                        "This League Admin account has no valid home league. Ask the Super Admin to assign one.");

                BusyMessage = "Syncing league data… The first initial sync may take a few minutes. Please keep the app open.";
                await _sync.SyncAllAsync().ConfigureAwait(true);
            }

            Password = string.Empty;
            await _navigator.ContinueAfterAuthAsync(freshLogin: true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = _auth.Current?.Role == JPL.Domain.AppRole.Admin
                ? $"League sync did not finish, so the account was not opened. {NetworkUi.UserMessage(ex)}"
                : AuthMessages.ForLogin(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
