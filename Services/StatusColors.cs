namespace JPL.Services;

public static class StatusColors
{
    public static Color Active { get; } = Color.FromArgb("#15803D");
    public static Color Inactive { get; } = Color.FromArgb("#C62828");

    public static Color For(bool isActive) => isActive ? Active : Inactive;
}
