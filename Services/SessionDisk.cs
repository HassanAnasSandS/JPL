using System.Text.Json;

namespace JPL.Services;

/// <summary>
/// Survives Android process death (recents swipe). Preferences.Apply() can be lost
/// if the OS kills the app before the async write lands; this file + a sync commit
/// keep the signed-in account.
/// </summary>
internal static class SessionDisk
{
    private const string FileName = "jpl.session.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public sealed record Payload(
        string UserId,
        string Username,
        string Role,
        string? Token,
        string? LeagueId,
        string? WhatsApp,
        int? Workspace = null);

    public static void Save(Payload payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, Json);
            foreach (var path in CandidatePaths().Distinct(StringComparer.OrdinalIgnoreCase))
                WriteAtomic(path, json);
        }
        catch
        {
            /* disk is best-effort */
        }

        FlushPlatformPrefs();
    }

    public static Payload? Load()
    {
        foreach (var path in CandidatePaths().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                var loaded = JsonSerializer.Deserialize<Payload>(File.ReadAllText(path), Json);
                if (loaded is not null && !string.IsNullOrWhiteSpace(loaded.UserId))
                    return loaded;
            }
            catch
            {
                /* try next path */
            }
        }

        return null;
    }

    public static void Clear()
    {
        foreach (var path in CandidatePaths().Distinct(StringComparer.OrdinalIgnoreCase))
        {
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

        FlushPlatformPrefs();
    }

    public static void FlushPlatformPrefs()
    {
#if ANDROID
        try
        {
            var ctx = Android.App.Application.Context;
            if (ctx is null)
                return;
            foreach (var name in new[]
                     {
                         $"{ctx.PackageName}.microsoft.maui.essentials.preferences",
                         ctx.PackageName!
                     })
            {
                using var prefs = ctx.GetSharedPreferences(name, Android.Content.FileCreationMode.Private);
                prefs?.Edit()?.Commit();
            }
        }
        catch
        {
            /* ignore */
        }
#endif
    }

    private static void WriteAtomic(string path, string json)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, path, overwrite: true);
        try
        {
            File.Delete(tmp);
        }
        catch
        {
            /* ignore */
        }
    }

    private static IEnumerable<string> CandidatePaths()
    {
#if ANDROID
        string? files = null;
        try
        {
            files = Android.App.Application.Context?.FilesDir?.AbsolutePath;
        }
        catch
        {
            /* fall through */
        }

        if (!string.IsNullOrWhiteSpace(files))
            yield return System.IO.Path.Combine(files, FileName);
#endif
        yield return System.IO.Path.Combine(FileSystem.AppDataDirectory, FileName);
    }
}
