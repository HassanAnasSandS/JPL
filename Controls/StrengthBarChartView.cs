using Microsoft.Maui.Graphics;

namespace JPL.Controls;

public sealed class StrengthBarItem
{
    public string Label { get; init; } = string.Empty;
    public int Value { get; init; }
    public string Color { get; init; } = "#4FC3F7";
    public string ColorStrong { get; init; } = "#0288D1";
}

public enum StrengthCardKind
{
    Player,
    Captain,
    Team
}

public sealed class StrengthChartCard
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string OverallText { get; init; } = string.Empty;
    public int Overall { get; init; }
    public bool IsCaptain { get; init; }
    public bool IsTeam { get; init; }
    public Color CardBackground { get; init; } = Colors.White;
    public Color CardStroke { get; init; } = Color.FromArgb("#D5DEE8");
    public Color OverallColor { get; init; } = Color.FromArgb("#1565C0");
    public IReadOnlyList<StrengthBarItem> Bars { get; init; } = Array.Empty<StrengthBarItem>();
    public int ChartHeight => (int)Math.Ceiling(
        StrengthBarChartView.ChartMetrics.Top
        + Bars.Count * StrengthBarChartView.ChartMetrics.RowH
        + StrengthBarChartView.ChartMetrics.ScaleH
        + 2);
}

/// <summary>Horizontal 0–100 pill bars with scale, grid, and tap tooltip.</summary>
public sealed class StrengthBarChartView : GraphicsView
{
    internal static class ChartMetrics
    {
        public static bool Compact => DeviceInfo.Idiom == DeviceIdiom.Phone;
        public static float Top => Compact ? 1f : 2f;
        public static float RowH => Compact ? 15f : 18f;
        public static float BarH => Compact ? 6.5f : 8f;
        public static float LabelW => Compact ? 48f : 56f;
        public static float RightPad => Compact ? 3f : 5f;
        public static float ScaleH => Compact ? 10f : 12f;
        public static float LabelFont => Compact ? 7.5f : 8.5f;
        public static float ValueFont => Compact ? 8.5f : 9.5f;
        public static float ScaleFont => Compact ? 7f : 8f;
    }

    public static readonly BindableProperty BarsProperty = BindableProperty.Create(
        nameof(Bars), typeof(IReadOnlyList<StrengthBarItem>), typeof(StrengthBarChartView),
        defaultValue: null,
        propertyChanged: OnBarsChanged);

    private int _selected = -1;

    public IReadOnlyList<StrengthBarItem>? Bars
    {
        get => (IReadOnlyList<StrengthBarItem>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public StrengthBarChartView()
    {
        Drawable = new Drawer(this);
        StartInteraction += OnStart;
        SizeChanged += (_, _) => Invalidate();
        BackgroundColor = Colors.Transparent;
    }

    private static void OnBarsChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (StrengthBarChartView)bindable;
        view._selected = -1;
        view.Invalidate();
    }

    private void OnStart(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0 || Bars is null || Bars.Count == 0)
            return;
        var y = e.Touches[0].Y;
        var index = (int)Math.Floor((y - ChartMetrics.Top) / ChartMetrics.RowH);
        if (index < 0 || index >= Bars.Count)
            return;
        _selected = _selected == index ? -1 : index;
        Invalidate();
    }

    private sealed class Drawer : IDrawable
    {
        private readonly StrengthBarChartView _owner;
        public Drawer(StrengthBarChartView owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var bars = _owner.Bars;
            if (bars is null || bars.Count == 0)
                return;

            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var labelColor = dark ? Color.FromArgb("#E8EDF3") : Color.FromArgb("#0D1B2A");
            var muted = dark ? Color.FromArgb("#8A9BB0") : Color.FromArgb("#8A96A3");
            var track = dark ? Color.FromArgb("#243447") : Color.FromArgb("#EEF3F8");
            var grid = dark ? Color.FromArgb("#3A4A5C") : Color.FromArgb("#D5DEE8");

            var labelW = ChartMetrics.LabelW;
            var rightPad = ChartMetrics.RightPad;
            var top = ChartMetrics.Top;
            var rowH = ChartMetrics.RowH;
            var barH = ChartMetrics.BarH;
            var chartLeft = labelW;
            var chartRight = dirtyRect.Width - rightPad;
            var chartW = Math.Max(40f, chartRight - chartLeft);

            canvas.SaveState();
            canvas.StrokeSize = 1;
            canvas.StrokeDashPattern = [3, 4];
            canvas.StrokeColor = grid;
            for (var t = 0; t <= 4; t++)
            {
                var x = chartLeft + chartW * (t / 4f);
                canvas.DrawLine(x, top, x, top + bars.Count * rowH);
            }
            canvas.StrokeDashPattern = null;
            canvas.RestoreState();

            for (var i = 0; i < bars.Count; i++)
            {
                var bar = bars[i];
                var rowTop = top + i * rowH;
                var selected = i == _owner._selected;
                canvas.FontSize = ChartMetrics.LabelFont;
                canvas.FontColor = selected ? Parse(bar.ColorStrong) : labelColor;
                canvas.DrawString(bar.Label, 0, rowTop, labelW - 3, rowH, HorizontalAlignment.Left, VerticalAlignment.Center);

                var by = rowTop + (rowH - barH) / 2f;
                canvas.FillColor = track;
                canvas.FillRoundedRectangle(chartLeft, by, chartW, barH, barH / 2f);

                var value = Math.Clamp(bar.Value, 0, 100);
                var bw = value <= 0 ? 0f : Math.Min(chartW, Math.Max(barH, chartW * (value / 100f)));
                if (bw > 0)
                {
                    canvas.FillColor = Parse(selected ? bar.ColorStrong : bar.Color);
                    canvas.FillRoundedRectangle(chartLeft, by, bw, barH, barH / 2f);
                }

                var num = value.ToString();
                var numW = ChartMetrics.Compact ? 18f : 22f;
                canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
                canvas.FontSize = ChartMetrics.ValueFont;
                var afterX = chartLeft + bw + 3;
                if (afterX + numW <= chartLeft + chartW)
                {
                    canvas.FontColor = labelColor;
                    canvas.DrawString(num, afterX, rowTop, numW, rowH, HorizontalAlignment.Left, VerticalAlignment.Center);
                }
                else
                {
                    canvas.FontColor = Colors.White;
                    canvas.DrawString(num, chartLeft + bw - numW - 2, rowTop, numW, rowH, HorizontalAlignment.Right, VerticalAlignment.Center);
                }
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            }

            var scaleY = top + bars.Count * rowH;
            canvas.FontSize = ChartMetrics.ScaleFont;
            canvas.FontColor = muted;
            string[] ticks = ["0", "25", "50", "75", "100"];
            for (var t = 0; t < ticks.Length; t++)
            {
                var x = chartLeft + chartW * (t / 4f);
                var align = t == 0 ? HorizontalAlignment.Left
                    : t == ticks.Length - 1 ? HorizontalAlignment.Right
                    : HorizontalAlignment.Center;
                var tw = 28f;
                var tx = t == 0 ? x : t == ticks.Length - 1 ? x - tw : x - tw / 2f;
                canvas.DrawString(ticks[t], tx, scaleY, tw, ChartMetrics.ScaleH, align, VerticalAlignment.Top);
            }

            if (_owner._selected is >= 0 and var sel && sel < bars.Count)
            {
                var bar = bars[sel];
                var value = Math.Clamp(bar.Value, 0, 100);
                var by = top + sel * rowH + (rowH - barH) / 2f;
                var bw = value <= 0 ? 0 : Math.Min(chartW, Math.Max(barH, chartW * (value / 100f)));
                DrawTooltip(canvas, chartLeft + Math.Min(bw, chartW - 8), by, bar, dark);
            }
        }

        private static void DrawTooltip(ICanvas canvas, float anchorX, float barY, StrengthBarItem bar, bool dark)
        {
            var compact = ChartMetrics.Compact;
            var boxW = compact ? 88f : 100f;
            var boxH = compact ? 28f : 32f;
            var x = Math.Max(4, anchorX - boxW / 2f);
            var y = barY - boxH - 3;
            if (y < 1)
                y = barY + ChartMetrics.BarH + 2;
            canvas.FillColor = dark ? Color.FromArgb("#1B2838") : Colors.White;
            canvas.StrokeColor = dark ? Color.FromArgb("#3A4A5C") : Color.FromArgb("#E0E6EE");
            canvas.StrokeSize = 1;
            canvas.SetShadow(new SizeF(0, 2), 4, Color.FromArgb("#33000000"));
            canvas.FillRoundedRectangle(x, y, boxW, boxH, 6);
            canvas.SetShadow(SizeF.Zero, 0, Colors.Transparent);
            canvas.DrawRoundedRectangle(x, y, boxW, boxH, 6);

            canvas.FontSize = compact ? 8.5f : 9.5f;
            canvas.FontColor = dark ? Colors.White : Color.FromArgb("#0D1B2A");
            canvas.DrawString(bar.Label, x + 6, y + 2, boxW - 12, 12, HorizontalAlignment.Left, VerticalAlignment.Top);

            canvas.FillColor = Parse(bar.ColorStrong);
            canvas.FillCircle(x + 11, y + (compact ? 20 : 22), 3f);
            canvas.FontSize = compact ? 8.5f : 9.5f;
            canvas.FontColor = dark ? Color.FromArgb("#B8C4D4") : Color.FromArgb("#5A6A7A");
            canvas.DrawString($"Rating  {bar.Value}", x + 17, y + (compact ? 13 : 15), boxW - 24, 12, HorizontalAlignment.Left, VerticalAlignment.Center);
        }

        private static Color Parse(string hex)
        {
            try { return Color.FromArgb(hex); }
            catch { return Color.FromArgb("#4FC3F7"); }
        }
    }
}
