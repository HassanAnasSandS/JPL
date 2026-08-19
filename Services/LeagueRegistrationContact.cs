namespace JPL.Services;

/// <summary>Public league-registration contact (not created in-app except by HassanAnas).</summary>
public static class LeagueRegistrationContact
{
    public const string WhatsAppDisplay = "+92 322 2857 730";
    public const string WhatsAppDigits = "923222857730";

    public static string PrefillMessage =>
        "Hello Cric360 team," + Environment.NewLine + Environment.NewLine +
        "I would like to register a new league." + Environment.NewLine + Environment.NewLine +
        "League name:" + Environment.NewLine +
        "League logo: (I will send the logo image)" + Environment.NewLine + Environment.NewLine +
        "Thank you.";

    public static Uri WhatsAppUri =>
        new($"https://wa.me/{WhatsAppDigits}?text={Uri.EscapeDataString(PrefillMessage)}");

    public static Task OpenWhatsAppAsync()
        => Launcher.Default.OpenAsync(WhatsAppUri);
}
