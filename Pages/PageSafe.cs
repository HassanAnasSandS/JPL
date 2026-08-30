using JPL.Services;

namespace JPL.Pages;

internal static class PageSafe
{
    public static async void Run(Func<Task> work)
    {
        try
        {
            await AppBusy.RunAsync(async () =>
            {
                await MauiProgram.WhenReadyAsync().ConfigureAwait(true);
                await work().ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        catch (SessionExpiredException)
        {
            // Auth handler already forces logout + login redirect.
        }
        catch (Exception ex)
        {
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
            else
            {
                try
                {
                    if (Shell.Current is not null)
                        await Shell.Current.DisplayAlert("Error", ex.Message, "OK").ConfigureAwait(true);
                }
                catch
                {
                    /* ignore */
                }
            }
        }
    }
}
