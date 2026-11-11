namespace JPL;

/// <summary>
/// Tap a label (or the row that wraps a checkbox/switch) to toggle the control.
/// </summary>
public static class ToggleOnTap
{
    public static readonly BindableProperty EnableProperty = BindableProperty.CreateAttached(
        "Enable",
        typeof(bool),
        typeof(ToggleOnTap),
        false,
        propertyChanged: OnEnableChanged);

    private static readonly BindableProperty TapProperty = BindableProperty.CreateAttached(
        "Tap",
        typeof(TapGestureRecognizer),
        typeof(ToggleOnTap),
        null);

    public static bool GetEnable(BindableObject obj) => (bool)obj.GetValue(EnableProperty);

    public static void SetEnable(BindableObject obj, bool value) => obj.SetValue(EnableProperty, value);

    private static void OnEnableChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view)
            return;

        if (newValue is true)
        {
            if (view.GetValue(TapProperty) is TapGestureRecognizer)
                return;

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => Toggle(view);
            view.GestureRecognizers.Add(tap);
            view.SetValue(TapProperty, tap);
            view.Loaded += OnLoaded;
            if (view is Layout layout)
                layout.ChildAdded += OnChildAdded;
            ApplyInputTransparent(view);
            return;
        }

        if (view.GetValue(TapProperty) is TapGestureRecognizer existing)
        {
            view.GestureRecognizers.Remove(existing);
            view.SetValue(TapProperty, null);
        }

        view.Loaded -= OnLoaded;
        if (view is Layout layoutOff)
            layoutOff.ChildAdded -= OnChildAdded;
    }

    private static void OnLoaded(object? sender, EventArgs e)
    {
        if (sender is View view)
            ApplyInputTransparent(view);
    }

    private static void OnChildAdded(object? sender, ElementEventArgs e)
    {
        if (sender is View view)
            ApplyInputTransparent(view);
    }

    private static void ApplyInputTransparent(View host)
    {
        foreach (var toggle in EnumerateSubtree(host))
        {
            if (toggle is CheckBox or Switch)
                toggle.InputTransparent = true;
        }
    }

    private static void Toggle(View host)
    {
        var target = FindToggle(host);
        if (target is CheckBox box && box.IsEnabled)
            box.IsChecked = !box.IsChecked;
        else if (target is Switch sw && sw.IsEnabled)
            sw.IsToggled = !sw.IsToggled;
    }

    private static View? FindToggle(View host)
    {
        foreach (var child in EnumerateSubtree(host))
        {
            if (child is CheckBox or Switch)
                return child;
        }

        if (host.Parent is not IVisualTreeElement parent)
            return null;

        foreach (var sibling in parent.GetVisualChildren())
        {
            if (ReferenceEquals(sibling, host))
                continue;
            if (sibling is CheckBox box)
                return box;
            if (sibling is Switch sw)
                return sw;
        }

        return null;
    }

    private static IEnumerable<View> EnumerateSubtree(View root)
    {
        if (root is not IVisualTreeElement tree)
            yield break;

        foreach (var child in tree.GetVisualChildren())
        {
            if (child is not View view)
                continue;
            yield return view;
            foreach (var nested in EnumerateSubtree(view))
                yield return nested;
        }
    }
}
