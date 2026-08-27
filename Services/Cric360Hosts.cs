namespace JPL.Services;

/// <summary>
/// One MAUI UI, two hosts. Do not mix these HttpClients.
/// League API owns Admin/User accounts. GroundOwner accounts are created on Grounds API
/// against the same Master Users table (live JPL.Api still rejects the new role until republished).
/// </summary>
public static class Cric360Hosts
{
    /// <summary>JPL.Api — login, users, leagues, matches, venues, live. Reverse proxy → :5025.</summary>
    public const string LeagueApi = "https://jpl.shoptick.shop/";

    /// <summary>JPL.Grounds.Api — catalog, images, ground bookings. Reverse proxy → :5026.</summary>
    public const string GroundsApi = "https://jplground.shoptick.shop/";
}
