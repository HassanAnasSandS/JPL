using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class LeagueRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public ImageSource Image { get; init; } = "cric360_logo.png";
}

public partial class LeaguesViewModel : ObservableObject
{
    private readonly ILeagueService _leagues;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly IAppNavigator _navigator;
    private readonly MasterDataCache _cache;

    public ObservableCollection<LeagueRow> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? nameFilter;
    [ObservableProperty] private string? errorMessage;

    private List<Domain.League> _all = new();

    public bool CanWrite => _auth.Current?.CanManageLeagues == true;
    public bool HasFilter => !string.IsNullOrWhiteSpace(NameFilter);

    public LeaguesViewModel(
        ILeagueService leagues,
        IAuthService auth,
        ILeagueContext league,
        IAppNavigator navigator,
        MasterDataCache cache)
    {
        _leagues = leagues;
        _auth = auth;
        _league = league;
        _navigator = navigator;
        _cache = cache;
    }

    partial void OnNameFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasFilter));
        ApplyFilter();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            _all = (await _leagues.GetAllAsync().ConfigureAwait(true)).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _all = new();
            Items.Clear();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        Items.Clear();
        IEnumerable<Domain.League> q = _all;
        if (!string.IsNullOrWhiteSpace(NameFilter))
        {
            var f = NameFilter.Trim();
            q = q.Where(v => v.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var v in q.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            Items.Add(new LeagueRow
            {
                Id = v.Id,
                Name = v.Name,
                Image = LeagueImages.ToImageSource(v.ImageBytes)
            });
        }
    }

    [RelayCommand]
    private void ClearFilter() => NameFilter = null;

    [RelayCommand]
    private async Task EditAsync(LeagueRow? row)
    {
        if (row is null || !CanWrite)
            return;
        await Shell.Current.GoToAsync($"{nameof(Pages.LeagueEditPage)}?id={row.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(LeagueRow? row)
    {
        if (row is null || !CanWrite)
            return;

        var ok = await Shell.Current.DisplayAlert(
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
            {
                _league.Clear();
                _cache.Invalidate();
                await _navigator.ShowLeaguePickerAsync().ConfigureAwait(true);
                return;
            }

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await Shell.Current.DisplayAlert("Delete failed", ex.Message, "OK").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
