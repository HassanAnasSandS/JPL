using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace JPL.Services;

internal static class AppBusyClicks
{
    private static readonly BindableProperty TrackedProperty =
        BindableProperty.CreateAttached("Tracked", typeof(bool), typeof(AppBusyClicks), false);

    public static void Attach(Button button)
    {
        if (IsTracked(button) || IsSkipped(button))
            return;
        SetTracked(button);
        WrapCommand(button);
        button.PropertyChanged += OnButtonPropertyChanged;
        button.Clicked += OnButtonClicked;
    }

    public static void Attach(ImageButton button)
    {
        if (IsTracked(button) || IsSkipped(button))
            return;
        SetTracked(button);
        WrapCommand(button);
        button.PropertyChanged += OnImagePropertyChanged;
        button.Clicked += OnImageClicked;
    }

    public static void AttachTap(TapGestureRecognizer tap)
    {
        if (tap.GetValue(TrackedProperty) is true)
            return;
        tap.SetValue(TrackedProperty, true);
        WrapTapCommand(tap);
        tap.PropertyChanged += OnTapPropertyChanged;
        tap.Tapped += OnTapped;
    }

    private static bool IsTracked(BindableObject obj)
        => obj.GetValue(TrackedProperty) is true;

    private static void SetTracked(BindableObject obj)
        => obj.SetValue(TrackedProperty, true);

    private static void OnTapPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is TapGestureRecognizer tap && e.PropertyName == nameof(TapGestureRecognizer.Command))
            WrapTapCommand(tap);
    }

    private static void WrapTapCommand(TapGestureRecognizer tap)
    {
        if (tap.Command is null or BusyDeferCommand)
            return;
        tap.Command = new BusyDeferCommand(tap.Command);
    }

    private static bool IsSkipped(VisualElement element)
        => string.Equals(element.StyleId, "SkipBusy", StringComparison.Ordinal);

    private static void OnButtonPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is Button button && e.PropertyName == nameof(Button.Command))
            WrapCommand(button);
    }

    private static void OnImagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is ImageButton button && e.PropertyName == nameof(ImageButton.Command))
            WrapCommand(button);
    }

    private static void WrapCommand(Button button)
    {
        if (button.Command is null or BusyDeferCommand)
            return;
        button.Command = new BusyDeferCommand(button.Command);
    }

    private static void WrapCommand(ImageButton button)
    {
        if (button.Command is null or BusyDeferCommand)
            return;
        button.Command = new BusyDeferCommand(button.Command);
    }

    private static async void OnButtonClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button)
            return;
        if (button.Command is BusyDeferCommand)
            return;
        await TrackAsync(button.Command, button.BindingContext).ConfigureAwait(true);
    }

    private static async void OnImageClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton button)
            return;
        if (button.Command is BusyDeferCommand)
            return;
        await TrackAsync(button.Command, button.BindingContext).ConfigureAwait(true);
    }

    private static async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TapGestureRecognizer tap)
            return;
        if (tap.Command is BusyDeferCommand)
            return;
        await TrackAsync(tap.Command, null).ConfigureAwait(true);
    }

    private static async Task TrackAsync(ICommand? command, object? bindingContext)
    {
        using (AppBusy.Begin())
        {
            await AppBusy.WaitForPaintAsync().ConfigureAwait(true);
            await AppBusy.TrackCommandAsync(command, bindingContext).ConfigureAwait(true);
        }
    }
}

internal sealed class BusyDeferCommand : ICommand
{
    private readonly ICommand _inner;

    public BusyDeferCommand(ICommand inner) => _inner = inner;

    public bool CanExecute(object? parameter) => _inner.CanExecute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add => _inner.CanExecuteChanged += value;
        remove => _inner.CanExecuteChanged -= value;
    }

    public async void Execute(object? parameter)
    {
        await AppBusy.RunAsync(async () =>
        {
            _inner.Execute(parameter);
            await AppBusy.TrackCommandAsync(_inner, null).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }
}
