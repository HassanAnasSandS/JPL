using System.Text.RegularExpressions;

namespace JPL.Api.Services;

/// <summary>
/// Team logos live on disk next to the API folder (not inside the publish output),
/// so republishing or deleting an old API folder does not wipe images. The database
/// stores only a short relative filename.
/// </summary>
public sealed class TeamImageStore
{
    public const int MaxBytes = 512 * 1024;
    private static readonly Regex SafeFileName = new(
        @"^[a-f0-9]{32}\.(jpg|jpeg|png|webp)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _root;

    public TeamImageStore(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["TeamImages:RootPath"]?.Trim();
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "jpl-data", "team-images"))
            : Path.GetFullPath(configured);
    }

    public string RootPath => _root;

    public async Task<string> SaveAsync(Guid leagueId, Guid teamId, IFormFile file, CancellationToken ct)
    {
        var ext = NormalizeExtension(file.FileName, file.ContentType);
        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct).ConfigureAwait(false);
        if (buffer.Length == 0)
            throw new InvalidOperationException("The selected file is empty.");
        if (buffer.Length > MaxBytes)
            throw new InvalidOperationException("Image must be 512 KB or smaller.");
        if (!LooksLikeImage(buffer.GetBuffer(), (int)buffer.Length, ext))
            throw new InvalidOperationException("Only JPEG, PNG, or WebP images are allowed.");

        var fileName = teamId.ToString("N") + ext;
        var dir = DirectoryFor(leagueId);
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, fileName);
        buffer.Position = 0;
        await using var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await buffer.CopyToAsync(output, ct).ConfigureAwait(false);
        return fileName;
    }

    public bool TryGetFullPath(Guid leagueId, string? imagePath, out string fullPath, out string contentType)
    {
        fullPath = string.Empty;
        contentType = "application/octet-stream";
        var name = Path.GetFileName(imagePath ?? string.Empty);
        if (name.Length == 0 || !SafeFileName.IsMatch(name))
            return false;

        fullPath = Path.Combine(DirectoryFor(leagueId), name);
        if (!File.Exists(fullPath))
            return false;

        contentType = ContentTypeOf(name);
        return true;
    }

    public void Delete(Guid leagueId, string? imagePath)
    {
        var name = Path.GetFileName(imagePath ?? string.Empty);
        if (name.Length == 0 || !SafeFileName.IsMatch(name))
            return;

        var fullPath = Path.Combine(DirectoryFor(leagueId), name);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }

    private string DirectoryFor(Guid leagueId)
        => Path.Combine(_root, leagueId == Guid.Empty ? "_default" : leagueId.ToString("N"));

    private static string NormalizeExtension(string? fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg" or ".png" or ".webp")
            return ext == ".jpeg" ? ".jpg" : ext;

        return contentType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new InvalidOperationException("Only JPEG, PNG, or WebP images are allowed.")
        };
    }

    private static string ContentTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    private static bool LooksLikeImage(byte[] bytes, int length, string ext)
    {
        if (length < 12)
            return false;

        if (ext is ".jpg" or ".jpeg")
            return bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        if (ext == ".png")
            return bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        if (ext == ".webp")
            return bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
                   && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';
        return false;
    }
}
