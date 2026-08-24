using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Services;

namespace JPL;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly IAuthService _auth;
    private readonly IAppNavigator _navigator;
    private readonly ILeagueContext _league;
    private readonly IAppStartupStatus _startup;
    private int _lifecycleSyncBusy;
    private DateTime _lastLifecycleSyncUtc = DateTime.MinValue;

    public App(
        IServiceProvider services,
        IAuthService auth,
        IAppNavigator navigator,
        ILeagueContext league,
        IAppStartupStatus startup)
    {
        InitializeComponent();
        CrashGuard.Install();
        _services = services;
        _auth = auth;
        _navigator = navigator;
        _league = league;
        _startup = startup;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        MauiProgram.StartInitialize(_services);

        // Preferences may hold a token from another database (e.g. JPL → JPLTesting).
        // Always open Login first; promote to Shell only after local DB is ready.
        _auth.RestoreLocalSession();
        if (_auth.IsAuthenticated)
            _startup.SetBusy("Restoring session…");

        var main = new Window
        {
            Title = "Cric360"
#if WINDOWS
            ,
            Width = 1280,
            Height = 800
#endif
        };
        _navigator.Attach(main);
        main.Page = _services.GetRequiredService<Pages.LoginPage>();

        main.Created += async (_, _) =>
        {
            try
            {
                if (_auth.IsAuthenticated)
                    _startup.SetBusy("Preparing…");

                // DB seed only — do NOT wait on network here (was making cold start feel hung).
                await MauiProgram.WhenReadyAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(true);

                if (_auth.IsAuthenticated)
                {
                    _startup.SetBusy("Checking account…");
                    try
                    {
                        // Do not use a short CTS here: cancelling an in-flight HTTPS call
                        // aborts the socket and Visual Studio reports TaskCanceledException
                        // at AuthHttpMessageHandler. HttpClient.Timeout already bounds the wait.
                        await _auth.ValidateRemoteSessionAsync().ConfigureAwait(true);
                    }
                    catch
                    {
                        /* offline / timeout — keep local session */
                    }
                }

                if (_auth.IsAuthenticated)
                {
                    try
                    {
                        _startup.SetBusy("Opening…");
                        if (_auth.Current?.NeedsLeaguePicker == true && !_league.HasSelection)
                            await _navigator.ShowLeaguePickerAsync().ConfigureAwait(true);
                        else
                            await _navigator.ShowMainAsync().ConfigureAwait(true);
                        return;
                    }
                    catch
                    {
                        await _auth.LogoutAsync().ConfigureAwait(true);
                    }
                }

                await _navigator.ShowLoginAsync().ConfigureAwait(true);
            }
            catch (SessionExpiredException)
            {
                await _navigator.ShowLoginAsync().ConfigureAwait(true);
            }
            catch
            {
                await _navigator.ShowLoginAsync().ConfigureAwait(true);
            }
        };

        main.Stopped += (_, _) =>
        {
            CheckpointLocalDb();
            RequestBackgroundSync();
        };
        main.Deactivated += (_, _) =>
        {
            CheckpointLocalDb();
            RequestBackgroundSync();
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
