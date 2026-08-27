using System.Text.RegularExpressions;

namespace JPL.Grounds.Api.Services;

public sealed class GroundImageStore
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxImagesPerGround = 10;
    private static readonly Regex SafeFileName = new(
        @"^[a-f0-9]{32}\.(jpg|jpeg|png|webp)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _root;

    public GroundImageStore(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["GroundImages:RootPath"]?.Trim();
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "jpl-data", "ground-images"))
            : Path.GetFullPath(configured);
    }

    public async Task<string> SaveAsync(Guid groundId, IFormFile file, CancellationToken ct)
    {
        var ext = NormalizeExtension(file.FileName, file.ContentType);
        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct).ConfigureAwait(false);
        if (buffer.Length == 0)
            throw new InvalidOperationException("The selected file is empty.");
        if (buffer.Length > MaxBytes)
            throw new InvalidOperationException("Image must be 2 MB or smaller.");
        if (!LooksLikeImage(buffer.GetBuffer(), (int)buffer.Length, ext))
            throw new InvalidOperationException("Only JPEG, PNG, or WebP images are allowed.");

        var fileName = Guid.NewGuid().ToString("N") + ext;
        var dir = DirectoryFor(groundId);
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, fileName);
        buffer.Position = 0;
        await using var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await buffer.CopyToAsync(output, ct).ConfigureAwait(false);
        return fileName;
    }

    public bool TryGetFullPath(Guid groundId, string? imagePath, out string fullPath, out string contentType)
    {
        fullPath = string.Empty;
        contentType = "application/octet-stream";
        var name = Path.GetFileName(imagePath ?? string.Empty);
        if (name.Length == 0 || !SafeFileName.IsMatch(name))
            return false;

        fullPath = Path.Combine(DirectoryFor(groundId), name);
        if (!File.Exists(fullPath))
            return false;

        contentType = ContentTypeOf(name);
        return true;
    }

    public void Delete(Guid groundId, string? imagePath)
    {
        var name = Path.GetFileName(imagePath ?? string.Empty);
        if (name.Length == 0 || !SafeFileName.IsMatch(name))
            return;

        var fullPath = Path.Combine(DirectoryFor(groundId), name);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }

    public void DeleteGroundFolder(Guid groundId)
    {
        var dir = DirectoryFor(groundId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private string DirectoryFor(Guid groundId)
        => Path.Combine(_root, groundId.ToString("N"));

    private static string NormalizeExtension(string fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg" or ".png" or ".webp")
            return ext == ".jpeg" ? ".jpg" : ext;
        if (string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
            return ".png";
        if (string.Equals(contentType, "image/webp", StringComparison.OrdinalIgnoreCase))
            return ".webp";
        return ".jpg";
    }

    private static string ContentTypeOf(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
    }

    private static bool LooksLikeImage(byte[] buffer, int length, string ext)
    {
        if (length < 12)
            return false;
        if (ext is ".jpg" or ".jpeg")
            return buffer[0] == 0xFF && buffer[1] == 0xD8;
        if (ext == ".png")
            return buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47;
        if (ext == ".webp")
            return buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[8] == 0x57 && buffer[9] == 0x45;
        return false;
    }
}
