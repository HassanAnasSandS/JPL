namespace JPL.Services;

public static class SchedulerGate
{
    public static Task<bool> EnsureCanOpenAsync(IAuthService auth, IAppNavigator navigator)
        => EnsureCanOpenAsync(auth, navigator, "Tournament Scheduler");

    public static async Task<bool> EnsureCanOpenAsync(IAuthService auth, IAppNavigator navigator, string featureName)
    {
        if (auth.Current is { IsGuest: false })
            return true;

        var page = Shell.Current
                   ?? (Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0].Page : null);
        if (page is null)
            return false;

        var login = await page.DisplayAlert(
            "Login required",
            $"Please login or sign up first to use {featureName}.",
            "Login / Sign up",
            "Cancel").ConfigureAwait(true);
        if (!login)
            return false;

        await auth.LogoutAsync().ConfigureAwait(true);
        await navigator.ShowLoginAsync().ConfigureAwait(true);
        return false;
    }
}
