using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JPL.Data;
using JPL.Domain;

namespace JPL.Services;

public interface IGroundBookingRequestSyncService
{
    Task<Guid> QueueAsync(Guid userId, Guid groundId, string groundName,
        GroundBookingCreateRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<GroundBookingRequestOutboxItem>> GetRequestsAsync(Guid userId, CancellationToken ct = default);
    Task<int> GetPendingCountAsync(CancellationToken ct = default);
    Task SyncPendingAsync(CancellationToken ct = default);
    void RequestSyncNow();
}

/// <summary>
/// Offline customer requests are durable local intents. They do not hold a ground slot until
/// the Grounds API accepts them; ClientRequestId makes upload retries idempotent.
/// </summary>
public sealed class GroundBookingRequestSyncService : IGroundBookingRequestSyncService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IGroundsApiClient _api;
    private readonly IAuthService _auth;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _requested;

    public GroundBookingRequestSyncService(
        IDbContextFactory<JplDbContext> dbFactory, IGroundsApiClient api, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _api = api;
        _auth = auth;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    public async Task<Guid> QueueAsync(
        Guid userId, Guid groundId, string groundName,
        GroundBookingCreateRequest request, CancellationToken ct = default)
    {
        if (userId == Guid.Empty || groundId == Guid.Empty)
            throw new InvalidOperationException("Sign in and select a ground before saving a booking request.");
        var requestId = request.ClientRequestId is Guid existing && existing != Guid.Empty
            ? existing
            : Guid.NewGuid();
        request = request with { ClientRequestId = requestId };

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.GroundBookingRequestOutbox.Add(new GroundBookingRequestOutboxItem
        {
            RequestId = requestId,
            UserId = userId,
            GroundId = groundId,
            GroundName = groundName,
            StartAt = request.StartAt,
            EndAt = request.EndAt,
            PayloadJson = JsonSerializer.Serialize(request, Json),
            Status = SyncOutboxStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return requestId;
    }

    public async Task<IReadOnlyList<GroundBookingRequestOutboxItem>> GetRequestsAsync(
        Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.GroundBookingRequestOutbox.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderByDescending(row => row.CreatedAtUtc)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.GroundBookingRequestOutbox.AsNoTracking()
            .CountAsync(row => row.Status != SyncOutboxStatus.Completed, ct).ConfigureAwait(false);
    }

    public void RequestSyncNow()
    {
        if (Interlocked.Exchange(ref _requested, 1) == 1)
            return;
        _ = Task.Run(async () =>
        {
            try { await SyncPendingAsync().ConfigureAwait(false); }
            catch { /* durable queue retries on the next connectivity/manual sync */ }
            finally { Interlocked.Exchange(ref _requested, 0); }
        });
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
            RequestSyncNow();
    }

    public async Task SyncPendingAsync(CancellationToken ct = default)
    {
        var session = _auth.Current;
        if (session is null || session.IsGuest || session.IsGroundOwner)
            return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using (var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
            {
                var interrupted = await db.GroundBookingRequestOutbox
                    .Where(row => row.UserId == session.UserId && row.Status == SyncOutboxStatus.InFlight)
                    .ToListAsync(ct).ConfigureAwait(false);
                foreach (var row in interrupted)
                    row.Status = SyncOutboxStatus.Pending;
                if (interrupted.Count > 0)
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            while (true)
            {
                GroundBookingRequestOutboxItem? item;
                await using (var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
                {
                    item = await db.GroundBookingRequestOutbox
                        .Where(row => row.UserId == session.UserId && row.Status == SyncOutboxStatus.Pending)
                        .OrderBy(row => row.CreatedAtUtc)
                        .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                    if (item is null)
                        return;
                    item.Status = SyncOutboxStatus.InFlight;
                    item.AttemptCount++;
                    item.LastAttemptAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                }

                try
                {
                    var request = JsonSerializer.Deserialize<GroundBookingCreateRequest>(item.PayloadJson, Json)
                                  ?? throw new InvalidDataException("Saved ground booking request is invalid.");
                    await _api.RequestBookingAsync(item.GroundId, request, ct).ConfigureAwait(false);
                    await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                    var saved = await db.GroundBookingRequestOutbox.FindAsync([item.RequestId], ct)
                        .ConfigureAwait(false);
                    if (saved is not null)
                        db.GroundBookingRequestOutbox.Remove(saved);
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var permanent = IsPermanentFailure(ex);
                    await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
                    var saved = await db.GroundBookingRequestOutbox.FindAsync([item.RequestId], ct)
                        .ConfigureAwait(false);
                    if (saved is not null)
                    {
                        saved.Status = permanent ? SyncOutboxStatus.Failed : SyncOutboxStatus.Pending;
                        saved.LastError = TrimError(ex.Message);
                        await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    }

                    if (NetworkUi.IsNetworkFailure(ex))
                        return;
                    if (!permanent)
                        return;
                    // A failed request stays visible for the user and does not block other requests.
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsPermanentFailure(Exception ex)
    {
        if (ex is BookingConflictException)
            return true;
        if (ex is not HttpRequestException http || http.StatusCode is not HttpStatusCode status)
            return false;
        return status is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
            or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity;
    }

    private static string TrimError(string text) => text.Length > 400 ? text[..400] : text;
}
