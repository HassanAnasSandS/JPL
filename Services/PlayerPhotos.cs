using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;

namespace JPL.Services;

public static class PlayerPhotos
{
    public const int MaxUploadBytes = 512 * 1024;
    private const float MaxEdge = 480;

    public static string Initials(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";

        var s = parts.Length == 1 ? parts[0] : "?";
        return s.Length >= 2
            ? s[..2].ToUpperInvariant()
            : s.ToUpperInvariant();
    }

    public static ImageSource? ToImageSource(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
            return null;
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    public static async Task<byte[]> ToJpegAsync(Stream source, CancellationToken ct = default)
    {
        using var copy = new MemoryStream();
        await source.CopyToAsync(copy, ct).ConfigureAwait(false);
        if (copy.Length == 0)
            throw new InvalidOperationException("The selected file is empty.");
        if (copy.Length > 8_000_000)
            throw new InvalidOperationException("Image is too large. Choose a file under 8 MB.");

        copy.Position = 0;
        try
        {
            var image = PlatformImage.FromStream(copy);
            if (image is null)
                throw new InvalidOperationException("Could not read that image.");

            Microsoft.Maui.Graphics.IImage ready = image;
            if (image.Width > MaxEdge || image.Height > MaxEdge)
                ready = image.Downsize(MaxEdge, disposeOriginal: true);

            using var jpeg = new MemoryStream();
            ready.Save(jpeg, ImageFormat.Jpeg, 0.82f);
            if (jpeg.Length == 0 || jpeg.Length > MaxUploadBytes)
                throw new InvalidOperationException("Image must be 512 KB or smaller after resize.");
            return jpeg.ToArray();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            if (copy.Length > MaxUploadBytes)
                throw new InvalidOperationException("Image must be 512 KB or smaller.");
            return copy.ToArray();
        }
    }
}
