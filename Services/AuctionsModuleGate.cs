namespace JPL.Services;

/// <summary>
/// Session lock kept for AuthService. Auction add/edit no longer uses a module password —
/// league admins can create; only the creator admin can edit/run/delete.
/// </summary>
public interface IAuctionsModuleGate
{
    bool IsUnlocked { get; }
    bool TryUnlock(string? password);
    void Unlock();
    void Lock();
}

public sealed class AuctionsModuleGate : IAuctionsModuleGate
{
    public bool IsUnlocked { get; private set; } = true;

    public bool TryUnlock(string? _)
    {
        IsUnlocked = true;
        return true;
    }

    public void Unlock() => IsUnlocked = true;

    public void Lock() => IsUnlocked = true;
}
