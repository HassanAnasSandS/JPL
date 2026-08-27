namespace JPL.Controls;

/// <summary>Chip/tab button that flashes gold on press, then restores style + selected trigger.</summary>
public class TabChipButton : Button
{
    public TabChipButton()
    {
        Pressed += (_, _) => ApplyPress();
        Released += (_, _) => Restore();
        Unfocused += (_, _) => Restore();
        HandlerChanged += OnHandlerChanged;
    }

    private void ApplyPress()
    {
        if (!IsEnabled)
            return;
        BackgroundColor = Color.FromArgb("#D4AF37");
        TextColor = Color.FromArgb("#0D1B2A");
        Scale = 0.96;
    }

    private void Restore()
    {
        void Apply()
        {
            ClearValue(BackgroundColorProperty);
            ClearValue(TextColorProperty);
            Scale = 1;
        }

        if (Dispatcher.IsDispatchRequired)
            Dispatcher.Dispatch(Apply);
        else
            Apply();
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.Button native)
            return;
        native.PointerPressed -= OnWinPointerPressed;
        native.PointerReleased -= OnWinPointerReleased;
        native.PointerCanceled -= OnWinPointerCanceled;
        native.PointerCaptureLost -= OnWinCaptureLost;
        native.PointerPressed += OnWinPointerPressed;
        native.PointerReleased += OnWinPointerReleased;
        native.PointerCanceled += OnWinPointerCanceled;
        native.PointerCaptureLost += OnWinCaptureLost;
#endif
    }

#if WINDOWS
    private void OnWinPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => ApplyPress();

    private void OnWinPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => Restore();

    private void OnWinPointerCanceled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => Restore();

    private void OnWinCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => Restore();
#endif
}
