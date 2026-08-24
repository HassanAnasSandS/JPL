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
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordToggleIcon))]
    private bool isPasswordHidden = true;

    public string PasswordToggleIcon => IsPasswordHidden ? "👁" : "🙈";

    public bool ShowBusyOverlay => IsBusy || _startup.IsBusy;
    public bool IsFormEnabled => !ShowBusyOverlay;
    public string BusyOverlayMessage =>
        IsBusy
            ? "Signing in…"
            : string.IsNullOrWhiteSpace(_startup.Message) ? "Please wait…" : _startup.Message;

    private bool CanSubmit => IsFormEnabled;

    public LoginViewModel(IAuthService auth, IAppNavigator navigator, IAppStartupStatus startup)
    {
        _auth = auth;
        _navigator = navigator;
        _startup = startup;
        _startup.Changed += OnStartupChanged;
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
            ErrorMessage = null;

            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);

            var ok = await _auth.LoginAsync(Username, Password).ConfigureAwait(true);
            if (!ok)
            {
                ErrorMessage = "Invalid username or password.";
                return;
            }

            Password = string.Empty;
            if (_auth.Current?.NeedsLeaguePicker == true)
                await _navigator.ShowLeaguePickerAsync().ConfigureAwait(true);
            else
                await _navigator.ShowMainAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
