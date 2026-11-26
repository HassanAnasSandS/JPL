namespace JPL.Services;

/// <summary>
/// Durable photo cache under AppData so Team/Player images survive offline and app restarts.
/// Binaries stay out of SyncOutbox (offline-sync rule).
/// </summary>
public static class ImageDiskCache
{
    private static string RootDir(string kind)
    {
        var dir = Path.Combine(FileSystem.AppDataDirectory, "image-cache", kind);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string PathFor(string kind, Guid id)
        => Path.Combine(RootDir(kind), id.ToString("D") + ".jpg");

    public static async Task SaveAsync(string kind, Guid id, byte[] bytes, CancellationToken ct = default)
    {
        if (bytes.Length == 0 || id == Guid.Empty)
            return;
        var path = PathFor(kind, id);
        await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
    }

    public static async Task<byte[]?> TryReadAsync(string kind, Guid id, CancellationToken ct = default)
    {
        var path = PathFor(kind, id);
        if (!File.Exists(path))
            return null;
        try
        {
            return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public static void Delete(string kind, Guid id)
    {
        var path = PathFor(kind, id);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* ignore */
        }
    }
}
