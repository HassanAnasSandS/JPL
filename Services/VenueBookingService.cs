using JPL.Domain;

namespace JPL.Services;

public sealed record VenueBookingParticipantDraft(Guid PlayerId, string Status, decimal Amount);

public sealed record VenueBookingDraft(
    Guid VenueId,
    DateTime BookingDateUtc,
    Guid BookedByPlayerId,
    decimal BookingCost,
    IReadOnlyList<VenueBookingParticipantDraft> Participants);

public sealed class VenueBookingListItem
{
    public Guid Id { get; init; }
    public Guid CreatedByUserId { get; init; }
    public string CreatedByUsername { get; init; } = string.Empty;
    public Guid VenueId { get; init; }
    public string VenueName { get; init; } = string.Empty;
    public DateTime BookingDateUtc { get; init; }
    public Guid BookedByPlayerId { get; init; }
    public string BookedByPlayerName { get; init; } = string.Empty;
    public decimal BookingCost { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal OutstandingAmount { get; init; }
    public int ParticipantCount { get; init; }
    public int PaidCount { get; init; }
    public int PendingCount { get; init; }
    public bool CanEdit { get; init; }

    public string DateText => BookingDateUtc.ToString("dd MMM yyyy");
    public string CostText => BookingCost.ToString("N0");
    public string PaidAmountText => PaidAmount.ToString("N0");
    public string OutstandingAmountText => OutstandingAmount.ToString("N0");
    public string ParticipantsLine => $"Players {ParticipantCount} · Paid {PaidCount} · Pending {PendingCount}";
    public string OwnerLine => $"Created by {CreatedByUsername}";
    public string BookedByLine => $"Booked by {BookedByPlayerName}";
}

public sealed class VenueBookingDetail
{
    public Guid Id { get; init; }
    public Guid CreatedByUserId { get; init; }
    public string CreatedByUsername { get; init; } = string.Empty;
    public Guid VenueId { get; init; }
    public string VenueName { get; init; } = string.Empty;
    public DateTime BookingDateUtc { get; init; }
    public Guid BookedByPlayerId { get; init; }
    public string BookedByPlayerName { get; init; } = string.Empty;
    public decimal BookingCost { get; init; }
    public bool CanEdit { get; init; }
    public IReadOnlyList<VenueBookingParticipantItem> Participants { get; init; } =
        Array.Empty<VenueBookingParticipantItem>();
}

public sealed class VenueBookingParticipantItem
{
    public Guid PlayerId { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public string Status { get; init; } = VenueBookingPaymentStatus.Pending;
    public decimal Amount { get; init; }
}

public interface IVenueBookingService
{
    Task<IReadOnlyList<VenueBookingListItem>> GetAllAsync(CancellationToken ct = default);
    Task<ApiPagedResult<VenueBookingListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default);
    Task<VenueBookingDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<VenueBookingDetail> CreateAsync(VenueBookingDraft draft, CancellationToken ct = default);
    Task<VenueBookingDetail> UpdateAsync(Guid id, VenueBookingDraft draft, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Venue bookings are API-only (SQL Server). Ownership: only creator admin may edit/delete.</summary>
public sealed class VenueBookingService : IVenueBookingService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;

    public VenueBookingService(IApiClient api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    public async Task<IReadOnlyList<VenueBookingListItem>> GetAllAsync(CancellationToken ct = default)
    {
        var remote = await _api.GetVenueBookingsAsync(ct).ConfigureAwait(false);
        var me = _auth.Current?.UserId;
        var canWrite = _auth.CanWrite;
        var isSuper = _auth.Current?.Role == AppRole.SuperAdmin;

        return remote
            .OrderByDescending(b => b.BookingDateUtc)
            .Select(b => ToListItem(b, canWrite && (isSuper || me == b.CreatedByUserId)))
            .ToList();
    }

    public async Task<ApiPagedResult<VenueBookingListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default)
    {
        var page = await _api.GetVenueBookingsPageAsync(skip, q, ct).ConfigureAwait(false);
        var me = _auth.Current?.UserId;
        var canWrite = _auth.CanWrite;
        var isSuper = _auth.Current?.Role == AppRole.SuperAdmin;
        var items = (page.Items ?? [])
            .Select(b => ToListItem(b, canWrite && (isSuper || me == b.CreatedByUserId)))
            .ToList();
        return page.WithItems(items);
    }

    public async Task<VenueBookingDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _api.GetVenueBookingAsync(id, ct).ConfigureAwait(false);
        if (dto is null) return null;

        var canEdit = _auth.CanWrite &&
                      (_auth.Current?.Role == AppRole.SuperAdmin || _auth.Current?.UserId == dto.CreatedByUserId);
        return ToDetail(dto, canEdit);
    }

    public async Task<VenueBookingDetail> CreateAsync(VenueBookingDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        Validate(draft);

        var remote = await _api.CreateVenueBookingAsync(new
        {
            id = (Guid?)null,
            venueId = draft.VenueId,
            bookingDateUtc = draft.BookingDateUtc,
            bookedByPlayerId = draft.BookedByPlayerId,
            bookingCost = draft.BookingCost,
            participants = draft.Participants.Select(p => new
            {
                playerId = p.PlayerId,
                status = VenueBookingPaymentStatus.Normalize(p.Status),
                amount = p.Amount
            }).ToList()
        }, ct).ConfigureAwait(false);

        return ToDetail(remote, canEdit: true);
    }

    public async Task<VenueBookingDetail> UpdateAsync(Guid id, VenueBookingDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        Validate(draft);

        var existing = await GetAsync(id, ct).ConfigureAwait(false)
                       ?? throw new InvalidOperationException("Venue booking not found.");
        if (!existing.CanEdit)
            throw new InvalidOperationException("Only the admin who created this booking can edit it.");

        var remote = await _api.UpdateVenueBookingAsync(id, new
        {
            id,
            venueId = draft.VenueId,
            bookingDateUtc = draft.BookingDateUtc,
            bookedByPlayerId = draft.BookedByPlayerId,
            bookingCost = draft.BookingCost,
            participants = draft.Participants.Select(p => new
            {
                playerId = p.PlayerId,
                status = VenueBookingPaymentStatus.Normalize(p.Status),
                amount = p.Amount
            }).ToList()
        }, ct).ConfigureAwait(false);

        return ToDetail(remote, canEdit: true);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        var existing = await GetAsync(id, ct).ConfigureAwait(false)
                       ?? throw new InvalidOperationException("Venue booking not found.");
        if (!existing.CanEdit)
            throw new InvalidOperationException("Only the admin who created this booking can delete it.");

        await _api.DeleteVenueBookingAsync(id, ct).ConfigureAwait(false);
    }

    private static void Validate(VenueBookingDraft draft)
    {
        if (draft.VenueId == Guid.Empty)
            throw new ArgumentException("Venue is required.");
        if (draft.BookedByPlayerId == Guid.Empty)
            throw new ArgumentException("Booked By player is required.");
        if (draft.BookingCost < 0)
            throw new ArgumentException("Booking cost cannot be negative.");

        var seen = new HashSet<Guid>();
        foreach (var p in draft.Participants)
        {
            if (p.PlayerId == Guid.Empty)
                throw new ArgumentException("Participant player is required.");
            if (!seen.Add(p.PlayerId))
                throw new ArgumentException("Duplicate participant player.");
            if (!VenueBookingPaymentStatus.IsValid(p.Status))
                throw new ArgumentException("Participant status must be Pending or Paid.");
            if (p.Amount < 0)
                throw new ArgumentException("Participant amount cannot be negative.");
        }
    }

    private static VenueBookingListItem ToListItem(ApiVenueBookingDto dto, bool canEdit)
    {
        var paidRows = dto.Participants
            .Where(p => string.Equals(p.Status, VenueBookingPaymentStatus.Paid, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var paidAmount = paidRows.Sum(p => p.Amount);
        var outstandingAmount = dto.Participants
            .Where(p => !string.Equals(p.Status, VenueBookingPaymentStatus.Paid, StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.Amount);
        return new VenueBookingListItem
        {
            Id = dto.Id,
            CreatedByUserId = dto.CreatedByUserId,
            CreatedByUsername = dto.CreatedByUsername,
            VenueId = dto.VenueId,
            VenueName = dto.VenueName,
            BookingDateUtc = dto.BookingDateUtc,
            BookedByPlayerId = dto.BookedByPlayerId,
            BookedByPlayerName = dto.BookedByPlayerName,
            BookingCost = dto.BookingCost,
            PaidAmount = paidAmount,
            OutstandingAmount = outstandingAmount,
            ParticipantCount = dto.Participants.Count,
            PaidCount = paidRows.Count,
            PendingCount = dto.Participants.Count - paidRows.Count,
            CanEdit = canEdit
        };
    }

    private static VenueBookingDetail ToDetail(ApiVenueBookingDto dto, bool canEdit)
        => new()
        {
            Id = dto.Id,
            CreatedByUserId = dto.CreatedByUserId,
            CreatedByUsername = dto.CreatedByUsername,
            VenueId = dto.VenueId,
            VenueName = dto.VenueName,
            BookingDateUtc = dto.BookingDateUtc,
            BookedByPlayerId = dto.BookedByPlayerId,
            BookedByPlayerName = dto.BookedByPlayerName,
            BookingCost = dto.BookingCost,
            CanEdit = canEdit,
            Participants = dto.Participants.Select(p => new VenueBookingParticipantItem
            {
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                Status = VenueBookingPaymentStatus.Normalize(p.Status),
                Amount = p.Amount
            }).ToList()
        };
}
