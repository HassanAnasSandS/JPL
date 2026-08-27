using JPL.Grounds.Api.Data;
using JPL.Grounds.Api.Middleware;
using JPL.Grounds.Api.Services;

namespace JPL.Grounds.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Local + publish default: http://localhost:5026
        // Production HTTPS terminates at reverse proxy (jplground.shoptick.shop).
        builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5026");

        builder.Services.AddControllers();
        builder.Services.AddOpenApi();
        builder.Services.AddCors(o =>
        {
            o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
        });

        builder.Services.AddMemoryCache(o => o.SizeLimit = 16 * 1024 * 1024);
        builder.Services.AddHttpClient(GoogleMapsLocationResolver.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(25);
            c.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-PK,en;q=0.9");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });
        builder.Services.AddSingleton<GroundsDatabase>();
        builder.Services.AddSingleton<IGroundsDbFactory>(sp => sp.GetRequiredService<GroundsDatabase>());
        builder.Services.AddSingleton<GroundImageStore>();
        builder.Services.AddSingleton<GoogleMapsLocationResolver>();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
            app.MapOpenApi();

        app.UseCors();
        app.UseMiddleware<SessionAuthMiddleware>();
        app.MapControllers();

        await InitializeDatabaseAsync(app.Services).ConfigureAwait(false);
        await app.RunAsync().ConfigureAwait(false);
    }

    private static async Task InitializeDatabaseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GroundsDatabase>();
        await db.EnsureCreatedAsync().ConfigureAwait(false);
    }
}
