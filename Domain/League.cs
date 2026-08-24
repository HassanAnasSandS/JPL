using System.ComponentModel.DataAnnotations.Schema;

namespace JPL.Domain;

public interface ILeagueScoped
{
    Guid LeagueId { get; set; }
}

public static class LeagueIds
{
    /// <summary>Stable id so existing JPL data migrates the same on every device / API.</summary>
    public static readonly Guid Jpl = Guid.Parse("b1000001-0000-4000-8000-000000000001");
}

public sealed class League
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public byte[]? ImageBytes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;

    [NotMapped]
    public int LiveMatchCount { get; set; }

    [NotMapped]
    public int LiveNetsCount { get; set; }

    [NotMapped]
    public bool HasLive => LiveMatchCount > 0;
}
