using JPL.Pages;
using JPL.Services;

namespace JPL;

public partial class AppShell : Shell
{
    private static int _routesRegistered;

    private readonly IAuthService _auth;
    private readonly IAppNavigator _navigator;
    private readonly ISyncService _sync;
    private readonly ILeagueContext _league;
    private readonly IServiceProvider _services;
    private readonly Label _pendingLabel;
    private readonly Label _leagueLabel;

    public AppShell(
        IServiceProvider services,
        IAuthService auth,
        IAppNavigator navigator,
        ISyncService sync,
        ILeagueContext league)
    {
        InitializeComponent();
        _auth = auth;
        _navigator = navigator;
        _sync = sync;
        _league = league;
        _services = services;

        EnsureRoutesRegistered(services);

        Items.Clear();

        var canManageMasterData = _auth.CanWrite;
        if (canManageMasterData)
        {
            Items.Add(CreateFlyout("Players", "players", "playershome", () => services.GetRequiredService<PlayersPage>()));
            Items.Add(CreateFlyout("Teams", "teams", "teamshome", () => services.GetRequiredService<TeamsPage>()));
        }
        Items.Add(CreateFlyout("Matches", "matches", "matcheshome", () => services.GetRequiredService<MatchesPage>()));
        Items.Add(CreateFlyout("Nets Practice", "nets", "netshome", () => services.GetRequiredService<NetSessionsPage>()));
        Items.Add(CreateFlyout("Tournaments", "tournaments", "tournamentshome", () => services.GetRequiredService<TournamentsPage>()));
        Items.Add(CreateFlyout("Series", "series", "serieshome", () => services.GetRequiredService<SeriesPage>()));
        Items.Add(CreateFlyout("Auctions", "auctions", "auctionshome", () => services.GetRequiredService<AuctionsPage>()));
        if (canManageMasterData)
        {
            Items.Add(CreateFlyout("Venues", "venues", "venueshome", () => services.GetRequiredService<VenuesPage>()));
            Items.Add(CreateFlyout("Venue Bookings", "venuebookings", "venuebookingshome", () => services.GetRequiredService<VenueBookingsPage>()));
        }
        Items.Add(CreateFlyout("Statistics", "stats", "statshome", () => services.GetRequiredService<StatsPage>()));
        Items.Add(CreateFlyout("Rankings", "rankings", "rankingshome", () => services.GetRequiredService<RankingsPage>()));
        Items.Add(CreateFlyout("Hall of Fame", "halloffame", "halloffamehome", () => services.GetRequiredService<HallOfFamePage>()));
        if (_auth.Current?.CanManageUsers == true)
            Items.Add(CreateFlyout("Users", "users", "usershome", () => services.GetRequiredService<UsersPage>()));

        _pendingLabel = new Label
        {
            Text = "Pending sync: …",
            FontSize = 12,
            TextColor = Color.FromArgb("#B8C4D4")
        };

        _leagueLabel = new Label
        {
            Text = string.IsNullOrWhiteSpace(_league.CurrentLeagueName)
                ? "Select a league"
                : _league.CurrentLeagueName,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#B6FF3B"),
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center
        };

        FlyoutHeader = new VerticalStackLayout
        {
            Padding = new Thickness(20, 28, 20, 16),
            Spacing = 10,
            BackgroundColor = Color.FromArgb("#0D1B2A"),
            Children =
            {
                new Image
                {
                    Source = "cric360_logo.png",
                    HeightRequest = 96,
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = "CRIC360",
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#B6FF3B"),
                    HorizontalOptions = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                _leagueLabel,
                _pendingLabel
            }
        };

        _pendingLabel.HorizontalOptions = LayoutOptions.Center;
        _pendingLabel.HorizontalTextAlignment = TextAlignment.Center;

        var logoutBtn = new Button
        {
            Text = "Logout",
            Margin = new Thickness(16, 8, 16, 16),
            BackgroundColor = Color.FromArgb("#B00020"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 12,
            HeightRequest = 48
        };
        logoutBtn.Clicked += async (_, _) =>
        {
            var ok = await DisplayAlert("Logout", "Sign out of Cric360?", "Logout", "Cancel");
            if (!ok)
                return;

            await _auth.LogoutAsync();
            await _navigator.ShowLoginAsync();
        };

        var footer = new VerticalStackLayout();
        if (_auth.Current?.CanSwitchLeague == true)
        {
            var changeBtn = new Button
            {
                Text = "Change League",
                Margin = new Thickness(16, 8, 16, 0),
                BackgroundColor = Colors.Transparent,
                TextColor = Color.FromArgb("#B6FF3B"),
                BorderColor = Color.FromArgb("#B6FF3B"),
                BorderWidth = 1.5,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = 12,
                HeightRequest = 48
            };
            changeBtn.Clicked += async (_, _) =>
            {
                _league.Clear();
                await _navigator.ShowLeaguePickerAsync();
            };
            footer.Children.Add(changeBtn);
        }

        footer.Children.Add(logoutBtn);
        FlyoutFooter = footer;

        Appearing += async (_, _) =>
        {
            await RefreshLeagueNameAsync().ConfigureAwait(true);
            await RefreshPendingAsync().ConfigureAwait(true);
        };
        // Defer pending-count so shell paints before first SQLite hit.
        _ = Task.Run(async () =>
        {
            await Task.Delay(200).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(RefreshPendingAsync).ConfigureAwait(false);
        });
    }

    private static void EnsureRoutesRegistered(IServiceProvider services)
    {
        // Shell routes are process-global — registering again after logout/login throws and blocks login.
        if (Interlocked.Exchange(ref _routesRegistered, 1) == 1)
            return;

        Routing.RegisterRoute(nameof(PlayerEditPage), new DiRouteFactory<PlayerEditPage>(services));
        Routing.RegisterRoute(nameof(TeamEditPage), new DiRouteFactory<TeamEditPage>(services));
        Routing.RegisterRoute(nameof(MatchEditPage), new DiRouteFactory<MatchEditPage>(services));
        Routing.RegisterRoute(nameof(ScoringPage), new DiRouteFactory<ScoringPage>(services));
        Routing.RegisterRoute(nameof(MatchScorecardPage), new DiRouteFactory<MatchScorecardPage>(services));
        Routing.RegisterRoute(nameof(NetSessionEditPage), new DiRouteFactory<NetSessionEditPage>(services));
        Routing.RegisterRoute(nameof(NetsScoringPage), new DiRouteFactory<NetsScoringPage>(services));
        Routing.RegisterRoute(nameof(NetSessionScorecardPage), new DiRouteFactory<NetSessionScorecardPage>(services));
        Routing.RegisterRoute(nameof(AuctionEditPage), new DiRouteFactory<AuctionEditPage>(services));
        Routing.RegisterRoute(nameof(AuctionReportPage), new DiRouteFactory<AuctionReportPage>(services));
        Routing.RegisterRoute(nameof(UserEditPage), new DiRouteFactory<UserEditPage>(services));
        Routing.RegisterRoute(nameof(VenueBookingEditPage), new DiRouteFactory<VenueBookingEditPage>(services));
        Routing.RegisterRoute(nameof(VenueEditPage), new DiRouteFactory<VenueEditPage>(services));
        Routing.RegisterRoute(nameof(TournamentEditPage), new DiRouteFactory<TournamentEditPage>(services));
        Routing.RegisterRoute(nameof(TournamentDetailPage), new DiRouteFactory<TournamentDetailPage>(services));
        Routing.RegisterRoute(nameof(SeriesEditPage), new DiRouteFactory<SeriesEditPage>(services));
        Routing.RegisterRoute(nameof(SeriesDetailPage), new DiRouteFactory<SeriesDetailPage>(services));
        Routing.RegisterRoute(nameof(LeagueEditPage), new DiRouteFactory<LeagueEditPage>(services));
    }

    private async Task RefreshLeagueNameAsync()
    {
        if (_league.CurrentLeagueId is not Guid id)
            return;
        try
        {
            var name = _league.CurrentLeagueName;
            if (string.IsNullOrWhiteSpace(name))
            {
                var league = await _services.GetRequiredService<ILeagueService>().GetAsync(id).ConfigureAwait(true);
                if (league is not null)
                {
                    _league.Set(league.Id, league.Name);
                    name = league.Name;
                }
            }

            if (!string.IsNullOrWhiteSpace(name))
                _leagueLabel.Text = name;
        }
        catch
        {
            /* keep placeholder */
        }
    }

    private async Task RefreshPendingAsync()
    {
        try
        {
            var n = await _sync.GetPendingCountAsync().ConfigureAwait(true);
            _pendingLabel.Text = n == 0
                ? "Pending sync: 0"
                : $"Pending sync: {n} (use Sync on Matches / Tournaments / Series / Nets)";
        }
        catch
        {
            _pendingLabel.Text = "Pending sync: ?";
        }
    }

    private static FlyoutItem CreateFlyout(string title, string route, string contentRoute, Func<Page> factory)
        => new()
        {
            Title = title,
            Route = route,
            Items =
            {
                new ShellContent
                {
                    Title = title,
                    Route = contentRoute,
                    ContentTemplate = new DataTemplate(() => factory())
                }
            }
        };
}

file sealed class DiRouteFactory<TPage> : RouteFactory where TPage : Page
{
    private readonly IServiceProvider _services;

    public DiRouteFactory(IServiceProvider services) => _services = services;

    public override Element GetOrCreate() => _services.GetRequiredService<TPage>();

    public override Element GetOrCreate(IServiceProvider services)
        => services.GetRequiredService<TPage>();
}
