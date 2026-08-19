namespace JPL.Domain;

public sealed class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FullName { get; set; } = string.Empty;

    /// <summary>Inactive players are hidden from team create/update squad pickers.</summary>
    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    public string StatusText => IsActive ? "Active" : "Inactive";

    public string ToggleActiveLabel => IsActive ? "Deactivate" : "Activate";

    /// <summary>Monotonic local version for optimistic sync (no row locks).</summary>
    public long Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;

    public ICollection<TeamPlayer> TeamPlayers { get; set; } = new List<TeamPlayer>();
}
