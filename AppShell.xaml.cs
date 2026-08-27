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
    private const double SidebarWidth = 280;
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
            Items.Add(CreateFlyout("Settings", "groundsettings", "groundsettingshome", () => services.GetRequiredService<GroundSettingsPage>()));
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
            Items.Add(CreateFlyout("Auctions", "auctions", "auctionshome", () => services.GetRequiredService<AuctionsPage>()));
            if (showLeagueCatalog)
            {
                Items.Add(CreateFlyout("Venues", "venues", "venueshome", () => services.GetRequiredService<VenuesPage>()));
                Items.Add(CreateFlyout("Formats", "formats", "formatshome", () => services.GetRequiredService<FormatsPage>()));
                Items.Add(CreateFlyout("Venue Bookings", "venuebookings", "venuebookingshome", () => services.GetRequiredService<VenueBookingsPage>()));
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
        Routing.RegisterRoute(nameof(LeagueEditPage), new DiRouteFactory<LeagueEditPage>(services));
        Routing.RegisterRoute(nameof(GroundDetailPage), new DiRouteFactory<GroundDetailPage>(services));
        Routing.RegisterRoute(nameof(GroundBookRequestPage), new DiRouteFactory<GroundBookRequestPage>(services));
        Routing.RegisterRoute(nameof(OwnerGroundEditPage), new DiRouteFactory<OwnerGroundEditPage>(services));
        Routing.RegisterRoute(nameof(OwnerBookingEditPage), new DiRouteFactory<OwnerBookingEditPage>(services));
    }

    private async Task RefreshLeagueNameAsync()
    {
        if (_workspace.Current is AppWorkspace.Grounds or AppWorkspace.GroundOwner or AppWorkspace.Users or AppWorkspace.Settings)
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
        KeepWindowsPaneOpen();
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
            KeepWindowsPaneOpen();
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
        FlyoutBehavior.Locked;
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
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), KeepWindowsPaneOpen);
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), KeepWindowsPaneOpen);
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

        foreach (var item in Items.OfType<FlyoutItem>())
        {
            var route = item.Route;
            var title = item.Title;
            var label = new Label
            {
                Text = title,
                TextColor = Colors.White,
                FontAttributes = FontAttributes.Bold,
                FontSize = 14,
                VerticalOptions = LayoutOptions.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1
            };
            var border = new Border
            {
                StrokeThickness = 0,
                Padding = new Thickness(12, 0),
                HeightRequest = 44,
                MinimumHeightRequest = 44,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                BackgroundColor = Colors.Transparent,
                HorizontalOptions = LayoutOptions.Fill,
                Content = label
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
            menu.Children.Add(border);
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
        foreach (var (route, border) in _sidebarButtons)
        {
            var selected = !string.IsNullOrWhiteSpace(current) &&
                           string.Equals(route, current, StringComparison.OrdinalIgnoreCase);
            border.BackgroundColor = selected ? Color.FromArgb("#33D4AF37") : Colors.Transparent;
            if (_sidebarLabels.TryGetValue(route, out var label))
                label.TextColor = selected ? Color.FromArgb("#B6FF3B") : Colors.White;
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

            if (PlatformFlyoutBehavior != FlyoutBehavior.Locked)
                FlyoutIsPresented = false;

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
        }
        finally
        {
            Interlocked.Exchange(ref _flyoutNavBusy, 0);
        }
    }

#if WINDOWS
    private void KeepWindowsPaneOpen()
    {
        var nav = _winNav ?? FindNavigationView(Handler?.PlatformView);
        if (nav is null)
            return;

        if (!ReferenceEquals(_winNav, nav))
        {
            if (_winNav is not null)
            {
                _winNav.PaneClosing -= OnWinPaneClosing;
                _winNav.PaneClosed -= OnWinPaneClosed;
            }

            _winNav = nav;
            nav.PaneClosing += OnWinPaneClosing;
            nav.PaneClosed += OnWinPaneClosed;
        }

        nav.PaneDisplayMode = Microsoft.UI.Xaml.Controls.NavigationViewPaneDisplayMode.Left;
        nav.ExpandedModeThresholdWidth = 0;
        nav.CompactModeThresholdWidth = 0;
        nav.CompactPaneLength = SidebarWidth;
        nav.OpenPaneLength = SidebarWidth;
        nav.IsPaneOpen = true;
        nav.IsSettingsVisible = false;
        FlyoutWidth = SidebarWidth;
        FlyoutIsPresented = true;
    }

    private void OnWinPaneClosing(Microsoft.UI.Xaml.Controls.NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewPaneClosingEventArgs args)
    {
        args.Cancel = true;
        sender.IsPaneOpen = true;
        sender.CompactPaneLength = SidebarWidth;
        sender.OpenPaneLength = SidebarWidth;
    }

    private void OnWinPaneClosed(Microsoft.UI.Xaml.Controls.NavigationView sender, object args)
    {
        sender.PaneDisplayMode = Microsoft.UI.Xaml.Controls.NavigationViewPaneDisplayMode.Left;
        sender.CompactPaneLength = SidebarWidth;
        sender.OpenPaneLength = SidebarWidth;
        sender.IsPaneOpen = true;
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
