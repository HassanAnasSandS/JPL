using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JPL.Data;

public static class DbBootstrap
{
    public static string GetDatabasePath()
    {
        var dir = FileSystem.AppDataDirectory;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "jpl.db");
    }

    public static async Task InitializeAsync(JplDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);

        // WAL = readers don't block writers; busy_timeout avoids lock storms under load.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=3000;", ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", ct).ConfigureAwait(false);
    }

    public static DbContextOptionsBuilder ConfigureSqlite(this DbContextOptionsBuilder builder)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = GetDatabasePath(),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        return builder.UseSqlite(csb.ToString());
    }
}
