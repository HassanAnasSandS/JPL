namespace JPL.Api.Services;

public interface IApiLeagueScope
{
    Guid CurrentLeagueId { get; }
}

public sealed class ApiLeagueScope : IApiLeagueScope
{
    public const string HeaderName = "X-League-Id";
    public const string LeagueClaim = "league_id";

    private readonly IHttpContextAccessor _http;

    public ApiLeagueScope(IHttpContextAccessor http) => _http = http;

    public Guid CurrentLeagueId
    {
        get
        {
            var ctx = _http.HttpContext;
            if (ctx is null)
                return Guid.Empty;

            // Picker header first so a bound League Admin can *view* another league.
            // Writes to a non-home league are rejected by LeagueWriteGuardMiddleware.
            if (ctx.Request.Headers.TryGetValue(HeaderName, out var header) &&
                Guid.TryParse(header.ToString(), out var selected) &&
                selected != Guid.Empty)
                return selected;

            var fromUser = ctx.User.FindFirst(LeagueClaim)?.Value;
            if (Guid.TryParse(fromUser, out var bound) && bound != Guid.Empty)
                return bound;

            return Guid.Empty;
        }
    }
}
