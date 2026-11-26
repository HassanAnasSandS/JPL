namespace JPL.Services;

public interface IPlayerImageService
{
    /// <summary>Loads photos only for the given player IDs (Player module). Other screens must not call this.</summary>
    Task<IReadOnlyDictionary<Guid, ImageSource>> GetPhotosAsync(IEnumerable<Guid> playerIds, CancellationToken ct = default);
    Task<ImageSource?> GetPhotoAsync(Guid playerId, CancellationToken ct = default);
    Task UploadAsync(Guid playerId, byte[] jpegBytes, string fileName, CancellationToken ct = default);
    Task DeleteAsync(Guid playerId, CancellationToken ct = default);
    void Invalidate(Guid playerId);
}

public sealed class PlayerImageService : IPlayerImageService
{
    private const string Kind = "players";
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly Dictionary<Guid, byte[]> _bytes = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Guid? _lastUserId;
    private bool? _lastLocalMode;

    public PlayerImageService(IApiClient api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    private bool UsesLocalStorage => LeagueSyncPolicy.UsesLocalStorage(_auth.Current);

    public async Task<ImageSource?> GetPhotoAsync(Guid playerId, CancellationToken ct = default)
    {
        var map = await GetPhotosAsync(new[] { playerId }, ct).ConfigureAwait(false);
        return map.TryGetValue(playerId, out var src) ? src : null;
    }

    public async Task<IReadOnlyDictionary<Guid, ImageSource>> GetPhotosAsync(
        IEnumerable<Guid> playerIds,
        CancellationToken ct = default)
    {
        var wanted = playerIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var result = new Dictionary<Guid, ImageSource>();
        if (wanted.Count == 0)
            return result;

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
                hasPhoto = (await _api.GetPlayerPhotoIdsAsync(timeout.Token).ConfigureAwait(false)).ToHashSet();
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
                    var bytes = await _api.GetPlayerImageAsync(id, timeout.Token).ConfigureAwait(false);
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

    public async Task UploadAsync(Guid playerId, byte[] jpegBytes, string fileName, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _bytes[playerId] = jpegBytes;
        }
        finally
        {
            _gate.Release();
        }
        if (UsesLocalStorage)
        {
            await ImageDiskCache.SaveAsync(Kind, playerId, jpegBytes, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await _api.UploadPlayerImageAsync(playerId, jpegBytes, fileName, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex))
        {
            /* kept on device */
        }
    }

    public async Task DeleteAsync(Guid playerId, CancellationToken ct = default)
    {
        Invalidate(playerId);
        if (UsesLocalStorage)
            return;
        try
        {
            await _api.DeletePlayerImageAsync(playerId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex))
        {
            /* local already cleared */
        }
    }

    public void Invalidate(Guid playerId)
    {
        _gate.Wait();
        try
        {
            _bytes.Remove(playerId);
        }
        finally
        {
            _gate.Release();
        }
        if (UsesLocalStorage)
            ImageDiskCache.Delete(Kind, playerId);
    }
}
