using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace JPL.Services;

/// <summary>
/// App-wide loading overlay. Blocks all input until the click's work finishes.
/// </summary>
public static class AppBusy
{
    private static int _count;
    private static readonly List<WeakReference<View>> Layers = [];

    public static bool IsBusy => Volatile.Read(ref _count) > 0;

    public static event Action<bool>? Changed;

    public static IDisposable Begin()
    {
        var n = Interlocked.Increment(ref _count);
        if (n == 1)
            Show(true);
        return new Scope();
    }

    public static void End()
    {
        var n = Interlocked.Decrement(ref _count);
        if (n == 0)
            Show(false);
        else if (n < 0)
            Interlocked.Exchange(ref _count, 0);
    }

    public static async Task RunAsync(Func<Task> work)
    {
        using (Begin())
        {
            // Nested overlay (click already painted) must not wait on frames —
            // Window.Page swap detaches the Android overlay and Post() never runs.
            if (Volatile.Read(ref _count) <= 1)
                await WaitForPaintAsync().ConfigureAwait(true);
            await work().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Yield until the overlay has actually been drawn. Setting IsVisible in the
    /// same UI turn as GoToAsync never paints — the next screen then flashes the spinner.
    /// Must never hang: a detached Android view Post() never runs after Window.Page swap.
    /// </summary>
    public static async Task WaitForPaintAsync()
    {
        async Task PulseAsync()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null)
            {
                await DispatchOnceAsync(dispatcher).ConfigureAwait(true);
                await DispatchOnceAsync(dispatcher).ConfigureAwait(true);
            }

            await AppBusyNative.WaitTwoFramesAsync().ConfigureAwait(true);
            await Task.Delay(16).ConfigureAwait(true);
        }

        var pulse = MainThread.IsMainThread
            ? PulseAsync()
            : MainThread.InvokeOnMainThreadAsync(PulseAsync);
        var timeout = Task.Delay(400);
        await Task.WhenAny(pulse, timeout).ConfigureAwait(true);
    }

    private static Task DispatchOnceAsync(IDispatcher dispatcher)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Dispatch(() => tcs.TrySetResult());
        return tcs.Task;
    }

    public static async Task TrackCommandAsync(ICommand? command, object? bindingContext)
    {
        if (command is IAsyncRelayCommand asyncCmd)
        {
            await WaitAsync(asyncCmd).ConfigureAwait(true);
            return;
        }

        await Task.Yield();
        await WaitRunningOnContextAsync(bindingContext).ConfigureAwait(true);
    }

    public static View CreateLayer()
    {
        var spinner = new ActivityIndicator
        {
            Color = Color.FromArgb("#B6FF3B"),
            WidthRequest = 48,
            HeightRequest = 48,
            HorizontalOptions = LayoutOptions.Center
        };
        var grid = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC0D1B2A"),
            ZIndex = 999,
            IsVisible = false,
            InputTransparent = false,
            CascadeInputTransparent = false
        };
        grid.Add(new VerticalStackLayout
        {
            Spacing = 16,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Padding = 28,
            Children =
            {
                spinner,
                new Label
                {
                    Text = "Please wait…",
                    TextColor = Color.FromArgb("#E8F5D8"),
                    FontSize = 15,
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        });
        grid.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == VisualElement.IsVisibleProperty.PropertyName)
                spinner.IsRunning = grid.IsVisible;
        };
        Register(grid);
        if (IsBusy)
            grid.IsVisible = true;
        return grid;
    }

    public static void AttachToPage(Page page)
    {
        if (page is not ContentPage content || page is JPL.Pages.LoginPage)
            return;
        if (content.Content is AppBusyRoot)
            return;

        var inner = content.Content;
        if (inner is null)
            return;

        var root = new AppBusyRoot
        {
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill
        };
        root.Add(inner);
        var layer = CreateLayer();
        root.Add(layer);
        content.Content = root;
        WatchGestures(root);
    }

    private static void SetSpinnerRunning(View view, bool running)
    {
        if (view is not Grid grid)
            return;
        foreach (var child in grid.Children)
        {
            if (child is not Layout layout)
                continue;
            foreach (var nested in layout.Children)
            {
                if (nested is ActivityIndicator spinner)
                    spinner.IsRunning = running;
            }
        }
    }

    public static void Register(View layer)
    {
        lock (Layers)
            Layers.Add(new WeakReference<View>(layer));
    }

    private static void Show(bool visible)
    {
        void Apply()
        {
            AppBusyNative.SetVisible(visible);
            lock (Layers)
            {
                for (var i = Layers.Count - 1; i >= 0; i--)
                {
                    if (!Layers[i].TryGetTarget(out var view) || view.Handler is null && view.Parent is null)
                    {
                        Layers.RemoveAt(i);
                        continue;
                    }
                    view.IsVisible = visible;
                    SetSpinnerRunning(view, visible);
                }
            }
            Changed?.Invoke(visible);
        }

        if (MainThread.IsMainThread)
            Apply();
        else
            MainThread.BeginInvokeOnMainThread(Apply);
    }

    private static async Task WaitAsync(IAsyncRelayCommand command)
    {
        var task = command.ExecutionTask;
        if (task is { IsCompleted: false })
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(20));
            await Task.WhenAny(task, timeout).ConfigureAwait(true);
            return;
        }

        var n = 0;
        while (command.IsRunning && n++ < 800)
            await Task.Delay(16).ConfigureAwait(true);
    }

    private static async Task WaitRunningOnContextAsync(object? bindingContext)
    {
        if (bindingContext is null)
            return;

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var running = RunningCommands(bindingContext).ToList();
            if (running.Count > 0)
            {
                await Task.WhenAll(running.Select(c => c.ExecutionTask ?? Task.CompletedTask))
                    .ConfigureAwait(true);
                return;
            }
            await Task.Delay(16).ConfigureAwait(true);
        }
    }

    private static IEnumerable<IAsyncRelayCommand> RunningCommands(object instance)
    {
        foreach (var prop in instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!typeof(IAsyncRelayCommand).IsAssignableFrom(prop.PropertyType) || !prop.CanRead)
                continue;
            IAsyncRelayCommand? command = null;
            try
            {
                command = prop.GetValue(instance) as IAsyncRelayCommand;
            }
            catch
            {
                /* ignore */
            }
            if (command?.IsRunning == true)
                yield return command;
        }
    }

    private static readonly BindableProperty GesturesWatchedProperty =
        BindableProperty.CreateAttached("GesturesWatched", typeof(bool), typeof(AppBusy), false);

    private static void WatchGestures(Element root)
    {
        if (root.GetValue(GesturesWatchedProperty) is true)
            return;
        root.SetValue(GesturesWatchedProperty, true);

        AttachGestures(root);
        root.ChildAdded += (_, args) => WatchGestures(args.Element);

        // ChildAdded is not raised for views that already exist (page content,
        // BindableLayout items). Walk them or league-card taps never get the overlay.
        if (root is IVisualTreeElement tree)
        {
            foreach (var child in tree.GetVisualChildren())
            {
                if (child is Element el)
                    WatchGestures(el);
            }
        }
    }

    private static void AttachGestures(Element element)
    {
        if (element is not View view)
            return;
        foreach (var tap in view.GestureRecognizers.OfType<TapGestureRecognizer>())
            AppBusyClicks.AttachTap(tap);
    }

    private sealed class Scope : IDisposable
    {
        private int _once;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _once, 1) == 1)
                return;
            End();
        }
    }
}

internal sealed class AppBusyRoot : Grid;
