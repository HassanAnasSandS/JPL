namespace JPL.Controls;

/// <summary>Creates its template only when requested, avoiding hidden view trees at page startup.</summary>
public sealed class DeferredContentView : ContentView
{
    public static readonly BindableProperty ContentTemplateProperty = BindableProperty.Create(
        nameof(ContentTemplate), typeof(DataTemplate), typeof(DeferredContentView), default(DataTemplate),
        propertyChanged: static (bindable, _, _) => ((DeferredContentView)bindable).EnsureContent());

    public static readonly BindableProperty LoadContentProperty = BindableProperty.Create(
        nameof(LoadContent), typeof(bool), typeof(DeferredContentView), false,
        propertyChanged: static (bindable, _, value) =>
        {
            if (value is true)
                ((DeferredContentView)bindable).EnsureContent();
        });

    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    public bool LoadContent
    {
        get => (bool)GetValue(LoadContentProperty);
        set => SetValue(LoadContentProperty, value);
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (Content is not null)
            Content.BindingContext = BindingContext;
        else
            EnsureContent();
    }

    private void EnsureContent()
    {
        if (!LoadContent || Content is not null || ContentTemplate?.CreateContent() is not View view)
            return;

        view.BindingContext = BindingContext;
        Content = view;
    }
}
