namespace JPL.Api.Models;

internal sealed class MatchSquadSnapshot
{
    public List<Guid> TeamAPlayerIds { get; set; } = [];
    public List<Guid> TeamBPlayerIds { get; set; } = [];
}
