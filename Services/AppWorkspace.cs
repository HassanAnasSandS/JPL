namespace JPL.Services;

public enum AppWorkspace : byte
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
    private const string Key = "jpl.session.workspace";
    private AppWorkspace _current = AppWorkspace.Leagues;

    public AppWorkspace Current => _current;

    public void Set(AppWorkspace workspace)
    {
        _current = workspace;
        Preferences.Default.Set(Key, (int)workspace);
    }

    public void Restore()
    {
        var raw = Preferences.Default.Get(Key, (int)AppWorkspace.Leagues);
        _current = Enum.IsDefined(typeof(AppWorkspace), raw) ? (AppWorkspace)raw : AppWorkspace.Leagues;
    }

    public void Clear()
    {
        _current = AppWorkspace.Leagues;
        Preferences.Default.Remove(Key);
    }
}
