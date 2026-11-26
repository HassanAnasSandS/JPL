using JPL.Api.Models;
using JPL.Api.Services;

namespace JPL.Api.Middleware;

/// <summary>
/// League Admin may mutate only their bound (home) league.
/// SuperAdmin may mutate platform paths only (users + league shells) — not league sports data.
/// Everyone else is read-only on mutating league APIs.
/// </summary>
public sealed class LeagueWriteGuardMiddleware
{
    private readonly RequestDelegate _next;

    public LeagueWriteGuardMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsMutating(context.Request.Method) || !IsApi(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (IsAnonymous(path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var role = context.Items.TryGetValue("JplRole", out var roleObj) && roleObj is string r
            ? r
            : context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;

        if (string.Equals(role, nameof(AppRole.SuperAdmin), StringComparison.OrdinalIgnoreCase))
        {
            if (IsPlatformManagementPath(path))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("SuperAdmin manages users and leagues only.")
                .ConfigureAwait(false);
            return;
        }

        if (IsAuctionApi(path)
            && (string.Equals(role, nameof(AppRole.Admin), StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, nameof(AppRole.User), StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (string.Equals(role, nameof(AppRole.Admin), StringComparison.OrdinalIgnoreCase)
            && IsHomeLeagueRequest(context))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Only the league admin can change this league.")
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Users + league directory records (create / edit / activate / delete).
    /// /api/leagues is exact or "/api/leagues/..." — never league sports data.
    /// </summary>
    private static bool IsPlatformManagementPath(string path)
        => path.StartsWith("/api/users", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
           || path.Equals("/api/leagues", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/api/leagues/", StringComparison.OrdinalIgnoreCase);

    private static bool IsHomeLeagueRequest(HttpContext context)
    {
        if (!context.Items.TryGetValue("JplHomeLeagueId", out var homeObj) || homeObj is not Guid home
            || home == Guid.Empty)
            return false;

        if (context.Request.Headers.TryGetValue(ApiLeagueScope.HeaderName, out var header)
            && Guid.TryParse(header.ToString(), out var selected)
            && selected != Guid.Empty)
            return selected == home;

        return true;
    }

    private static bool IsAuctionApi(string path)
        => path.StartsWith("/api/auctions", StringComparison.OrdinalIgnoreCase);

    private static bool IsMutating(string method)
        => HttpMethods.IsPost(method)
           || HttpMethods.IsPut(method)
           || HttpMethods.IsPatch(method)
           || HttpMethods.IsDelete(method);

    private static bool IsApi(PathString path)
        => path.StartsWithSegments("/api");

    private static bool IsAnonymous(string path)
        => path.Equals("/api/health", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/api/health/", StringComparison.OrdinalIgnoreCase)
           || path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/api/public/", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/api/scheduler", StringComparison.OrdinalIgnoreCase);
}
