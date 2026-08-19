namespace JPL.Services;

public sealed class ApiOptions
{
    public const string SectionName = "Api";
    public const string HttpClientName = "JplApi";

    /// <summary>Public API host (reverse proxy → Kestrel). Override in MauiProgram if needed.</summary>
    public string BaseUrl { get; set; } = "https://jpl.shoptick.shop/";
}
