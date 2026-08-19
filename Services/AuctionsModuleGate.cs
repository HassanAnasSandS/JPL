namespace JPL.Services;

/// <summary>Session unlock for Auctions write actions (Add / Edit / Delete).</summary>
public interface IAuctionsModuleGate
{
    bool IsUnlocked { get; }
    bool TryUnlock(string? password);
    void Unlock();
    void Lock();
}

public sealed class AuctionsModuleGate : IAuctionsModuleGate
{
    public const string ModulePassword = "25593";

    public bool IsUnlocked { get; private set; }

    public bool TryUnlock(string? password)
    {
        if (string.Equals(password?.Trim(), ModulePassword, StringComparison.Ordinal))
        {
            IsUnlocked = true;
            return true;
        }

        IsUnlocked = false;
        return false;
    }

    public void Unlock() => IsUnlocked = true;

    public void Lock() => IsUnlocked = false;
}
