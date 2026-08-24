using Microsoft.Maui.Graphics;

namespace JPL.Controls;

/// <summary>One rounded horizontal bar; width follows <see cref="Ratio"/> (0–1).</summary>
public sealed class RankBarView : GraphicsView
{
    public static readonly BindableProperty RatioProperty = BindableProperty.Create(
        nameof(Ratio), typeof(double), typeof(RankBarView), 0d,
        propertyChanged: static (b, _, _) => ((RankBarView)b).Invalidate());

    public static readonly BindableProperty FillColorProperty = BindableProperty.Create(
        nameof(FillColor), typeof(Color), typeof(RankBarView), Colors.SteelBlue,
        propertyChanged: static (b, _, _) => ((RankBarView)b).Invalidate());

    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    public Color FillColor
    {
        get => (Color)GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    public RankBarView()
    {
        Drawable = new Drawer(this);
        HeightRequest = DeviceInfo.Idiom == DeviceIdiom.Phone ? 14 : 18;
        BackgroundColor = Colors.Transparent;
        SizeChanged += (_, _) => Invalidate();
    }

    private sealed class Drawer : IDrawable
    {
        private readonly RankBarView _owner;
        public Drawer(RankBarView owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (dirtyRect.Width <= 1 || dirtyRect.Height <= 1)
                return;

            var h = Math.Min(dirtyRect.Height, DeviceInfo.Idiom == DeviceIdiom.Phone ? 14f : 18f);
            var y = dirtyRect.Y + (dirtyRect.Height - h) / 2f;
            var radius = h / 2f;
            var track = new RectF(dirtyRect.X, y, dirtyRect.Width, h);

            canvas.FillColor = Color.FromArgb("#E6EEF6");
            canvas.FillRoundedRectangle(track, radius);

            var ratio = Math.Clamp(_owner.Ratio, 0, 1);
            if (ratio <= 0)
                return;

            var w = Math.Max((float)(dirtyRect.Width * ratio), ratio > 0 ? h : 0);
            w = Math.Min(w, dirtyRect.Width);
            var fill = new RectF(dirtyRect.X, y, w, h);
            var color = _owner.FillColor ?? Colors.SteelBlue;
            var dark = color.WithLuminosity(Math.Max(color.GetLuminosity() - 0.12f, 0.12f));
            canvas.SetFillPaint(
                new LinearGradientPaint
                {
                    StartColor = color,
                    EndColor = dark,
                    StartPoint = new Point(0, 0.5),
                    EndPoint = new Point(1, 0.5)
                },
                fill);
            canvas.FillRoundedRectangle(fill, radius);
        }
    }
}
