namespace JPL.Services;

/// <summary>Pakistan mobile / WhatsApp: 03XXXXXXXXX or +92 3XXXXXXXXX.</summary>
public static class PakistanWhatsApp
{
    public const string Hint = "03XX XXXXXXX or +92 3XXXXXXXXX";
    public const string RequiredError = "Enter a valid Pakistan WhatsApp number (03XX XXXXXXX).";
    public const string InvalidError = "WhatsApp must be a valid Pakistan mobile (03XX XXXXXXX or +92 3XXXXXXXXX).";
    public const string PhoneInvalidError = "Phone must be a valid Pakistan mobile (03XX XXXXXXX or +92 3XXXXXXXXX).";

    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];

        string local;
        if (digits.StartsWith("92", StringComparison.Ordinal) && digits.Length == 12)
            local = "0" + digits[2..];
        else if (digits.Length == 10 && digits[0] == '3')
            local = "0" + digits;
        else
            local = digits;

        if (local.Length != 11 || !local.StartsWith("03", StringComparison.Ordinal))
            return false;

        normalized = local;
        return true;
    }

    public static bool TryNormalizeOptional(string? raw, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        if (!TryNormalize(raw, out var value))
            return false;
        normalized = value;
        return true;
    }

    public static string? FormatDisplay(string? raw)
    {
        if (!TryNormalize(raw, out var local))
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        return local.Length == 11 ? $"{local[..4]} {local[4..]}" : local;
    }

    public static bool TryWaMeDigits(string? raw, out string digits)
    {
        digits = string.Empty;
        if (!TryNormalize(raw, out var local))
            return false;
        digits = "92" + local[1..];
        return true;
    }

    public static Uri ChatUri(string waMeDigits, string? prefill = null)
    {
        var url = $"https://wa.me/{waMeDigits}";
        if (!string.IsNullOrWhiteSpace(prefill))
            url += "?text=" + Uri.EscapeDataString(prefill);
        return new Uri(url);
    }

    public static async Task OpenChatAsync(string? phone, string? prefill = null)
    {
        if (!TryWaMeDigits(phone, out var digits))
        {
            var page = CurrentPage;
            if (page is not null)
                await page.DisplayAlert("WhatsApp", "No WhatsApp number is listed.", "OK").ConfigureAwait(true);
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(ChatUri(digits, prefill)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            var page = CurrentPage;
            if (page is not null)
                await page.DisplayAlert("WhatsApp", "Could not open WhatsApp. " + ex.Message, "OK").ConfigureAwait(true);
        }
    }

    private static Page? CurrentPage =>
        Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0].Page : null;
}
