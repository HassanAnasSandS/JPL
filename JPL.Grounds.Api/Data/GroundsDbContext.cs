using Microsoft.EntityFrameworkCore;
using JPL.Grounds.Api.Models;

namespace JPL.Grounds.Api.Data;

public sealed class GroundsDbContext : DbContext
{
    public GroundsDbContext(DbContextOptions<GroundsDbContext> options) : base(options)
    {
    }

    public DbSet<Ground> Grounds => Set<Ground>();
    public DbSet<GroundImage> GroundImages => Set<GroundImage>();
    public DbSet<GroundAmenity> GroundAmenities => Set<GroundAmenity>();
    public DbSet<GroundService> GroundServices => Set<GroundService>();
    public DbSet<GroundBookingOffer> GroundBookingOffers => Set<GroundBookingOffer>();
    public DbSet<GroundLink> GroundLinks => Set<GroundLink>();
    public DbSet<GroundBooking> Bookings => Set<GroundBooking>();
    public DbSet<GroundBookingService> GroundBookingServices => Set<GroundBookingService>();
    public DbSet<AmenityCatalog> AmenityCatalog => Set<AmenityCatalog>();
    public DbSet<ServiceCatalog> ServiceCatalog => Set<ServiceCatalog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ground>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.AddressLine).HasMaxLength(240);
            e.Property(x => x.ContactPhone).HasMaxLength(30);
            e.Property(x => x.WhatsAppNumber).HasMaxLength(30);
            e.Property(x => x.ProvinceName).HasMaxLength(80).IsRequired();
            e.Property(x => x.CityName).HasMaxLength(80).IsRequired();
            e.Property(x => x.AreaName).HasMaxLength(80);
            e.Property(x => x.PostalCode).HasMaxLength(16);
            e.Property(x => x.CountryName).HasMaxLength(80);
            e.Property(x => x.GoogleMapsUrl).HasMaxLength(500);
            e.Property(x => x.CoverImagePath).HasMaxLength(80);
            e.Property(x => x.HourlyRate).HasPrecision(12, 2);
            e.HasIndex(x => x.OwnerUserId);
            e.HasIndex(x => new { x.ListingStatus, x.IsDeleted, x.PostalCode, x.HourlyRate, x.CreatedAtUtc });
            e.HasIndex(x => new { x.ListingStatus, x.IsDeleted, x.CreatedAtUtc, x.Id });
        });

        modelBuilder.Entity<GroundImage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(80).IsRequired();
            e.HasOne(x => x.Ground).WithMany(g => g.Images).HasForeignKey(x => x.GroundId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.GroundId, x.SortOrder });
        });

        modelBuilder.Entity<GroundAmenity>(e =>
        {
            e.HasKey(x => new { x.GroundId, x.Code });
            e.Property(x => x.Code).HasMaxLength(40);
            e.HasOne(x => x.Ground).WithMany(g => g.Amenities).HasForeignKey(x => x.GroundId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GroundService>(e =>
        {
            e.HasKey(x => new { x.GroundId, x.Code });
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.HasOne(x => x.Ground).WithMany(g => g.Services).HasForeignKey(x => x.GroundId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GroundBookingOffer>(e =>
        {
            e.HasKey(x => new { x.GroundId, x.Kind });
            e.Property(x => x.Kind).HasMaxLength(64).IsRequired();
            e.Property(x => x.Title).HasMaxLength(80).IsRequired();
            e.Property(x => x.Hint).HasMaxLength(200);
            e.Property(x => x.Price).HasPrecision(12, 2);
            e.Property(x => x.NightPrice).HasPrecision(12, 2);
            e.Property(x => x.WeekendPrice).HasPrecision(12, 2);
            e.Property(x => x.WeekendNightPrice).HasPrecision(12, 2);
            e.Property(x => x.DurationHours).HasPrecision(6, 2);
            e.HasOne(x => x.Ground).WithMany(g => g.Offers).HasForeignKey(x => x.GroundId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GroundLink>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(80).IsRequired();
            e.Property(x => x.Url).HasMaxLength(500).IsRequired();
            e.HasOne(x => x.Ground).WithMany(g => g.Links).HasForeignKey(x => x.GroundId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.GroundId, x.SortOrder });
        });

        modelBuilder.Entity<GroundBooking>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CustomerName).HasMaxLength(120).IsRequired();
            e.Property(x => x.CustomerPhone).HasMaxLength(30);
            e.Property(x => x.Status).HasMaxLength(24).IsRequired();
            e.Property(x => x.BookingKind).HasMaxLength(64);
            e.Property(x => x.BookedHours).HasPrecision(6, 2);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.PaymentNote).HasMaxLength(400);
            e.Property(x => x.BookingAmount).HasPrecision(12, 2);
            e.Property(x => x.Advance).HasPrecision(12, 2);
            e.Ignore(x => x.Balance);
            e.HasOne(x => x.Ground).WithMany(g => g.Bookings).HasForeignKey(x => x.GroundId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.GroundId, x.Status, x.StartAt, x.EndAt });
            e.HasIndex(x => new { x.CustomerUserId, x.CreatedAtUtc });
            e.HasIndex(x => new { x.IsDeleted, x.CreatedAtUtc, x.Id });
        });

        modelBuilder.Entity<GroundBookingService>(e =>
        {
            e.HasKey(x => new { x.BookingId, x.Code });
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.HasOne(x => x.Booking).WithMany(b => b.Services).HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AmenityCatalog>(e =>
        {
            e.ToTable("AmenityCatalog");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.SortOrder);
        });

        modelBuilder.Entity<ServiceCatalog>(e =>
        {
            e.ToTable("ServiceCatalog");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.SortOrder);
        });

        SqlServerLocalDateTimes.Map(modelBuilder, (Database.ProviderName ?? "").Contains("SqlServer", StringComparison.OrdinalIgnoreCase));
    }
}
