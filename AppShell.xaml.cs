using JPL.Controls;
using JPL.Pages;
using JPL.Services;
using Microsoft.Maui.Controls.Shapes;

namespace JPL;

public partial class AppShell : Shell
{
    private static int _routesRegistered;

    private readonly IAuthService _auth;
    private readonly IAppNavigator _navigator;
    private readonly ILeagueContext _league;
    private readonly IAppWorkspace _workspace;
    private readonly IServiceProvider _services;
    private readonly Label _pendingLabel;
    private readonly Label _leagueLabel;
    private readonly Dictionary<string, Border> _sidebarButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Label> _sidebarLabels = new(StringComparer.OrdinalIgnoreCase);
    private View? _flyoutHeaderView;
    private View? _flyoutFooterView;
    private int _flyoutNavBusy;
    private int _applyingFlyout;
    private readonly Dictionary<string, bool> _groupExpanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Label> _groupChevrons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, View> _groupChildren = new(StringComparer.OrdinalIgnoreCase);
    private const double SidebarWidth = 280;
    private static readonly Color SidebarAccent = Color.FromArgb("#B6FF3B");
    private static readonly Color SidebarSelectedBg = Color.FromArgb("#33D4AF37");

    private static readonly (string Key, string Title, string Icon, string[] Routes)[] SidebarGroups =
    {
        ("players", "Players & Team", "people", ["players", "teams"]),
        ("events", "Matches, Tournaments & Series", "matches", ["matches", "tournaments", "series"]),
        ("venues", "Venues & Bookings", "calendar", ["venues", "venuebookings"]),
        ("stats", "Statistics & Rankings", "stats", ["rankings", "stats"]),
    };
#if WINDOWS
    private Microsoft.UI.Xaml.Controls.NavigationView? _winNav;
#endif

    public AppShell(
        IServiceProvider services,
        IAuthService auth,
        IAppNavigator navigator,
        ILeagueContext league,
        IAppWorkspace workspaceStore)
    {
        InitializeComponent();
        _auth = auth;
        _navigator = navigator;
        _league = league;
        _workspace = workspaceStore;
        _services = services;

        EnsureRoutesRegistered(services);
        Navigated += (_, _) => Dispatcher.Dispatch(EnsureDesktopSidebar);
        HandlerChanged += OnShellHandlerChanged;
        Loaded += (_, _) =>
        {
            ApplyPlatformFlyout();
            EnsureDesktopSidebar();
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CurrentItem))
                UpdateSidebarSelection();
        };

        Items.Clear();

        var session = _auth.Current;
        var currentWorkspace = _workspace.Current;
        if (currentWorkspace == AppWorkspace.Users && session?.CanManagePlatform != true)
            currentWorkspace = AppWorkspace.Leagues;
        if (currentWorkspace == AppWorkspace.Settings && session?.CanManagePlatform != true)
            currentWorkspace = AppWorkspace.Leagues;
        if (currentWorkspace == AppWorkspace.GroundOwner && session?.IsGroundOwner != true && session?.CanManagePlatform != true)
            currentWorkspace = AppWorkspace.Grounds;
        if (currentWorkspace == AppWorkspace.Grounds && session?.IsGroundOwner == true)
            currentWorkspace = AppWorkspace.GroundOwner;
        if (currentWorkspace == AppWorkspace.Scheduler && session?.IsGuest == true)
            currentWorkspace = AppWorkspace.Leagues;

        if (currentWorkspace == AppWorkspace.GroundOwner)
        {
            Items.Add(CreateFlyout("My Grounds", "ownergrounds", "ownergroundshome", () => services.GetRequiredService<OwnerGroundsPage>()));
            Items.Add(CreateFlyout("Bookings", "ownerbookings", "ownerbookingshome", () => services.GetRequiredService<OwnerBookingsPage>()));
            Items.Add(CreateFlyout("Availability", "owneravailability", "owneravailabilityhome", () => services.GetRequiredService<OwnerAvailabilityPage>()));
        }
        else if (currentWorkspace == AppWorkspace.Grounds)
        {
            Items.Add(CreateFlyout("Discover", "grounddiscover", "grounddiscoverhome", () => services.GetRequiredService<GroundDiscoverPage>()));
            Items.Add(CreateFlyout("My Bookings", "customerbookings", "customerbookingshome", () => services.GetRequiredService<CustomerBookingsPage>()));
        }
        else if (currentWorkspace == AppWorkspace.Users)
        {
            Items.Add(CreateFlyout("Users", "users", "usershome", () => services.GetRequiredService<UsersPage>()));
        }
        else if (currentWorkspace == AppWorkspace.Settings)
        {
            Items.Add(CreateFlyout("Amenities", "groundamenities", "groundamenitieshome", () => services.GetRequiredService<AmenitiesPage>()));
            Items.Add(CreateFlyout("Services", "groundservices", "groundserviceshome", () => services.GetRequiredService<ServicesPage>()));
            Items.Add(CreateFlyout("Shop", "groundshop", "groundshophome", () => services.GetRequiredService<ShopCatalogPage>()));
        }
        else if (currentWorkspace == AppWorkspace.Scheduler)
        {
            Items.Add(CreateFlyout("Tournament Scheduler", "tournamentscheduler", "tournamentschedulerhome", () => services.GetRequiredService<TournamentSchedulerPage>()));
        }
        else
        {
            var showLeagueCatalog = session?.CanViewCatalogFor(_league.CurrentLeagueId) == true;
            if (showLeagueCatalog)
            {
                Items.Add(CreateFlyout("Players", "players", "playershome", () => services.GetRequiredService<PlayersPage>()));
                Items.Add(CreateFlyout("Teams", "teams", "teamshome", () => services.GetRequiredService<TeamsPage>()));
            }
            Items.Add(CreateFlyout("Matches", "matches", "matcheshome", () => services.GetRequiredService<MatchesPage>()));
            Items.Add(CreateFlyout("Nets Practice", "nets", "netshome", () => services.GetRequiredService<NetSessionsPage>()));
            Items.Add(CreateFlyout("Tournaments", "tournaments", "tournamentshome", () => services.GetRequiredService<TournamentsPage>()));
            Items.Add(CreateFlyout("Series", "series", "serieshome", () => services.GetRequiredService<SeriesPage>()));
            Items.Add(CreateFlyout("Auction/Drafting", "auctions", "auctionshome", () => services.GetRequiredService<AuctionsPage>()));
            if (showLeagueCatalog)
            {
                Items.Add(CreateFlyout("Venues", "venues", "venueshome", () => services.GetRequiredService<VenuesPage>()));
                Items.Add(CreateFlyout("Venue Bookings", "venuebookings", "venuebookingshome", () => services.GetRequiredService<VenueBookingsPage>()));
                Items.Add(CreateFlyout("Formats", "formats", "formatshome", () => services.GetRequiredService<FormatsPage>()));
            }
            Items.Add(CreateFlyout("Statistics", "stats", "statshome", () => services.GetRequiredService<StatsPage>()));
            Items.Add(CreateFlyout("Rankings", "rankings", "rankingshome", () => services.GetRequiredService<RankingsPage>()));
            Items.Add(CreateFlyout("Hall of Fame", "halloffame", "halloffamehome", () => services.GetRequiredService<HallOfFamePage>()));
            Items.Add(CreateFlyout("Strength Meter", "strengthmeter", "strengthmeterhome", () => services.GetRequiredService<StrengthMeterPage>()));
        }

        _pendingLabel = new Label
        {
            Text = currentWorkspace is AppWorkspace.Grounds or AppWorkspace.GroundOwner
                ? "Pakistan"
                : currentWorkspace == AppWorkspace.Users
                    ? "Accounts"
                    : currentWorkspace == AppWorkspace.Settings
                        ? "Catalog"
                        : currentWorkspace == AppWorkspace.Scheduler
                            ? "Manual"
                            : "Live",
            FontSize = 12,
            TextColor = Color.FromArgb("#B8C4D4"),
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 2
        };

        _leagueLabel = new Label
        {
            Text = currentWorkspace == AppWorkspace.GroundOwner
                ? "Ground Owner"
                : currentWorkspace == AppWorkspace.Grounds
                    ? "Ground Bookings"
                    : currentWorkspace == AppWorkspace.Users
                        ? "Users"
                        : currentWorkspace == AppWorkspace.Settings
                            ? "Settings"
                            : currentWorkspace == AppWorkspace.Scheduler
                                ? "Tournament Scheduler"
                                : string.IsNullOrWhiteSpace(_league.CurrentLeagueName)
                                ? "Select a league"
                                : _league.CurrentLeagueName,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#B6FF3B"),
            HorizontalOptions = LayoutOptions.Fill,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        var header = new VerticalStackLayout
        {
            Padding = new Thickness(16, 20, 16, 12),
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = Color.FromArgb("#0D1B2A"),
            Children =
            {
                new Image
                {
                    Source = "cric360_logo.png",
                    HeightRequest = 72,
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
                new SloganTicker
                {
                    FontSize = 11,
                    TextColor = Color.FromArgb("#B8C4D4"),
                    HorizontalOptions = LayoutOptions.Center,
                    IntervalMs = 3800
                },
                _leagueLabel,
                _pendingLabel
            }
        };
        _flyoutHeaderView = header;

        _pendingLabel.HorizontalOptions = LayoutOptions.Center;
        _pendingLabel.HorizontalTextAlignment = TextAlignment.Center;

        var nameLabel = new Label
        {
            Text = session?.SignedInName ?? string.Empty,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Fill,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            Margin = new Thickness(16, 10, 16, 0)
        };
        var roleLabel = new Label
        {
            Text = session?.RoleLabel ?? string.Empty,
            FontSize = 12,
            TextColor = Color.FromArgb("#B6FF3B"),
            HorizontalOptions = LayoutOptions.Fill,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            Margin = new Thickness(16, 2, 16, 6)
        };

        var logoutBtn = new Button
        {
            Text = "Logout",
            StyleId = "SkipBusy",
            Margin = new Thickness(16, 8, 16, 16),
            BackgroundColor = Color.FromArgb("#B00020"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 12,
            HeightRequest = 44
        };
        logoutBtn.Clicked += async (_, _) =>
        {
            var ok = await DisplayAlert("Logout", "Sign out of Cric360?", "Logout", "Cancel");
            if (!ok)
                return;

            await AppBusy.RunAsync(async () =>
            {
                await _auth.LogoutAsync();
                await _navigator.ShowLoginAsync();
            }).ConfigureAwait(true);
        };

        if (session is not null)
        {
            var homeBtn = new Button
            {
                Text = "Home",
                Margin = new Thickness(0, 10, 0, 0),
                BackgroundColor = Colors.Transparent,
                TextColor = Color.FromArgb("#B6FF3B"),
                BorderColor = Color.FromArgb("#B6FF3B"),
                BorderWidth = 1.5,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = 12,
                HeightRequest = 44
            };
            homeBtn.Clicked += async (_, _) =>
            {
                await AppBusy.RunAsync(async () =>
                {
                    _workspace.Clear();
                    await _navigator.ShowModeChooserAsync();
                }).ConfigureAwait(true);
            };
            header.Children.Add(homeBtn);
        }

        var footer = new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Fill
        };

        if (!string.IsNullOrWhiteSpace(nameLabel.Text))
            footer.Children.Add(nameLabel);
        if (!string.IsNullOrWhiteSpace(roleLabel.Text) &&
            !string.Equals(roleLabel.Text, nameLabel.Text, StringComparison.OrdinalIgnoreCase))
            footer.Children.Add(roleLabel);
        footer.Children.Add(logoutBtn);
        _flyoutFooterView = footer;
        FlyoutHeader = null;
        FlyoutFooter = null;
        BuildSidebarContent();

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

        ApplyPlatformFlyout();
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
        Routing.RegisterRoute(nameof(MatchAnalyticsPage), new DiRouteFactory<MatchAnalyticsPage>(services));
        Routing.RegisterRoute(nameof(NetSessionEditPage), new DiRouteFactory<NetSessionEditPage>(services));
        Routing.RegisterRoute(nameof(NetsScoringPage), new DiRouteFactory<NetsScoringPage>(services));
        Routing.RegisterRoute(nameof(NetSessionScorecardPage), new DiRouteFactory<NetSessionScorecardPage>(services));
        Routing.RegisterRoute(nameof(AuctionEditPage), new DiRouteFactory<AuctionEditPage>(services));
        Routing.RegisterRoute(nameof(AuctionReportPage), new DiRouteFactory<AuctionReportPage>(services));
        Routing.RegisterRoute(nameof(UserEditPage), new DiRouteFactory<UserEditPage>(services));
        Routing.RegisterRoute(nameof(VenueBookingEditPage), new DiRouteFactory<VenueBookingEditPage>(services));
        Routing.RegisterRoute(nameof(VenueEditPage), new DiRouteFactory<VenueEditPage>(services));
        Routing.RegisterRoute(nameof(FormatEditPage), new DiRouteFactory<FormatEditPage>(services));
        Routing.RegisterRoute(nameof(TournamentEditPage), new DiRouteFactory<TournamentEditPage>(services));
        Routing.RegisterRoute(nameof(TournamentDetailPage), new DiRouteFactory<TournamentDetailPage>(services));
        Routing.RegisterRoute(nameof(SeriesEditPage), new DiRouteFactory<SeriesEditPage>(services));
        Routing.RegisterRoute(nameof(SeriesDetailPage), new DiRouteFactory<SeriesDetailPage>(services));
        Routing.RegisterRoute(nameof(TournamentSchedulerEditPage), new DiRouteFactory<TournamentSchedulerEditPage>(services));
        Routing.RegisterRoute(nameof(TournamentSchedulerDetailPage), new DiRouteFactory<TournamentSchedulerDetailPage>(services));
        Routing.RegisterRoute(nameof(LeagueEditPage), new DiRouteFactory<LeagueEditPage>(services));
        Routing.RegisterRoute(nameof(GroundDetailPage), new DiRouteFactory<GroundDetailPage>(services));
        Routing.RegisterRoute(nameof(GroundBookRequestPage), new DiRouteFactory<GroundBookRequestPage>(services));
        Routing.RegisterRoute(nameof(GroundReviewPage), new DiRouteFactory<GroundReviewPage>(services));
        Routing.RegisterRoute(nameof(OwnerGroundEditPage), new DiRouteFactory<OwnerGroundEditPage>(services));
        Routing.RegisterRoute(nameof(OwnerBookingCreatePage), new DiRouteFactory<OwnerBookingCreatePage>(services));
        Routing.RegisterRoute(nameof(OwnerBookingEditPage), new DiRouteFactory<OwnerBookingEditPage>(services));
        Routing.RegisterRoute(nameof(AmenityEditPage), new DiRouteFactory<AmenityEditPage>(services));
        Routing.RegisterRoute(nameof(ServiceEditPage), new DiRouteFactory<ServiceEditPage>(services));
        Routing.RegisterRoute(nameof(ShopCatalogEditPage), new DiRouteFactory<ShopCatalogEditPage>(services));
    }

    private async Task RefreshLeagueNameAsync()
    {
        if (_workspace.Current is AppWorkspace.Grounds or AppWorkspace.GroundOwner or AppWorkspace.Users or AppWorkspace.Settings or AppWorkspace.Scheduler)
            return;
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
        if (_workspace.Current is AppWorkspace.Grounds or AppWorkspace.GroundOwner)
        {
            _pendingLabel.Text = "Pakistan";
            return;
        }
        if (_workspace.Current == AppWorkspace.Users)
        {
            _pendingLabel.Text = "Accounts";
            return;
        }
        if (_workspace.Current == AppWorkspace.Settings)
        {
            _pendingLabel.Text = "Catalog";
            return;
        }
        if (_workspace.Current == AppWorkspace.Scheduler)
        {
            _pendingLabel.Text = "Manual";
            return;
        }
        try
        {
            var live = _services.GetRequiredService<ILivePublisher>();
            var n = await live.GetPendingCountAsync().ConfigureAwait(true);
            _pendingLabel.Text = n == 0
                ? "Live"
                : n == 1
                    ? "Offline: 1 update will go live when internet returns"
                    : $"Offline: {n} updates will go live when internet returns";
        }
        catch
        {
            _pendingLabel.Text = "Live status unavailable";
        }
    }

    internal void EnsureDesktopSidebar()
    {
        if (CurrentPage is not null)
            Shell.SetFlyoutBehavior(CurrentPage, PlatformFlyoutBehavior);

        if (PlatformFlyoutBehavior == FlyoutBehavior.Locked)
            FlyoutIsPresented = true;

#if WINDOWS
        ConfigureWindowsPane();
#endif
    }

    internal void ApplyPlatformFlyout()
    {
        if (Interlocked.Exchange(ref _applyingFlyout, 1) == 1)
            return;

        try
        {
            var behavior = PlatformFlyoutBehavior;
            FlyoutBehavior = behavior;
            FlyoutWidth = ResolveFlyoutWidth();
            if (behavior == FlyoutBehavior.Locked)
                FlyoutIsPresented = true;

            if (CurrentPage is not null)
                Shell.SetFlyoutBehavior(CurrentPage, behavior);

#if WINDOWS
            ConfigureWindowsPane();
#endif
        }
        finally
        {
            Interlocked.Exchange(ref _applyingFlyout, 0);
        }
    }

    private static double ResolveFlyoutWidth()
    {
        if (PlatformFlyoutBehavior == FlyoutBehavior.Locked)
            return SidebarWidth;

        try
        {
            var display = DeviceDisplay.MainDisplayInfo;
            var pageWidth = display.Width / display.Density;
            if (pageWidth > 0)
                return Math.Clamp(pageWidth * 0.82, 240, SidebarWidth);
        }
        catch
        {
            /* keep default */
        }

        return SidebarWidth;
    }

    private static FlyoutBehavior PlatformFlyoutBehavior =>
#if WINDOWS || MACCATALYST
        FlyoutBehavior.Flyout;
#else
        DeviceInfo.Idiom == DeviceIdiom.Desktop ? FlyoutBehavior.Locked : FlyoutBehavior.Flyout;
#endif

    private void OnShellHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        _winNav = null;
#endif
        ApplyPlatformFlyout();
#if WINDOWS
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), ConfigureWindowsPane);
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), ConfigureWindowsPane);
#endif
    }

    private void BuildSidebarContent()
    {
        _sidebarButtons.Clear();
        _sidebarLabels.Clear();
        var menu = new VerticalStackLayout
        {
            Padding = new Thickness(8, 4),
            Spacing = 4,
            HorizontalOptions = LayoutOptions.Fill
        };

        _groupChevrons.Clear();
        _groupChildren.Clear();
        var flyouts = Items.OfType<FlyoutItem>().ToList();
        var byRoute = flyouts
            .Where(f => !string.IsNullOrWhiteSpace(f.Route))
            .ToDictionary(f => f.Route, StringComparer.OrdinalIgnoreCase);
        var groupedRoutes = new HashSet<string>(
            SidebarGroups.SelectMany(g => g.Routes),
            StringComparer.OrdinalIgnoreCase);
        var emittedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in flyouts)
        {
            var route = item.Route ?? string.Empty;
            var group = SidebarGroups.FirstOrDefault(g =>
                g.Routes.Any(r => string.Equals(r, route, StringComparison.OrdinalIgnoreCase)));
            if (group.Key is not null)
            {
                if (!emittedGroups.Add(group.Key))
                    continue;
                var children = group.Routes
                    .Select(r => byRoute.TryGetValue(r, out var flyout) ? flyout : null)
                    .Where(f => f is not null)
                    .Cast<FlyoutItem>()
                    .ToList();
                if (children.Count == 0)
                    continue;
                menu.Children.Add(CreateFlyoutGroup(group.Key, group.Title, group.Icon, children));
                continue;
            }

            if (groupedRoutes.Contains(route))
                continue;

            menu.Children.Add(CreateSidebarButton(item.Route, item.Title, iconKey: IconKeyForRoute(item.Route)));
        }

        var scroll = new ScrollView
        {
            Content = menu,
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Always,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill
        };
        scroll.HandlerChanged += OnFlyoutScrollHandlerChanged;

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = Color.FromArgb("#0D1B2A")
        };
        if (_flyoutHeaderView is not null)
            root.Add(_flyoutHeaderView, 0, 0);
        root.Add(scroll, 0, 1);
        if (_flyoutFooterView is not null)
            root.Add(_flyoutFooterView, 0, 2);

        var busyLayer = AppBusy.CreateLayer();
        Grid.SetRowSpan(busyLayer, 3);
        root.Add(busyLayer, 0, 0);

        AppBusy.Changed += visible =>
        {
            if (_flyoutHeaderView is not null)
                _flyoutHeaderView.IsEnabled = !visible;
            scroll.IsEnabled = !visible;
            if (_flyoutFooterView is not null)
                _flyoutFooterView.IsEnabled = !visible;
        };

        FlyoutContent = root;
        UpdateSidebarSelection();
    }

    private View CreateFlyoutGroup(string key, string title, string iconKey, IReadOnlyList<FlyoutItem> items)
    {
        var expanded = _groupExpanded.GetValueOrDefault(key, true);
        _groupExpanded[key] = expanded;

        var chevron = new Label
        {
            Text = expanded ? "▾" : "▸",
            TextColor = Colors.White,
            FontSize = 16,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };
        _groupChevrons[key] = chevron;

        var parentLabel = new Label
        {
            Text = title,
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            HorizontalOptions = LayoutOptions.Fill
        };

        var parentRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8,
            VerticalOptions = LayoutOptions.Center
        };
        parentRow.Add(CreateSidebarIcon(iconKey), 0, 0);
        parentRow.Add(parentLabel, 1, 0);
        parentRow.Add(chevron, 2, 0);

        var parentBorder = new Border
        {
            StrokeThickness = 0,
            Padding = new Thickness(12, 0),
            HeightRequest = 44,
            MinimumHeightRequest = 44,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            Content = parentRow
        };
        var parentTap = new TapGestureRecognizer();
        parentTap.Tapped += (_, _) => ToggleFlyoutGroup(key);
        parentBorder.GestureRecognizers.Add(parentTap);

        var children = new VerticalStackLayout
        {
            Spacing = 2,
            Padding = new Thickness(8, 0, 0, 4),
            IsVisible = expanded
        };
        foreach (var child in items)
            children.Add(CreateSidebarButton(child.Route, child.Title, isSubItem: true));
        _groupChildren[key] = children;

        return new VerticalStackLayout
        {
            Spacing = 2,
            Children = { parentBorder, children }
        };
    }

    private void ToggleFlyoutGroup(string key)
    {
        var expanded = !_groupExpanded.GetValueOrDefault(key, true);
        _groupExpanded[key] = expanded;
        if (_groupChildren.TryGetValue(key, out var children))
            children.IsVisible = expanded;
        if (_groupChevrons.TryGetValue(key, out var chevron))
            chevron.Text = expanded ? "▾" : "▸";
    }

    private static string IconKeyForRoute(string? route) => route?.ToLowerInvariant() switch
    {
        "nets" => "nets",
        "auctions" => "auction",
        "formats" => "formats",
        "halloffame" => "fame",
        "strengthmeter" => "strength",
        "ownergrounds" => "grounds",
        "ownerbookings" => "bookings",
        "owneravailability" => "availability",
        "grounddiscover" => "discover",
        "customerbookings" => "bookings",
        "users" => "users",
        "groundamenities" => "formats",
        "groundservices" => "formats",
        "groundshop" => "shop",
        "tournamentscheduler" => "scheduler",
        _ => "dot"
    };

    private static View CreateSidebarIcon(string key) => key switch
    {
        "people" or "users" => CreatePeopleIcon(),
        "calendar" or "bookings" or "scheduler" => CreateCalendarIcon(),
        "matches" or "grounds" => CreateBallIcon(),
        "stats" => CreateBarsIcon(),
        "nets" => CreateGridIcon(),
        "auction" => CreateDiamondIcon(),
        "formats" or "availability" => CreateLinesIcon(),
        "fame" => CreateStarIcon(),
        "strength" => CreateGaugeIcon(),
        "discover" => CreateSearchIcon(),
        "shop" => CreateBoxIcon(),
        _ => CreateDotIcon()
    };

    private static View CreateCalendarIcon()
    {
        return new Border
        {
            WidthRequest = 16,
            HeightRequest = 16,
            Stroke = SidebarAccent,
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 2 },
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(0, 3, 0, 0),
            Content = new BoxView
            {
                Color = SidebarAccent,
                HeightRequest = 3,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Start
            }
        };
    }

    private static View CreatePeopleIcon()
    {
        var grid = new Grid
        {
            WidthRequest = 16,
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Center
        };
        grid.Add(new Ellipse
        {
            Fill = SidebarAccent,
            WidthRequest = 8,
            HeightRequest = 8,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        });
        grid.Add(new Ellipse
        {
            Fill = SidebarAccent,
            WidthRequest = 8,
            HeightRequest = 8,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center
        });
        return grid;
    }

    private static View CreateBallIcon()
        => new Ellipse
        {
            Stroke = SidebarAccent,
            StrokeThickness = 1.8,
            Fill = Colors.Transparent,
            WidthRequest = 16,
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Center
        };

    private static View CreateBarsIcon()
        => new HorizontalStackLayout
        {
            Spacing = 2,
            HeightRequest = 16,
            WidthRequest = 16,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new BoxView { Color = SidebarAccent, WidthRequest = 4, HeightRequest = 7, VerticalOptions = LayoutOptions.End },
                new BoxView { Color = SidebarAccent, WidthRequest = 4, HeightRequest = 11, VerticalOptions = LayoutOptions.End },
                new BoxView { Color = SidebarAccent, WidthRequest = 4, HeightRequest = 16, VerticalOptions = LayoutOptions.End }
            }
        };

    private static View CreateGridIcon()
    {
        var grid = new Grid
        {
            WidthRequest = 16,
            HeightRequest = 16,
            RowDefinitions = { new RowDefinition(), new RowDefinition() },
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() },
            RowSpacing = 2,
            ColumnSpacing = 2,
            VerticalOptions = LayoutOptions.Center
        };
        for (var r = 0; r < 2; r++)
        for (var c = 0; c < 2; c++)
        {
            var cell = new BoxView { Color = SidebarAccent };
            grid.Add(cell, c, r);
        }
        return grid;
    }

    private static View CreateDiamondIcon()
        => new Label
        {
            Text = "◆",
            TextColor = SidebarAccent,
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            WidthRequest = 16
        };

    private static View CreateLinesIcon()
        => new VerticalStackLayout
        {
            Spacing = 3,
            WidthRequest = 16,
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new BoxView { Color = SidebarAccent, HeightRequest = 2 },
                new BoxView { Color = SidebarAccent, HeightRequest = 2 },
                new BoxView { Color = SidebarAccent, HeightRequest = 2 }
            }
        };

    private static View CreateStarIcon()
        => new Label
        {
            Text = "★",
            TextColor = SidebarAccent,
            FontSize = 15,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 16,
            HorizontalTextAlignment = TextAlignment.Center
        };

    private static View CreateGaugeIcon()
        => new Ellipse
        {
            Stroke = SidebarAccent,
            StrokeThickness = 2,
            Fill = SidebarAccent,
            WidthRequest = 12,
            HeightRequest = 12,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };

    private static View CreateSearchIcon()
        => new Ellipse
        {
            Stroke = SidebarAccent,
            StrokeThickness = 2,
            Fill = Colors.Transparent,
            WidthRequest = 14,
            HeightRequest = 14,
            VerticalOptions = LayoutOptions.Center
        };

    private static View CreateBoxIcon()
        => new Border
        {
            WidthRequest = 16,
            HeightRequest = 16,
            Stroke = SidebarAccent,
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 2 },
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center
        };

    private static View CreateDotIcon()
        => new BoxView
        {
            Color = SidebarAccent,
            WidthRequest = 8,
            HeightRequest = 8,
            CornerRadius = 4,
            VerticalOptions = LayoutOptions.Center
        };

    private Border CreateSidebarButton(string? route, string title, bool isSubItem = false, string? iconKey = null)
    {
        var label = new Label
        {
            Text = title,
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = isSubItem ? 13 : 14,
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            HorizontalOptions = LayoutOptions.Fill
        };

        View content;
        if (isSubItem)
        {
            content = new HorizontalStackLayout
            {
                Spacing = 10,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new BoxView
                    {
                        Color = SidebarAccent,
                        WidthRequest = 6,
                        HeightRequest = 6,
                        CornerRadius = 3,
                        VerticalOptions = LayoutOptions.Center
                    },
                    label
                }
            };
        }
        else if (!string.IsNullOrWhiteSpace(iconKey))
        {
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 8,
                VerticalOptions = LayoutOptions.Center
            };
            row.Add(CreateSidebarIcon(iconKey), 0, 0);
            row.Add(label, 1, 0);
            content = row;
        }
        else
        {
            content = label;
        }

        var border = new Border
        {
            StrokeThickness = 0,
            Padding = new Thickness(isSubItem ? 20 : 12, 0),
            HeightRequest = isSubItem ? 40 : 44,
            MinimumHeightRequest = isSubItem ? 40 : 44,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            Content = content
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
            await AppBusy.RunAsync(() => GoToFlyoutRootAsync(route, title)).ConfigureAwait(true);
        border.GestureRecognizers.Add(tap);
        if (!string.IsNullOrWhiteSpace(route))
        {
            _sidebarButtons[route] = border;
            _sidebarLabels[route] = label;
        }

        return border;
    }

    private static void OnFlyoutScrollHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        if (sender is not ScrollView scroll)
            return;
        if (scroll.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.ScrollViewer win)
            return;

        win.VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Visible;
        win.VerticalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Enabled;
        win.HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled;
        win.HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden;
#endif
    }

    private void UpdateSidebarSelection()
    {
        var current = CurrentItem?.Route;
        foreach (var group in SidebarGroups)
        {
            var inGroup = group.Routes.Any(r =>
                string.Equals(r, current, StringComparison.OrdinalIgnoreCase));
            if (!inGroup)
                continue;
            _groupExpanded[group.Key] = true;
            if (_groupChildren.TryGetValue(group.Key, out var children))
                children.IsVisible = true;
            if (_groupChevrons.TryGetValue(group.Key, out var chevron))
                chevron.Text = "▾";
        }

        foreach (var (route, border) in _sidebarButtons)
        {
            var selected = !string.IsNullOrWhiteSpace(current) &&
                           string.Equals(route, current, StringComparison.OrdinalIgnoreCase);
            border.BackgroundColor = selected ? SidebarSelectedBg : Colors.Transparent;
            if (_sidebarLabels.TryGetValue(route, out var label))
                label.TextColor = selected ? SidebarAccent : Colors.White;
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (FlyoutBehavior != FlyoutBehavior.Locked && FlyoutIsPresented)
        {
            FlyoutIsPresented = false;
            return true;
        }

        var visible = CurrentPage;
        if (visible is not null && visible != this && visible.SendBackButtonPressed())
            return true;

        var nav = Current?.Navigation ?? Navigation;
        if (nav.ModalStack.Count > 0)
        {
            DispatchNav(() => nav.PopModalAsync());
            return true;
        }

        var sectionStack = CurrentItem?.CurrentItem?.Stack?.Count ?? 0;
        if (sectionStack > 1 || nav.NavigationStack.Count > 1 || CanPopShellLocation())
        {
            DispatchNav(async () =>
            {
                try
                {
                    await GoToAsync("..").ConfigureAwait(true);
                }
                catch
                {
                    if (nav.NavigationStack.Count > 1)
                        await nav.PopAsync().ConfigureAwait(true);
                }
            });
            return true;
        }

        // Root screen of a module — go Home instead of exiting the app.
        if (_auth.IsAuthenticated)
        {
            DispatchNav(async () =>
            {
                _workspace.Clear();
                await _navigator.ShowModeChooserAsync().ConfigureAwait(true);
            });
            return true;
        }

        return false;
    }

    private bool CanPopShellLocation()
    {
        try
        {
            var loc = CurrentState?.Location?.OriginalString ?? string.Empty;
            var parts = loc.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 2;
        }
        catch
        {
            return false;
        }
    }

    private void DispatchNav(Func<Task> action)
    {
        Dispatcher.Dispatch(() => _ = SafeNavAsync(action));
    }

    private static async Task SafeNavAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch
        {
            /* ignore */
        }
    }

    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        if (args.Source == ShellNavigationSource.ShellItemChanged)
            RemovePushedPages();
        base.OnNavigating(args);
    }

    private void RemovePushedPages()
    {
        try
        {
            var nav = Current?.Navigation;
            if (nav is null)
                return;
            foreach (var page in nav.NavigationStack.Skip(1).ToList())
                nav.RemovePage(page);
        }
        catch
        {
            /* ignore */
        }
    }

    private async Task GoToFlyoutRootAsync(string? route, string? title = null)
    {
        if (Interlocked.Exchange(ref _flyoutNavBusy, 1) == 1)
            return;

        try
        {
            var item = Items.OfType<FlyoutItem>().FirstOrDefault(f =>
                (!string.IsNullOrWhiteSpace(route) &&
                 string.Equals(f.Route, route, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(title) &&
                 string.Equals(f.Title, title, StringComparison.OrdinalIgnoreCase)));
            if (item is null || string.IsNullOrWhiteSpace(item.Route))
                return;

            try
            {
                await GoToAsync($"//{item.Route}").ConfigureAwait(true);
            }
            catch
            {
                CurrentItem = item;
                var nav = Current?.Navigation;
                if (nav is not null && nav.NavigationStack.Count > 1)
                    await nav.PopToRootAsync().ConfigureAwait(true);
            }

            UpdateSidebarSelection();
            EnsureDesktopSidebar();
            if (PlatformFlyoutBehavior != FlyoutBehavior.Locked)
                FlyoutIsPresented = false;
        }
        finally
        {
            Interlocked.Exchange(ref _flyoutNavBusy, 0);
        }
    }

#if WINDOWS
    private void ConfigureWindowsPane()
    {
        var nav = _winNav ?? FindNavigationView(Handler?.PlatformView);
        if (nav is null)
            return;

        _winNav = nav;
        nav.IsSettingsVisible = false;
        nav.IsPaneToggleButtonVisible = true;
        nav.OpenPaneLength = SidebarWidth;
        nav.PaneDisplayMode = Microsoft.UI.Xaml.Controls.NavigationViewPaneDisplayMode.LeftMinimal;
        FlyoutWidth = SidebarWidth;
    }

    private static Microsoft.UI.Xaml.Controls.NavigationView? FindNavigationView(object? platformView)
    {
        if (platformView is Microsoft.UI.Xaml.Controls.NavigationView nav)
            return nav;
        if (platformView is not Microsoft.UI.Xaml.DependencyObject root)
            return null;

        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindNavigationView(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }

        return null;
    }
#endif

    private static FlyoutItem CreateFlyout(string title, string route, string contentRoute, Func<Page> factory)
    {
        var item = new FlyoutItem
        {
            Title = title,
            Route = route,
            FlyoutDisplayOptions = FlyoutDisplayOptions.AsSingleItem,
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
        // Keep the item in Shell.Items for navigation, but do not draw the default
        // flyout row (Android otherwise stacks those separator lines over our labels).
        Shell.SetFlyoutItemIsVisible(item, false);
        return item;
    }
}

file sealed class DiRouteFactory<TPage> : RouteFactory where TPage : Page
{
    private readonly IServiceProvider _services;

    public DiRouteFactory(IServiceProvider services) => _services = services;

    public override Element GetOrCreate() => _services.GetRequiredService<TPage>();

    public override Element GetOrCreate(IServiceProvider services)
        => services.GetRequiredService<TPage>();
}
