namespace JPL.Services;

/// <summary>Public league-registration contact (not created in-app except by HassanAnas).</summary>
public static class LeagueRegistrationContact
{
    public const string WhatsAppDisplay = "0322 2857730";
    public const string WhatsAppDigits = "923222857730";
    public const string EmailDisplay = "official.cric360@gmail.com";
    public const string FacebookDisplay = "Official.Cric360";
    public const string FacebookUrl = "https://www.facebook.com/profile.php?id=61593997581019";
    public const string InstagramDisplay = "@official.cric360";
    public const string InstagramUrl = "https://www.instagram.com/official.cric360/";
    public const string TikTokDisplay = "@official.cric360";
    public const string TikTokUrl = "https://www.tiktok.com/@official.cric360";
    public const string YouTubeDisplay = "cric360 Cricket Scoring";
    public const string YouTubeUrl = "https://www.youtube.com/channel/UCMNxf-lp9EwY4LW95D1HFiA";

    public static string PrefillMessage =>
        "Hello Cric360 team," + Environment.NewLine + Environment.NewLine +
        "I would like to register my cricket league on Cric360." + Environment.NewLine + Environment.NewLine +
        "League name:" + Environment.NewLine +
        "League logo: (I will send the logo image)" + Environment.NewLine + Environment.NewLine +
        "Please share next steps. Thank you.";

    public static string GroundOwnerPrefillMessage =>
        "Hello Cric360 team," + Environment.NewLine + Environment.NewLine +
        "I would like to register as a Ground Owner on Cric360." + Environment.NewLine + Environment.NewLine +
        "Ground name:" + Environment.NewLine +
        "City / area:" + Environment.NewLine +
        "WhatsApp:" + Environment.NewLine + Environment.NewLine +
        "Please share next steps. Thank you.";

    public static Uri WhatsAppUri => WhatsAppUriFor(PrefillMessage);

    public static Uri GroundOwnerWhatsAppUri => WhatsAppUriFor(GroundOwnerPrefillMessage);

    public static Uri EmailUri => EmailUriFor("Cric360 League Registration", PrefillMessage);

    public static Uri GroundOwnerEmailUri => EmailUriFor("Cric360 Ground Owner Registration", GroundOwnerPrefillMessage);

    private static Uri WhatsAppUriFor(string message)
        => new($"https://wa.me/{WhatsAppDigits}?text={Uri.EscapeDataString(message)}");

    private static Uri EmailUriFor(string subject, string body)
        => new($"mailto:{EmailDisplay}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}");

    public static Uri FacebookUri => new(FacebookUrl);

    public static Uri InstagramUri => new(InstagramUrl);

    public static Uri TikTokUri => new(TikTokUrl);

    public static Uri YouTubeUri => new(YouTubeUrl);

    public static Task OpenWhatsAppAsync()
        => Launcher.Default.OpenAsync(WhatsAppUri);

    public static Task OpenGroundOwnerWhatsAppAsync()
        => Launcher.Default.OpenAsync(GroundOwnerWhatsAppUri);

    public static Task OpenEmailAsync()
        => Launcher.Default.OpenAsync(EmailUri);

    public static Task OpenGroundOwnerEmailAsync()
        => Launcher.Default.OpenAsync(GroundOwnerEmailUri);

    public static Task OpenFacebookAsync()
        => Launcher.Default.OpenAsync(FacebookUri);

    public static Task OpenInstagramAsync()
        => Launcher.Default.OpenAsync(InstagramUri);

    public static Task OpenTikTokAsync()
        => Launcher.Default.OpenAsync(TikTokUri);

    public static Task OpenYouTubeAsync()
        => Launcher.Default.OpenAsync(YouTubeUri);
}
