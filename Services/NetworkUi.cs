using System.Net.Sockets;

namespace JPL.Services;

/// <summary>Thrown when the device is offline or the API host is unreachable.</summary>
public sealed class OfflineException : Exception
{
    public OfflineException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public static class NetworkUi
{
    public const string OfflineTitle = "Offline";
    public const string OfflineMessage =
        "Internet is offline or the server is unreachable.\n\nThe app will keep running with local data. Try again when you are back online.";

    private static int _popupBusy;
    private static DateTime _lastPopupUtc = DateTime.MinValue;

    public static bool IsNetworkFailure(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is OfflineException or HttpRequestException or TaskCanceledException or SocketException or IOException)
                return true;
            if (e.Message.Contains("No such host", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("Unable to resolve", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("failed to connect", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string UserMessage(Exception? ex)
        => IsNetworkFailure(ex) ? OfflineMessage : (ex?.Message ?? OfflineMessage);

    public static bool IsCurrentlyOffline()
    {
        try
        {
            return Connectivity.Current.NetworkAccess != NetworkAccess.Internet;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Show offline popup (throttled). Never throws.</summary>
    public static async Task ShowOfflinePopupAsync(Exception? ex = null)
    {
        if (Interlocked.CompareExchange(ref _popupBusy, 1, 0) != 0)
            return;

        try
        {
            if (DateTime.Now - _lastPopupUtc < TimeSpan.FromSeconds(8))
                return;

            _lastPopupUtc = DateTime.Now;
            var message = UserMessage(ex);

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    if (Shell.Current is not null)
                    {
                        await Shell.Current.DisplayAlert(OfflineTitle, message, "OK").ConfigureAwait(true);
                        return;
                    }

                    var page = Application.Current?.Windows?.FirstOrDefault()?.Page;
                    if (page is not null)
                        await page.DisplayAlert(OfflineTitle, message, "OK").ConfigureAwait(true);
                }
                catch
                {
                    /* never crash while showing offline UI */
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _popupBusy, 0);
        }
    }

    public static void ShowOfflinePopupFireAndForget(Exception? ex = null)
        => _ = ShowOfflinePopupAsync(ex);
}
