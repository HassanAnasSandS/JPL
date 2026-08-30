using System.Net;
using System.Net.Http.Headers;

namespace JPL.Services;

/// <summary>
/// Attaches Bearer token to API calls. On 401 (deleted / deactivated account),
/// forces logout and login redirect.
/// </summary>
public sealed class AuthHttpMessageHandler : DelegatingHandler
{
    private readonly AuthTokenStore _tokens;
    private readonly IServiceProvider _services;

    public AuthHttpMessageHandler(AuthTokenStore tokens, IServiceProvider services)
    {
        _tokens = tokens;
        _services = services;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var isAnonymous = IsAnonymous(path);

        var sentToken = false;
        if (!isAnonymous)
        {
            var token = _tokens.Token;
            if (!string.IsNullOrWhiteSpace(token))
            {
                sentToken = true;
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var league = _services.GetService<ILeagueContext>();
            if (league?.CurrentLeagueId is Guid leagueId && leagueId != Guid.Empty)
                request.Headers.TryAddWithoutValidation("X-League-Id", leagueId.ToString("D"));
        }

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (NetworkUi.IsNetworkFailure(ex))
        {
            // HttpClient.Timeout and socket abort (app pause / slow host) must not
            // surface as TaskCanceledException in the debugger or crash the UI.
            throw new OfflineException(NetworkUi.OfflineMessage, ex);
        }

        if (!isAnonymous && response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Only kick out when the server says the account is gone. A generic 401
            // (stale header, wrong host) must not wipe a saved login on every launch.
            if (sentToken && !_tokens.SuppressForcedLogout)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                response.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                if (AuthMessages.IsAccountGone(body))
                {
                    _tokens.Clear();
                    var forcedLogout = _services.GetRequiredService<IAuthForcedLogout>();
                    _ = forcedLogout.ForceLogoutAsync(body);
                }
            }
        }

        return response;
    }

    private static bool IsAnonymous(string path)
    {
        if (path.Contains("/api/auth/login", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/api/auth/signup", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.Contains("/api/health", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
