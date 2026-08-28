using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class LeaguePickRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public ImageSource Image { get; init; } = "cric360_logo.png";
    public string LiveTag { get; init; } = string.Empty;
    public bool HasLive => !string.IsNullOrWhiteSpace(LiveTag);
    public bool IsHome { get; init; }
    public bool IsViewOnly { get; init; }
    public string Badge { get; init; } = string.Empty;
    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);
    public string Hint { get; init; } = "Tap to open";
}

public partial class LeaguePickerViewModel : ObservableObject
{
    private readonly ILeagueService _leagues;
    private readonly ILeagueContext _league;
    private readonly IAppNavigator _navigator;
    private readonly IAppWorkspace _workspace;
    private readonly MasterDataCache _cache;
    private readonly IAuthService _auth;

    public ObservableCollection<LeaguePickRow> Items { get; } = new();
    private int _loadGen;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canManageLeagues;
    [ObservableProperty] private bool showRegisterPopup;
    [ObservableProperty] private bool hasHomeLeague;

    public bool ShowRegisterLeague => !CanManageLeagues;
    public string PickerSubtitle => HasHomeLeague
        ? "Your league is at the top. Other leagues are view only."
        : "Choose a league to continue";

    public LeaguePickerViewModel(
        ILeagueService leagues,
        ILeagueContext league,
        IAppNavigator navigator,
        IAppWorkspace workspace,
        MasterDataCache cache,
        IAuthService auth)
    {
        _leagues = leagues;
        _league = league;
        _navigator = navigator;
        _workspace = workspace;
        _cache = cache;
        _auth = auth;
        CanManageLeagues = _auth.Current?.CanManageLeagues == true;
        OnPropertyChanged(nameof(ShowRegisterLeague));
    }

    partial void OnCanManageLeaguesChanged(bool value)
        => OnPropertyChanged(nameof(ShowRegisterLeague));

    partial void OnHasHomeLeagueChanged(bool value)
        => OnPropertyChanged(nameof(PickerSubtitle));

    [RelayCommand]
    public async Task LoadAsync()
    {
        var gen = Interlocked.Increment(ref _loadGen);
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            CanManageLeagues = _auth.Current?.CanManageLeagues == true;
            OnPropertyChanged(nameof(ShowRegisterLeague));
            var list = await _leagues.GetAllAsync().ConfigureAwait(true);
            if (gen != Volatile.Read(ref _loadGen))
                return;

            var homeId = _auth.Current?.HasHomeLeague == true ? _auth.Current.LeagueId : null;
            var viewOthersReadOnly = homeId is Guid;
            var rows = list
                .GroupBy(l => l.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => PreferOneLeague(g, homeId))
                .Select(l => ToRow(l, isHome: homeId == l.Id, viewOthersReadOnly))
                .OrderByDescending(r => r.IsHome)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Items.Clear();
            foreach (var row in rows)
                Items.Add(row);

            HasHomeLeague = rows.Any(r => r.IsHome);

            if (Items.Count == 0)
                ErrorMessage = CanManageLeagues
                    ? "No leagues yet. Tap + Add to create one."
                    : "No leagues available yet. Tap Register your Own League to request yours.";
        }
        catch (Exception ex)
        {
            if (gen == Volatile.Read(ref _loadGen))
                ErrorMessage = ex.Message;
        }
        finally
        {
            if (gen == Volatile.Read(ref _loadGen))
                IsBusy = false;
        }
    }

    private static League PreferOneLeague(IGrouping<string, League> group, Guid? homeId)
    {
        if (homeId is Guid hid)
        {
            var home = group.FirstOrDefault(x => x.Id == hid);
            if (home is not null)
                return home;
        }

        var canonical = group.FirstOrDefault(x => x.Id == LeagueIds.Jpl);
        if (canonical is not null)
            return canonical;

        return group.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).First();
    }

    private static LeaguePickRow ToRow(League l, bool isHome, bool viewOthersReadOnly)
    {
        var viewOnly = viewOthersReadOnly && !isHome;
        return new LeaguePickRow
        {
            Id = l.Id,
            Name = l.Name,
            Image = LeagueImages.ToImageSource(l.ImageBytes),
            LiveTag = LiveTagFor(l),
            IsHome = isHome,
            IsViewOnly = viewOnly,
            Badge = isHome ? "Your league" : string.Empty,
            Hint = isHome
                ? "Tap to open your league"
                : viewOnly
                    ? "View only"
                    : "Tap to open"
        };
    }

    private static string LiveTagFor(League l)
    {
        if (!l.HasLive)
            return string.Empty;
        var parts = new List<string>();
        if (l.LiveMatchCount > 0)
            parts.Add(l.LiveMatchCount == 1 ? "1 match" : $"{l.LiveMatchCount} matches");
        return "LIVE · " + string.Join(" · ", parts);
    }

    [RelayCommand]
    private async Task HomeAsync()
    {
        _workspace.Clear();
        await _navigator.ShowModeChooserAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task SelectAsync(LeaguePickRow? row)
    {
        if (row is null)
            return;
        _league.Set(row.Id, row.Name);
        _cache.Invalidate();
        await _navigator.ShowMainAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenRegisterPopup() => ShowRegisterPopup = true;

    [RelayCommand]
    private void CloseRegisterPopup() => ShowRegisterPopup = false;

    [RelayCommand]
    private async Task WhatsAppRegisterAsync()
    {
        try
        {
            await LeagueRegistrationContact.OpenWhatsAppAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not open WhatsApp. Message Cric360 at "
                           + LeagueRegistrationContact.WhatsAppDisplay
                           + ". " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task EmailRegisterAsync()
    {
        try
        {
            await LeagueRegistrationContact.OpenEmailAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not open Email. Write to "
                           + LeagueRegistrationContact.EmailDisplay
                           + ". " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task FacebookRegisterAsync()
    {
        try
        {
            await LeagueRegistrationContact.OpenFacebookAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not open Facebook. Visit "
                           + LeagueRegistrationContact.FacebookUrl
                           + ". " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task InstagramRegisterAsync()
    {
        try
        {
            await LeagueRegistrationContact.OpenInstagramAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not open Instagram. Visit "
                           + LeagueRegistrationContact.InstagramUrl
                           + ". " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task TikTokRegisterAsync()
    {
        try
        {
            await LeagueRegistrationContact.OpenTikTokAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not open TikTok. Visit "
                           + LeagueRegistrationContact.TikTokUrl
                           + ". " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task YouTubeRegisterAsync()
    {
        try
        {
            await LeagueRegistrationContact.OpenYouTubeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not open YouTube. Visit "
                           + LeagueRegistrationContact.YouTubeUrl
                           + ". " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanManageLeagues)
            return;
        await _navigator.ShowLeagueEditorAsync(null).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync(LeaguePickRow? row)
    {
        if (!CanManageLeagues || row is null)
            return;
        await _navigator.ShowLeagueEditorAsync(row.Id).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(LeaguePickRow? row)
    {
        if (!CanManageLeagues || row is null)
            return;

        var ok = await ConfirmAsync(
            "Delete league",
            $"Delete \"{row.Name}\"? The league will be hidden. Match data is kept.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await _leagues.DeleteAsync(row.Id).ConfigureAwait(true);
            if (_league.CurrentLeagueId == row.Id)
                _league.Clear();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            var page = CurrentPage;
            if (page is not null)
                await page.DisplayAlert("Delete failed", ex.Message, "OK").ConfigureAwait(true);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        var ok = await ConfirmAsync(
            "Logout",
            "Sign out of Cric360?",
            "Logout",
            "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        await _auth.LogoutAsync().ConfigureAwait(true);
        await _navigator.ShowLoginAsync().ConfigureAwait(true);
    }

    private static Page? CurrentPage =>
        Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0].Page : null;

    private static async Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
    {
        var page = CurrentPage;
        if (page is null)
            return false;
        return await page.DisplayAlert(title, message, accept, cancel).ConfigureAwait(true);
    }
}
