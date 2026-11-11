using System.Windows.Input;

namespace JPL.Services;

/// <summary>
/// Loading overlay disabled — it blocked input and froze the UI on every tap.
/// Methods remain as no-ops so existing call sites keep compiling.
/// </summary>
public static class AppBusy
{
    public static bool IsBusy => false;

    public static event Action<bool>? Changed
    {
        add { }
        remove { }
    }

    public static IDisposable Begin() => Noop.Instance;

    public static void End()
    {
    }

    public static Task RunAsync(Func<Task> work) => work();

    public static Task WaitForPaintAsync() => Task.CompletedTask;

    public static Task TrackCommandAsync(ICommand? command, object? bindingContext)
        => Task.CompletedTask;

    public static View CreateLayer() => new Grid
    {
        IsVisible = false,
        InputTransparent = true,
        CascadeInputTransparent = true
    };

    public static void AttachToPage(Page page)
    {
    }

    public static void Register(View layer)
    {
    }

    private sealed class Noop : IDisposable
    {
        public static readonly Noop Instance = new();

        public void Dispose()
        {
        }
    }
}

internal sealed class AppBusyRoot : Grid;
