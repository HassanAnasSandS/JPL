using System.Text.RegularExpressions;
using Microsoft.Maui.Controls;

namespace JPL.Services;

/// <summary>Ground gallery videos are links only — never uploaded. Any http(s) URL is allowed.</summary>
public static class GroundVideoUrls
{
    public const int MaxPerGround = 8;
    public const int MaxUrlLength = 1000;

    private static readonly Regex IframeSrc = new(
        """src\s*=\s*["'](?<url>https?://[^"']+)["']""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex YouTubeId = new(
        @"(?:youtube\.com/(?:watch\?(?:.*&)?v=|embed/|live/|shorts/)|youtu\.be/)(?<id>[A-Za-z0-9_-]{6,})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex VimeoId = new(
        @"(?:vimeo\.com/(?:video/)?|player\.vimeo\.com/video/)(?<id>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryNormalize(string? raw, out string url)
    {
        url = string.Empty;
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;

        var iframe = IframeSrc.Match(text);
        if (iframe.Success)
            text = iframe.Groups["url"].Value.Trim();

        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
            return false;

        var normalized = uri.AbsoluteUri;
        if (normalized.Length > MaxUrlLength)
            return false;
        url = normalized;
        return true;
    }

    public static string Provider(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "Video";
        var host = uri.Host.Trim().TrimStart('.').ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host[4..];

        if (YouTubeId.IsMatch(url) || host is "youtube.com" or "m.youtube.com" or "youtu.be")
            return "YouTube";
        if (VimeoId.IsMatch(url) || host.Contains("vimeo", StringComparison.Ordinal))
            return "Vimeo";
        if (host.Contains("facebook", StringComparison.Ordinal) || host is "fb.watch" or "fb.com")
            return "Facebook";
        if (host.Contains("instagram", StringComparison.Ordinal))
            return "Instagram";
        if (host.Contains("tiktok", StringComparison.Ordinal))
            return "TikTok";
        if (host.Contains("dailymotion", StringComparison.Ordinal) || host is "dai.ly")
            return "Dailymotion";
        if (host is "x.com" or "twitter.com" or "mobile.twitter.com")
            return "X";
        if (string.IsNullOrWhiteSpace(host))
            return "Video";
        return host;
    }

    public static string FallbackTitle(string? url)
        => $"{Provider(url)} video";

    public static string? ThumbnailUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var yt = YouTubeId.Match(url);
        if (yt.Success)
            return $"https://i.ytimg.com/vi/{yt.Groups["id"].Value}/hqdefault.jpg";
        return null;
    }

    public static WebViewSource? ToPlayerSource(string? url, string? embedApiRoot = null)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        var yt = YouTubeId.Match(url);
        if (yt.Success)
        {
            var root = (embedApiRoot ?? Cric360Hosts.GroundsApi).Trim().TrimEnd('/');
            if (Uri.TryCreate(root, UriKind.Absolute, out var api)
                && (api.Scheme == Uri.UriSchemeHttp || api.Scheme == Uri.UriSchemeHttps))
                return new UrlWebViewSource { Url = $"{root}/embed/youtube/{yt.Groups["id"].Value}" };
            return LiveStreamUrls.ToWebViewSource(url, root);
        }

        var vimeo = VimeoId.Match(url);
        if (vimeo.Success)
            return IframeHtml($"https://player.vimeo.com/video/{vimeo.Groups["id"].Value}?autoplay=1");

        if (IsDirectFile(url))
            return VideoFileHtml(url);

        var host = uri.Host;
        if (host.Contains("facebook", StringComparison.OrdinalIgnoreCase)
            || host.Contains("fb.watch", StringComparison.OrdinalIgnoreCase)
            || host.Equals("fb.watch", StringComparison.OrdinalIgnoreCase))
        {
            var embed = LiveStreamUrls.ToEmbedSrc(url);
            if (!string.IsNullOrWhiteSpace(embed))
                return IframeHtml(embed);
        }

        return new UrlWebViewSource { Url = uri.AbsoluteUri };
    }

    static bool IsDirectFile(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        var path = uri.AbsolutePath;
        return path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".mov", StringComparison.OrdinalIgnoreCase);
    }

    static HtmlWebViewSource IframeHtml(string src)
    {
        var safe = System.Net.WebUtility.HtmlEncode(src);
        return new HtmlWebViewSource
        {
            Html =
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\" />" +
                "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no\" />" +
                "<style>html,body{margin:0;padding:0;background:#000;height:100%;overflow:hidden}" +
                "iframe{position:absolute;inset:0;width:100%;height:100%;border:0}</style></head><body>" +
                "<iframe src=\"" + safe + "\" " +
                "allow=\"accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; fullscreen\" " +
                "allowfullscreen></iframe></body></html>"
        };
    }

    static HtmlWebViewSource VideoFileHtml(string src)
    {
        var safe = System.Net.WebUtility.HtmlEncode(src);
        return new HtmlWebViewSource
        {
            Html =
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\" />" +
                "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no\" />" +
                "<style>html,body{margin:0;padding:0;background:#000;height:100%;overflow:hidden}" +
                "video{position:absolute;inset:0;width:100%;height:100%;background:#000}</style></head><body>" +
                "<video src=\"" + safe + "\" controls autoplay playsinline></video></body></html>"
        };
    }
}
