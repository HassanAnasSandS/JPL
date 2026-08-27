namespace JPL.Services;

public sealed class GroundsApiOptions
{
    public const string SectionName = "GroundsApi";
    public const string HttpClientName = "JplGroundsApi";

    /// <summary>Public Grounds API host (reverse proxy → Kestrel :5026).</summary>
    public string BaseUrl { get; set; } = "https://jplground.shoptick.shop/";
}
