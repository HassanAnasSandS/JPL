namespace JPL.Grounds.Api.Models;

/// <summary>Same gate as JPL.Api — only HassanAnas may create GroundOwner accounts.</summary>
public static class PlatformAdmin
{
    public const string Username = "HassanAnas";

    public static bool Is(string? username)
        => string.Equals(username?.Trim(), Username, StringComparison.OrdinalIgnoreCase);
}
