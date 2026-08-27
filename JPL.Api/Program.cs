using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Data;
using JPL.Api.Models;
using JPL.Api.Services;

namespace JPL.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Local + publish default: http://localhost:5025 (override with ASPNETCORE_URLS on host)
        builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5025");

        builder.Services.AddControllers();
        builder.Services.AddOpenApi();
        builder.Services.AddSignalR();
        builder.Services.AddCors(o =>
        {
            o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
        });

        builder.Services.AddMemoryCache(o => o.SizeLimit = 32 * 1024 * 1024);
        builder.Services.AddSingleton<NeuralCommentaryTts>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IApiLeagueScope, ApiLeagueScope>();
        builder.Services.AddSingleton<PlayerImageStore>();
        builder.Services.AddSingleton<TeamImageStore>();
        builder.Services.AddSingleton<LeagueDatabaseManager>();
        builder.Services.AddScoped<IApiDbFactory, ApiDbFactory>();
        builder.Services.AddScoped(sp => sp.GetRequiredService<IApiDbFactory>().CreateForRequest());
        builder.Services.AddScoped<ILiveFeed, LiveFeed>();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
            app.MapOpenApi();

        app.UseCors();
        app.Use(async (ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                if (!ctx.Response.Headers.ContainsKey("Referrer-Policy"))
                    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                return Task.CompletedTask;
            });
            await next();
        });
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                if (ctx.File.Name.Equals("favicon.ico", StringComparison.OrdinalIgnoreCase)
                    || ctx.File.Name.Equals("favicon.png", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Context.Response.Headers.CacheControl = "public, max-age=0, must-revalidate";
                }
            }
        });
        // Local is HTTP :5025; production HTTPS terminates at reverse proxy (jpl.shoptick.shop).
        // Do not force HTTPS redirect on the Kestrel app itself.
        app.UseMiddleware<JPL.Api.Middleware.SessionAuthMiddleware>();
        app.UseMiddleware<JPL.Api.Middleware.LeagueWriteGuardMiddleware>();
        app.MapControllers();
        app.MapHub<JPL.Api.Hubs.LiveHub>("/hubs/live");

        await InitializeDatabaseAsync(app.Services).ConfigureAwait(false);

        await app.RunAsync().ConfigureAwait(false);
    }

    private static async Task InitializeDatabaseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbManager = scope.ServiceProvider.GetRequiredService<LeagueDatabaseManager>();
        await dbManager.EnsureMasterCatalogAsync().ConfigureAwait(false);

        await using var db = dbManager.CreateMaster();

        await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
        await EnsureMasterSchemaAsync(db).ConfigureAwait(false);
        await EnsureDefaultUsersAsync(db).ConfigureAwait(false);
        await dbManager.DropMasterSportsTablesAsync().ConfigureAwait(false);
        await dbManager.EnsureAllLeagueDatabasesAsync(db).ConfigureAwait(false);
    }

    private static async Task EnsureMasterSchemaAsync(MasterDbContext db)
    {
        var provider = db.Database.ProviderName ?? "";
        var sqlServer = provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
        if (sqlServer)
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('Leagues', 'DatabaseName') IS NULL
                    ALTER TABLE Leagues ADD DatabaseName nvarchar(128) NULL;
                """).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('Users', 'LeagueId') IS NULL
                    ALTER TABLE Users ADD LeagueId uniqueidentifier NULL;
                """).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('Users', 'WhatsAppNumber') IS NULL
                    ALTER TABLE Users ADD WhatsAppNumber nvarchar(30) NULL;
                """).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.LeagueLiveIndexes', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.LeagueLiveIndexes (
                        LeagueId uniqueidentifier NOT NULL CONSTRAINT PK_LeagueLiveIndexes PRIMARY KEY,
                        LiveMatchCount int NOT NULL,
                        LiveNetsCount int NOT NULL,
                        UpdatedAtUtc datetimeoffset NOT NULL
                    );
                END
                """).ConfigureAwait(false);
        }
        else
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Leagues" ADD COLUMN "DatabaseName" TEXT NULL;""")
                    .ConfigureAwait(false);
            }
            catch { /* already exists */ }
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Users" ADD COLUMN "LeagueId" TEXT NULL;""")
                    .ConfigureAwait(false);
            }
            catch { /* already exists */ }
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE "Users" ADD COLUMN "WhatsAppNumber" TEXT NULL;""")
                    .ConfigureAwait(false);
            }
            catch { /* already exists */ }
            try
            {
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS "LeagueLiveIndexes" (
                        "LeagueId" TEXT NOT NULL CONSTRAINT "PK_LeagueLiveIndexes" PRIMARY KEY,
                        "LiveMatchCount" INTEGER NOT NULL,
                        "LiveNetsCount" INTEGER NOT NULL,
                        "UpdatedAtUtc" TEXT NOT NULL
                    );
                    """).ConfigureAwait(false);
            }
            catch { /* already exists */ }
        }
    }


    private static async Task EnsureDefaultUsersAsync(MasterDbContext db)
    {
        var hasher = new PasswordHasher<AppUser>();

        async Task EnsureUser(string username, string password, AppRole role, Guid? leagueId)
        {
            if (await db.Users.AnyAsync(u => u.Username == username).ConfigureAwait(false))
                return;
            var user = new AppUser
            {
                Username = username,
                Role = role,
                LeagueId = leagueId,
                IsActive = true,
                CreatedAtUtc = DateTime.Now
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        var stale = await db.Users
            .Where(u => u.Username == "AmaanRehan")
            .ToListAsync()
            .ConfigureAwait(false);
        if (stale.Count > 0)
        {
            db.Users.RemoveRange(stale);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        await EnsureUser("user", "user123", AppRole.Guest, null).ConfigureAwait(false);
        await EnsureUser("groundowner", "ground123", AppRole.GroundOwner, null).ConfigureAwait(false);
        await EnsureSoleSuperAdminAsync(db, hasher).ConfigureAwait(false);

        var guest = await db.Users.FirstOrDefaultAsync(u => u.Username == "user").ConfigureAwait(false);
        if (guest is not null)
        {
            guest.Role = AppRole.Guest;
            guest.LeagueId = null;
            guest.IsActive = true;
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    private static async Task EnsureSoleSuperAdminAsync(MasterDbContext db, PasswordHasher<AppUser> hasher)
    {
        const string username = "HassanAnas";
        const string password = "Qwerty123@";

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == username).ConfigureAwait(false);
        if (existing is null)
        {
            var user = new AppUser
            {
                Username = username,
                Role = AppRole.SuperAdmin,
                LeagueId = null,
                IsActive = true,
                CreatedAtUtc = DateTime.Now
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
        }
        else
        {
            existing.Role = AppRole.SuperAdmin;
            existing.LeagueId = null;
            existing.IsActive = true;
            existing.PasswordHash = hasher.HashPassword(existing, password);
        }

        var others = await db.Users
            .Where(u => u.Role == AppRole.SuperAdmin)
            .ToListAsync()
            .ConfigureAwait(false);
        foreach (var other in others)
        {
            if (string.Equals(other.Username, username, StringComparison.OrdinalIgnoreCase))
                continue;
            other.Role = AppRole.User;
            other.IsActive = false;
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }
}
