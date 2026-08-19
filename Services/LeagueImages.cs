namespace JPL.Services;

public static class LeagueImages
{
    public static ImageSource ToImageSource(byte[]? bytes)
    {
        if (bytes is { Length: > 0 })
            return ImageSource.FromStream(() => new MemoryStream(bytes));
        return "cric360_logo.png";
    }
}
