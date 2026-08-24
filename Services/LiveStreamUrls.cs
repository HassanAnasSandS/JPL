using System.Text.RegularExpressions;
using Microsoft.Maui.Controls;

namespace JPL.Services;

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

    public static bool HasValue(string? url)
        => !string.IsNullOrWhiteSpace(url);

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
            || host.Contains("fb.watch", StringComparison.OrdinalIgnoreCase)
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

    public static WebViewSource? ToWebViewSource(string? url, string? pageOrigin = null)
    {
        var embed = ToEmbedSrc(url);
        if (string.IsNullOrWhiteSpace(embed))
            return null;

        var yt = YouTubeId.Match(url ?? string.Empty);
        if (!yt.Success)
            return new UrlWebViewSource { Url = embed };

        embed = YouTubeEmbedUrl(yt.Groups["id"].Value, pageOrigin);

        var safe = System.Net.WebUtility.HtmlEncode(embed);
        // BaseUrl + referrerpolicy so YouTube Error 153 does not appear in the in-app player.
        return new HtmlWebViewSource
        {
            BaseUrl = "https://www.youtube.com/",
            Html =
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\" />" +
                "<meta name=\"referrer\" content=\"strict-origin-when-cross-origin\" />" +
                "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no\" />" +
                "<style>html,body{margin:0;padding:0;background:#000;height:100%;overflow:hidden}" +
                "iframe{position:absolute;inset:0;width:100%;height:100%;border:0}</style></head><body>" +
                "<iframe src=\"" + safe + "\" " +
                "referrerpolicy=\"strict-origin-when-cross-origin\" " +
                "allow=\"accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; fullscreen\" " +
                "allowfullscreen></iframe></body></html>"
        };
    }
}
