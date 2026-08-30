using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly IAppNavigator _navigator;
    private readonly IAppStartupStatus _startup;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string whatsApp = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool showSignup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordToggleIcon))]
    private bool isPasswordHidden = true;

    public string PasswordToggleIcon => IsPasswordHidden ? "👁" : "🙈";
    public bool ShowLogin => !ShowSignup;
    public bool ShowLoginForm => ShowLogin && IsFormEnabled;
    public bool ShowSignupForm => ShowSignup && IsFormEnabled;

    public bool ShowBusyOverlay => IsBusy || _startup.IsBusy;
    public bool IsFormEnabled => !ShowBusyOverlay;
    public string BusyOverlayMessage =>
        IsBusy
            ? (ShowSignup ? "Creating account…" : "Signing in…")
            : string.IsNullOrWhiteSpace(_startup.Message) ? "Please wait…" : _startup.Message;

    private bool CanSubmit => IsFormEnabled;

    public LoginViewModel(IAuthService auth, IAppNavigator navigator, IAppStartupStatus startup)
    {
        _auth = auth;
        _navigator = navigator;
        _startup = startup;
        _startup.Changed += OnStartupChanged;
        Username = Preferences.Default.Get(AuthService.SessionLastUsernameKey, string.Empty);
        NotifyOverlay();
    }

    public void PrepareSignup()
    {
        ShowSignup = true;
        Username = string.Empty;
        Password = string.Empty;
        WhatsApp = string.Empty;
        ErrorMessage = null;
    }

    public void Detach() => _startup.Changed -= OnStartupChanged;

    private void OnStartupChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(NotifyOverlay);

    partial void OnIsBusyChanged(bool value) => NotifyOverlay();
    partial void OnShowSignupChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLogin));
        NotifyOverlay();
    }

    private void NotifyOverlay()
    {
        OnPropertyChanged(nameof(ShowBusyOverlay));
        OnPropertyChanged(nameof(IsFormEnabled));
        OnPropertyChanged(nameof(ShowLoginForm));
        OnPropertyChanged(nameof(ShowSignupForm));
        OnPropertyChanged(nameof(BusyOverlayMessage));
        LoginCommand.NotifyCanExecuteChanged();
        SignupCommand.NotifyCanExecuteChanged();
        ContinueWithoutLoginCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordHidden = !IsPasswordHidden;

    [RelayCommand]
    private void ToggleSignup()
    {
        ShowSignup = !ShowSignup;
        ErrorMessage = null;
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SignupAsync()
    {
        if (!CanSubmit)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);
            if (!PakistanWhatsApp.TryNormalize(WhatsApp, out _))
            {
                ErrorMessage = PakistanWhatsApp.RequiredError;
                return;
            }
            await _auth.SignupAsync(Username, Password, WhatsApp).ConfigureAwait(true);
            Password = string.Empty;
            WhatsApp = string.Empty;
            await _navigator.ContinueAfterAuthAsync(freshLogin: true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = AuthMessages.ForSignup(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

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
            ErrorMessage = null;

            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);

            var ok = await _auth.LoginAsync(Username, Password).ConfigureAwait(true);
            if (!ok)
            {
                ErrorMessage = AuthMessages.Generic;
                return;
            }

            Password = string.Empty;
            await _navigator.ContinueAfterAuthAsync(freshLogin: true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = AuthMessages.ForLogin(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
