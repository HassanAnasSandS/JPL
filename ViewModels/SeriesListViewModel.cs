using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class SeriesRow
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

public partial class SeriesListViewModel : ObservableObject
{
    private readonly ISeriesService _series;
    private readonly IAuthService _auth;
    private readonly ISyncService _sync;

    public ObservableCollection<SeriesRow> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? nameFilter;
    [ObservableProperty] private string? errorMessage;

    private List<SeriesListItem> _all = new();

    public bool CanWrite => _auth.CanWrite;
    public bool HasFilter => !string.IsNullOrWhiteSpace(NameFilter);

    public SeriesListViewModel(ISeriesService series, IAuthService auth, ISyncService sync)
    {
        _series = series;
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
            _all = (await _series.GetAllAsync().ConfigureAwait(true)).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _all = new List<SeriesListItem>();
            Items.Clear();
        }
        finally { IsBusy = false; }
    }

    private void ApplyFilter()
    {
        Items.Clear();
        IEnumerable<SeriesListItem> q = _all;
        if (!string.IsNullOrWhiteSpace(NameFilter))
        {
            var f = NameFilter.Trim();
            q = q.Where(s =>
                s.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                || s.StatusText.Contains(f, StringComparison.OrdinalIgnoreCase)
                || s.WinnerLine.Contains(f, StringComparison.OrdinalIgnoreCase)
                || s.MetaLine.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var s in q)
        {
            var draft = s.Status == 0;
            Items.Add(new SeriesRow
            {
                Id = s.Id,
                Name = s.Name,
                MetaLine = s.MetaLine,
                StatusText = s.StatusText,
                WinnerLine = s.WinnerLine,
                IsDraft = draft,
                CanEdit = CanWrite && draft,
                CanDelete = CanWrite && draft,
                NeedsSync = CanWrite && s.NeedsSync
            });
        }
    }

    [RelayCommand]
    private void ClearFilter() => NameFilter = null;

    [RelayCommand]
    private async Task OpenAsync(SeriesRow? row)
    {
        if (row is null) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.SeriesDetailPage)}?id={row.Id}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync(SeriesRow? row)
    {
        if (row is null || !row.CanEdit) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.SeriesEditPage)}?id={row.Id}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SyncAsync(SeriesRow? row)
    {
        if (row is null || !CanWrite || !row.NeedsSync) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await _series.EnsureReadyToSyncAsync(row.Id).ConfigureAwait(true);
            await _sync.FlushSeriesAsync(row.Id).ConfigureAwait(true);
            try { await _series.GetAsync(row.Id).ConfigureAwait(true); }
            catch { /* offline */ }
            await LoadAsync().ConfigureAwait(true);
            await Shell.Current.DisplayAlert("Sync", "Series matches synced.", "OK").ConfigureAwait(true);
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
    private async Task DeleteAsync(SeriesRow? row)
    {
        if (row is null || !row.CanDelete) return;
        var ok = await Shell.Current.DisplayAlert(
            "Delete draft",
            $"Delete draft \"{row.Name}\"? Only draft series can be deleted.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            await _series.DeleteAsync(row.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Delete failed", ex.Message, "OK").ConfigureAwait(true);
        }
    }
}
