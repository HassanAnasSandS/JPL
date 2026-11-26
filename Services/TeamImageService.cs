namespace JPL.Services;

public interface ITeamImageService
{
    /// <summary>Loads logos only for the given team IDs (Team module). Other screens must not call this.</summary>
    Task<IReadOnlyDictionary<Guid, ImageSource>> GetPhotosAsync(IEnumerable<Guid> teamIds, CancellationToken ct = default);
    Task<ImageSource?> GetPhotoAsync(Guid teamId, CancellationToken ct = default);
    Task UploadAsync(Guid teamId, byte[] jpegBytes, string fileName, CancellationToken ct = default);
    Task DeleteAsync(Guid teamId, CancellationToken ct = default);
    void Invalidate(Guid teamId);
}

public sealed class TeamImageService : ITeamImageService
{
    private const string Kind = "teams";
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly Dictionary<Guid, byte[]> _bytes = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Guid? _lastUserId;
    private bool? _lastLocalMode;

    public TeamImageService(IApiClient api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    private bool UsesLocalStorage => LeagueSyncPolicy.UsesLocalStorage(_auth.Current);

    public async Task<ImageSource?> GetPhotoAsync(Guid teamId, CancellationToken ct = default)
    {
        var map = await GetPhotosAsync(new[] { teamId }, ct).ConfigureAwait(false);
        return map.TryGetValue(teamId, out var src) ? src : null;
    }

    public async Task<IReadOnlyDictionary<Guid, ImageSource>> GetPhotosAsync(
        IEnumerable<Guid> teamIds,
        CancellationToken ct = default)
    {
        var wanted = teamIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var result = new Dictionary<Guid, ImageSource>();
        if (wanted.Count == 0)
            return result;

        // Warm memory from disk first so offline lists never wait on HTTP.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var userId = _auth.Current?.UserId;
            if (_lastUserId != userId || _lastLocalMode != UsesLocalStorage)
            {
                _bytes.Clear();
                _lastUserId = userId;
                _lastLocalMode = UsesLocalStorage;
            }
            if (UsesLocalStorage)
            {
                foreach (var id in wanted.Where(id => !_bytes.ContainsKey(id)))
                {
                    var disk = await ImageDiskCache.TryReadAsync(Kind, id, ct).ConfigureAwait(false);
                    if (disk is { Length: > 0 })
                        _bytes[id] = disk;
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        HashSet<Guid>? hasPhoto = null;
        if (!UsesLocalStorage)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                hasPhoto = (await _api.GetTeamPhotoIdsAsync(timeout.Token).ConfigureAwait(false)).ToHashSet();
            }
            catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex)
                                       || ex is OperationCanceledException)
            {
                /* offline — serve memory only */
            }
        }

        List<Guid> missing;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (hasPhoto is not null)
            {
                foreach (var id in _bytes.Keys.Where(id => !hasPhoto.Contains(id)).ToList())
                {
                    _bytes.Remove(id);
                    if (UsesLocalStorage)
                        ImageDiskCache.Delete(Kind, id);
                }
            }

            missing = hasPhoto is null
                ? []
                : wanted.Where(id => hasPhoto.Contains(id) && !_bytes.ContainsKey(id)).ToList();
        }
        finally
        {
            _gate.Release();
        }

        if (missing.Count > 0)
        {
            var loaded = await Task.WhenAll(missing.Select(async id =>
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(4));
                    var bytes = await _api.GetTeamImageAsync(id, timeout.Token).ConfigureAwait(false);
                    return (id, bytes);
                }
                catch
                {
                    return (id, (byte[]?)null);
                }
            })).ConfigureAwait(false);

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (var (id, bytes) in loaded)
                {
                    if (bytes is not { Length: > 0 })
                        continue;
                    _bytes[id] = bytes;
                    if (!UsesLocalStorage)
                        continue;
                    await ImageDiskCache.SaveAsync(Kind, id, bytes, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var id in wanted)
            {
                if (!_bytes.TryGetValue(id, out var bytes))
                    continue;
                var src = PlayerPhotos.ToImageSource(bytes);
                if (src is not null)
                    result[id] = src;
            }
        }
        finally
        {
            _gate.Release();
        }

        return result;
    }

    public async Task UploadAsync(Guid teamId, byte[] jpegBytes, string fileName, CancellationToken ct = default)
    {
        // Local first — UI never depends on upload succeeding for the photo to appear.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _bytes[teamId] = jpegBytes;
        }
        finally
        {
            _gate.Release();
        }
        if (UsesLocalStorage)
        {
            await ImageDiskCache.SaveAsync(Kind, teamId, jpegBytes, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await _api.UploadTeamImageAsync(teamId, jpegBytes, fileName, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex))
        {
            // Kept on device; next online visit can re-upload from edit screen if needed.
        }
    }

    public async Task DeleteAsync(Guid teamId, CancellationToken ct = default)
    {
        Invalidate(teamId);
        if (UsesLocalStorage)
            return;
        try
        {
            await _api.DeleteTeamImageAsync(teamId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex))
        {
            /* local already cleared */
        }
    }

    public void Invalidate(Guid teamId)
    {
        _gate.Wait();
        try
        {
            _bytes.Remove(teamId);
        }
        finally
        {
            _gate.Release();
        }
        if (UsesLocalStorage)
            ImageDiskCache.Delete(Kind, teamId);
    }
}
