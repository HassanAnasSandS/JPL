using Microsoft.Maui.Graphics;
using JPL.Services;

namespace JPL.Controls;

public sealed class LineChartView : GraphicsView
{
    public static readonly BindableProperty SeriesProperty = BindableProperty.Create(
        nameof(Series), typeof(IReadOnlyList<ChartSeriesData>), typeof(LineChartView),
        propertyChanged: static (b, _, _) => ((LineChartView)b).Invalidate());

    public IReadOnlyList<ChartSeriesData>? Series
    {
        get => (IReadOnlyList<ChartSeriesData>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public LineChartView()
    {
        Drawable = new Drawer(this);
        HeightRequest = 200;
        BackgroundColor = Colors.Transparent;
        SizeChanged += (_, _) => Invalidate();
    }

    private sealed class Drawer : IDrawable
    {
        private readonly LineChartView _owner;
        public Drawer(LineChartView owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF dirty)
        {
            if (dirty.Width < 8 || dirty.Height < 8)
                return;
            var series = _owner.Series;
            if (series is null || series.Count == 0 || series.All(s => s.Points.Count < 2))
            {
                canvas.FontColor = Color.FromArgb("#8FA3B8");
                canvas.FontSize = 12;
                canvas.DrawString("Not enough balls yet", dirty, HorizontalAlignment.Center, VerticalAlignment.Center);
                return;
            }

            var padL = 28f;
            var padR = 8f;
            var padT = 10f;
            var padB = 22f;
            var plot = new RectF(dirty.X + padL, dirty.Y + padT, dirty.Width - padL - padR, dirty.Height - padT - padB);
            double maxX = 1, maxY = 1;
            foreach (var s in series)
            foreach (var p in s.Points)
            {
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            canvas.StrokeColor = Color.FromArgb("#D0D7E0");
            canvas.StrokeSize = 1;
            canvas.DrawRectangle(plot);

            canvas.FontColor = Color.FromArgb("#5A6A7A");
            canvas.FontSize = 9;
            for (var t = 0; t <= 4; t++)
            {
                var yv = maxY * t / 4.0;
                var y = plot.Bottom - (float)(plot.Height * (t / 4.0));
                canvas.StrokeColor = Color.FromArgb("#E8EDF3");
                canvas.DrawLine(plot.Left, y, plot.Right, y);
                canvas.DrawString($"{yv:0}", plot.X - 26, y - 7, 24, 14, HorizontalAlignment.Right, VerticalAlignment.Top);
            }

            foreach (var s in series)
            {
                if (s.Points.Count < 2)
                    continue;
                var color = Color.FromArgb(s.Color);
                canvas.StrokeColor = color;
                canvas.StrokeSize = 2.4f;
                canvas.StrokeLineJoin = LineJoin.Round;
                PathF path = new();
                for (var i = 0; i < s.Points.Count; i++)
                {
                    var p = s.Points[i];
                    var x = plot.Left + (float)(plot.Width * (maxX <= 0 ? 0 : p.X / maxX));
                    var y = plot.Bottom - (float)(plot.Height * (maxY <= 0 ? 0 : p.Y / maxY));
                    if (i == 0) path.MoveTo(x, y);
                    else path.LineTo(x, y);
                }
                canvas.DrawPath(path);
            }

            var legendX = plot.Left;
            foreach (var s in series)
            {
                canvas.FillColor = Color.FromArgb(s.Color);
                canvas.FillCircle(legendX + 4, plot.Bottom + 12, 4);
                canvas.FontColor = Color.FromArgb("#0D1B2A");
                canvas.FontSize = 10;
                canvas.DrawString(s.Label, legendX + 12, plot.Bottom + 4, 90, 16, HorizontalAlignment.Left, VerticalAlignment.Top);
                legendX += 110;
            }
        }
    }
}

public sealed class BarChartView : GraphicsView
{
    public static readonly BindableProperty BarsProperty = BindableProperty.Create(
        nameof(Bars), typeof(IReadOnlyList<BarPoint>), typeof(BarChartView),
        propertyChanged: static (b, _, _) => ((BarChartView)b).Invalidate());

    public IReadOnlyList<BarPoint>? Bars
    {
        get => (IReadOnlyList<BarPoint>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public BarChartView()
    {
        Drawable = new Drawer(this);
        HeightRequest = 180;
        BackgroundColor = Colors.Transparent;
        SizeChanged += (_, _) => Invalidate();
    }

    private sealed class Drawer : IDrawable
    {
        private readonly BarChartView _owner;
        public Drawer(BarChartView owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF dirty)
        {
            var bars = _owner.Bars;
            if (bars is null || bars.Count == 0)
            {
                canvas.FontColor = Color.FromArgb("#8FA3B8");
                canvas.FontSize = 12;
                canvas.DrawString("No overs yet", dirty, HorizontalAlignment.Center, VerticalAlignment.Center);
                return;
            }

            var padL = 8f;
            var padB = 18f;
            var padT = 8f;
            var plot = new RectF(dirty.X + padL, dirty.Y + padT, dirty.Width - padL * 2, dirty.Height - padT - padB);
            var min = Math.Min(0, bars.Min(b => b.Value));
            var max = Math.Max(0.01, bars.Max(b => b.Value));
            var span = max - min;
            if (span <= 0) span = 1;
            var zeroY = plot.Bottom - (float)(plot.Height * ((0 - min) / span));
            var gap = 3f;
            var w = Math.Max(4, (plot.Width - gap * (bars.Count + 1)) / bars.Count);

            canvas.StrokeColor = Color.FromArgb("#D0D7E0");
            canvas.StrokeSize = 1;
            canvas.DrawLine(plot.Left, zeroY, plot.Right, zeroY);

            for (var i = 0; i < bars.Count; i++)
            {
                var b = bars[i];
                var x = plot.Left + gap + i * (w + gap);
                var yVal = plot.Bottom - (float)(plot.Height * ((b.Value - min) / span));
                var top = Math.Min(yVal, zeroY);
                var h = Math.Max(1, Math.Abs(zeroY - yVal));
                canvas.FillColor = Color.FromArgb(string.IsNullOrWhiteSpace(b.Color) ? "#1565C0" : b.Color);
                canvas.FillRoundedRectangle(x, top, w, h, 3);
                if (b.Wickets > 0)
                {
                    canvas.FillColor = Colors.White;
                    for (var wkt = 0; wkt < Math.Min(b.Wickets, 3); wkt++)
                        canvas.FillCircle(x + w / 2, top + 6 + wkt * 8, 3);
                }
                canvas.FontColor = Color.FromArgb("#5A6A7A");
                canvas.FontSize = bars.Count > 16 ? 8 : 10;
                canvas.DrawString(b.Label, x - 2, plot.Bottom + 1, w + 4, 16, HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }
    }
}

public sealed class WagonWheelView : GraphicsView
{
    public static readonly BindableProperty SectorsProperty = BindableProperty.Create(
        nameof(Sectors), typeof(IReadOnlyList<WagonSector>), typeof(WagonWheelView),
        propertyChanged: static (b, _, _) => ((WagonWheelView)b).Invalidate());

    public IReadOnlyList<WagonSector>? Sectors
    {
        get => (IReadOnlyList<WagonSector>?)GetValue(SectorsProperty);
        set => SetValue(SectorsProperty, value);
    }

    public WagonWheelView()
    {
        Drawable = new Drawer(this);
        HeightRequest = 240;
        BackgroundColor = Colors.Transparent;
        SizeChanged += (_, _) => Invalidate();
    }

    private sealed class Drawer : IDrawable
    {
        private readonly WagonWheelView _owner;
        public Drawer(WagonWheelView owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF dirty)
        {
            var sectors = _owner.Sectors;
            var cx = dirty.Center.X;
            var cy = dirty.Center.Y;
            var r = Math.Min(dirty.Width, dirty.Height) / 2f - 18f;
            canvas.StrokeColor = Color.FromArgb("#D0D7E0");
            canvas.StrokeSize = 1;
            canvas.FillColor = Color.FromArgb("#F7FAFC");
            canvas.FillCircle(cx, cy, r);
            canvas.DrawCircle(cx, cy, r);
            canvas.DrawCircle(cx, cy, r * 0.45f);

            if (sectors is null || sectors.Count == 0 || sectors.All(s => s.Runs == 0))
            {
                canvas.FontColor = Color.FromArgb("#8FA3B8");
                canvas.FontSize = 11;
                canvas.DrawString("Tag 4s/6s while scoring", dirty, HorizontalAlignment.Center, VerticalAlignment.Center);
                return;
            }

            var max = Math.Max(1, sectors.Max(s => s.Runs));
            var n = sectors.Count;
            for (var i = 0; i < n; i++)
            {
                var start = (float)(-Math.PI / 2 + i * (2 * Math.PI / n));
                var end = start + (float)(2 * Math.PI / n);
                var mid = (start + end) / 2;
                var len = r * 0.2f + r * 0.7f * sectors[i].Runs / max;
                var color = sectors[i].Sixes > 0 ? Color.FromArgb("#C62828")
                    : sectors[i].Fours > 0 ? Color.FromArgb("#1565C0")
                    : Color.FromArgb("#D4AF37");
                canvas.StrokeColor = color;
                canvas.StrokeSize = 3;
                canvas.DrawLine(cx, cy, cx + (float)Math.Cos(mid) * len, cy + (float)Math.Sin(mid) * len);

                canvas.FontColor = Color.FromArgb("#5A6A7A");
                canvas.FontSize = 9;
                var lx = cx + (float)Math.Cos(mid) * (r + 12);
                var ly = cy + (float)Math.Sin(mid) * (r + 12);
                canvas.DrawString(sectors[i].Name, lx - 36, ly - 8, 72, 16, HorizontalAlignment.Center, VerticalAlignment.Top);
            }

            canvas.FillColor = Color.FromArgb("#0D1B2A");
            canvas.FillCircle(cx, cy, 5);
        }
    }
}
