using System.Security.Claims;
using JPL.Grounds.Api.Models;

namespace JPL.Grounds.Api.Services;

internal static class GroundsCaller
{
    public static Guid UserId(HttpContext http)
    {
        if (http.Items.TryGetValue("JplUserId", out var idObj) && idObj is Guid id)
            return id;
        var text = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(text, out var parsed) ? parsed : Guid.Empty;
    }

    public static string Username(HttpContext http)
    {
        if (http.Items.TryGetValue("JplUsername", out var nameObj) && nameObj is string n && n.Length > 0)
            return n;
        return http.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
    }

    public static string? WhatsAppNumber(HttpContext http)
    {
        if (http.Items.TryGetValue("JplWhatsApp", out var waObj) && waObj is string wa && wa.Length > 0)
            return wa;
        var claim = http.User.FindFirstValue("whatsapp");
        return string.IsNullOrWhiteSpace(claim) ? null : claim;
    }

    public static string Role(HttpContext http)
    {
        if (http.Items.TryGetValue("JplRole", out var roleObj) && roleObj is string r && r.Length > 0)
            return r;
        return http.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
    }

    public static bool IsGuest(HttpContext http)
        => string.Equals(Role(http), nameof(AppRole.Guest), StringComparison.OrdinalIgnoreCase)
           || string.Equals(Username(http), "user", StringComparison.OrdinalIgnoreCase);

    public static bool IsGroundOwner(HttpContext http)
        => string.Equals(Role(http), nameof(AppRole.GroundOwner), StringComparison.OrdinalIgnoreCase);

    public static bool IsSuperAdmin(HttpContext http)
        => string.Equals(Role(http), nameof(AppRole.SuperAdmin), StringComparison.OrdinalIgnoreCase);

    /// <summary>Ground owners only see their own rows. SuperAdmin may see every owner.</summary>
    public static bool MustScopeToOwnGrounds(HttpContext http)
        => IsGroundOwner(http) && !IsSuperAdmin(http);

    public static IQueryable<Ground> WhereOwnedByCaller(IQueryable<Ground> query, HttpContext http)
    {
        if (!MustScopeToOwnGrounds(http))
            return query;
        var id = UserId(http);
        if (id == Guid.Empty)
            return query.Where(g => false);
        return query.Where(g => g.OwnerUserId == id);
    }

    public static bool CanViewGround(HttpContext http, Ground ground)
    {
        if (MustScopeToOwnGrounds(http))
            return UserId(http) != Guid.Empty && ground.OwnerUserId == UserId(http);
        if (ground.ListingStatus == GroundListingStatus.Published)
            return true;
        return CanManageGround(http, ground.OwnerUserId);
    }

    public static bool CanManageGround(HttpContext http, Guid ownerUserId)
        => IsSuperAdmin(http) || (IsGroundOwner(http) && UserId(http) != Guid.Empty && UserId(http) == ownerUserId);
}
