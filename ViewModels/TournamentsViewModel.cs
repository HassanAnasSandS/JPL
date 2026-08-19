using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class TournamentRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string MetaLine { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string WinnerLine { get; init; } = string.Empty;
    public bool IsDraft { get; init; }
    public bool CanEdit { get; init; }
    public bool CanDelete { get; init; }
    public bool NeedsSync { get; init; }
    public bool HasWinner => !string.IsNullOrWhiteSpace(WinnerLine);
}

public partial class TournamentsViewModel : ObservableObject
{
    private readonly ITournamentService _tournaments;
    private readonly IAuthService _auth;
    private readonly ISyncService _sync;

    public ObservableCollection<TournamentRow> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? nameFilter;
    [ObservableProperty] private string? errorMessage;

    private List<TournamentListItem> _all = new();

    public bool CanWrite => _auth.CanWrite;
    public bool HasFilter => !string.IsNullOrWhiteSpace(NameFilter);

    public TournamentsViewModel(ITournamentService tournaments, IAuthService auth, ISyncService sync)
    {
        _tournaments = tournaments;
        _auth = auth;
        _sync = sync;
    }

    partial void OnNameFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasFilter));
        ApplyFilter();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            _all = (await _tournaments.GetAllAsync().ConfigureAwait(true)).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _all = new List<TournamentListItem>();
            Items.Clear();
        }
        finally { IsBusy = false; }
    }

    private void ApplyFilter()
    {
        Items.Clear();
        IEnumerable<TournamentListItem> q = _all;
        if (!string.IsNullOrWhiteSpace(NameFilter))
        {
            var f = NameFilter.Trim();
            q = q.Where(t =>
                t.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                || t.StatusText.Contains(f, StringComparison.OrdinalIgnoreCase)
                || t.WinnerLine.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var t in q)
        {
            var draft = t.Status == 0;
            Items.Add(new TournamentRow
            {
                Id = t.Id,
                Name = t.Name,
                MetaLine = t.MetaLine,
                StatusText = t.StatusText,
                WinnerLine = t.WinnerLine,
                IsDraft = draft,
                CanEdit = CanWrite && draft,
                CanDelete = CanWrite && draft,
                NeedsSync = CanWrite && t.NeedsSync
            });
        }
    }

    [RelayCommand]
    private void ClearFilter() => NameFilter = null;

    [RelayCommand]
    private async Task OpenAsync(TournamentRow? row)
    {
        if (row is null) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.TournamentDetailPage)}?id={row.Id}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync(TournamentRow? row)
    {
        if (row is null || !row.CanEdit) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.TournamentEditPage)}?id={row.Id}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SyncAsync(TournamentRow? row)
    {
        if (row is null || !CanWrite || !row.NeedsSync) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await _tournaments.EnsureReadyToSyncAsync(row.Id).ConfigureAwait(true);
            await _sync.FlushTournamentAsync(row.Id).ConfigureAwait(true);
            // Refresh tournament on API so ResolveProgress can crown champion / unlock knockouts.
            try { await _tournaments.GetAsync(row.Id).ConfigureAwait(true); }
            catch { /* offline — local sync already attempted */ }
            await LoadAsync().ConfigureAwait(true);
            await Shell.Current.DisplayAlert(
                "Sync",
                "Tournament matches synced.",
                "OK").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — cannot sync right now."
                : ex.Message;
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
            else
                await Shell.Current.DisplayAlert("Sync failed", ex.Message, "OK").ConfigureAwait(true);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteAsync(TournamentRow? row)
    {
        if (row is null || !row.CanDelete) return;
        var ok = await Shell.Current.DisplayAlert(
            "Delete draft",
            $"Delete draft \"{row.Name}\"? Only draft tournaments can be deleted.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            await _tournaments.DeleteAsync(row.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Delete failed", ex.Message, "OK").ConfigureAwait(true);
        }
    }
}
