using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly IAppNavigator _navigator;

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

    public LoginViewModel(IAuthService auth, IAppNavigator navigator)
    {
        _auth = auth;
        _navigator = navigator;
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordHidden = !IsPasswordHidden;

    [RelayCommand]
    private Task ContinueWithoutLoginAsync()
    {
        Username = "user";
        Password = "user123";
        return LoginAsync();
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy)
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
