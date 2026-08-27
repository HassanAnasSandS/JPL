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
        button.Clicked += OnButtonClicked;
    }

    public static void Attach(ImageButton button)
    {
        if (IsTracked(button) || IsSkipped(button))
            return;
        SetTracked(button);
        button.Clicked += OnImageClicked;
    }

    public static void AttachTap(TapGestureRecognizer tap)
    {
        if (tap.GetValue(TrackedProperty) is true)
            return;
        tap.SetValue(TrackedProperty, true);
        tap.Tapped += OnTapped;
    }

    private static bool IsTracked(BindableObject obj)
        => obj.GetValue(TrackedProperty) is true;

    private static void SetTracked(BindableObject obj)
        => obj.SetValue(TrackedProperty, true);

    private static bool IsSkipped(VisualElement element)
        => string.Equals(element.StyleId, "SkipBusy", StringComparison.Ordinal);

    private static async void OnButtonClicked(object? sender, EventArgs e)
    {
        if (sender is Button button)
            await TrackAsync(button.Command, button.BindingContext).ConfigureAwait(true);
    }

    private static async void OnImageClicked(object? sender, EventArgs e)
    {
        if (sender is ImageButton button)
            await TrackAsync(button.Command, button.BindingContext).ConfigureAwait(true);
    }

    private static async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TapGestureRecognizer tap)
            return;
        await TrackAsync(tap.Command, null).ConfigureAwait(true);
    }

    private static async Task TrackAsync(ICommand? command, object? bindingContext)
    {
        using (AppBusy.Begin())
            await AppBusy.TrackCommandAsync(command, bindingContext).ConfigureAwait(true);
    }
}
