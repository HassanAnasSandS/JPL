using JPL.Services;

namespace JPL.Controls;

/// <summary>Rotates <see cref="BrandSlogans"/> so brand copy stays visible without crowding the form.</summary>
public sealed class SloganTicker : Label
{
    public static readonly BindableProperty IntervalMsProperty = BindableProperty.Create(
        nameof(IntervalMs),
        typeof(int),
        typeof(SloganTicker),
        3400,
        propertyChanged: (b, _, _) => ((SloganTicker)b).RestartTimer());

    private IDispatcherTimer? _timer;
    private int _index;
    private int _fadeBusy;

    public int IntervalMs
    {
        get => (int)GetValue(IntervalMsProperty);
        set => SetValue(IntervalMsProperty, value);
    }

    public SloganTicker()
    {
        HorizontalTextAlignment = TextAlignment.Center;
        LineBreakMode = LineBreakMode.WordWrap;
        MaxLines = 2;
        Text = BrandSlogans.All[0];
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e) => Start();

    private void OnUnloaded(object? sender, EventArgs e) => Stop();

    private void Start()
    {
        if (BrandSlogans.All.Length == 0)
            return;
        Text = BrandSlogans.All[_index % BrandSlogans.All.Length];
        RestartTimer();
    }

    private void RestartTimer()
    {
        StopTimer();
        if (!IsLoaded)
            return;
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(IntervalMs, 1600, 12000));
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void Stop()
    {
        StopTimer();
        Opacity = 1;
    }

    private void StopTimer()
    {
        if (_timer is null)
            return;
        _timer.Tick -= OnTick;
        _timer.Stop();
        _timer = null;
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _fadeBusy, 1) == 1)
            return;
        try
        {
            await this.FadeTo(0.15, 160).ConfigureAwait(true);
            _index = (_index + 1) % BrandSlogans.All.Length;
            Text = BrandSlogans.All[_index];
            await this.FadeTo(1, 220).ConfigureAwait(true);
        }
        catch
        {
            Opacity = 1;
        }
        finally
        {
            Interlocked.Exchange(ref _fadeBusy, 0);
        }
    }
}
