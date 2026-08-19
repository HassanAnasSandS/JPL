using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class VenueBookingRow
{
    public Guid Id { get; init; }
    public string VenueName { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public string CostText { get; init; } = string.Empty;
    public string BookedByLine { get; init; } = string.Empty;
    public string ParticipantsLine { get; init; } = string.Empty;
    public string OwnerLine { get; init; } = string.Empty;
    public bool CanEdit { get; init; }
}

public partial class VenueBookingsViewModel : ObservableObject
{
    private readonly IVenueBookingService _bookings;
    private readonly IAuthService _auth;

    public ObservableCollection<VenueBookingRow> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private string nameFilter = string.Empty;

    public bool HasFilter => !string.IsNullOrWhiteSpace(NameFilter);

    private List<VenueBookingListItem> _all = new();

    public VenueBookingsViewModel(IVenueBookingService bookings, IAuthService auth)
    {
        _bookings = bookings;
        _auth = auth;
        CanWrite = _auth.CanWrite;
    }

    partial void OnNameFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasFilter));
        ApplyFilters();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            CanWrite = _auth.CanWrite;

            _all = (await _bookings.GetAllAsync().ConfigureAwait(true)).ToList();
            ApplyFilters();
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — Venue Bookings need internet / API."
                : ex.Message;
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
            _all = new List<VenueBookingListItem>();
            ApplyFilters();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearFilter() => NameFilter = string.Empty;

    private void ApplyFilters()
    {
        IEnumerable<VenueBookingListItem> q = _all;
        var name = NameFilter?.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            q = q.Where(b =>
                b.VenueName.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                b.BookedByPlayerName.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                b.CreatedByUsername.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        Items.Clear();
        foreach (var b in q)
        {
            Items.Add(new VenueBookingRow
            {
                Id = b.Id,
                VenueName = b.VenueName,
                DateText = b.DateText,
                CostText = b.CostText,
                BookedByLine = b.BookedByLine,
                ParticipantsLine = b.ParticipantsLine,
                OwnerLine = b.OwnerLine,
                CanEdit = b.CanEdit
            });
        }
    }

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
