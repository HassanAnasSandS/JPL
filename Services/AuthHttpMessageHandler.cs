using System.Net;
using System.Net.Http.Headers;
using JPL.Domain;

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

        // League Admin uses SQLite as the working store. Do not let a feature page,
        // background cache refresh, or accidental API fallback reach either server.
        // Only explicit sync endpoints may reach the league API during a sync run.
        if (!path.Contains("/api/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            var auth = _services.GetService<IAuthService>();
            if (auth?.Current?.Role == AppRole.Admin)
            {
                var selectedLeagueId = _services.GetService<ILeagueContext>()?.CurrentLeagueId;
                var canSync = LeagueSyncPolicy.AllowsLocalOfflineSync(auth.Current, selectedLeagueId);
                var isSyncing = _services.GetService<ISyncService>()?.IsSyncing == true;
                if (!IsSyncRequest(path, request.Method, canSync, isSyncing, AdminSyncRequestScope.IsActive))
                    throw new OfflineException("League Admin data is local-first. Run Sync to contact the server.");
            }
        }

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
        catch (Exception ex) when (NetworkUi.IsHttpTimeout(ex))
        {
            // Host reachable but slow — do not call this Offline/unreachable.
            throw new TimeoutException(NetworkUi.SlowServerMessage, ex);
        }
        catch (Exception ex) when (NetworkUi.IsNetworkFailure(ex))
        {
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
        if (path.Contains("/api/auth/login", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.Contains("/api/health", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool IsSyncRequest(string path, HttpMethod method, bool canSync, bool isSyncing, bool isSyncScope)
    {
        if (path.EndsWith("/api/sync", StringComparison.OrdinalIgnoreCase))
            return canSync && method == HttpMethod.Post;

        if (path.Contains("/api/sync/entity-types", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/api/sync/changes", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/api/sync/snapshot", StringComparison.OrdinalIgnoreCase))
            return canSync && isSyncing && method == HttpMethod.Get;

        if (path.EndsWith("/api/scheduler/sync", StringComparison.OrdinalIgnoreCase))
            return canSync && method == HttpMethod.Post;

        // Scheduler and statistics APIs are used only by the explicitly scoped Sync workflow.
        if (canSync && isSyncScope && method == HttpMethod.Get
            && path.Contains("/api/stats/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (canSync && isSyncScope && method == HttpMethod.Get
            && (path.EndsWith("/api/scheduler", StringComparison.OrdinalIgnoreCase)
                || System.Text.RegularExpressions.Regex.IsMatch(
                    path, @"/api/scheduler/[0-9a-fA-F-]{36}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            return true;

        return false;
    }
}

/// <summary>Marks API requests that are part of a Sync workflow, excluding concurrent page requests.</summary>
internal static class AdminSyncRequestScope
{
    private static readonly AsyncLocal<int> Depth = new();
    public static bool IsActive => Depth.Value > 0;

    public static IDisposable Enter()
    {
        var previous = Depth.Value;
        Depth.Value = previous + 1;
        return new Scope(previous);
    }

    private sealed class Scope(int previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Depth.Value = previous;
        }
    }
}
