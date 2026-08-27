using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using JPL.Grounds.Api.Data;
using JPL.Grounds.Api.Models;

namespace JPL.Grounds.Api.Services;

public interface IGroundsDbFactory
{
    MasterUsersDbContext CreateMasterUsers();
    GroundsDbContext CreateGrounds();
}

public sealed class GroundsDatabase : IGroundsDbFactory
{
    public const string GroundsCatalogName = "Cric360Grounds";

    private readonly bool _sqlServer;
    private readonly string _masterConnection;
    private readonly string _groundsConnection;

    public GroundsDatabase(IConfiguration config)
    {
        _sqlServer = string.Equals(config["DatabaseProvider"], "SqlServer", StringComparison.OrdinalIgnoreCase);
        var raw = config.GetConnectionString("Default")
                  ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

        if (_sqlServer)
        {
            var master = new SqlConnectionStringBuilder(raw) { InitialCatalog = "Cric360" };
            var grounds = new SqlConnectionStringBuilder(raw) { InitialCatalog = GroundsCatalogName };
            _masterConnection = master.ConnectionString;
            _groundsConnection = grounds.ConnectionString;
        }
        else
        {
            _masterConnection = raw;
            _groundsConnection = raw.Replace("cric360-master.db", "cric360-grounds.db", StringComparison.OrdinalIgnoreCase);
        }
    }

    public MasterUsersDbContext CreateMasterUsers()
    {
        var builder = new DbContextOptionsBuilder<MasterUsersDbContext>();
        if (_sqlServer)
            builder.UseSqlServer(_masterConnection);
        else
            builder.UseSqlite(_masterConnection);
        return new MasterUsersDbContext(builder.Options);
    }

    public GroundsDbContext CreateGrounds()
    {
        var builder = new DbContextOptionsBuilder<GroundsDbContext>();
        if (_sqlServer)
            builder.UseSqlServer(_groundsConnection);
        else
            builder.UseSqlite(_groundsConnection);
        return new GroundsDbContext(builder.Options);
    }

    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        if (_sqlServer)
            await CreateSqlServerDatabaseAsync(GroundsCatalogName, ct).ConfigureAwait(false);

        await using var db = CreateGrounds();
        await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
        await DropLocationsTableAsync(db, ct).ConfigureAwait(false);
        await EnsureGroundMapsColumnsAsync(db, ct).ConfigureAwait(false);
        await EnsureBookingTypesAsync(db, ct).ConfigureAwait(false);
        await EnsureGroundServicesAsync(db, ct).ConfigureAwait(false);
        await EnsureRateWindowsAsync(db, ct).ConfigureAwait(false);
        await EnsureCatalogAsync(db, ct).ConfigureAwait(false);
        await EnsureGroundLinksAsync(db, ct).ConfigureAwait(false);
    }

    private async Task EnsureRateWindowsAsync(GroundsDbContext db, CancellationToken ct)
    {
        if (_sqlServer)
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH(N'dbo.Grounds', N'NightStartsHour') IS NULL
                    ALTER TABLE dbo.Grounds ADD NightStartsHour int NOT NULL CONSTRAINT DF_Grounds_NightStartsHour DEFAULT 18;
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH(N'dbo.Grounds', N'NightStartsHour') IS NOT NULL
                    EXEC(N'UPDATE dbo.Grounds SET NightStartsHour = 18 WHERE NightStartsHour < 12 OR NightStartsHour > 23');
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH(N'dbo.Grounds', N'NightEndsHour') IS NULL
                    ALTER TABLE dbo.Grounds ADD NightEndsHour int NOT NULL CONSTRAINT DF_Grounds_NightEndsHour DEFAULT 6;
                """, ct).ConfigureAwait(false);
            foreach (var sql in new[]
            {
                """
                IF COL_LENGTH(N'dbo.GroundBookingOffers', N'NightPrice') IS NULL
                    ALTER TABLE dbo.GroundBookingOffers ADD NightPrice decimal(12,2) NOT NULL CONSTRAINT DF_Offers_NightPrice DEFAULT 0;
                """,
                """
                IF COL_LENGTH(N'dbo.GroundBookingOffers', N'WeekendPrice') IS NULL
                    ALTER TABLE dbo.GroundBookingOffers ADD WeekendPrice decimal(12,2) NOT NULL CONSTRAINT DF_Offers_WeekendPrice DEFAULT 0;
                """,
                """
                IF COL_LENGTH(N'dbo.GroundBookingOffers', N'WeekendNightPrice') IS NULL
                    ALTER TABLE dbo.GroundBookingOffers ADD WeekendNightPrice decimal(12,2) NOT NULL CONSTRAINT DF_Offers_WeekendNightPrice DEFAULT 0;
                """,
                """
                IF COL_LENGTH(N'dbo.GroundBookingOffers', N'Title') IS NULL
                    ALTER TABLE dbo.GroundBookingOffers ADD Title nvarchar(80) NULL;
                """,
                """
                IF COL_LENGTH(N'dbo.GroundBookingOffers', N'Hint') IS NULL
                    ALTER TABLE dbo.GroundBookingOffers ADD Hint nvarchar(200) NULL;
                """,
                """
                IF COL_LENGTH(N'dbo.GroundBookingOffers', N'PricesPerHour') IS NULL
                    ALTER TABLE dbo.GroundBookingOffers ADD PricesPerHour bit NOT NULL CONSTRAINT DF_Offers_PricesPerHour DEFAULT 0;
                """,
                """
                IF COL_LENGTH(N'dbo.GroundBookingOffers', N'SortOrder') IS NULL
                    ALTER TABLE dbo.GroundBookingOffers ADD SortOrder int NOT NULL CONSTRAINT DF_Offers_SortOrder DEFAULT 0;
                """
            })
            {
                await db.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false);
            }
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE dbo.GroundBookingOffers ALTER COLUMN Kind nvarchar(64) NOT NULL;""", ct)
                    .ConfigureAwait(false);
            }
            catch { /* already widened */ }
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    """ALTER TABLE dbo.Bookings ALTER COLUMN BookingKind nvarchar(64) NULL;""", ct)
                    .ConfigureAwait(false);
            }
            catch { /* ignore */ }
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE dbo.GroundBookingOffers
                SET Title = CASE Kind
                    WHEN N'Hourly' THEN N'Hourly'
                    WHEN N'Match' THEN N'Match'
                    WHEN N'HalfDay' THEN N'Half day'
                    WHEN N'FullDay' THEN N'Full day'
                    WHEN N'Night' THEN N'Night / floodlights'
                    WHEN N'Nets' THEN N'Practice nets'
                    WHEN N'Tournament' THEN N'Tournament day'
                    ELSE Kind
                END
                WHERE Title IS NULL OR LTRIM(RTRIM(Title)) = N'';
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE dbo.GroundBookingOffers
                SET PricesPerHour = 1
                WHERE Kind IN (N'Hourly', N'Nets');
                """, ct).ConfigureAwait(false);
            return;
        }

        foreach (var sql in new[]
        {
            """ALTER TABLE "Grounds" ADD COLUMN "NightStartsHour" INTEGER NOT NULL DEFAULT 18;""",
            """ALTER TABLE "Grounds" ADD COLUMN "NightEndsHour" INTEGER NOT NULL DEFAULT 6;""",
            """ALTER TABLE "GroundBookingOffers" ADD COLUMN "NightPrice" TEXT NOT NULL DEFAULT '0';""",
            """ALTER TABLE "GroundBookingOffers" ADD COLUMN "WeekendPrice" TEXT NOT NULL DEFAULT '0';""",
            """ALTER TABLE "GroundBookingOffers" ADD COLUMN "WeekendNightPrice" TEXT NOT NULL DEFAULT '0';""",
            """ALTER TABLE "GroundBookingOffers" ADD COLUMN "Title" TEXT NULL;""",
            """ALTER TABLE "GroundBookingOffers" ADD COLUMN "Hint" TEXT NULL;""",
            """ALTER TABLE "GroundBookingOffers" ADD COLUMN "PricesPerHour" INTEGER NOT NULL DEFAULT 0;""",
            """ALTER TABLE "GroundBookingOffers" ADD COLUMN "SortOrder" INTEGER NOT NULL DEFAULT 0;"""
        })
        {
            try { await db.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false); }
            catch { /* already exists */ }
        }
    }

    private async Task EnsureGroundServicesAsync(GroundsDbContext db, CancellationToken ct)
    {
        if (_sqlServer)
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.GroundServices', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[GroundServices] (
                        [GroundId] uniqueidentifier NOT NULL,
                        [Code] nvarchar(40) NOT NULL,
                        [Amount] decimal(12,2) NOT NULL,
                        CONSTRAINT [PK_GroundServices] PRIMARY KEY ([GroundId], [Code]),
                        CONSTRAINT [FK_GroundServices_Grounds] FOREIGN KEY ([GroundId])
                            REFERENCES [dbo].[Grounds] ([Id]) ON DELETE CASCADE
                    );
                END
                """, ct).ConfigureAwait(false);

            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.GroundBookingServices', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[GroundBookingServices] (
                        [BookingId] uniqueidentifier NOT NULL,
                        [Code] nvarchar(40) NOT NULL,
                        [Amount] decimal(12,2) NOT NULL,
                        CONSTRAINT [PK_GroundBookingServices] PRIMARY KEY ([BookingId], [Code]),
                        CONSTRAINT [FK_GroundBookingServices_Bookings] FOREIGN KEY ([BookingId])
                            REFERENCES [dbo].[Bookings] ([Id]) ON DELETE CASCADE
                    );
                END
                """, ct).ConfigureAwait(false);
            return;
        }

        foreach (var sql in new[]
        {
            """
            CREATE TABLE IF NOT EXISTS "GroundServices" (
                "GroundId" TEXT NOT NULL,
                "Code" TEXT NOT NULL,
                "Amount" TEXT NOT NULL,
                PRIMARY KEY ("GroundId", "Code"),
                FOREIGN KEY ("GroundId") REFERENCES "Grounds" ("Id") ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "GroundBookingServices" (
                "BookingId" TEXT NOT NULL,
                "Code" TEXT NOT NULL,
                "Amount" TEXT NOT NULL,
                PRIMARY KEY ("BookingId", "Code"),
                FOREIGN KEY ("BookingId") REFERENCES "Bookings" ("Id") ON DELETE CASCADE
            );
            """
        })
        {
            try { await db.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false); }
            catch { /* already exists */ }
        }
    }

    private async Task EnsureGroundMapsColumnsAsync(GroundsDbContext db, CancellationToken ct)
    {
        if (_sqlServer)
        {
            // Each ALTER is its own batch. SQL Server compiles a whole batch before
            // running it, so UPDATE cannot sit in the same script as ADD CountryName.
            foreach (var sql in new[]
            {
                """
                IF COL_LENGTH(N'dbo.Grounds', N'GoogleMapsUrl') IS NULL
                    ALTER TABLE dbo.Grounds ADD GoogleMapsUrl nvarchar(500) NULL;
                """,
                """
                IF COL_LENGTH(N'dbo.Grounds', N'PostalCode') IS NULL
                    ALTER TABLE dbo.Grounds ADD PostalCode nvarchar(16) NULL;
                """,
                """
                IF COL_LENGTH(N'dbo.Grounds', N'CountryName') IS NULL
                    ALTER TABLE dbo.Grounds ADD CountryName nvarchar(80) NULL;
                """,
                """
                IF COL_LENGTH(N'dbo.Grounds', N'Latitude') IS NULL
                    ALTER TABLE dbo.Grounds ADD Latitude float NULL;
                """,
                """
                IF COL_LENGTH(N'dbo.Grounds', N'Longitude') IS NULL
                    ALTER TABLE dbo.Grounds ADD Longitude float NULL;
                """
            })
            {
                await db.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false);
            }

            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH(N'dbo.Grounds', N'CountryName') IS NOT NULL
                    EXEC(N'UPDATE dbo.Grounds SET CountryName = N''Pakistan'' WHERE CountryName IS NULL');
                """, ct).ConfigureAwait(false);

            await db.Database.ExecuteSqlRawAsync("""
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_Grounds_PostalCode' AND object_id = OBJECT_ID(N'dbo.Grounds'))
                    CREATE INDEX [IX_Grounds_PostalCode]
                    ON [dbo].[Grounds] ([ListingStatus], [IsDeleted], [PostalCode], [HourlyRate], [CreatedAtUtc]);
                """, ct).ConfigureAwait(false);
            return;
        }

        foreach (var sql in new[]
        {
            """ALTER TABLE "Grounds" ADD COLUMN "GoogleMapsUrl" TEXT NULL;""",
            """ALTER TABLE "Grounds" ADD COLUMN "PostalCode" TEXT NULL;""",
            """ALTER TABLE "Grounds" ADD COLUMN "CountryName" TEXT NULL;""",
            """ALTER TABLE "Grounds" ADD COLUMN "Latitude" REAL NULL;""",
            """ALTER TABLE "Grounds" ADD COLUMN "Longitude" REAL NULL;"""
        })
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false);
            }
            catch { /* already exists */ }
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_Grounds_PostalCode"
                ON "Grounds" ("ListingStatus", "IsDeleted", "PostalCode", "HourlyRate", "CreatedAtUtc");
                """, ct).ConfigureAwait(false);
        }
        catch { /* ignore */ }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE "Grounds" SET "CountryName" = 'Pakistan' WHERE "CountryName" IS NULL;""", ct)
                .ConfigureAwait(false);
        }
        catch { /* ignore */ }
    }

    private async Task EnsureBookingTypesAsync(GroundsDbContext db, CancellationToken ct)
    {
        if (_sqlServer)
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.GroundBookingOffers', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[GroundBookingOffers] (
                        [GroundId] uniqueidentifier NOT NULL,
                        [Kind] nvarchar(24) NOT NULL,
                        [Price] decimal(12,2) NOT NULL,
                        [DurationHours] decimal(6,2) NOT NULL,
                        [IsEnabled] bit NOT NULL,
                        CONSTRAINT [PK_GroundBookingOffers] PRIMARY KEY ([GroundId], [Kind]),
                        CONSTRAINT [FK_GroundBookingOffers_Grounds] FOREIGN KEY ([GroundId])
                            REFERENCES [dbo].[Grounds] ([Id]) ON DELETE CASCADE
                    );
                END
                """, ct).ConfigureAwait(false);

            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH(N'dbo.Bookings', N'BookingKind') IS NULL
                    ALTER TABLE dbo.Bookings ADD BookingKind nvarchar(24) NULL;
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH(N'dbo.Bookings', N'BookedHours') IS NULL
                    ALTER TABLE dbo.Bookings ADD BookedHours decimal(6,2) NULL;
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH(N'dbo.Bookings', N'BookingKind') IS NOT NULL
                    EXEC(N'UPDATE dbo.Bookings SET BookingKind = N''Hourly'' WHERE BookingKind IS NULL');
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH(N'dbo.Bookings', N'BookedHours') IS NOT NULL
                    EXEC(N'UPDATE dbo.Bookings SET BookedHours = 0 WHERE BookedHours IS NULL');
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO dbo.GroundBookingOffers (GroundId, Kind, Price, DurationHours, IsEnabled)
                SELECT g.Id, N'Hourly', g.HourlyRate, 1, CASE WHEN g.HourlyRate > 0 THEN 1 ELSE 0 END
                FROM dbo.Grounds g
                WHERE NOT EXISTS (
                    SELECT 1 FROM dbo.GroundBookingOffers o
                    WHERE o.GroundId = g.Id AND o.Kind = N'Hourly');
                """, ct).ConfigureAwait(false);
            return;
        }

        foreach (var sql in new[]
        {
            """
            CREATE TABLE IF NOT EXISTS "GroundBookingOffers" (
                "GroundId" TEXT NOT NULL,
                "Kind" TEXT NOT NULL,
                "Price" TEXT NOT NULL,
                "DurationHours" TEXT NOT NULL,
                "IsEnabled" INTEGER NOT NULL,
                PRIMARY KEY ("GroundId", "Kind"),
                FOREIGN KEY ("GroundId") REFERENCES "Grounds" ("Id") ON DELETE CASCADE
            );
            """,
            """ALTER TABLE "Bookings" ADD COLUMN "BookingKind" TEXT NULL;""",
            """ALTER TABLE "Bookings" ADD COLUMN "BookedHours" TEXT NULL;"""
        })
        {
            try { await db.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false); }
            catch { /* already exists */ }
        }
    }

    private async Task EnsureCatalogAsync(GroundsDbContext db, CancellationToken ct)
    {
        if (_sqlServer)
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.AmenityCatalog', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[AmenityCatalog] (
                        [Id] uniqueidentifier NOT NULL,
                        [Name] nvarchar(40) NOT NULL,
                        [SortOrder] int NOT NULL,
                        CONSTRAINT [PK_AmenityCatalog] PRIMARY KEY ([Id])
                    );
                    CREATE UNIQUE INDEX [IX_AmenityCatalog_Name] ON [dbo].[AmenityCatalog] ([Name]);
                    CREATE INDEX [IX_AmenityCatalog_SortOrder] ON [dbo].[AmenityCatalog] ([SortOrder]);
                END
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.ServiceCatalog', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ServiceCatalog] (
                        [Id] uniqueidentifier NOT NULL,
                        [Name] nvarchar(40) NOT NULL,
                        [SortOrder] int NOT NULL,
                        CONSTRAINT [PK_ServiceCatalog] PRIMARY KEY ([Id])
                    );
                    CREATE UNIQUE INDEX [IX_ServiceCatalog_Name] ON [dbo].[ServiceCatalog] ([Name]);
                    CREATE INDEX [IX_ServiceCatalog_SortOrder] ON [dbo].[ServiceCatalog] ([SortOrder]);
                END
                """, ct).ConfigureAwait(false);
        }
        else
        {
            foreach (var sql in new[]
            {
                """
                CREATE TABLE IF NOT EXISTS "AmenityCatalog" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_AmenityCatalog" PRIMARY KEY,
                    "Name" TEXT NOT NULL,
                    "SortOrder" INTEGER NOT NULL
                );
                """,
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_AmenityCatalog_Name" ON "AmenityCatalog" ("Name");""",
                """
                CREATE TABLE IF NOT EXISTS "ServiceCatalog" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_ServiceCatalog" PRIMARY KEY,
                    "Name" TEXT NOT NULL,
                    "SortOrder" INTEGER NOT NULL
                );
                """,
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_ServiceCatalog_Name" ON "ServiceCatalog" ("Name");"""
            })
            {
                try { await db.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false); }
                catch { /* already exists */ }
            }
        }

        if (!await db.AmenityCatalog.AnyAsync(ct).ConfigureAwait(false))
        {
            var order = 0;
            foreach (var name in GroundCatalogSeed.Amenities)
                db.AmenityCatalog.Add(new AmenityCatalog { Name = name, SortOrder = order++ });
        }

        if (!await db.ServiceCatalog.AnyAsync(ct).ConfigureAwait(false))
        {
            var order = 0;
            foreach (var name in GroundCatalogSeed.Services)
                db.ServiceCatalog.Add(new ServiceCatalog { Name = name, SortOrder = order++ });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureGroundLinksAsync(GroundsDbContext db, CancellationToken ct)
    {
        if (_sqlServer)
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.GroundLinks', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[GroundLinks] (
                        [Id] uniqueidentifier NOT NULL,
                        [GroundId] uniqueidentifier NOT NULL,
                        [Title] nvarchar(80) NOT NULL,
                        [Url] nvarchar(500) NOT NULL,
                        [SortOrder] int NOT NULL,
                        CONSTRAINT [PK_GroundLinks] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_GroundLinks_Grounds_GroundId] FOREIGN KEY ([GroundId]) REFERENCES [dbo].[Grounds] ([Id]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_GroundLinks_GroundId_SortOrder] ON [dbo].[GroundLinks] ([GroundId], [SortOrder]);
                END
                """, ct).ConfigureAwait(false);
            return;
        }

        foreach (var sql in new[]
        {
            """
            CREATE TABLE IF NOT EXISTS "GroundLinks" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_GroundLinks" PRIMARY KEY,
                "GroundId" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "Url" TEXT NOT NULL,
                "SortOrder" INTEGER NOT NULL,
                FOREIGN KEY ("GroundId") REFERENCES "Grounds" ("Id") ON DELETE CASCADE
            );
            """,
            """CREATE INDEX IF NOT EXISTS "IX_GroundLinks_GroundId_SortOrder" ON "GroundLinks" ("GroundId", "SortOrder");"""
        })
        {
            try { await db.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false); }
            catch { /* already exists */ }
        }
    }

    private async Task DropLocationsTableAsync(GroundsDbContext db, CancellationToken ct)
    {
        if (_sqlServer)
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.Locations', N'U') IS NOT NULL
                    DROP TABLE [dbo].[Locations];
                """, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync("""DROP TABLE IF EXISTS "Locations";""", ct)
                .ConfigureAwait(false);
        }
        catch { /* ignore */ }
    }

    private async Task CreateSqlServerDatabaseAsync(string dbName, CancellationToken ct)
    {
        var system = new SqlConnectionStringBuilder(_masterConnection) { InitialCatalog = "master" };
        await using var conn = new SqlConnection(system.ConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            IF DB_ID(N'{dbName}') IS NULL
                CREATE DATABASE [{dbName}];
            """;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
