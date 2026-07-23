using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using JPL.Data;
using JPL.Pages;
using JPL.Services;
using JPL.ViewModels;

namespace JPL;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddDbContextFactory<JplDbContext>((sp, options) =>
        {
            options.ConfigureSqlite();
        });

        builder.Services.AddSingleton<IOutboxWriter, OutboxWriter>();
        builder.Services.AddSingleton<ISqlServerSyncClient, NoOpSqlServerSyncClient>();
        builder.Services.AddSingleton<SyncService>();
        builder.Services.AddSingleton<ISyncService>(sp => sp.GetRequiredService<SyncService>());
        builder.Services.AddSingleton<ISyncTrigger>(sp => sp.GetRequiredService<SyncService>());

        builder.Services.AddSingleton<IPlayerService, PlayerService>();
        builder.Services.AddSingleton<ITeamService, TeamService>();

        builder.Services.AddTransient<PlayersViewModel>();
        builder.Services.AddTransient<PlayerEditViewModel>();
        builder.Services.AddTransient<TeamsViewModel>();
        builder.Services.AddTransient<TeamEditViewModel>();

        builder.Services.AddTransient<PlayersPage>();
        builder.Services.AddTransient<PlayerEditPage>();
        builder.Services.AddTransient<TeamsPage>();
        builder.Services.AddTransient<TeamEditPage>();
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // Local DB ready before first UI frame; sync stays background.
        InitializeDatabase(app.Services);

        return app;
    }

    private static void InitializeDatabase(IServiceProvider services)
    {
        try
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JplDbContext>>();
            using var db = factory.CreateDbContext();
            DbBootstrap.InitializeAsync(db).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DB init failed: {ex}");
            throw;
        }
    }
}
