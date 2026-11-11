namespace JPL.Services;

public enum AppWorkspace
{
    Leagues = 0,
    Grounds = 1,
    GroundOwner = 2,
    Users = 3,
    Settings = 4,
    Scheduler = 5
}

public interface IAppWorkspace
{
    AppWorkspace Current { get; }
    void Set(AppWorkspace workspace);
    void Restore();
    void Clear();
}

public sealed class AppWorkspaceStore : IAppWorkspace
{
    internal const string Key = "jpl.session.workspace";
    private AppWorkspace _current = AppWorkspace.Leagues;

    public AppWorkspace Current => _current;

    public void Set(AppWorkspace workspace)
    {
        _current = workspace;
        Preferences.Default.Set(Key, (int)workspace);
        var disk = SessionDisk.Load();
        if (disk is not null)
            SessionDisk.Save(disk with { Workspace = (int)workspace });
        else
            SessionDisk.FlushPlatformPrefs();
    }

    public void Restore()
    {
        try
        {
            if (Preferences.Default.ContainsKey(Key))
            {
                _current = FromStored(Preferences.Default.Get(Key, 0));
                return;
            }

            var disk = SessionDisk.Load();
            if (disk?.Workspace is int w)
            {
                _current = FromStored(w);
                Preferences.Default.Set(Key, (int)_current);
                SessionDisk.FlushPlatformPrefs();
                return;
            }
        }
        catch
        {
            /* Corrupt prefs must not block login. */
        }

        _current = AppWorkspace.Leagues;
    }

    /// <summary>
    /// Preferences/disk store an int. Do not use Enum.IsDefined(typeof, int) —
    /// that throws if the enum is backed by byte.
    /// </summary>
    internal static AppWorkspace FromStored(int raw)
    {
        foreach (var value in Enum.GetValues<AppWorkspace>())
        {
            if ((int)value == raw)
                return value;
        }

        return AppWorkspace.Leagues;
    }

    public void Clear()
    {
        _current = AppWorkspace.Leagues;
        Preferences.Default.Remove(Key);
        SessionDisk.FlushPlatformPrefs();
    }
}
