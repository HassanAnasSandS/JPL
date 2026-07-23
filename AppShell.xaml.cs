using JPL.Pages;

namespace JPL;

public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(PlayerEditPage), new DiRouteFactory<PlayerEditPage>(services));
        Routing.RegisterRoute(nameof(TeamEditPage), new DiRouteFactory<TeamEditPage>(services));

        Items.Clear();

        Items.Add(new FlyoutItem
        {
            Title = "Players",
            Route = "players",
            Items =
            {
                new ShellContent
                {
                    Title = "Players",
                    Route = "playershome",
                    Content = services.GetRequiredService<PlayersPage>()
                }
            }
        });

        Items.Add(new FlyoutItem
        {
            Title = "Teams",
            Route = "teams",
            Items =
            {
                new ShellContent
                {
                    Title = "Teams",
                    Route = "teamshome",
                    Content = services.GetRequiredService<TeamsPage>()
                }
            }
        });
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
