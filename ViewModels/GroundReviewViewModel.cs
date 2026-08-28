using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed record ReviewStarVm(int Value, string Glyph, bool IsSelected);

[QueryProperty(nameof(BookingId), "bookingId")]
public partial class GroundReviewViewModel : ObservableObject
{
    private readonly IGroundsApiClient _api;
    private bool _hasExisting;

    public string? BookingId { get; set; }

    [ObservableProperty] private GroundBookingDto? booking;
    [ObservableProperty] private int rating;
    [ObservableProperty] private string comment = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private IReadOnlyList<ReviewStarVm> stars = [];

    public string GroundName => Booking?.GroundName ?? "Review";
    public string WhenLine => Booking?.WhenLabel ?? string.Empty;
    public string RatingHint => Rating <= 0 ? "Tap a star to rate" : $"{Rating} / 5";
    public string SubmitText => _hasExisting ? "Update review" : "Submit review";
    public bool CanSubmit => Rating is >= 1 and <= 5 && !IsBusy;

    public GroundReviewViewModel(IGroundsApiClient api)
    {
        _api = api;
        RebuildStars();
    }

    public async Task LoadAsync()
    {
        if (!Guid.TryParse(BookingId, out var id))
            return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            Booking = await _api.GetBookingAsync(id).ConfigureAwait(true);
            if (Booking is null)
            {
                ErrorMessage = "Booking not found.";
                return;
            }

            if (!Booking.IsCompleted)
            {
                ErrorMessage = "You can review only after the booking is marked Completed.";
                return;
            }

            var existing = await _api.GetBookingReviewAsync(id).ConfigureAwait(true);
            _hasExisting = existing is not null;
            if (existing is not null)
            {
                Rating = existing.Rating;
                Comment = existing.Comment ?? string.Empty;
            }
            else
            {
                Rating = 0;
                Comment = string.Empty;
            }

            OnPropertyChanged(nameof(GroundName));
            OnPropertyChanged(nameof(WhenLine));
            OnPropertyChanged(nameof(SubmitText));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnRatingChanged(int value)
    {
        RebuildStars();
        OnPropertyChanged(nameof(RatingHint));
        OnPropertyChanged(nameof(CanSubmit));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSubmit));

    [RelayCommand]
    private void PickRating(object? value)
    {
        var n = value switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 0
        };
        if (n is < 1 or > 5)
            return;
        Rating = n;
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (!Guid.TryParse(BookingId, out var id) || !CanSubmit)
            return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var text = string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim();
            if (_hasExisting)
                await _api.UpdateBookingReviewAsync(id, Rating, text).ConfigureAwait(true);
            else
                await _api.CreateBookingReviewAsync(id, Rating, text).ConfigureAwait(true);
            _hasExisting = true;
            await Shell.Current.DisplayAlert("Review", "Thanks for rating this ground.", "OK")
                .ConfigureAwait(true);
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(SubmitText));
        }
    }

    void RebuildStars()
        => Stars = Enumerable.Range(1, 5)
            .Select(i => new ReviewStarVm(i, i <= Rating ? "★" : "☆", i <= Rating))
            .ToList();
}
