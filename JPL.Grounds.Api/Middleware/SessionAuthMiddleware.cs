using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using JPL.Grounds.Api.Services;

namespace JPL.Grounds.Api.Middleware;

/// <summary>
/// Same token format as JPL.Api: {opaque}.{userId:N}. Validates against Master Users.
/// </summary>
public sealed class SessionAuthMiddleware
{
    private readonly RequestDelegate _next;

    public SessionAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IGroundsDbFactory dbs)
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

        await using var db = dbs.CreateMasterUsers();
        var user = await db.Users.AsNoTracking()
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
        if (!string.IsNullOrWhiteSpace(user.WhatsAppNumber))
        {
            identity.AddClaim(new Claim("whatsapp", user.WhatsAppNumber));
            context.Items["JplWhatsApp"] = user.WhatsAppNumber;
        }
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

        // Published ground photos — MAUI Image.FromUri cannot send Bearer.
        if (path.Contains("/images/", StringComparison.OrdinalIgnoreCase) &&
            path.StartsWith("/api/grounds/", StringComparison.OrdinalIgnoreCase))
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
