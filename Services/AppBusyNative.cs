namespace JPL.Services;

internal static class AppBusyNative
{
    public static void SetVisible(bool visible)
    {
    }

    public static Task WaitTwoFramesAsync() => Task.CompletedTask;
}
