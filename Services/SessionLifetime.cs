namespace JPL.Services;

/// <summary>
/// One sign-in "generation". Logout / account switch cancels every background sync that was
/// started for the previous League Admin and waits briefly for it to unwind, so a sync never
/// continues with the next user's token, league filter, or outbox.
/// </summary>
public interface ISessionLifetime
{
    /// <summary>Cancelled when the current sign-in ends (logout, forced logout, new login).</summary>
    CancellationToken Token { get; }

    /// <summary>Increments on every session end; lets long runs detect an account switch.</summary>
    long Generation { get; }

    /// <summary>Background workers register a callback that waits (bounded) until they are idle.</summary>
    void RegisterDrain(Func<TimeSpan, Task> drain);

    /// <summary>Cancel the current session's background work and wait (bounded) for it to stop.</summary>
    Task EndSessionAsync();
}

public sealed class SessionLifetime : ISessionLifetime
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    private readonly object _lock = new();
    private readonly List<Func<TimeSpan, Task>> _drains = new();
    private CancellationTokenSource _cts = new();
    private long _generation;

    public CancellationToken Token
    {
        get { lock (_lock) return _cts.Token; }
    }

    public long Generation => Interlocked.Read(ref _generation);

    public void RegisterDrain(Func<TimeSpan, Task> drain)
    {
        lock (_lock)
            _drains.Add(drain);
    }

    public async Task EndSessionAsync()
    {
        CancellationTokenSource old;
        Func<TimeSpan, Task>[] drains;
        lock (_lock)
        {
            old = _cts;
            Interlocked.Increment(ref _generation);
            drains = _drains.ToArray();
        }

        try { old.Cancel(); }
        catch { /* a registration callback threw — still end the session */ }

        if (drains.Length > 0)
        {
            try
            {
                var all = Task.WhenAll(drains.Select(SafeDrain));
                await Task.WhenAny(all, Task.Delay(DrainTimeout)).ConfigureAwait(false);
            }
            catch
            {
                /* never block logout */
            }
        }

        // Swap only after draining: anything that tries to start while we drain still sees the
        // cancelled token and stops immediately. Do not Dispose the old source — in-flight work
        // may still be linked to its (cancelled) token.
        lock (_lock)
        {
            if (ReferenceEquals(_cts, old))
                _cts = new CancellationTokenSource();
        }
    }

    private static async Task SafeDrain(Func<TimeSpan, Task> drain)
    {
        try { await drain(DrainTimeout).ConfigureAwait(false); }
        catch { /* drain is best-effort */ }
    }
}
