namespace JPL.Controls;

/// <summary>
/// Full-view photo with pinch, pan, mouse-wheel, and +/- zoom. Resets when <see cref="Source"/> changes.
/// </summary>
public sealed class ZoomableImageView : ContentView
{
    const double MinScale = 1;
    const double MaxScale = 5;
    const double Step = 0.4;

    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source),
        typeof(object),
        typeof(ZoomableImageView),
        propertyChanged: (bindable, _, value) =>
        {
            var view = (ZoomableImageView)bindable;
            view._image.Source = ToSource(value);
            view.ResetZoom();
        });

    readonly Image _image;
    double _scale = 1;
    double _pinchStartScale = 1;
    double _panStartX;
    double _panStartY;
    double _offsetX;
    double _offsetY;

    public ZoomableImageView()
    {
        IsClippedToBounds = true;
        BackgroundColor = Colors.Transparent;

        _image = new Image
        {
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Transparent
        };

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinchUpdated;
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += (_, _) =>
        {
            if (_scale > 1.05)
                ResetZoom();
            else
                ZoomIn();
        };

        GestureRecognizers.Add(pinch);
        GestureRecognizers.Add(pan);
        GestureRecognizers.Add(doubleTap);

        Content = _image;
        HandlerChanged += OnHandlerChanged;
    }

    public object? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    static ImageSource? ToSource(object? value) => value switch
    {
        ImageSource img => img,
        Uri uri => ImageSource.FromUri(uri),
        string s when Uri.TryCreate(s, UriKind.Absolute, out var abs)
            && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps)
            => new UriImageSource { Uri = abs, CachingEnabled = true, CacheValidity = TimeSpan.FromDays(7) },
        string s when !string.IsNullOrWhiteSpace(s) => ImageSource.FromFile(s),
        _ => null
    };

    public void ZoomIn() => ApplyScale(_scale + Step);

    public void ZoomOut() => ApplyScale(_scale - Step);

    public void ResetZoom()
    {
        _scale = 1;
        _offsetX = 0;
        _offsetY = 0;
        _image.Scale = 1;
        _image.TranslationX = 0;
        _image.TranslationY = 0;
    }

    void ApplyScale(double next)
    {
        _scale = Math.Clamp(next, MinScale, MaxScale);
        _image.Scale = _scale;
        if (_scale <= MinScale)
            ResetZoom();
        else
            ClampPan();
    }

    void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _pinchStartScale = _scale;
                break;
            case GestureStatus.Running:
                ApplyScale(_pinchStartScale * e.Scale);
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                ClampPan();
                break;
        }
    }

    void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_scale <= MinScale)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartX = _offsetX;
                _panStartY = _offsetY;
                break;
            case GestureStatus.Running:
                _image.TranslationX = _panStartX + e.TotalX;
                _image.TranslationY = _panStartY + e.TotalY;
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _offsetX = _image.TranslationX;
                _offsetY = _image.TranslationY;
                ClampPan();
                break;
        }
    }

    void ClampPan()
    {
        if (_scale <= MinScale || Width <= 0 || Height <= 0)
        {
            _offsetX = 0;
            _offsetY = 0;
            _image.TranslationX = 0;
            _image.TranslationY = 0;
            return;
        }

        var maxX = Math.Max(0, Width * (_scale - 1) / 2);
        var maxY = Math.Max(0, Height * (_scale - 1) / 2);
        _offsetX = Math.Clamp(_image.TranslationX, -maxX, maxX);
        _offsetY = Math.Clamp(_image.TranslationY, -maxY, maxY);
        _image.TranslationX = _offsetX;
        _image.TranslationY = _offsetY;
    }

    void OnHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement native)
            return;
        native.PointerWheelChanged -= OnWinWheel;
        native.PointerWheelChanged += OnWinWheel;
#endif
    }

#if WINDOWS
    void OnWinWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint((Microsoft.UI.Xaml.UIElement)sender).Properties.MouseWheelDelta;
        if (delta > 0)
            ZoomIn();
        else
            ZoomOut();
        e.Handled = true;
    }
#endif
}
