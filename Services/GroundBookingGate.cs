namespace JPL.Services;

public static class GroundBookingGate
{
    public static async Task<bool> EnsureCanBookAsync(IAuthService auth, IAppNavigator navigator)
    {
        if (auth.Current?.CanBookGrounds == true)
            return true;

        var page = Shell.Current
                   ?? (Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0].Page : null);
        if (page is null)
            return false;

        // Guest (or not signed in): must create a real account to book.
        if (auth.Current is null || auth.Current.IsGuest)
        {
            var signup = await page.DisplayAlert(
                "Login required",
                "Please login or sign up to book a ground. Browsing stays open without an account.",
                "Login / Sign up",
                "Cancel").ConfigureAwait(true);
            if (!signup)
                return false;

            await auth.LogoutAsync().ConfigureAwait(true);
            await navigator.ShowLoginAsync(openSignup: true).ConfigureAwait(true);
            return false;
        }

        // Ground owners book from the owner dashboard, not the public book flow.
        await page.DisplayAlert(
            "Booking",
            "Ground owners manage bookings from My Grounds.",
            "OK").ConfigureAwait(true);
        return false;
    }
}
