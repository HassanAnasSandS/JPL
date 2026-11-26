using JPL.Data;
using JPL.Domain;
using Microsoft.EntityFrameworkCore;

namespace JPL.Services;

public sealed record VenueBookingParticipantDraft(Guid PlayerId, string Status, decimal Amount);
public sealed record VenueBookingDraft(Guid VenueId, DateTime BookingDateUtc, Guid BookedByPlayerId,
    decimal BookingCost, IReadOnlyList<VenueBookingParticipantDraft> Participants);

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
    public IReadOnlyList<VenueBookingParticipantItem> Participants { get; init; } = Array.Empty<VenueBookingParticipantItem>();
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

/// <summary>SQLite provides offline booking records; changes and participants enter the same durable outbox transaction.</summary>
public sealed class VenueBookingService : IVenueBookingService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IOutboxWriter _outbox;
    private readonly ISyncTrigger _sync;

    public VenueBookingService(IApiClient api, IAuthService auth, ILeagueContext league, IDbContextFactory<JplDbContext> dbFactory,
        IOutboxWriter outbox, ISyncTrigger sync)
    {
        _api = api; _auth = auth; _league = league; _dbFactory = dbFactory; _outbox = outbox; _sync = sync;
    }

    public async Task<IReadOnlyList<VenueBookingListItem>> GetAllAsync(CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetVenueBookingsAsync(ct).ConfigureAwait(false);
            return live.Select(dto => ToListItem(dto, CanEdit(dto.CreatedByUserId))).ToList();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await LocalListAsync(db, ct).ConfigureAwait(false);
    }

    public async Task<ApiPagedResult<VenueBookingListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetVenueBookingsPageAsync(skip, q, ct).ConfigureAwait(false);
            return live.WithItems((live.Items ?? [])
                .Select(dto => ToListItem(dto, CanEdit(dto.CreatedByUserId))).ToList());
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var list = await LocalListAsync(db, ct).ConfigureAwait(false);
        var filtered = string.IsNullOrWhiteSpace(q) ? list : list.Where(row =>
            row.VenueName.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)
            || row.BookedByPlayerName.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)
            || row.CreatedByUsername.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return new ApiPagedResult<VenueBookingListItem>(filtered.Skip(Math.Max(0, skip)).Take(ListPaging.PageSize).ToList(),
            filtered.Count, Math.Max(0, skip), ListPaging.PageSize);
    }

    public async Task<VenueBookingDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetVenueBookingAsync(id, ct).ConfigureAwait(false);
            return live is null ? null : ToDetail(live, CanEdit(live.CreatedByUserId));
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await LocalDetailAsync(db, id, ct).ConfigureAwait(false);
    }

    public async Task<VenueBookingDetail> CreateAsync(VenueBookingDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite(); Validate(draft);
        var userId = _auth.Current?.UserId ?? Guid.Empty;
        if (userId == Guid.Empty) throw new InvalidOperationException("Sign in again before creating a booking.");
        var now = DateTime.UtcNow;
        var booking = new VenueBooking { Id = Guid.NewGuid(), CreatedByUserId = userId, VenueId = draft.VenueId,
            BookingDateUtc = draft.BookingDateUtc, BookedByPlayerId = draft.BookedByPlayerId, BookingCost = draft.BookingCost,
            Version = 1, CreatedAtUtc = now, UpdatedAtUtc = now };
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.VenueBookings.Add(booking);
        foreach (var row in draft.Participants)
            db.VenueBookingParticipants.Add(new VenueBookingParticipant { VenueBookingId = booking.Id, PlayerId = row.PlayerId,
                Status = VenueBookingPaymentStatus.Normalize(row.Status), Amount = row.Amount });
        _outbox.EnqueueEntity(db, booking, "Upsert");
        foreach (var entry in db.ChangeTracker.Entries<VenueBookingParticipant>().Where(entry => entry.State == EntityState.Added))
            _outbox.EnqueueEntity(db, entry.Entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
        return await LocalDetailAsync(db, booking.Id, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("The booking was saved locally but could not be loaded.");
    }

    public async Task<VenueBookingDetail> UpdateAsync(Guid id, VenueBookingDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite(); Validate(draft);
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var booking = await db.VenueBookings.Include(row => row.Participants).FirstOrDefaultAsync(row => row.Id == id, ct)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Venue booking not found.");
        if (!CanEdit(booking.CreatedByUserId)) throw new InvalidOperationException("Only the admin who created this booking can edit it.");
        booking.VenueId = draft.VenueId; booking.BookingDateUtc = draft.BookingDateUtc;
        booking.BookedByPlayerId = draft.BookedByPlayerId; booking.BookingCost = draft.BookingCost;
        booking.Version++; booking.UpdatedAtUtc = DateTime.UtcNow;
        var desired = draft.Participants.ToDictionary(row => row.PlayerId);
        foreach (var participant in booking.Participants.ToList())
        {
            if (!desired.Remove(participant.PlayerId, out var next))
            {
                db.VenueBookingParticipants.Remove(participant);
                _outbox.EnqueueEntity(db, participant, "Delete");
                continue;
            }
            var status = VenueBookingPaymentStatus.Normalize(next.Status);
            if (participant.Status != status || participant.Amount != next.Amount)
            {
                participant.Status = status; participant.Amount = next.Amount;
                _outbox.EnqueueEntity(db, participant, "Upsert");
            }
        }
        foreach (var next in desired.Values)
        {
            var participant = new VenueBookingParticipant { VenueBookingId = booking.Id, PlayerId = next.PlayerId,
                Status = VenueBookingPaymentStatus.Normalize(next.Status), Amount = next.Amount };
            db.VenueBookingParticipants.Add(participant);
            _outbox.EnqueueEntity(db, participant, "Upsert");
        }
        _outbox.EnqueueEntity(db, booking, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
        return await LocalDetailAsync(db, id, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("The booking was saved locally but could not be loaded.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var booking = await db.VenueBookings.Include(row => row.Participants).FirstOrDefaultAsync(row => row.Id == id, ct)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Venue booking not found.");
        if (!CanEdit(booking.CreatedByUserId)) throw new InvalidOperationException("Only the admin who created this booking can delete it.");
        foreach (var participant in booking.Participants.ToList())
        {
            db.VenueBookingParticipants.Remove(participant);
            _outbox.EnqueueEntity(db, participant, "Delete");
        }
        booking.IsDeleted = true; booking.Version++; booking.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.EnqueueEntity(db, booking, "Delete");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
    }

    private async Task CacheAsync(JplDbContext db, IEnumerable<ApiVenueBookingDto> rows, CancellationToken ct)
    {
        var list = rows.ToList();
        var ids = list.Select(row => row.Id).ToHashSet();
        var pending = await db.SyncOutbox.AsNoTracking().Where(row => row.EntityType == nameof(VenueBooking)
            && row.Status != SyncOutboxStatus.Completed && ids.Contains(row.EntityId))
            .Select(row => row.EntityId).ToListAsync(ct).ConfigureAwait(false);
        var protectedIds = pending.ToHashSet();
        foreach (var dto in list.Where(row => !protectedIds.Contains(row.Id)))
        {
            var booking = await db.VenueBookings.IgnoreQueryFilters().Include(row => row.Participants)
                .FirstOrDefaultAsync(row => row.Id == dto.Id, ct).ConfigureAwait(false);
            if (booking is null) { booking = new VenueBooking { Id = dto.Id }; db.VenueBookings.Add(booking); }
            booking.CreatedByUserId = dto.CreatedByUserId; booking.VenueId = dto.VenueId; booking.BookingDateUtc = dto.BookingDateUtc;
            booking.BookedByPlayerId = dto.BookedByPlayerId; booking.BookingCost = dto.BookingCost; booking.Version = dto.Version;
            booking.CreatedAtUtc = dto.CreatedAtUtc; booking.UpdatedAtUtc = dto.UpdatedAtUtc; booking.IsDeleted = dto.IsDeleted;
            var desired = dto.Participants.ToDictionary(row => row.PlayerId);
            foreach (var participant in booking.Participants.Where(row => !desired.ContainsKey(row.PlayerId)).ToList())
                db.VenueBookingParticipants.Remove(participant);
            var have = booking.Participants.ToDictionary(row => row.PlayerId);
            foreach (var row in desired.Values)
            {
                if (have.TryGetValue(row.PlayerId, out var participant))
                { participant.Status = VenueBookingPaymentStatus.Normalize(row.Status); participant.Amount = row.Amount; }
                else booking.Participants.Add(new VenueBookingParticipant { VenueBookingId = booking.Id, PlayerId = row.PlayerId,
                    Status = VenueBookingPaymentStatus.Normalize(row.Status), Amount = row.Amount });
            }
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<VenueBookingListItem>> LocalListAsync(JplDbContext db, CancellationToken ct)
    {
        var rows = await db.VenueBookings.AsNoTracking().Include(row => row.Participants)
            .OrderByDescending(row => row.BookingDateUtc).ToListAsync(ct).ConfigureAwait(false);
        var venues = await db.Venues.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, ct).ConfigureAwait(false);
        var players = await db.Players.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.FullName, ct).ConfigureAwait(false);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Username, ct).ConfigureAwait(false);
        return rows.Select(row => ToListItem(row, row.Participants.ToList(), venues, players, users)).ToList();
    }

    private async Task<VenueBookingDetail?> LocalDetailAsync(JplDbContext db, Guid id, CancellationToken ct)
    {
        var booking = await db.VenueBookings.AsNoTracking().Include(row => row.Participants)
            .FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
        if (booking is null) return null;
        var venue = await db.Venues.AsNoTracking().Where(row => row.Id == booking.VenueId)
            .Select(row => row.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        var participantIds = booking.Participants.Select(participant => participant.PlayerId).Append(booking.BookedByPlayerId).Distinct().ToArray();
        var playerNames = await db.Players.AsNoTracking().Where(row => row.Id == booking.BookedByPlayerId
                || participantIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.FullName, ct).ConfigureAwait(false);
        var username = await db.Users.AsNoTracking().Where(row => row.Id == booking.CreatedByUserId)
            .Select(row => row.Username).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? CurrentUsername(booking.CreatedByUserId);
        return ToDetail(booking, booking.Participants.ToList(), venue, playerNames, username);
    }

    private VenueBookingListItem ToListItem(ApiVenueBookingDto dto, bool canEdit)
    {
        var paid = dto.Participants.Where(row => string.Equals(row.Status, VenueBookingPaymentStatus.Paid, StringComparison.OrdinalIgnoreCase)).ToList();
        var paidAmount = paid.Sum(row => row.Amount);
        var outstanding = dto.Participants.Where(row => !string.Equals(row.Status, VenueBookingPaymentStatus.Paid, StringComparison.OrdinalIgnoreCase))
            .Sum(row => row.Amount);
        return new VenueBookingListItem { Id = dto.Id, CreatedByUserId = dto.CreatedByUserId, CreatedByUsername = dto.CreatedByUsername,
            VenueId = dto.VenueId, VenueName = dto.VenueName, BookingDateUtc = dto.BookingDateUtc,
            BookedByPlayerId = dto.BookedByPlayerId, BookedByPlayerName = dto.BookedByPlayerName, BookingCost = dto.BookingCost,
            PaidAmount = paidAmount, OutstandingAmount = outstanding,
            ParticipantCount = dto.Participants.Count, PaidCount = paid.Count, PendingCount = dto.Participants.Count - paid.Count, CanEdit = canEdit };
    }

    private VenueBookingDetail ToDetail(ApiVenueBookingDto dto, bool canEdit) => new()
    {
        Id = dto.Id, CreatedByUserId = dto.CreatedByUserId, CreatedByUsername = dto.CreatedByUsername,
        VenueId = dto.VenueId, VenueName = dto.VenueName, BookingDateUtc = dto.BookingDateUtc,
        BookedByPlayerId = dto.BookedByPlayerId, BookedByPlayerName = dto.BookedByPlayerName,
        BookingCost = dto.BookingCost, CanEdit = canEdit,
        Participants = dto.Participants.Select(row => new VenueBookingParticipantItem { PlayerId = row.PlayerId,
            PlayerName = row.PlayerName, Status = VenueBookingPaymentStatus.Normalize(row.Status), Amount = row.Amount }).ToList()
    };

    private VenueBookingListItem ToListItem(VenueBooking entity, IReadOnlyList<VenueBookingParticipant> participants,
        IReadOnlyDictionary<Guid, string> venues, IReadOnlyDictionary<Guid, string> players, IReadOnlyDictionary<Guid, string> users)
    {
        var paid = participants.Where(row => string.Equals(row.Status, VenueBookingPaymentStatus.Paid, StringComparison.OrdinalIgnoreCase)).ToList();
        var paidAmount = paid.Sum(row => row.Amount);
        var outstanding = participants.Where(row => !string.Equals(row.Status, VenueBookingPaymentStatus.Paid, StringComparison.OrdinalIgnoreCase))
            .Sum(row => row.Amount);
        return new VenueBookingListItem { Id = entity.Id, CreatedByUserId = entity.CreatedByUserId,
            CreatedByUsername = users.GetValueOrDefault(entity.CreatedByUserId, CurrentUsername(entity.CreatedByUserId)),
            VenueId = entity.VenueId, VenueName = venues.GetValueOrDefault(entity.VenueId, string.Empty),
            BookingDateUtc = entity.BookingDateUtc, BookedByPlayerId = entity.BookedByPlayerId,
            BookedByPlayerName = players.GetValueOrDefault(entity.BookedByPlayerId, string.Empty), BookingCost = entity.BookingCost,
            PaidAmount = paidAmount, OutstandingAmount = outstanding,
            ParticipantCount = participants.Count, PaidCount = paid.Count, PendingCount = participants.Count - paid.Count,
            CanEdit = CanEdit(entity.CreatedByUserId) };
    }

    private VenueBookingDetail ToDetail(VenueBooking booking, IReadOnlyList<VenueBookingParticipant> participants,
        string venueName, IReadOnlyDictionary<Guid, string> players, string username)
        => new() { Id = booking.Id, CreatedByUserId = booking.CreatedByUserId, CreatedByUsername = username,
            VenueId = booking.VenueId, VenueName = venueName, BookingDateUtc = booking.BookingDateUtc,
            BookedByPlayerId = booking.BookedByPlayerId, BookedByPlayerName = players.GetValueOrDefault(booking.BookedByPlayerId, string.Empty),
            BookingCost = booking.BookingCost, CanEdit = CanEdit(booking.CreatedByUserId),
            Participants = participants.Select(row => new VenueBookingParticipantItem { PlayerId = row.PlayerId,
                PlayerName = players.GetValueOrDefault(row.PlayerId, string.Empty), Status = VenueBookingPaymentStatus.Normalize(row.Status),
                Amount = row.Amount }).ToList() };

    private string CurrentUsername(Guid userId) => _auth.Current?.UserId == userId ? _auth.Current.Username : string.Empty;
    private bool CanEdit(Guid ownerId) => _auth.CanWrite && _auth.Current?.UserId == ownerId;
    private Task<bool> IsBootstrappedAsync(JplDbContext db, CancellationToken ct)
        => LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct);

    private static void Validate(VenueBookingDraft draft)
    {
        if (draft.VenueId == Guid.Empty) throw new ArgumentException("Venue is required.");
        if (draft.BookedByPlayerId == Guid.Empty) throw new ArgumentException("Booked By player is required.");
        if (draft.BookingCost < 0) throw new ArgumentException("Booking cost cannot be negative.");
        var seen = new HashSet<Guid>();
        foreach (var row in draft.Participants)
        {
            if (row.PlayerId == Guid.Empty) throw new ArgumentException("Participant player is required.");
            if (!seen.Add(row.PlayerId)) throw new ArgumentException("Duplicate participant player.");
            if (!VenueBookingPaymentStatus.IsValid(row.Status)) throw new ArgumentException("Participant status must be Pending or Paid.");
            if (row.Amount < 0) throw new ArgumentException("Participant amount cannot be negative.");
        }
    }
}
