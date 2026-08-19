namespace JPL.Services;

/// <summary>Process-wide auth token for HTTP calls (set by AuthService on login/restore/logout).</summary>
public sealed class AuthTokenStore
{
    private readonly object _gate = new();
    private string? _token;
    private Guid? _userId;
    private bool _suppressForcedLogout;

    public string? Token
    {
        get { lock (_gate) return _token; }
    }

    public Guid? UserId
    {
        get { lock (_gate) return _userId; }
    }

    public bool SuppressForcedLogout
    {
        get { lock (_gate) return _suppressForcedLogout; }
        set { lock (_gate) _suppressForcedLogout = value; }
    }

    public void Set(string token, Guid userId)
    {
        lock (_gate)
        {
            _token = token;
            _userId = userId;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _token = null;
            _userId = null;
        }
    }
}

public sealed class SessionExpiredException : Exception
{
    public SessionExpiredException(string message) : base(message) { }
}

public interface IAuthForcedLogout
{
    Task ForceLogoutAsync(string reason);
}

/// <summary>Clears session and returns to Login when the server rejects the account.</summary>
public sealed class AuthForcedLogout : IAuthForcedLogout
{
    private readonly IAuthService _auth;
    private readonly IAppNavigator _navigator;
    private readonly AuthTokenStore _tokens;
    private int _busy;

    public AuthForcedLogout(IAuthService auth, IAppNavigator navigator, AuthTokenStore tokens)
    {
        _auth = auth;
        _navigator = navigator;
        _tokens = tokens;
    }

    public async Task ForceLogoutAsync(string reason)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;

        try
        {
            if (!_auth.IsAuthenticated && string.IsNullOrEmpty(_tokens.Token))
                return;

            await _auth.LogoutAsync().ConfigureAwait(true);

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await _navigator.ShowLoginAsync().ConfigureAwait(true);
                    if (Application.Current?.Windows.FirstOrDefault()?.Page is Page page)
                    {
                        await page.DisplayAlert(
                                "Signed out",
                                string.IsNullOrWhiteSpace(reason)
                                    ? "Your account is no longer active. Please sign in again."
                                    : reason,
                                "OK")
                            .ConfigureAwait(true);
                    }
                }
                catch
                {
                    /* navigation may race during shutdown */
                }
            });
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}
