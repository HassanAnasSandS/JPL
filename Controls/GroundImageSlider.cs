using System.Windows.Input;
using JPL.Services;
using Microsoft.Maui.Controls.Shapes;

namespace JPL.Controls;

/// <summary>
/// Horizontal ground-photo slider with arrows, dots, thumbnails, swipe, and tap-to-open.
/// </summary>
public sealed class GroundImageSlider : ContentView
{
    public static readonly BindableProperty ImagesProperty = BindableProperty.Create(
        nameof(Images),
        typeof(IReadOnlyList<GroundImageDto>),
        typeof(GroundImageSlider),
        propertyChanged: (b, _, _) => ((GroundImageSlider)b).Rebuild());

    public static readonly BindableProperty PositionProperty = BindableProperty.Create(
        nameof(Position),
        typeof(int),
        typeof(GroundImageSlider),
        0,
        BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((GroundImageSlider)b).ShowCurrent());

    public static readonly BindableProperty OpenCommandProperty = BindableProperty.Create(
        nameof(OpenCommand),
        typeof(ICommand),
        typeof(GroundImageSlider));

    static readonly Color Overlay = Color.FromArgb("#E60D1B2A");
    static readonly Color Gold = Color.FromArgb("#D4AF37");

    readonly Image _image;
    readonly Label _counter;
    readonly Border _prev;
    readonly Border _next;
    readonly HorizontalStackLayout _dots;
    readonly HorizontalStackLayout _thumbs;
    readonly ScrollView _thumbScroll;
    bool _ignoreTap;
    double _panX;

    public GroundImageSlider()
    {
        _image = new Image
        {
            Aspect = Aspect.AspectFill,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Color.FromArgb("#152033")
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (_ignoreTap)
            {
                _ignoreTap = false;
                return;
            }

            Open();
        };
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPan;
        _image.GestureRecognizers.Add(tap);
        _image.GestureRecognizers.Add(pan);
        _image.HandlerChanged += (_, _) => HookWindowsPointer();

        _counter = new Label
        {
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        };

        _prev = MakeChevron("‹", new Thickness(10, 0, 0, 0), LayoutOptions.Start, Prev);
        _next = MakeChevron("›", new Thickness(0, 0, 10, 0), LayoutOptions.End, Next);

        _dots = new HorizontalStackLayout
        {
            Spacing = 5,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 0, 10),
            InputTransparent = false
        };

        var stage = new Grid { HeightRequest = 240, BackgroundColor = Color.FromArgb("#152033") };
        stage.Children.Add(_image);
        stage.Children.Add(_prev);
        stage.Children.Add(_next);
        stage.Children.Add(new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Overlay,
            Padding = new Thickness(8, 4),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 10, 10, 0),
            InputTransparent = true,
            Content = _counter
        });
        stage.Children.Add(_dots);

        _thumbs = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(0, 2) };
        _thumbScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            HeightRequest = 70,
            Content = _thumbs
        };

        Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                stage,
                _thumbScroll,
                new Label
                {
                    Text = "Swipe or tap a thumbnail · tap photo to zoom",
                    FontSize = 11,
                    TextColor = Color.FromArgb("#5A6A7A")
                }
            }
        };
    }

    public IReadOnlyList<GroundImageDto>? Images
    {
        get => (IReadOnlyList<GroundImageDto>?)GetValue(ImagesProperty);
        set => SetValue(ImagesProperty, value);
    }

    public int Position
    {
        get => (int)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public ICommand? OpenCommand
    {
        get => (ICommand?)GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }

    void Rebuild()
    {
        var list = Images ?? [];
        _thumbScroll.IsVisible = list.Count > 1;
        _prev.IsVisible = list.Count > 1;
        _next.IsVisible = list.Count > 1;
        if (list.Count == 0)
        {
            _image.Source = null;
            _dots.Children.Clear();
            _thumbs.Children.Clear();
            _counter.Text = string.Empty;
            return;
        }

        var pos = Math.Clamp(Position, 0, list.Count - 1);
        if (pos != Position)
            Position = pos;

        BuildDots(list.Count);
        BuildThumbs(list);
        ShowCurrent();
    }

    void ShowCurrent()
    {
        var list = Images ?? [];
        if (list.Count == 0)
            return;

        var index = Math.Clamp(Position, 0, list.Count - 1);
        _image.Source = ToSource(list[index].Url);
        _counter.Text = $"{index + 1} / {list.Count}";
        _counter.IsVisible = true;

        for (var i = 0; i < _dots.Children.Count; i++)
        {
            var dot = _dots.Children[i] is Grid wrap && wrap.Children.Count > 0
                ? wrap.Children[0] as BoxView
                : _dots.Children[i] as BoxView;
            if (dot is null)
                continue;
            dot.Color = i == index ? Gold : Color.FromArgb("#66FFFFFF");
            dot.WidthRequest = i == index ? 18 : 8;
        }

        for (var i = 0; i < _thumbs.Children.Count; i++)
        {
            if (_thumbs.Children[i] is Border border)
                border.Stroke = i == index ? Gold : Colors.Transparent;
        }
    }

    void BuildDots(int count)
    {
        _dots.Children.Clear();
        _dots.IsVisible = count > 1;
        for (var i = 0; i < count; i++)
        {
            var index = i;
            var dot = new BoxView
            {
                HeightRequest = 8,
                WidthRequest = 8,
                CornerRadius = 4,
                Color = Color.FromArgb("#66FFFFFF"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            var hit = new Grid { Padding = new Thickness(6, 8), Children = { dot } };
            hit.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => Position = index)
            });
            _dots.Children.Add(hit);
        }
    }

    void BuildThumbs(IReadOnlyList<GroundImageDto> list)
    {
        _thumbs.Children.Clear();
        for (var i = 0; i < list.Count; i++)
        {
            var index = i;
            var border = new Border
            {
                StrokeThickness = 2,
                Stroke = Colors.Transparent,
                BackgroundColor = Color.FromArgb("#152033"),
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = 0,
                WidthRequest = 76,
                HeightRequest = 56,
                Content = new Image
                {
                    Source = ToSource(list[i].Url),
                    Aspect = Aspect.AspectFill
                }
            };
            border.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => Position = index)
            });
            _thumbs.Children.Add(border);
        }
    }

    void Prev()
    {
        var count = Images?.Count ?? 0;
        if (count == 0)
            return;
        Position = (Position - 1 + count) % count;
    }

    void Next()
    {
        var count = Images?.Count ?? 0;
        if (count == 0)
            return;
        Position = (Position + 1) % count;
    }

    void Open()
    {
        if (OpenCommand?.CanExecute(null) == true)
            OpenCommand.Execute(null);
    }

    void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panX = 0;
                break;
            case GestureStatus.Running:
                _panX = e.TotalX;
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_panX <= -64)
                {
                    _ignoreTap = true;
                    Next();
                }
                else if (_panX >= 64)
                {
                    _ignoreTap = true;
                    Prev();
                }

                _panX = 0;
                break;
        }
    }

    static Border MakeChevron(string text, Thickness margin, LayoutOptions hAlign, Action onTap)
    {
        var label = new Label
        {
            Text = text,
            TextColor = Colors.White,
            FontSize = 30,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        var border = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Overlay,
            WidthRequest = 46,
            HeightRequest = 46,
            StrokeShape = new RoundRectangle { CornerRadius = 23 },
            Padding = 0,
            Margin = margin,
            HorizontalOptions = hAlign,
            VerticalOptions = LayoutOptions.Center,
            Content = label,
            ZIndex = 4
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(onTap) });
        return border;
    }

    static ImageSource? ToSource(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return new UriImageSource { Uri = uri, CachingEnabled = true, CacheValidity = TimeSpan.FromDays(7) };
        return ImageSource.FromFile(url);
    }

    void HookWindowsPointer()
    {
#if WINDOWS
        if (_image.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement native)
            return;
        native.PointerPressed -= OnWinPressed;
        native.PointerReleased -= OnWinReleased;
        native.PointerPressed += OnWinPressed;
        native.PointerReleased += OnWinReleased;
#endif
    }

#if WINDOWS
    double _winX;

    void OnWinPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => _winX = e.GetCurrentPoint((Microsoft.UI.Xaml.UIElement)sender).Position.X;

    void OnWinReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var x = e.GetCurrentPoint((Microsoft.UI.Xaml.UIElement)sender).Position.X;
        var dx = x - _winX;
        if (dx <= -64)
        {
            _ignoreTap = true;
            Next();
            e.Handled = true;
        }
        else if (dx >= 64)
        {
            _ignoreTap = true;
            Prev();
            e.Handled = true;
        }
    }
#endif
}
