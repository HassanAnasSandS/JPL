using System.Net;
using System.Text.RegularExpressions;

namespace JPL.Api.Services;

/// <summary>Normalizes a pasted live link and builds an iframe-safe embed URL (YouTube / Facebook / https).</summary>
public static class LiveStreamUrls
{
    public const int MaxLength = 1000;

    private static readonly Regex IframeSrc = new(
        """src\s*=\s*["'](?<url>https?://[^"']+)["']""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex YouTubeId = new(
        @"(?:youtube\.com/(?:watch\?(?:.*&)?v=|embed/|live/|shorts/)|youtu\.be/)(?<id>[A-Za-z0-9_-]{6,})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? Normalize(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
            return null;
        if (text.Length > MaxLength)
            throw new ArgumentException("Live stream link must be 1000 characters or fewer.");

        var iframe = IframeSrc.Match(text);
        if (iframe.Success)
            text = iframe.Groups["url"].Value.Trim();

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Paste a YouTube, Facebook, or https live link.");

        return uri.ToString();
    }

    public static string? ToEmbedSrc(string? url)
    {
        var normalized = url?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return null;

        var yt = YouTubeId.Match(uri.ToString());
        if (yt.Success)
        {
            var id = yt.Groups["id"].Value;
            return YouTubeEmbedUrl(id);
        }

        var host = uri.Host;
        if (host.Contains("facebook.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("fb.watch", StringComparison.OrdinalIgnoreCase))
        {
            var href = Uri.EscapeDataString(uri.ToString());
            return $"https://www.facebook.com/plugins/video.php?href={href}&show_text=false&autoplay=true";
        }

        return uri.ToString();
    }

    public static string YouTubeEmbedUrl(string videoId, string? pageOrigin = null)
    {
        var qs = "autoplay=1&mute=0&playsinline=1&rel=0&modestbranding=1&enablejsapi=1&fs=1";
        if (!string.IsNullOrWhiteSpace(pageOrigin)
            && Uri.TryCreate(pageOrigin, UriKind.Absolute, out var origin))
        {
            qs += "&origin=" + Uri.EscapeDataString(origin.GetLeftPart(UriPartial.Authority));
            qs += "&widget_referrer=" + Uri.EscapeDataString(origin.GetLeftPart(UriPartial.Authority));
        }

        return $"https://www.youtube.com/embed/{videoId}?{qs}";
    }

    public static string HtmlEncode(string value) => WebUtility.HtmlEncode(value);
}
