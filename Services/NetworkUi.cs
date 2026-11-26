using System.Net.Sockets;
using System.Text.Json;

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

    public const string SlowServerMessage =
        "The server took too long to respond (Cloudflare / API).\n\nYour local data is safe — tap Sync now again.";

    private static int _popupBusy;
    private static DateTime _lastPopupUtc = DateTime.MinValue;

    /// <summary>
    /// True only for real transport failures (no TCP/DNS/socket).
    /// Never treat JSON parse, HTTP 4xx/5xx bodies, or generic TaskCanceled as "offline".
    /// </summary>
    public static bool IsNetworkFailure(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is OfflineException or SocketException)
                return true;

            // HttpClient.Timeout → TaskCanceledException; caller may map to SlowServer — not "unreachable".
            if (e is TimeoutException)
                return false;

            if (e is HttpRequestException http)
            {
                // A StatusCode means the proxy/server answered — not a transport offline.
                if (http.StatusCode is not null)
                    return false;
                return true;
            }

            if (e.Message.Contains("No such host", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("Unable to resolve", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("failed to connect", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("Connection reset", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsHttpTimeout(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is TimeoutException)
                return true;
            if (e is TaskCanceledException && e.Message.Contains("HttpClient.Timeout", StringComparison.OrdinalIgnoreCase))
                return true;
            if (e is TaskCanceledException && e.InnerException is TimeoutException)
                return true;
            // HttpClient.Timeout on .NET often wraps as TaskCanceledException without ct cancel.
            if (e is TaskCanceledException tce
                && tce.CancellationToken.IsCancellationRequested == false
                && tce.InnerException is null or TimeoutException)
                return true;
        }

        return false;
    }

    public static string UserMessage(Exception? ex)
    {
        if (ex is OfflineException || IsNetworkFailure(ex))
            return OfflineMessage;

        if (IsHttpTimeout(ex))
            return SlowServerMessage;

        // Prefer the deepest useful message (JSON mismatch, HTTP 500 body, EF inner, …).
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is InvalidDataException or JsonException or FormatException
                || e is HttpRequestException)
            {
                var detail = e.Message;
                if (e.InnerException is not null && !string.IsNullOrWhiteSpace(e.InnerException.Message))
                    detail = $"{detail}\n\n{e.InnerException.Message}";
                return detail.Length <= 600 ? detail : detail[..600] + "…";
            }

            if (e.InnerException is null)
                break;
            if (e.Message.Contains("saving the entities", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("See the inner exception", StringComparison.OrdinalIgnoreCase))
            {
                var inner = e.InnerException.Message;
                if (!string.IsNullOrWhiteSpace(inner))
                    return inner;
            }
        }

        var raw = ex?.Message ?? OfflineMessage;
        if (raw.StartsWith("API ", StringComparison.Ordinal) && raw.Contains(':'))
        {
            var rest = raw[(raw.IndexOf(':') + 1)..].Trim().Trim('"');
            if (rest.StartsWith('{') && rest.Contains("message", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var doc = JsonDocument.Parse(rest);
                    if (doc.RootElement.TryGetProperty("message", out var m)
                        || doc.RootElement.TryGetProperty("Message", out m))
                    {
                        var text = m.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            return text;
                    }
                }
                catch
                {
                    /* keep rest */
                }
            }

            if (!string.IsNullOrWhiteSpace(rest) && !rest.StartsWith('{'))
                return rest;
        }

        if (raw.StartsWith("Sync API returned HTTP", StringComparison.Ordinal))
            return raw;

        return raw;
    }

    /// <summary>
    /// Soft hint only (UI messaging). Sync / API must NOT hard-gate on this —
    /// MAUI Connectivity is unreliable on Windows.
    /// </summary>
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
            var title = IsHttpTimeout(ex) ? "Server slow" : OfflineTitle;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    if (Shell.Current is not null)
                    {
                        await Shell.Current.DisplayAlert(title, message, "OK").ConfigureAwait(true);
                        return;
                    }

                    var page = Application.Current?.Windows?.FirstOrDefault()?.Page;
                    if (page is not null)
                        await page.DisplayAlert(title, message, "OK").ConfigureAwait(true);
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
