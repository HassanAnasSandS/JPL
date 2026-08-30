using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Activity;

namespace JPL;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    EnableOnBackInvokedCallback = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        OnBackPressedDispatcher.AddCallback(this, new AppBackCallback(this));
    }

    sealed class AppBackCallback : OnBackPressedCallback
    {
        readonly MainActivity _activity;

        public AppBackCallback(MainActivity activity) : base(enabled: true)
        {
            _activity = activity;
        }

        public override void HandleOnBackPressed()
        {
            try
            {
                if (TryHandleMauiBack())
                    return;
            }
            catch
            {
                /* ignore */
            }

            _activity.MoveTaskToBack(true);
        }

        static bool TryHandleMauiBack()
        {
            var page = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null)
                return false;

            try
            {
                if (page.SendBackButtonPressed())
                    return true;
            }
            catch
            {
                /* continue fallbacks */
            }

            try
            {
                if (page is NavigationPage nav)
                {
                    if (nav.Navigation.ModalStack.Count > 0)
                    {
                        _ = nav.Navigation.PopModalAsync();
                        return true;
                    }

                    if (nav.Navigation.NavigationStack.Count > 1)
                    {
                        _ = nav.Navigation.PopAsync();
                        return true;
                    }

                    if (nav.CurrentPage is not null && nav.CurrentPage.SendBackButtonPressed())
                        return true;
                }
            }
            catch
            {
                /* ignore */
            }

            return false;
        }
    }
}
