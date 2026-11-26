using JPL.Domain;

namespace JPL.Services;

/// <summary>
/// Saves only the last Shell route so Android can reopen the user's screen if it
/// reclaims the app process while the task is in the background.
/// </summary>
internal static class AndroidScreenState
{
    private const string RouteKey = "cric360.screen.route";
    private const string UserKey = "cric360.screen.user";
    private const string RoleKey = "cric360.screen.role";
    private const string LeagueKey = "cric360.screen.league";

    private static Snapshot? _current;

    public static void Save(AuthSession? session, Guid? leagueId, string? route)
    {
        if (session is null || string.IsNullOrWhiteSpace(route) || route.Length > 2048)
            return;

        _current = new Snapshot(session.UserId, session.Role, NormalizeLeague(leagueId), route);
    }

    public static string? Load(AuthSession? session, Guid? leagueId)
    {
#if ANDROID
        var snapshot = _current;
        if (session is null || snapshot is null
            || snapshot.UserId != session.UserId
            || snapshot.Role != session.Role
            || snapshot.LeagueId != NormalizeLeague(leagueId))
            return null;

        return snapshot.Route;
#else
        return null;
#endif
    }

#if ANDROID
    public static void RestoreFromBundle(Android.OS.Bundle? state)
    {
        _current = null;
        if (state is null)
            return;

        var route = state.GetString(RouteKey);
        var userText = state.GetString(UserKey);
        var roleValue = state.GetInt(RoleKey, -1);
        var leagueText = state.GetString(LeagueKey);
        if (string.IsNullOrWhiteSpace(route) || route.Length > 2048
            || !Guid.TryParse(userText, out var userId) || userId == Guid.Empty
            || roleValue < 0 || roleValue > byte.MaxValue)
            return;

        Guid? leagueId = Guid.TryParse(leagueText, out var parsedLeague) && parsedLeague != Guid.Empty
            ? parsedLeague
            : null;
        _current = new Snapshot(userId, (AppRole)roleValue, leagueId, route);
    }

    public static void SaveToBundle(Android.OS.Bundle state)
    {
        var snapshot = _current;
        if (snapshot is null)
            return;

        state.PutString(RouteKey, snapshot.Route);
        state.PutString(UserKey, snapshot.UserId.ToString("D"));
        state.PutInt(RoleKey, (int)snapshot.Role);
        state.PutString(LeagueKey, snapshot.LeagueId?.ToString("D"));
    }
#endif

    private static Guid? NormalizeLeague(Guid? leagueId)
        => leagueId is Guid id && id != Guid.Empty ? id : null;

    private sealed record Snapshot(Guid UserId, AppRole Role, Guid? LeagueId, string Route);
}
