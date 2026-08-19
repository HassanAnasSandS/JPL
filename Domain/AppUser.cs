namespace JPL.Domain;

public enum AppRole : byte
{
    User = 0,
    Admin = 1,
    SuperAdmin = 2
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public AppRole Role { get; set; } = AppRole.User;

    /// <summary>Null = public viewer or SuperAdmin (not bound to one league).</summary>
    public Guid? LeagueId { get; set; }

    /// <summary>Display-only; not stored on AppUser.</summary>
    public string LeagueName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
}
