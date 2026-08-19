namespace JPL.Api.Models;

/// <summary>Sole account allowed to manage Leagues and Users modules.</summary>
public static class PlatformAdmin
{
    public const string Username = "HassanAnas";

    public static bool Is(string? username)
        => string.Equals(username?.Trim(), Username, StringComparison.OrdinalIgnoreCase);
}
