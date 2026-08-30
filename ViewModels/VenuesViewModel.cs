using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class VenueRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public partial class VenuesViewModel : ObservableObject
{
    private readonly IVenueService _venues;
    private readonly IAuthService _auth;
    private int _loadBusy;
    private int _filterGen;

    public ObservableCollection<VenueRow> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private string? nameFilter;
    [ObservableProperty] private string? errorMessage;

    public bool CanWrite => _auth.CanWrite;
    public bool HasFilter => !string.IsNullOrWhiteSpace(NameFilter);
    public string CountLabel => TotalCount <= 0 ? "No venues" : $"{Items.Count} of {TotalCount}";

    public VenuesViewModel(IVenueService venues, IAuthService auth)
    {
        _venues = venues;
        _auth = auth;
    }

    partial void OnNameFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasFilter));
        _ = ReloadDebouncedAsync();
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));

    [RelayCommand]
    private Task LoadAsync() => ReloadAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        HasMore = true;
        Items.Clear();
        OnPropertyChanged(nameof(CountLabel));
        await LoadMoreAsync().ConfigureAwait(true);
    }

    private async Task ReloadDebouncedAsync()
    {
        var gen = Interlocked.Increment(ref _filterGen);
        await Task.Delay(350).ConfigureAwait(true);
        if (gen != _filterGen)
            return;
        await ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || Interlocked.Exchange(ref _loadBusy, 1) == 1)
            return;
        try
        {
            if (Items.Count == 0)
                IsBusy = true;
            else
                IsLoadingMore = true;
            ErrorMessage = null;
            var page = await _venues.GetPageAsync(Items.Count, NameFilter).ConfigureAwait(true);
            TotalCount = page.TotalCount;
            ListPaging.Append(Items, page.Items.Select(v => new VenueRow { Id = v.Id, Name = v.Name }), r => r.Id);
            HasMore = page.HasMore;
            OnPropertyChanged(nameof(CountLabel));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            if (Items.Count == 0)
                TotalCount = 0;
        }
        finally
        {
            IsBusy = false;
            IsLoadingMore = false;
            Interlocked.Exchange(ref _loadBusy, 0);
        }
    }

    [RelayCommand]
    private void ClearFilter() => NameFilter = null;

    [RelayCommand]
    private async Task EditAsync(VenueRow? row)
    {
        if (row is null || !CanWrite) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.VenueEditPage)}?id={row.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(VenueRow? row)
    {
        if (row is null || !CanWrite) return;
        var ok = await Shell.Current.DisplayAlert(
            "Delete venue",
            $"Delete \"{row.Name}\"? Only allowed if no bookings use it.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            await _venues.DeleteAsync(row.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Delete failed", ex.Message, "OK").ConfigureAwait(true);
        }
    }
}
