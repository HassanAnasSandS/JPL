using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using JPL.Data;
using JPL.Pages;
using JPL.Services;
using JPL.ViewModels;

namespace JPL;

public static class MauiProgram
{
    private static int _initStarted;
    private static Task? _initTask;

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
            .ConfigureMauiHandlers(handlers =>
            {
#if WINDOWS
                // Ensure CollectionView / list pages can scroll with mouse wheel & touch.
                Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler.Mapper.AppendToMapping(
                    "JplEnableScroll",
                    static (handler, _) =>
                    {
                        if (handler.PlatformView is Microsoft.UI.Xaml.Controls.ListViewBase list)
                        {
                            list.IsTabStop = true;
                            Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollMode(
                                list, Microsoft.UI.Xaml.Controls.ScrollMode.Enabled);
                            Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(
                                list, Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto);
                            Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollMode(
                                list, Microsoft.UI.Xaml.Controls.ScrollMode.Disabled);
                        }
                    });
#endif
            })
            .ConfigureLifecycleEvents(events =>
            {
#if WINDOWS
                events.AddWindows(w => w.OnWindowCreated(window =>
                {
                    try
                    {
                        window.Activate();
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                        appWindow?.Show(true);
                    }
                    catch
                    {
                        /* ignore activate failures */
                    }
                }));
#endif
            });

        builder.Services.AddSingleton<ILeagueContext, LeagueContext>();
        builder.Services.AddDbContextFactory<JplDbContext>((_, options) =>
        {
            options.ConfigureSqlite();
        });

        builder.Services.AddSingleton<AuthTokenStore>();
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddSingleton<IAuthForcedLogout, AuthForcedLogout>();
        builder.Services.AddSingleton<IAppNavigator, AppNavigator>();
        builder.Services.AddSingleton<MasterDataCache>();

        builder.Services.Configure<ApiOptions>(options =>
        {
            // Same hosted API for all platforms (Windows DEBUG used to hit localhost:5025,
            // while Android already used production — phones worked, Windows looked "offline").
            options.BaseUrl = "https://jpl.shoptick.shop/";
        });

        builder.Services.AddTransient<AuthHttpMessageHandler>();
        builder.Services.AddHttpClient(ApiOptions.HttpClientName, (sp, client) =>
        {
            var baseUrl = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiOptions>>().Value.BaseUrl
                .TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            // Bound hung requests without aborting sockets via a separate short CTS.
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddHttpMessageHandler<AuthHttpMessageHandler>();

        builder.Services.AddSingleton<IApiClient, ApiClient>();
        builder.Services.AddSingleton<ISqlServerSyncClient, HttpSyncClient>();

        builder.Services.AddSingleton<IOutboxWriter, OutboxWriter>();
        builder.Services.AddSingleton<SyncService>();
        builder.Services.AddSingleton<ISyncService>(sp => sp.GetRequiredService<SyncService>());
        builder.Services.AddSingleton<ISyncTrigger>(sp => sp.GetRequiredService<SyncService>());
        builder.Services.AddSingleton<IMatchSquadCache, MatchSquadCache>();

        builder.Services.AddSingleton<IUsersModuleGate, UsersModuleGate>();
        builder.Services.AddSingleton<IAuctionsModuleGate, AuctionsModuleGate>();
        builder.Services.AddSingleton<IAuctionService, AuctionService>();
        builder.Services.AddSingleton<IPlayerService, PlayerService>();
        builder.Services.AddSingleton<ITeamService, TeamService>();
        builder.Services.AddSingleton<IScoringFeedback, ScoringFeedback>();
        builder.Services.AddSingleton<IMatchService, MatchService>();
        builder.Services.AddSingleton<INetSessionService, NetSessionService>();
        builder.Services.AddSingleton<IStatsService, StatsService>();
        builder.Services.AddSingleton<IUserService, UserService>();
        builder.Services.AddSingleton<IVenueBookingService, VenueBookingService>();
        builder.Services.AddSingleton<IVenueService, VenueService>();
        builder.Services.AddSingleton<ITournamentService, TournamentService>();
        builder.Services.AddSingleton<ILeagueService, LeagueService>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LeaguePickerViewModel>();
        builder.Services.AddSingleton<LeaguesViewModel>();
        builder.Services.AddTransient<LeagueEditViewModel>();
        builder.Services.AddSingleton<PlayersViewModel>();
        builder.Services.AddTransient<PlayerEditViewModel>();
        builder.Services.AddSingleton<TeamsViewModel>();
        builder.Services.AddTransient<TeamEditViewModel>();
        builder.Services.AddSingleton<MatchesViewModel>();
        builder.Services.AddTransient<MatchEditViewModel>();
        builder.Services.AddTransient<ScoringViewModel>();
        builder.Services.AddSingleton<NetSessionsViewModel>();
        builder.Services.AddTransient<NetSessionEditViewModel>();
        builder.Services.AddTransient<NetsScoringViewModel>();
        builder.Services.AddTransient<NetSessionScorecardViewModel>();
        builder.Services.AddSingleton<StatsViewModel>();
        builder.Services.AddSingleton<RankingsViewModel>();
        builder.Services.AddSingleton<HallOfFameViewModel>();
        builder.Services.AddTransient<MatchScorecardViewModel>();
        builder.Services.AddSingleton<AuctionsViewModel>();
        builder.Services.AddTransient<AuctionEditViewModel>();
        builder.Services.AddSingleton<AuctionReportViewModel>();
        builder.Services.AddSingleton<UsersViewModel>();
        builder.Services.AddTransient<UserEditViewModel>();
        builder.Services.AddSingleton<VenueBookingsViewModel>();
        builder.Services.AddTransient<VenueBookingEditViewModel>();
        builder.Services.AddSingleton<VenuesViewModel>();
        builder.Services.AddTransient<VenueEditViewModel>();
        builder.Services.AddSingleton<TournamentsViewModel>();
        builder.Services.AddTransient<TournamentEditViewModel>();
        builder.Services.AddTransient<TournamentDetailViewModel>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<LeaguePickerPage>();
        builder.Services.AddTransient<LeaguesPage>();
        builder.Services.AddTransient<LeagueEditPage>();
        builder.Services.AddTransient<PlayersPage>();
        builder.Services.AddTransient<PlayerEditPage>();
        builder.Services.AddTransient<TeamsPage>();
        builder.Services.AddTransient<TeamEditPage>();
        builder.Services.AddTransient<MatchesPage>();
        builder.Services.AddTransient<MatchEditPage>();
        builder.Services.AddTransient<ScoringPage>();
        builder.Services.AddTransient<MatchScorecardPage>();
        builder.Services.AddTransient<NetSessionsPage>();
        builder.Services.AddTransient<NetSessionEditPage>();
        builder.Services.AddTransient<NetsScoringPage>();
        builder.Services.AddTransient<NetSessionScorecardPage>();
        builder.Services.AddTransient<StatsPage>();
        builder.Services.AddTransient<RankingsPage>();
        builder.Services.AddTransient<HallOfFamePage>();
        builder.Services.AddTransient<AuctionsPage>();
        builder.Services.AddTransient<AuctionEditPage>();
        builder.Services.AddTransient<AuctionReportPage>();
        builder.Services.AddTransient<UsersPage>();
        builder.Services.AddTransient<UserEditPage>();
        builder.Services.AddTransient<VenueBookingsPage>();
        builder.Services.AddTransient<VenueBookingEditPage>();
        builder.Services.AddTransient<VenuesPage>();
        builder.Services.AddTransient<VenueEditPage>();
        builder.Services.AddTransient<TournamentsPage>();
        builder.Services.AddTransient<TournamentEditPage>();
        builder.Services.AddTransient<TournamentDetailPage>();
        builder.Services.AddTransient<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Starts DB + local auth seed off the UI thread. Never blocks CreateWindow.
    /// Callers that need SQLite should await <see cref="WhenReadyAsync"/>.
    /// </summary>
    public static void StartInitialize(IServiceProvider services)
    {
        if (Interlocked.Exchange(ref _initStarted, 1) == 1)
            return;

        _initTask = Task.Run(async () =>
        {
            try
            {
                using var scope = services.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JplDbContext>>();
                await using var db = await factory.CreateDbContextAsync().ConfigureAwait(false);
                await DbBootstrap.InitializeAsync(db).ConfigureAwait(false);

                var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
                await auth.InitializeAsync().ConfigureAwait(false);
            }
            catch
            {
                /* never leave WhenReadyAsync hanging — login must stay usable */
            }
        });
    }

    public static Task WhenReadyAsync()
        => _initTask ?? Task.CompletedTask;
}
