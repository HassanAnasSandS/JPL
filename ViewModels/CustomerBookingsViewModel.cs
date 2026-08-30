using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class CustomerBookingsViewModel : ObservableObject
{
    private readonly IGroundsApiClient _api;
    private int _loadBusy;
    private string? _cursor;

    public ObservableCollection<GroundBookingDto> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private string? errorMessage;

    public string CountLabel => TotalCount <= 0 ? "No bookings" : $"{Items.Count} of {TotalCount}";

    public CustomerBookingsViewModel(IGroundsApiClient api) => _api = api;

    [RelayCommand]
    private Task ReloadAsync()
    {
        _cursor = null;
        HasMore = true;
        Items.Clear();
        return LoadMoreAsync();
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
            ErrorMessage = null;
            var page = await _api.GetMyBookingsAsync(_cursor).ConfigureAwait(true);
            foreach (var row in page.Items)
                Items.Add(row);
            TotalCount = page.TotalCount;
            HasMore = page.HasMore;
            _cursor = page.NextCursor;
            OnPropertyChanged(nameof(CountLabel));
            if (Items.Count == 0)
                ErrorMessage = "No bookings yet. Discover a ground and send a request.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _loadBusy, 0);
        }
    }

    [RelayCommand]
    private Task ChatOwnerAsync(GroundBookingDto? row)
        => PakistanWhatsApp.OpenChatAsync(
            row?.OwnerChatPhone,
            row is null ? null : $"Assalam o Alaikum, I want to chat about my booking at {row.GroundName}.");

    [RelayCommand]
    private async Task WriteReviewAsync(GroundBookingDto? row)
    {
        if (row is null || !row.CanOpenReview)
            return;
        await Shell.Current.GoToAsync($"{nameof(Pages.GroundReviewPage)}?bookingId={row.Id}").ConfigureAwait(true);
    }
}
