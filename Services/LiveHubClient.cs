using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace JPL.Services;

public interface ILiveHubClient
{
    Task WatchMatchAsync(Guid matchId, Func<Task> onUpdated, CancellationToken ct = default);
    Task WatchNetsAsync(Guid sessionId, Func<Task> onUpdated, CancellationToken ct = default);
    Task StopAsync();
}

/// <summary>
/// One hub connection per app. Subscribes only while a scorecard is open.
/// </summary>
public sealed class LiveHubClient : ILiveHubClient, IAsyncDisposable
{
    private readonly IOptions<ApiOptions> _options;
    private readonly AuthTokenStore _tokens;
    private readonly ILeagueContext _league;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HubConnection? _connection;
    private Guid? _matchId;
    private Guid? _sessionId;
    private Func<Task>? _onUpdated;

    public LiveHubClient(IOptions<ApiOptions> options, AuthTokenStore tokens, ILeagueContext league)
    {
        _options = options;
        _tokens = tokens;
        _league = league;
    }

    public Task WatchMatchAsync(Guid matchId, Func<Task> onUpdated, CancellationToken ct = default)
        => WatchAsync(matchId, sessionId: null, onUpdated, ct);

    public Task WatchNetsAsync(Guid sessionId, Func<Task> onUpdated, CancellationToken ct = default)
        => WatchAsync(matchId: null, sessionId, onUpdated, ct);

    private async Task WatchAsync(Guid? matchId, Guid? sessionId, Func<Task> onUpdated, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await LeaveLockedAsync().ConfigureAwait(false);
            _matchId = matchId;
            _sessionId = sessionId;
            _onUpdated = onUpdated;

            if (!await EnsureConnectedAsync(ct).ConfigureAwait(false))
                return;

            if (matchId is Guid mid)
                await _connection!.InvokeAsync("JoinMatch", mid, ct).ConfigureAwait(false);
            if (sessionId is Guid sid)
                await _connection!.InvokeAsync("JoinNets", sid, ct).ConfigureAwait(false);
        }
        catch
        {
            /* scorecard falls back to REST poll */
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await LeaveLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LeaveLockedAsync()
    {
        try
        {
            if (_connection is { State: HubConnectionState.Connected })
            {
                if (_matchId is Guid mid)
                    await _connection.InvokeAsync("LeaveMatch", mid).ConfigureAwait(false);
                if (_sessionId is Guid sid)
                    await _connection.InvokeAsync("LeaveNets", sid).ConfigureAwait(false);
            }
        }
        catch
        {
            /* leaving is best-effort */
        }

        _matchId = null;
        _sessionId = null;
        _onUpdated = null;
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken ct)
    {
        var token = _tokens.Token;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (_connection is { State: HubConnectionState.Connected })
            return true;

        if (_connection is not null)
        {
            try { await _connection.DisposeAsync().ConfigureAwait(false); }
            catch { /* recreate */ }
            _connection = null;
        }

        var baseUrl = _options.Value.BaseUrl.TrimEnd('/') + "/";
        var leagueId = _league.CurrentLeagueId;

        _connection = new HubConnectionBuilder()
            .WithUrl(baseUrl + "hubs/live", o =>
            {
                o.AccessTokenProvider = () => Task.FromResult<string?>(_tokens.Token);
                if (leagueId is Guid id && id != Guid.Empty)
                    o.Headers["X-League-Id"] = id.ToString("D");
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<LiveTick>("matchUpdated", _ => Tick());
        _connection.On<LiveTick>("netsUpdated", _ => Tick());
        _connection.Reconnected += async _ =>
        {
            try
            {
                if (_matchId is Guid mid)
                    await _connection.InvokeAsync("JoinMatch", mid).ConfigureAwait(false);
                if (_sessionId is Guid sid)
                    await _connection.InvokeAsync("JoinNets", sid).ConfigureAwait(false);
                Tick();
            }
            catch
            {
                /* poll fallback */
            }
        };

        await _connection.StartAsync(ct).ConfigureAwait(false);
        return _connection.State == HubConnectionState.Connected;
    }

    private void Tick()
    {
        var cb = _onUpdated;
        if (cb is null)
            return;
        _ = Task.Run(async () =>
        {
            try { await cb().ConfigureAwait(false); }
            catch { /* UI refresh best-effort */ }
        });
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
        _gate.Dispose();
    }

    private sealed class LiveTick
    {
        public Guid MatchId { get; set; }
        public Guid SessionId { get; set; }
    }
}
