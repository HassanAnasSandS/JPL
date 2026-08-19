namespace JPL.Domain;

public static class VenueBookingPaymentStatus
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";

    public static bool IsValid(string? status)
        => string.Equals(status, Pending, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, Paid, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? status)
        => string.Equals(status, Paid, StringComparison.OrdinalIgnoreCase) ? Paid : Pending;
}

public sealed class VenueBooking
{
    public Guid Id { get; set; }
    /// <summary>Admin user who created this booking — only they may edit/delete.</summary>
    public Guid CreatedByUserId { get; set; }
    public Guid VenueId { get; set; }
    public DateTime BookingDateUtc { get; set; } = DateTime.Now;
    public Guid BookedByPlayerId { get; set; }
    public decimal BookingCost { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }

    public ICollection<VenueBookingParticipant> Participants { get; set; } = new List<VenueBookingParticipant>();
}

public sealed class VenueBookingParticipant
{
    public Guid VenueBookingId { get; set; }
    public VenueBooking VenueBooking { get; set; } = null!;
    public Guid PlayerId { get; set; }
    /// <summary>"Pending" or "Paid".</summary>
    public string Status { get; set; } = VenueBookingPaymentStatus.Pending;
}
