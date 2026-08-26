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
    private readonly IApiClient _api;
    private readonly Dictionary<Guid, byte[]> _bytes = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TeamImageService(IApiClient api) => _api = api;

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

        var hasPhoto = (await _api.GetTeamPhotoIdsAsync(ct).ConfigureAwait(false)).ToHashSet();
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
                var bytes = await _api.GetTeamImageAsync(id, ct).ConfigureAwait(false);
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

    public async Task UploadAsync(Guid teamId, byte[] jpegBytes, string fileName, CancellationToken ct = default)
    {
        await _api.UploadTeamImageAsync(teamId, jpegBytes, fileName, ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _bytes[teamId] = jpegBytes;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid teamId, CancellationToken ct = default)
    {
        await _api.DeleteTeamImageAsync(teamId, ct).ConfigureAwait(false);
        Invalidate(teamId);
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
    }
}
