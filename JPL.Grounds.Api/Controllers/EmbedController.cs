using Microsoft.AspNetCore.Mvc;

namespace JPL.Grounds.Api.Controllers;

/// <summary>
/// Top-level HTTPS page for in-app WebView playback.
/// YouTube Error 153 happens when the player is loaded from a string/about:blank with no Referer.
/// </summary>
[ApiController]
[Route("embed")]
public sealed class EmbedController : ControllerBase
{
    [HttpGet("youtube/{videoId}")]
    public IActionResult YouTube(string videoId)
    {
        var id = (videoId ?? string.Empty).Trim();
        if (id.Length is < 6 or > 20 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-'))
            return BadRequest("Invalid video.");

        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        Response.Headers["Content-Security-Policy"] = "frame-ancestors *";

        var safeId = System.Net.WebUtility.HtmlEncode(id);
        var html =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\" />" +
            "<meta name=\"referrer\" content=\"strict-origin-when-cross-origin\" />" +
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no\" />" +
            "<style>html,body{margin:0;padding:0;background:#000;height:100%;overflow:hidden}" +
            "iframe{position:absolute;inset:0;width:100%;height:100%;border:0}</style></head><body>" +
            "<iframe id=\"p\" allow=\"accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; fullscreen\" allowfullscreen></iframe>" +
            "<script>(function(){var u=new URL('https://www.youtube.com/embed/" + safeId + "');" +
            "u.searchParams.set('autoplay','1');u.searchParams.set('mute','0');u.searchParams.set('playsinline','1');" +
            "u.searchParams.set('rel','0');u.searchParams.set('modestbranding','1');u.searchParams.set('enablejsapi','1');" +
            "u.searchParams.set('fs','1');u.searchParams.set('origin',location.origin);" +
            "u.searchParams.set('widget_referrer',location.origin);" +
            "var f=document.getElementById('p');f.referrerPolicy='strict-origin-when-cross-origin';" +
            "f.setAttribute('referrerpolicy','strict-origin-when-cross-origin');f.src=u.toString();})();</script>" +
            "</body></html>";
        return Content(html, "text/html; charset=utf-8");
    }
}
