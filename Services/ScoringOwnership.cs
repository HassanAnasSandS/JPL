using JPL.Domain;

namespace JPL.Services;

/// <summary>Stable id for this app install (survives login/logout, not a factory reset).</summary>
public static class DeviceInstall
{
    private const string Key = "jpl.device.installId";

    public static Guid Id
    {
        get
        {
            var text = Preferences.Default.Get(Key, string.Empty);
            if (Guid.TryParse(text, out var id) && id != Guid.Empty)
                return id;
            id = Guid.NewGuid();
            Preferences.Default.Set(Key, id.ToString("D"));
            return id;
        }
    }
}

public static class ScoringOwnership
{
    public const string OtherAccountMessage =
        "Only the admin account that started this match can continue or edit it. You can view the live scorecard.";

    public const string OtherDeviceMessage =
        "This match can only be continued on the device where it was started. You can view the live scorecard.";

    public static bool IsOwner(Match match, IAuthService auth)
    {
        if (!auth.CanWrite)
            return false;
        var userId = auth.Current?.UserId;
        if (userId is null)
            return false;
        if (match.StartedByUserId is Guid uid && uid != userId)
            return false;
        if (match.StartedOnDeviceId is Guid did && did != DeviceInstall.Id)
            return false;
        return true;
    }

    public static void EnsureOwner(Match match, IAuthService auth)
    {
        auth.EnsureCanWrite();
        if (match.StartedByUserId is Guid uid && auth.Current?.UserId != uid)
            throw new InvalidOperationException(OtherAccountMessage);
        if (match.StartedOnDeviceId is Guid did && did != DeviceInstall.Id)
            throw new InvalidOperationException(OtherDeviceMessage);
    }

    public static void Stamp(Match match, IAuthService auth)
    {
        var userId = auth.Current?.UserId;
        if (userId is null)
            return;
        match.StartedByUserId ??= userId;
        match.StartedOnDeviceId ??= DeviceInstall.Id;
    }
}
