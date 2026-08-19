using JPL.ViewModels;

namespace JPL.Pages;

public partial class MatchEditPage : ContentPage
{
    private readonly MatchEditViewModel _vm;
    private int _flipGeneration;

    public MatchEditPage(MatchEditViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PageSafe.Run(() => _vm.InitializeAsync());
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Interlocked.Increment(ref _flipGeneration);
        CoinDisc.AbortAnimation("CoinFlipX");
        CoinDisc.AbortAnimation("CoinPulse");
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MatchEditViewModel.IsCoinFlipping)
            && _vm.IsCoinFlipping
            && _vm.IsTossOverlayVisible)
        {
            _ = RunCoinFlipAsync();
        }
    }

    private async Task RunCoinFlipAsync()
    {
        var gen = Interlocked.Increment(ref _flipGeneration);
        var landsHeads = Random.Shared.Next(2) == 0;

        try
        {
            CoinDisc.AbortAnimation("CoinFlipX");
            CoinDisc.AbortAnimation("CoinPulse");
            CoinDisc.CancelAnimations();
            CoinShadow.CancelAnimations();

            CoinDisc.Scale = 1;
            CoinDisc.ScaleX = 1;
            CoinDisc.Rotation = 0;
            CoinDisc.TranslationY = 0;
            CoinShadow.Opacity = 0.4;
            CoinShadow.ScaleX = 1;

            _vm.TossOverlayStatus = "Coin in the air…";

            var launch = CoinDisc.TranslateTo(0, -72, 300, Easing.CubicOut);
            var shadowShrink = AnimateScaleXAsync(CoinShadow, CoinShadow.ScaleX, 0.55, 300, Easing.CubicOut, "ShadowShrink");
            await Task.WhenAll(launch, shadowShrink).ConfigureAwait(true);
            if (gen != _flipGeneration) return;

            const int flips = 12;
            for (var i = 0; i < flips; i++)
            {
                if (gen != _flipGeneration) return;

                var showHeads = (i % 2) == 0;
                _vm.CoinFaceLabel = showHeads ? "H" : "T";
                _vm.CoinFaceTitle = showHeads ? "HEADS" : "TAILS";

                await AnimateScaleXAsync(CoinDisc, 1, 0.06, 50, Easing.Linear, "CoinFlipX").ConfigureAwait(true);
                if (gen != _flipGeneration) return;
                await AnimateScaleXAsync(CoinDisc, 0.06, 1, 50, Easing.Linear, "CoinFlipX").ConfigureAwait(true);
            }

            await AnimateScaleXAsync(CoinDisc, 1, 0.06, 70, Easing.Linear, "CoinFlipX").ConfigureAwait(true);
            if (gen != _flipGeneration) return;

            _vm.CoinFaceLabel = landsHeads ? "H" : "T";
            _vm.CoinFaceTitle = landsHeads ? "HEADS" : "TAILS";

            await AnimateScaleXAsync(CoinDisc, 0.06, 1, 100, Easing.CubicOut, "CoinFlipX").ConfigureAwait(true);
            if (gen != _flipGeneration) return;

            var drop = CoinDisc.TranslateTo(0, 0, 340, Easing.BounceOut);
            var shadowGrow = AnimateScaleXAsync(CoinShadow, CoinShadow.ScaleX, 1.0, 340, Easing.BounceOut, "ShadowGrow");
            var spin = CoinDisc.RotateTo(landsHeads ? 10 : -10, 340, Easing.CubicOut);
            await Task.WhenAll(drop, shadowGrow, spin).ConfigureAwait(true);
            if (gen != _flipGeneration) return;

            await AnimateScaleAsync(CoinDisc, 1.0, 1.07, 90, Easing.CubicOut, "CoinPulse").ConfigureAwait(true);
            await AnimateScaleAsync(CoinDisc, 1.07, 1.0, 120, Easing.CubicIn, "CoinPulse").ConfigureAwait(true);
            if (gen != _flipGeneration) return;

            _vm.FinishToss(landsHeads);
        }
        catch
        {
            if (gen == _flipGeneration)
                _vm.FinishToss(landsHeads);
        }
    }

    private static Task AnimateScaleXAsync(
        VisualElement view,
        double from,
        double to,
        uint length,
        Easing easing,
        string name)
    {
        var tcs = new TaskCompletionSource<bool>();
        view.AbortAnimation(name);
        view.ScaleX = from;
        var anim = new Animation(v => view.ScaleX = v, from, to, easing);
        anim.Commit(view, name, 16, length, finished: (_, canceled) =>
        {
            if (!canceled)
                view.ScaleX = to;
            tcs.TrySetResult(!canceled);
        });
        return tcs.Task;
    }

    private static Task AnimateScaleAsync(
        VisualElement view,
        double from,
        double to,
        uint length,
        Easing easing,
        string name)
    {
        var tcs = new TaskCompletionSource<bool>();
        view.AbortAnimation(name);
        view.Scale = from;
        var anim = new Animation(v => view.Scale = v, from, to, easing);
        anim.Commit(view, name, 16, length, finished: (_, canceled) =>
        {
            if (!canceled)
                view.Scale = to;
            tcs.TrySetResult(!canceled);
        });
        return tcs.Task;
    }
}
