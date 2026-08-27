namespace JPL.Domain;

public enum AppRole : byte
{
    User = 0,
    Admin = 1,
    SuperAdmin = 2,
    GroundOwner = 3,
    PublicUser = 4,
    Guest = 5
}

public static class AppRoleLabels
{
    public static string Of(AppRole role) => role switch
    {
        AppRole.SuperAdmin => "Super Admin",
        AppRole.Admin => "League Admin",
        AppRole.User => "League User",
        AppRole.GroundOwner => "Ground Owner",
        AppRole.PublicUser => "Public User",
        AppRole.Guest => "Guest",
        _ => role.ToString()
    };

    public static bool TryParseManaged(string? text, out AppRole role)
    {
        role = AppRole.User;
        var t = text?.Trim() ?? string.Empty;
        if (t.Length == 0)
            return false;
        if (t.Contains("League Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, nameof(AppRole.Admin), StringComparison.OrdinalIgnoreCase))
        {
            role = AppRole.Admin;
            return true;
        }
        if (t.Contains("Ground Owner", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, nameof(AppRole.GroundOwner), StringComparison.OrdinalIgnoreCase))
        {
            role = AppRole.GroundOwner;
            return true;
        }
        if (t.Contains("Guest", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, nameof(AppRole.Guest), StringComparison.OrdinalIgnoreCase))
        {
            role = AppRole.Guest;
            return true;
        }
        if (t.Contains("Public", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, nameof(AppRole.PublicUser), StringComparison.OrdinalIgnoreCase))
        {
            role = AppRole.PublicUser;
            return true;
        }
        if (t.Contains("League User", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, nameof(AppRole.User), StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "User", StringComparison.OrdinalIgnoreCase))
        {
            role = AppRole.User;
            return true;
        }
        return Enum.TryParse(t, ignoreCase: true, out role)
               && role is AppRole.Admin or AppRole.User or AppRole.GroundOwner or AppRole.PublicUser or AppRole.Guest;
    }

    public static bool IsGuestUsername(string? username)
        => string.Equals(username?.Trim(), "user", StringComparison.OrdinalIgnoreCase);

    public static bool SkipsLeague(AppRole role)
        => role is AppRole.GroundOwner or AppRole.PublicUser or AppRole.Guest or AppRole.SuperAdmin;
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public AppRole Role { get; set; } = AppRole.User;

    public Guid? LeagueId { get; set; }

    /// <summary>Required for PublicUser signup; optional on other roles.</summary>
    public string? WhatsAppNumber { get; set; }

    /// <summary>Display-only; not stored on AppUser.</summary>
    public string LeagueName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
}
