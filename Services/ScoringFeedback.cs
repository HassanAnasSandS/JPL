namespace JPL.Services;

/// <summary>Click sound + short vibrate when a run pad entry (0–6) is accepted.</summary>
public interface IScoringFeedback
{
    void PlayBallEntered();
}

public sealed class ScoringFeedback : IScoringFeedback, IDisposable
{
#if ANDROID
    private Android.Media.ToneGenerator? _tone;
    private readonly object _gate = new();
#endif

    public void PlayBallEntered()
    {
        PlayClick();
        Vibrate();
    }

    private void PlayClick()
    {
        try
        {
#if ANDROID
            lock (_gate)
            {
                _tone ??= new Android.Media.ToneGenerator(Android.Media.Stream.Music, 80);
                _tone.StartTone(Android.Media.Tone.PropBeep, 70);
            }
#elif IOS || MACCATALYST
            // 1104 = keyboard "Tock"
            new AudioToolbox.SystemSound(1104).PlaySystemSound();
#elif WINDOWS
            // Console.Beep blocks for the full duration — never on the scoring thread.
            _ = Task.Run(static () =>
            {
                try { Console.Beep(880, 50); }
                catch { /* no audio device */ }
            });
#endif
        }
        catch
        {
            /* sound is confirmation-only — never block scoring */
        }
    }

    private static void Vibrate()
    {
        try
        {
            if (Vibration.Default.IsSupported)
            {
                Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(40));
                return;
            }
        }
        catch
        {
            /* fall through to haptic */
        }

        try
        {
            if (HapticFeedback.Default.IsSupported)
                HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch
        {
            /* ignore */
        }
    }

    public void Dispose()
    {
#if ANDROID
        lock (_gate)
        {
            _tone?.Release();
            _tone?.Dispose();
            _tone = null;
        }
#endif
    }
}
