using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class VenueBookingRow
{
    public Guid Id { get; init; }
    public string VenueName { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public string CostText { get; init; } = string.Empty;
    public string PaidAmountText { get; init; } = string.Empty;
    public string OutstandingAmountText { get; init; } = string.Empty;
    public string BookedByLine { get; init; } = string.Empty;
    public string ParticipantsLine { get; init; } = string.Empty;
    public string OwnerLine { get; init; } = string.Empty;
    public bool CanEdit { get; init; }
}

public partial class VenueBookingsViewModel : ObservableObject
{
    private readonly IVenueBookingService _bookings;
    private readonly IAuthService _auth;
    private int _loadBusy;
    private int _filterGen;

    public ObservableCollection<VenueBookingRow> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private string nameFilter = string.Empty;

    public bool HasFilter => !string.IsNullOrWhiteSpace(NameFilter);
    public string CountLabel => TotalCount <= 0 ? "No bookings" : $"{Items.Count} of {TotalCount}";

    public VenueBookingsViewModel(IVenueBookingService bookings, IAuthService auth)
    {
        _bookings = bookings;
        _auth = auth;
        CanWrite = _auth.CanWrite;
    }

    partial void OnNameFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasFilter));
        _ = ReloadDebouncedAsync();
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));

    [RelayCommand]
    public Task LoadAsync() => ReloadAsync();

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
            CanWrite = _auth.CanWrite;
            var page = await _bookings.GetPageAsync(Items.Count, NameFilter).ConfigureAwait(true);
            TotalCount = page.TotalCount;
            ListPaging.Append(Items, page.Items.Select(ToRow), r => r.Id);
            HasMore = page.HasMore;
            OnPropertyChanged(nameof(CountLabel));
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — Venue Bookings need internet / API."
                : ex.Message;
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
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
    private void ClearFilter() => NameFilter = string.Empty;

    private static VenueBookingRow ToRow(VenueBookingListItem b) => new()
    {
        Id = b.Id,
        VenueName = b.VenueName,
        DateText = b.DateText,
        CostText = b.CostText,
        PaidAmountText = b.PaidAmountText,
        OutstandingAmountText = b.OutstandingAmountText,
        BookedByLine = b.BookedByLine,
        ParticipantsLine = b.ParticipantsLine,
        OwnerLine = b.OwnerLine,
        CanEdit = b.CanEdit
    };

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanWrite) return;
        await Shell.Current.GoToAsync(nameof(Pages.VenueBookingEditPage)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ViewAsync(VenueBookingRow? row)
    {
        if (row is null) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.VenueBookingEditPage)}?id={row.Id}&mode=view")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync(VenueBookingRow? row)
    {
        if (row is null || !row.CanEdit) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.VenueBookingEditPage)}?id={row.Id}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(VenueBookingRow? row)
    {
        if (row is null || !row.CanEdit) return;

        var ok = await Shell.Current.DisplayAlert(
            "Delete booking",
            $"Delete venue booking \"{row.VenueName}\"?",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok) return;

        try
        {
            await _bookings.DeleteAsync(row.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
