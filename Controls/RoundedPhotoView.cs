using Microsoft.Maui.Controls.Shapes;

namespace JPL.Controls;

/// <summary>
/// Team-list tile: 56×56 rounded square, photo fills the tile (AspectFill).
/// </summary>
public sealed class RoundedPhotoView : ContentView
{
    public const double TileSize = 56;
    public const double Corner = 12;

    public static readonly BindableProperty PhotoProperty = BindableProperty.Create(
        nameof(Photo), typeof(ImageSource), typeof(RoundedPhotoView),
        propertyChanged: OnPhotoChanged);

    public static readonly BindableProperty InitialsProperty = BindableProperty.Create(
        nameof(Initials), typeof(string), typeof(RoundedPhotoView),
        propertyChanged: (bindable, _, value) =>
            ((RoundedPhotoView)bindable)._initials.Text = value as string ?? string.Empty);

    public static readonly BindableProperty TappedCommandProperty = BindableProperty.Create(
        nameof(TappedCommand), typeof(System.Windows.Input.ICommand), typeof(RoundedPhotoView));

    readonly Label _initials;
    readonly Image _image;
    readonly AbsoluteLayout _photoHost;

    public RoundedPhotoView()
    {
        WidthRequest = TileSize;
        HeightRequest = TileSize;
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Center;

        var root = new Grid
        {
            WidthRequest = TileSize,
            HeightRequest = TileSize,
            Clip = new RoundRectangleGeometry
            {
                CornerRadius = new CornerRadius(Corner),
                Rect = new Rect(0, 0, TileSize, TileSize)
            }
        };

        var background = new BoxView();
        background.SetDynamicResource(BoxView.ColorProperty, "Primary");

        _initials = new Label
        {
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        _image = new Image
        {
            Aspect = Aspect.AspectFill,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsVisible = false,
            InputTransparent = true
        };
        _image.HandlerChanged += OnImageHandlerChanged;

        _photoHost = new AbsoluteLayout
        {
            WidthRequest = TileSize,
            HeightRequest = TileSize,
            IsVisible = false
        };
        AbsoluteLayout.SetLayoutBounds(_image, new Rect(0, 0, TileSize, TileSize));
        _photoHost.Children.Add(_image);

        root.Add(background);
        root.Add(_initials);
        root.Add(_photoHost);
        Content = root;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (Photo is null)
                return;
            if (TappedCommand?.CanExecute(null) == true)
                TappedCommand.Execute(null);
        };
        GestureRecognizers.Add(tap);
    }

    public ImageSource? Photo
    {
        get => (ImageSource?)GetValue(PhotoProperty);
        set => SetValue(PhotoProperty, value);
    }

    public string? Initials
    {
        get => (string?)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    public System.Windows.Input.ICommand? TappedCommand
    {
        get => (System.Windows.Input.ICommand?)GetValue(TappedCommandProperty);
        set => SetValue(TappedCommandProperty, value);
    }

    static void OnPhotoChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (RoundedPhotoView)bindable;
        var source = newValue as ImageSource;
        var hasPhoto = source is not null;
        view._image.Source = source;
        view._image.IsVisible = hasPhoto;
        view._photoHost.IsVisible = hasPhoto;
        view._initials.IsVisible = !hasPhoto;
    }

    void OnImageHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        if (_image.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Image native)
            native.Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill;
#endif
    }
}
