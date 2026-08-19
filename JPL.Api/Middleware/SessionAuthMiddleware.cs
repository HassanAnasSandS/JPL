using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Services;

namespace JPL.Api.Middleware;

/// <summary>
/// Requires Bearer token on all /api/* calls except login + health.
/// Re-checks the user in SQL on every request — deleted / inactive → 401.
/// Token format from login: {opaque}.{userId:N}
/// </summary>
public sealed class SessionAuthMiddleware
{
    private readonly RequestDelegate _next;

    public SessionAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IApiDbFactory dbs)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
            IsAnonymous(path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!TryGetBearerToken(context, out var token) ||
            !TryParseUserId(token, out var userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Authentication required.").ConfigureAwait(false);
            return;
        }

        await using var db = dbs.CreateMaster();
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, context.RequestAborted)
            .ConfigureAwait(false);

        if (user is null || !user.IsActive)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync(
                    user is null
                        ? "Account was deleted."
                        : "Account is deactivated.")
                .ConfigureAwait(false);
            return;
        }

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            },
            authenticationType: "JplSession");
        if (user.LeagueId is Guid leagueId && leagueId != Guid.Empty)
            identity.AddClaim(new Claim("league_id", leagueId.ToString("D")));
        context.User = new ClaimsPrincipal(identity);
        context.Items["JplUserId"] = user.Id;
        context.Items["JplUsername"] = user.Username;
        context.Items["JplRole"] = user.Role.ToString();

        await _next(context).ConfigureAwait(false);
    }

    private static bool IsAnonymous(string path)
    {
        if (path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/health/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool TryGetBearerToken(HttpContext context, out string token)
    {
        token = string.Empty;
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
            return false;

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        token = header[prefix.Length..].Trim();
        return token.Length > 0;
    }

    private static bool TryParseUserId(string token, out Guid userId)
    {
        userId = Guid.Empty;
        var dot = token.LastIndexOf('.');
        if (dot <= 0 || dot >= token.Length - 1)
            return false;

        return Guid.TryParseExact(token[(dot + 1)..], "N", out userId);
    }
}
