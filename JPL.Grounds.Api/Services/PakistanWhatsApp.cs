namespace JPL.Grounds.Api.Services;

/// <summary>Pakistan mobile / WhatsApp: 03XXXXXXXXX or +92 3XXXXXXXXX.</summary>
public static class PakistanWhatsApp
{
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
}
