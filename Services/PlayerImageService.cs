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
    private readonly IApiClient _api;
    private readonly Dictionary<Guid, byte[]> _bytes = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PlayerImageService(IApiClient api) => _api = api;

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

        var hasPhoto = (await _api.GetPlayerPhotoIdsAsync(ct).ConfigureAwait(false)).ToHashSet();
        List<Guid> missing;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var id in _bytes.Keys.Where(id => !hasPhoto.Contains(id)).ToList())
                _bytes.Remove(id);
            missing = wanted.Where(id => hasPhoto.Contains(id) && !_bytes.ContainsKey(id)).ToList();
        }
        finally
        {
            _gate.Release();
        }

        if (missing.Count > 0)
        {
            var loaded = await Task.WhenAll(missing.Select(async id =>
            {
                var bytes = await _api.GetPlayerImageAsync(id, ct).ConfigureAwait(false);
                return (id, bytes);
            })).ConfigureAwait(false);

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (var (id, bytes) in loaded)
                {
                    if (bytes is { Length: > 0 })
                        _bytes[id] = bytes;
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
                if (_bytes.TryGetValue(id, out var bytes))
                {
                    var src = PlayerPhotos.ToImageSource(bytes);
                    if (src is not null)
                        result[id] = src;
                }
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
        await _api.UploadPlayerImageAsync(playerId, jpegBytes, fileName, ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _bytes[playerId] = jpegBytes;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid playerId, CancellationToken ct = default)
    {
        await _api.DeletePlayerImageAsync(playerId, ct).ConfigureAwait(false);
        Invalidate(playerId);
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
    }
}
