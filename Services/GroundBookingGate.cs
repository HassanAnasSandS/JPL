namespace JPL.Services;

public static class GroundBookingGate
{
    public static async Task<bool> EnsureCanBookAsync(IAuthService auth, IAppNavigator navigator)
    {
        if (auth.Current?.CanBookGrounds == true)
            return true;

        if (auth.Current?.IsGuest != true)
            return false;

        var page = Shell.Current
                   ?? (Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0].Page : null);
        if (page is null)
            return false;

        var signup = await page.DisplayAlert(
            "Sign up required",
            "Create your own Cric360 account to book a ground. Guest login cannot send bookings.",
            "Sign up",
            "Cancel").ConfigureAwait(true);
        if (!signup)
            return false;

        await auth.LogoutAsync().ConfigureAwait(true);
        await navigator.ShowLoginAsync(openSignup: true).ConfigureAwait(true);
        return false;
    }
}
