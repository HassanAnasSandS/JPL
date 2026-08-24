using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using JPL.Api.Contracts;
using JPL.Api.Data;
using JPL.Api.Hubs;
using JPL.Api.Models;

namespace JPL.Api.Services;

public interface ILiveFeed
{
    Task NotifyAfterApplyAsync(SyncPushRequest request, CancellationToken ct);
}

public sealed class LiveFeed : ILiveFeed
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ApiDbContext _db;
    private readonly IApiDbFactory _dbs;
    private readonly IHubContext<LiveHub> _hub;
    private readonly IApiLeagueScope _league;

    public LiveFeed(ApiDbContext db, IApiDbFactory dbs, IHubContext<LiveHub> hub, IApiLeagueScope league)
    {
        _db = db;
        _dbs = dbs;
        _hub = hub;
        _league = league;
    }

    public async Task NotifyAfterApplyAsync(SyncPushRequest request, CancellationToken ct)
    {
        try
        {
            switch (request.EntityType)
            {
                case "Delivery":
                case "Innings":
                case "Match":
                    if (TryMatchId(request, out var matchId))
                    {
                        await _hub.Clients.Group(LiveHub.MatchGroup(matchId))
                            .SendAsync(LiveHub.MatchUpdated, new { matchId, at = DateTime.Now }, ct)
                            .ConfigureAwait(false);
                    }
                    if (request.EntityType == "Match")
                        await RefreshPresenceAsync(ct).ConfigureAwait(false);
                    break;

                case "NetDelivery":
                case "NetSession":
                    // Nets stay complete-and-sync — no live telecast / presence tag.
                    break;
            }
        }
        catch
        {
            /* live notify must never fail the durable apply */
        }
    }

    private async Task RefreshPresenceAsync(CancellationToken ct)
    {
        var leagueId = _league.CurrentLeagueId;
        if (leagueId == Guid.Empty)
            return;

        var liveMatches = await _db.Matches.AsNoTracking()
            .CountAsync(m => !m.IsDeleted && m.Status == MatchStatus.InProgress, ct)
            .ConfigureAwait(false);

        await using var master = _dbs.CreateMaster();
        var row = await master.LeagueLiveIndexes.FirstOrDefaultAsync(x => x.LeagueId == leagueId, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            master.LeagueLiveIndexes.Add(new LeagueLiveIndex
            {
                LeagueId = leagueId,
                LiveMatchCount = liveMatches,
                LiveNetsCount = 0,
                UpdatedAtUtc = DateTime.Now
            });
        }
        else
        {
            row.LiveMatchCount = liveMatches;
            row.LiveNetsCount = 0;
            row.UpdatedAtUtc = DateTime.Now;
        }

        await master.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static bool TryMatchId(SyncPushRequest request, out Guid matchId)
    {
        matchId = Guid.Empty;
        if (string.Equals(request.EntityType, "Match", StringComparison.Ordinal))
        {
            matchId = request.EntityId;
            return matchId != Guid.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.PayloadJson) ? "{}" : request.PayloadJson);
            if (doc.RootElement.TryGetProperty("matchId", out var p) && p.ValueKind != JsonValueKind.Null)
            {
                matchId = p.GetGuid();
                return matchId != Guid.Empty;
            }
        }
        catch
        {
            /* payload optional on delete */
        }

        return false;
    }
}
