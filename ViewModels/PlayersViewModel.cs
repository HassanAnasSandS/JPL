using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class PlayerRow
{
    public required Player Player { get; init; }
    public ImageSource? Photo { get; init; }
    public string Initials { get; init; } = "?";
    public bool HasPhoto => Photo is not null;
    public bool ShowInitials => !HasPhoto;
    public Guid Id => Player.Id;
    public string FullName => Player.FullName;
    public bool IsActive => Player.IsActive;
    public string StatusText => Player.StatusText;
    public Color StatusColor => StatusColors.For(IsActive);
    public string ToggleActiveLabel => Player.ToggleActiveLabel;
}

public partial class PlayersViewModel : ObservableObject
{
    private readonly IPlayerService _players;
    private readonly IPlayerImageService _images;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly IOptions<ApiOptions> _api;
    private List<PlayerRow> _all = new();

    public ObservableCollection<PlayerRow> Items { get; } = new();

    /// <summary>0 = All, 1 = Active, 2 = Deactive</summary>
    [ObservableProperty] private int statusFilter;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private string nameFilter = string.Empty;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private int activeCount;
    [ObservableProperty] private int inactiveCount;

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(NameFilter) || StatusFilter != 0;

    public bool IsAllFilter => StatusFilter == 0;
    public bool IsActiveFilter => StatusFilter == 1;
    public bool IsInactiveFilter => StatusFilter == 2;

    public string AllFilterLabel => $"All ({TotalCount})";
    public string ActiveFilterLabel => $"Active ({ActiveCount})";
    public string InactiveFilterLabel => $"Deactive ({InactiveCount})";

    public PlayersViewModel(
        IPlayerService players,
        IPlayerImageService images,
        IAuthService auth,
        ILeagueContext league,
        IOptions<ApiOptions> api)
    {
        _players = players;
        _images = images;
        _auth = auth;
        _league = league;
        _api = api;
        CanWrite = _auth.CanWrite;
    }

    partial void OnNameFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        ApplyFilters();
    }

    partial void OnStatusFilterChanged(int value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsActiveFilter));
        OnPropertyChanged(nameof(IsInactiveFilter));
        ApplyFilters();
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(AllFilterLabel));
    partial void OnActiveCountChanged(int value) => OnPropertyChanged(nameof(ActiveFilterLabel));
    partial void OnInactiveCountChanged(int value) => OnPropertyChanged(nameof(InactiveFilterLabel));

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            CanWrite = _auth.CanWrite;
            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);

            var players = (await _players.GetAllAsync().ConfigureAwait(true)).ToList();
            IReadOnlyDictionary<Guid, ImageSource> photos = new Dictionary<Guid, ImageSource>();
            try
            {
                photos = await _images.GetPhotosAsync(players.Select(p => p.Id)).ConfigureAwait(true);
            }
            catch
            {
                // List still works if photos fail.
            }

            _all = players.Select(p => new PlayerRow
            {
                Player = p,
                Photo = photos.GetValueOrDefault(p.Id),
                Initials = PlayerPhotos.Initials(p.FullName)
            }).ToList();
            RefreshCounts();
            ApplyFilters();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _all = new List<PlayerRow>();
            RefreshCounts();
            ApplyFilters();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectStatus(string? status)
    {
        StatusFilter = status switch
        {
            "active" => 1,
            "inactive" or "deactive" => 2,
            _ => 0
        };
    }

    [RelayCommand]
    private void ClearFilters()
    {
        NameFilter = string.Empty;
        StatusFilter = 0;
    }

    private void RefreshCounts()
    {
        TotalCount = _all.Count;
        ActiveCount = _all.Count(p => p.IsActive);
        InactiveCount = _all.Count(p => !p.IsActive);
    }

    private void ApplyFilters()
    {
        IEnumerable<PlayerRow> q = _all;

        q = StatusFilter switch
        {
            1 => q.Where(p => p.IsActive),
            2 => q.Where(p => !p.IsActive),
            _ => q
        };

        var name = NameFilter?.Trim();
        if (!string.IsNullOrWhiteSpace(name))
            q = q.Where(p => p.FullName.Contains(name, StringComparison.OrdinalIgnoreCase));

        Items.Clear();
        foreach (var p in q)
            Items.Add(p);
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanWrite)
            return;
        await Shell.Current.GoToAsync(nameof(Pages.PlayerEditPage)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ViewAsync(Player? player)
    {
        if (player is null)
            return;

        await Shell.Current.GoToAsync($"{nameof(Pages.PlayerEditPage)}?id={player.Id}&mode=view").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync(Player? player)
    {
        if (!CanWrite || player is null)
            return;

        await Shell.Current.GoToAsync($"{nameof(Pages.PlayerEditPage)}?id={player.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(Player? player)
    {
        if (!CanWrite || player is null)
            return;

        var next = !player.IsActive;
        var verb = next ? "Activate" : "Deactivate";
        var ok = await Shell.Current.DisplayAlert(
            $"{verb} player",
            $"{verb} {player.FullName}? Inactive players will not appear when creating or updating teams.",
            verb,
            "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            await _players.SetActiveAsync(player.Id, next).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(Player? player)
    {
        if (!CanWrite || player is null)
            return;

        var ok = await Shell.Current.DisplayAlert(
            "Delete player",
            $"Remove {player.FullName}?",
            "Delete",
            "Cancel").ConfigureAwait(true);

        if (!ok)
            return;

        try
        {
            await _players.SoftDeleteAsync(player.Id).ConfigureAwait(true);
            _images.Invalidate(player.Id);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.UserMessage(ex);
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task OpenPublicDirectoryAsync()
    {
        var url = TryPublicDirectoryUrl();
        if (url is null)
        {
            ErrorMessage = "Select a league first to open public player profiles.";
            return;
        }

        try
        {
            await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.UserMessage(ex);
        }
    }

    private string? TryPublicDirectoryUrl()
    {
        if (_league.CurrentLeagueId is not Guid leagueId || leagueId == Guid.Empty)
            return null;
        var root = (_api.Value.BaseUrl ?? string.Empty).TrimEnd('/');
        return string.IsNullOrWhiteSpace(root) ? null : $"{root}/p/{leagueId:D}";
    }
}
