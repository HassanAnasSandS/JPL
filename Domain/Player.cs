namespace JPL.Domain;

public sealed class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FullName { get; set; } = string.Empty;

    /// <summary>Public nickname shown on the shareable bio page.</summary>
    public string? NickName { get; set; }
    /// <summary>Batter / Bowler / All-rounder / Wicket-keeper.</summary>
    public string? PlayingRole { get; set; }
    public string? BattingStyle { get; set; }
    public string? BowlingStyle { get; set; }
    /// <summary>Short public bio. Never put phone numbers here.</summary>
    public string? Bio { get; set; }

    /// <summary>Inactive players are hidden from team create/update squad pickers.</summary>
    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    /// <summary>Relative filename on the API host. Null = no photo. Not used by local SQLite CRUD.</summary>
    public string? ImagePath { get; set; }

    public string StatusText => IsActive ? "Active" : "Inactive";

    public string ToggleActiveLabel => IsActive ? "Deactivate" : "Activate";

    /// <summary>Monotonic local version for optimistic sync (no row locks).</summary>
    public long Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;

    public ICollection<TeamPlayer> TeamPlayers { get; set; } = new List<TeamPlayer>();
}
