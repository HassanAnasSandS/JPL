using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace JPL.Api.Services;

/// <summary>
/// Neural (human-like) commentary audio via Microsoft Edge online TTS.
/// English: Indian male (cricket names). Urdu: Pakistani male.
/// </summary>
public sealed class NeuralCommentaryTts
{
    private const string Token = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string ChromiumVersion = "143.0.3650.75";
    private const string SecMsGecVersion = "1-" + ChromiumVersion;
    private const string EnVoice = "en-IN-PrabhatNeural";
    private const string UrVoice = "ur-PK-AsadNeural";
    private const int MaxChars = 400;
    private static readonly string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        $"Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0";

    private static readonly string[] SocketUrls =
    {
        $"wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken={Token}",
        $"wss://api.msedgeservices.com/tts/cognitiveservices/websocket/v1?Ocp-Apim-Subscription-Key={Token}"
    };

    private readonly IMemoryCache _cache;
    private readonly ILogger<NeuralCommentaryTts> _log;
    private double _skewSeconds;

    public NeuralCommentaryTts(IMemoryCache cache, ILogger<NeuralCommentaryTts> log)
    {
        _cache = cache;
        _log = log;
    }

    public async Task<byte[]?> SpeakMp3Async(string? text, string? lang, CancellationToken ct)
    {
        text = Clean(text);
        if (text is null)
            return null;

        var urdu = string.Equals(lang, "ur", StringComparison.OrdinalIgnoreCase);
        var key = $"tts:{ (urdu ? "ur" : "en") }:{text}";
        if (_cache.TryGetValue(key, out byte[]? hit) && hit is { Length: > 0 })
            return hit;

        byte[]? mp3 = null;
        foreach (var url in SocketUrls)
        {
            mp3 = await TrySpeakAsync(url, text, urdu, ct).ConfigureAwait(false);
            if (mp3 is { Length: > 0 })
                break;
        }

        if (mp3 is { Length: > 0 })
        {
            _cache.Set(key, mp3, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(8),
                Size = mp3.Length
            });
        }

        return mp3;
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var chars = text.Trim().Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray();
        var cleaned = new string(chars).Trim();
        if (cleaned.Length == 0)
            return null;
        return cleaned.Length <= MaxChars ? cleaned : cleaned[..MaxChars];
    }

    private async Task<byte[]?> TrySpeakAsync(string baseUrl, string text, bool urdu, CancellationToken ct)
    {
        var originalSkew = _skewSeconds;
        foreach (var skew in new[] { originalSkew, originalSkew - 300, originalSkew + 300 })
        {
            _skewSeconds = skew;
            try
            {
                var mp3 = await ConnectAndReadAsync(baseUrl, text, urdu, ct).ConfigureAwait(false);
                if (mp3 is { Length: > 0 })
                    return mp3;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Neural TTS attempt failed (skew {Skew})", skew);
            }
        }
        _skewSeconds = originalSkew;
        _log.LogWarning("Neural TTS failed for {Lang}", urdu ? "ur" : "en");
        return null;
    }

    private async Task<byte[]?> ConnectAndReadAsync(string baseUrl, string text, bool urdu, CancellationToken ct)
    {
        var voice = urdu ? UrVoice : EnVoice;
        var xmlLang = urdu ? "ur-PK" : "en-IN";
        var escaped = System.Security.SecurityElement.Escape(text) ?? text;
        var ssml =
            $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{xmlLang}'>" +
            $"<voice name='{voice}'>" +
            $"<prosody rate='-4%' pitch='+0Hz' volume='+0%'>{escaped}</prosody>" +
            "</voice></speak>";

        using var ws = new ClientWebSocket();
        ws.Options.DangerousDeflateOptions = new WebSocketDeflateOptions
        {
            ClientMaxWindowBits = 15,
            ServerMaxWindowBits = 15
        };
        TryHeader(ws, "Pragma", "no-cache");
        TryHeader(ws, "Cache-Control", "no-cache");
        TryHeader(ws, "Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        TryHeader(ws, "User-Agent", UserAgent);
        TryHeader(ws, "Accept-Language", "en-US,en;q=0.9");
        TryHeader(ws, "Cookie", $"muid={Guid.NewGuid():N};");

        var connId = Guid.NewGuid().ToString("N");
        var gec = SecMsGec(_skewSeconds);
        var url = $"{baseUrl}&ConnectionId={connId}&Sec-MS-GEC={gec}&Sec-MS-GEC-Version={SecMsGecVersion}";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        await ws.ConnectAsync(new Uri(url), timeout.Token).ConfigureAwait(false);

        var stamp = DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss") + " GMT+0000 (Coordinated Universal Time)";
        var config =
            $"X-Timestamp:{stamp}\r\n" +
            "Content-Type:application/json; charset=utf-8\r\n" +
            "Path:speech.config\r\n\r\n" +
            "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{" +
            "\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"" +
            "},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}\r\n";
        await SendTextAsync(ws, config, timeout.Token).ConfigureAwait(false);

        var reqId = Guid.NewGuid().ToString("N");
        var ssmlMsg =
            $"X-RequestId:{reqId}\r\n" +
            "Content-Type:application/ssml+xml\r\n" +
            $"X-Timestamp:{stamp}Z\r\n" +
            "Path:ssml\r\n\r\n" +
            ssml;
        await SendTextAsync(ws, ssmlMsg, timeout.Token).ConfigureAwait(false);

        using var audio = new MemoryStream();
        var buffer = new byte[64 * 1024];
        var msg = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, timeout.Token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                break;
            msg.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
                continue;

            var payload = msg.ToArray();
            msg.SetLength(0);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var s = Encoding.UTF8.GetString(payload);
                if (s.Contains("Path:turn.end", StringComparison.OrdinalIgnoreCase))
                    break;
            }
            else if (result.MessageType == WebSocketMessageType.Binary
                     && TryAudioChunk(payload, out var chunk))
            {
                audio.Write(chunk);
            }
        }

        return audio.Length > 0 ? audio.ToArray() : null;
    }

    private static bool TryAudioChunk(byte[] msg, out byte[] chunk)
    {
        chunk = [];
        if (msg.Length < 2)
            return false;
        var headerLen = (msg[0] << 8) | msg[1];
        if (headerLen <= 0 || headerLen > msg.Length)
            return false;

        string headers;
        int bodyAt;
        if (2 + headerLen <= msg.Length)
        {
            headers = Encoding.UTF8.GetString(msg, 2, headerLen);
            bodyAt = 2 + headerLen;
        }
        else
        {
            headers = Encoding.UTF8.GetString(msg, 0, Math.Min(headerLen, msg.Length));
            bodyAt = Math.Min(headerLen + 2, msg.Length);
        }

        if (!headers.Contains("Path:audio", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!headers.Contains("audio/mpeg", StringComparison.OrdinalIgnoreCase))
            return false;
        if (bodyAt >= msg.Length)
            return false;
        chunk = msg[bodyAt..];
        return chunk.Length > 0;
    }

    private static async Task SendTextAsync(ClientWebSocket ws, string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private static void TryHeader(ClientWebSocket ws, string name, string value)
    {
        try { ws.Options.SetRequestHeader(name, value); }
        catch { /* restricted header on this runtime */ }
    }

    private static string SecMsGec(double skewSeconds)
    {
        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)Math.Round(skewSeconds);
        var ticks = unix + 11_644_473_600L;
        ticks -= ticks % 300L;
        var fileTime = ticks * 10_000_000L;
        var payload = Encoding.ASCII.GetBytes(fileTime.ToString() + Token);
        var hash = SHA256.HashData(payload);
        return Convert.ToHexString(hash);
    }
}
