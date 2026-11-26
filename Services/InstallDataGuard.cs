namespace JPL.Services;

/// <summary>
/// Android may restore app files/preferences during device transfer even after uninstall.
/// A marker in NoBackupFilesDir lets the first launch after reinstall discard restored state.
/// </summary>
public static class InstallDataGuard
{
    public static void PrepareForLaunch()
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            var noBackup = context.NoBackupFilesDir?.AbsolutePath;
            if (string.IsNullOrWhiteSpace(noBackup))
                return;

            Directory.CreateDirectory(noBackup);
            var marker = Path.Combine(noBackup, "install-state-v1");
            if (File.Exists(marker))
                return;

            ClearAppDataDirectory();
            ClearAndroidPreferences(context);
            SessionDisk.Clear();
            File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        }
        catch
        {
            // Never block app startup; backup exclusions still prevent future restores.
            try { ClearAndroidPreferences(Android.App.Application.Context); }
            catch { /* ignore */ }
            try { SessionDisk.Clear(); }
            catch { /* ignore */ }
        }
#endif
    }

#if ANDROID
    private static void ClearAppDataDirectory()
    {
        var root = Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
        if (!Directory.Exists(root))
            return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(root).ToList())
        {
            if (Directory.Exists(entry))
                Directory.Delete(entry, recursive: true);
            else
                File.Delete(entry);
        }
    }

    private static void ClearAndroidPreferences(Android.Content.Context context)
    {
        foreach (var name in new[]
                 {
                     $"{context.PackageName}.microsoft.maui.essentials.preferences",
                     context.PackageName
                 })
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            using var preferences = context.GetSharedPreferences(name, Android.Content.FileCreationMode.Private);
            preferences?.Edit()?.Clear()?.Commit();
        }
    }
#endif
}
