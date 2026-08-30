namespace JPL.Services;

/// <summary>
/// Platform overlay that sits above the Shell flyout and page content.
/// MAUI page overlays stay behind the Android drawer, so the spinner never
/// painted until the next screen opened.
/// </summary>
internal static class AppBusyNative
{
    public static void SetVisible(bool visible)
    {
#if ANDROID
        AndroidOverlay.SetVisible(visible);
#endif
    }

    public static Task WaitTwoFramesAsync()
    {
#if ANDROID
        return AndroidOverlay.WaitTwoFramesAsync();
#else
        return Task.CompletedTask;
#endif
    }

#if ANDROID
    private static class AndroidOverlay
    {
        private static Android.Widget.FrameLayout? _host;
        private static Android.Widget.ProgressBar? _spinner;
        private static Android.App.Activity? _activity;

        public static void SetVisible(bool visible)
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity is { IsFinishing: false, IsDestroyed: false })
                EnsureHost(activity);

            if (_host is null)
            {
                if (visible)
                {
                    try
                    {
                        activity?.Window?.DecorView?.Post(() => SetVisible(true));
                    }
                    catch
                    {
                        /* activity gone */
                    }
                }
                return;
            }

            _host.Visibility = visible
                ? Android.Views.ViewStates.Visible
                : Android.Views.ViewStates.Gone;
            try
            {
                _host.BringToFront();
            }
            catch
            {
                /* not attached yet */
            }
            if (_spinner is not null)
                _spinner.Indeterminate = visible;
            if (visible)
                _host.Invalidate();
        }

        public static async Task WaitTwoFramesAsync()
        {
            var view = AttachedWaitView();
            if (view is null)
            {
                await Task.Delay(32).ConfigureAwait(true);
                return;
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.Post(() =>
            {
                if (!view.IsAttachedToWindow)
                {
                    tcs.TrySetResult();
                    return;
                }

                view.Post(() => tcs.TrySetResult());
            });

            var timeout = Task.Delay(250);
            await Task.WhenAny(tcs.Task, timeout).ConfigureAwait(true);
        }

        private static Android.Views.View? AttachedWaitView()
        {
            if (_host is { IsAttachedToWindow: true })
                return _host;

            var decor = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.DecorView;
            return decor is { IsAttachedToWindow: true } ? decor : null;
        }

        private static void EnsureHost(Android.App.Activity activity)
        {
            if (_host is not null &&
                ReferenceEquals(_activity, activity) &&
                _host.IsAttachedToWindow)
                return;

            var content = activity.FindViewById<Android.Views.ViewGroup>(Android.Resource.Id.Content)
                          ?? activity.Window?.DecorView as Android.Views.ViewGroup;
            if (content is null)
                return;

            if (_host is not null)
            {
                try
                {
                    (_host.Parent as Android.Views.ViewGroup)?.RemoveView(_host);
                }
                catch
                {
                    /* old activity is gone */
                }
            }

            _activity = activity;
            _host = new Android.Widget.FrameLayout(activity)
            {
                Clickable = true,
                Focusable = true,
                Elevation = 64f
            };
            _host.SetBackgroundColor(Android.Graphics.Color.Argb(0xCC, 0x0D, 0x1B, 0x2A));

            var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
            var size = (int)Math.Round(48 * density);
            _spinner = new Android.Widget.ProgressBar(activity)
            {
                Indeterminate = true
            };
            _spinner.IndeterminateTintList = Android.Content.Res.ColorStateList.ValueOf(
                Android.Graphics.Color.ParseColor("#B6FF3B"));

            var spinnerLp = new Android.Widget.FrameLayout.LayoutParams(size, size)
            {
                Gravity = Android.Views.GravityFlags.Center
            };
            _host.AddView(_spinner, spinnerLp);

            content.AddView(
                _host,
                new Android.Views.ViewGroup.LayoutParams(
                    Android.Views.ViewGroup.LayoutParams.MatchParent,
                    Android.Views.ViewGroup.LayoutParams.MatchParent));
            _host.Visibility = Android.Views.ViewStates.Gone;
        }
    }
#endif
}
