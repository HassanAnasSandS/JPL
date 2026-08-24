using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Services;

namespace JPL.Api.Hubs;

/// <summary>
/// Viewers join only the match/nets they opened. List pages never join —
/// 50 live games must not fan out to every phone.
/// </summary>
public sealed class LiveHub : Hub
{
    public const string MatchUpdated = "matchUpdated";
    public const string NetsUpdated = "netsUpdated";

    private readonly IApiDbFactory _dbs;

    public LiveHub(IApiDbFactory dbs) => _dbs = dbs;

    public override async Task OnConnectedAsync()
    {
        // Authenticated MAUI clients and the public watch iframe both join match groups.
        // Unauthenticated connections can only Join/Leave — they cannot mutate scores.
        await TryAuthenticateAsync().ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public Task JoinMatch(Guid matchId)
        => Groups.AddToGroupAsync(Context.ConnectionId, MatchGroup(matchId));

    public Task LeaveMatch(Guid matchId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, MatchGroup(matchId));

    public Task JoinNets(Guid sessionId)
        => Groups.AddToGroupAsync(Context.ConnectionId, NetsGroup(sessionId));

    public Task LeaveNets(Guid sessionId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, NetsGroup(sessionId));

    public static string MatchGroup(Guid matchId) => $"match:{matchId:D}";
    public static string NetsGroup(Guid sessionId) => $"nets:{sessionId:D}";

    private async Task<bool> TryAuthenticateAsync()
    {
        var http = Context.GetHttpContext();
        if (http is null)
            return false;

        var token = http.Request.Query["access_token"].ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            var header = http.Request.Headers.Authorization.ToString();
            const string prefix = "Bearer ";
            if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                token = header[prefix.Length..].Trim();
        }

        if (!TryParseUserId(token, out var userId))
            return false;

        await using var db = _dbs.CreateMaster();
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive)
            .ConfigureAwait(false);
        if (user is null)
            return false;

        return true;
    }

    private static bool TryParseUserId(string? token, out Guid userId)
    {
        userId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token))
            return false;
        var dot = token.LastIndexOf('.');
        if (dot <= 0 || dot >= token.Length - 1)
            return false;
        return Guid.TryParseExact(token[(dot + 1)..], "N", out userId);
    }
}
