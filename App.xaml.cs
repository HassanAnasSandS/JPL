using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Services;

namespace JPL;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly IAuthService _auth;
    private readonly IAppNavigator _navigator;
    private readonly IAppStartupStatus _startup;
    private int _lifecycleSyncBusy;
    private DateTime _lastLifecycleSyncUtc = DateTime.MinValue;

    public App(
        IServiceProvider services,
        IAuthService auth,
        IAppNavigator navigator,
        IAppStartupStatus startup)
    {
        InitializeComponent();
        CrashGuard.Install();
        PageAppearing += (_, page) => AppBusy.AttachToPage(page);
        _services = services;
        _auth = auth;
        _navigator = navigator;
        _startup = startup;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        MauiProgram.StartInitialize(_services);
        _auth.RestoreLocalSession();

        var startingMessage = _auth.IsAuthenticated ? "Opening your account…" : "Starting Cric360…";
        _startup.SetBusy(startingMessage);

        var splash = new Pages.SplashPage(_startup);
        splash.SetStatus(startingMessage);

        var main = new Window
        {
            Title = "Cric360",
            Page = splash
#if WINDOWS
            ,
            Width = 1280,
            Height = 800
#endif
        };
        _navigator.Attach(main);

        // Do not build AppShell here — that used to freeze the native splash logo
        // for several seconds with no spinner. Paint splash first, then continue.
        main.Created += async (_, _) =>
        {
            try
            {
                await Task.Delay(80).ConfigureAwait(true);
                _auth.RestoreLocalSession();
                if (_auth.IsAuthenticated)
                {
                    _startup.SetBusy("Opening your account…");
                    splash.SetStatus("Opening your account…");
                    await _navigator.ContinueAfterAuthAsync(freshLogin: false).ConfigureAwait(true);
                    if (_auth.IsAuthenticated)
                        _ = ValidateSessionInBackgroundAsync();
                    else
                        await _navigator.ShowLoginAsync().ConfigureAwait(true);
                }
                else
                {
                    await _navigator.ShowLoginAsync().ConfigureAwait(true);
                }
            }
            catch
            {
                _auth.RestoreLocalSession();
                if (_auth.IsAuthenticated)
                    await _navigator.ContinueAfterAuthAsync(freshLogin: false).ConfigureAwait(true);
                else
                    await _navigator.ShowLoginAsync().ConfigureAwait(true);
            }
            finally
            {
                _startup.Clear();
            }
        };

        main.Resumed += (_, _) => RequestBackgroundSync();
        main.Stopped += (_, _) =>
        {
            // Recents swipe kills the process after Stopped. Flush login to disk
            // here — never call the API (a 401 used to wipe the saved session).
            _auth.FlushPersistedSession();
            CheckpointLocalDb();
        };
        main.Deactivated += (_, _) =>
        {
            _auth.FlushPersistedSession();
            CheckpointLocalDb();
        };
        main.Destroying += (_, _) =>
        {
            _auth.FlushPersistedSession();
            CheckpointLocalDb();
        };

        return main;
    }

    private async Task ValidateSessionInBackgroundAsync()
    {
        try
        {
            await _auth.ValidateRemoteSessionAsync().ConfigureAwait(false);
            if (_auth.IsAuthenticated)
                return;

            // Only deleted / deactivated accounts clear the session.

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await _navigator.ShowLoginAsync().ConfigureAwait(true);
            }).ConfigureAwait(false);
        }
        catch (SessionExpiredException)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await _navigator.ShowLoginAsync().ConfigureAwait(true);
            }).ConfigureAwait(false);
        }
        catch
        {
            /* offline / timeout — keep local session */
        }
    }

    private void CheckpointLocalDb()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await MauiProgram.WhenReadyAsync().ConfigureAwait(false);
                var factory = _services.GetRequiredService<IDbContextFactory<JplDbContext>>();
                await using var db = await factory.CreateDbContextAsync().ConfigureAwait(false);
                await DbBootstrap.CheckpointAsync(db).ConfigureAwait(false);
            }
            catch
            {
                /* ignore */
            }
        });
    }

    private void RequestBackgroundSync()
    {
        if (!_auth.IsAuthenticated)
            return;
        if (DateTime.Now - _lastLifecycleSyncUtc < TimeSpan.FromSeconds(30))
            return;
        if (Interlocked.CompareExchange(ref _lifecycleSyncBusy, 1, 0) != 0)
            return;

        _lastLifecycleSyncUtc = DateTime.Now;
        _ = Task.Run(async () =>
        {
            try
            {
                await MauiProgram.WhenReadyAsync().ConfigureAwait(false);
                _services.GetRequiredService<ILivePublisher>().RequestFlush();
                await _services.GetRequiredService<ISyncService>().FlushAsync().ConfigureAwait(false);
            }
            catch (SessionExpiredException)
            {
                /* ForceLogout already triggered by auth handler */
            }
            catch
            {
                /* ignore */
            }
            finally
            {
                Interlocked.Exchange(ref _lifecycleSyncBusy, 0);
            }
        });
    }
}
