using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class OwnerBookingCreateViewModel : ObservableObject
{
    private readonly IGroundsApiClient _api;

    public ObservableCollection<GroundCardDto> GroundChoices { get; } = new();
    public ObservableCollection<GroundBookingOfferDto> OfferChoices { get; } = new();
    public ObservableCollection<BookableServiceChoice> ServiceChoices { get; } = new();
    public ObservableCollection<BookableShopChoice> ShopChoices { get; } = new();
    public IReadOnlyList<string> HourChoices { get; } = ["1", "2", "3", "4", "5", "6", "8"];

    [ObservableProperty] private GroundCardDto? selectedGround;
    [ObservableProperty] private GroundBookingOfferDto? selectedOffer;
    [ObservableProperty] private string selectedHoursText = "2";
    [ObservableProperty] private DateTime startDate = DateTime.Today;
    [ObservableProperty] private TimeSpan startTime = new(9, 0, 0);
    [ObservableProperty] private string quoteLine = string.Empty;
    [ObservableProperty] private string windowLine = string.Empty;
    [ObservableProperty] private string bandLine = string.Empty;
    [ObservableProperty] private string extrasLine = string.Empty;
    [ObservableProperty] private string customerName = string.Empty;
    [ObservableProperty] private string customerPhone = string.Empty;
    [ObservableProperty] private string notes = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;

    public bool ShowHours => SelectedOffer?.IsHourly == true;
    public bool HasPaidServices => ServiceChoices.Count > 0;
    public bool HasShopItems => ShopChoices.Count > 0;
    public bool HasExtras => !string.IsNullOrWhiteSpace(ExtrasLine);
    public bool HasGrounds => GroundChoices.Count > 0;

    private int _nightStartsHour = 18;
    private int _nightEndsHour = 6;

    public OwnerBookingCreateViewModel(IGroundsApiClient api) => _api = api;

    public async Task LoadAsync()
    {
        if (GroundChoices.Count > 0)
            return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var page = await _api.GetOwnerGroundsAsync(null, 50).ConfigureAwait(true);
            GroundChoices.Clear();
            foreach (var row in page.Items)
                GroundChoices.Add(row);
            OnPropertyChanged(nameof(HasGrounds));
            if (GroundChoices.Count == 0)
            {
                ErrorMessage = "Add a ground first, then you can confirm a booking.";
                return;
            }

            SelectedGround = GroundChoices[0];
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

    partial void OnSelectedGroundChanged(GroundCardDto? value)
        => _ = ApplySelectedGroundAsync(value);

    private async Task ApplySelectedGroundAsync(GroundCardDto? value)
    {
        OfferChoices.Clear();
        ServiceChoices.Clear();
        ShopChoices.Clear();
        SelectedOffer = null;
        if (value is null)
        {
            OnPropertyChanged(nameof(HasPaidServices));
            OnPropertyChanged(nameof(HasShopItems));
            RefreshQuote();
            return;
        }

        try
        {
            var g = await _api.GetGroundAsync(value.Id).ConfigureAwait(true);
            if (!ReferenceEquals(SelectedGround, value))
                return;
            if (g is null)
            {
                ErrorMessage = "Ground not found.";
                return;
            }

            _nightStartsHour = GroundBookingKinds.ClampNightHour(g.NightStartsHour);
            _nightEndsHour = GroundBookingKinds.ClampNightEndHour(g.NightEndsHour);
            foreach (var offer in g.Offers ?? [])
            {
                if (offer.IsEnabled)
                    OfferChoices.Add(offer);
            }

            foreach (var service in g.Services ?? [])
            {
                ServiceChoices.Add(new BookableServiceChoice
                {
                    Name = service.Name,
                    Amount = service.Amount,
                    SelectionChanged = RefreshQuote
                });
            }

            foreach (var item in g.Shop ?? [])
            {
                ShopChoices.Add(new BookableShopChoice
                {
                    Name = item.Name,
                    Price = item.Price,
                    Kind = item.Kind,
                    QtyChanged = RefreshQuote
                });
            }

            SelectedOffer = OfferChoices.FirstOrDefault();
            OnPropertyChanged(nameof(HasPaidServices));
            OnPropertyChanged(nameof(HasShopItems));
            RefreshQuote();
            if (OfferChoices.Count == 0)
                ErrorMessage = "This ground has no booking types enabled.";
            else if (ErrorMessage == "This ground has no booking types enabled.")
                ErrorMessage = null;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(SelectedGround, value))
                ErrorMessage = ex.Message;
        }
    }

    partial void OnSelectedOfferChanged(GroundBookingOfferDto? value)
    {
        if (value is not null)
            StartTime = new TimeSpan(Math.Clamp(value.SuggestedStartHour, 0, 23), 0, 0);
        OnPropertyChanged(nameof(ShowHours));
        RefreshQuote();
    }

    partial void OnSelectedHoursTextChanged(string value) => RefreshQuote();
    partial void OnStartDateChanged(DateTime value) => RefreshQuote();
    partial void OnStartTimeChanged(TimeSpan value) => RefreshQuote();
    partial void OnExtrasLineChanged(string value) => OnPropertyChanged(nameof(HasExtras));

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (SelectedGround is null || IsBusy)
            return;
        if (SelectedOffer is null)
        {
            ErrorMessage = "Pick a booking type.";
            return;
        }
        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            ErrorMessage = "Customer name is required.";
            return;
        }
        if (!PakistanWhatsApp.TryNormalize(CustomerPhone, out var customerPhone))
        {
            ErrorMessage = PakistanWhatsApp.RequiredError;
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var start = StartDate.Date + StartTime;
            if (start < DateTime.Now.AddMinutes(-2))
            {
                ErrorMessage = "Choose a future date and time.";
                await Shell.Current.DisplayAlert("Time not available", ErrorMessage, "OK").ConfigureAwait(true);
                return;
            }
            var hours = ShowHours ? ParseHours() : SelectedOffer.DurationHours;
            var end = start.AddHours((double)(hours <= 0 ? 1 : hours));
            await _api.CreateOwnerBookingAsync(SelectedGround.Id, new
            {
                startAt = start,
                endAt = end,
                bookingKind = SelectedOffer.Kind,
                hours,
                customerName = CustomerName.Trim(),
                customerPhone = customerPhone,
                notes = Notes,
                services = ServiceChoices.Where(s => s.IsSelected).Select(s => s.Name).ToList(),
                shop = ShopChoices.Where(s => s.Qty > 0).Select(s => new { name = s.Name, qty = s.Qty }).ToList()
            }).ConfigureAwait(true);
            await Shell.Current.DisplayAlert("Confirmed", "Booking confirmed for this customer.", "OK")
                .ConfigureAwait(true);
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
        }
        catch (BookingConflictException ex)
        {
            ErrorMessage = ex.Message;
            await Shell.Current.DisplayAlert("Time not available", ex.Message, "OK").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await Shell.Current.DisplayAlert("Could not book", ex.Message, "OK").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshQuote()
    {
        if (SelectedOffer is null)
        {
            QuoteLine = string.Empty;
            WindowLine = string.Empty;
            BandLine = string.Empty;
            ExtrasLine = string.Empty;
            return;
        }

        var start = StartDate.Date + StartTime;
        decimal hours;
        decimal amount;
        DateTime end;
        string band;
        if (SelectedOffer.IsHourly)
        {
            hours = ParseHours();
            end = start.AddHours((double)hours);
            amount = GroundBookingKinds.AmountAcross(
                SelectedOffer.Price, SelectedOffer.NightPrice, SelectedOffer.WeekendPrice, SelectedOffer.WeekendNightPrice,
                start, end, _nightStartsHour, _nightEndsHour);
            band = GroundBookingKinds.DescribeBands(
                SelectedOffer.Price, SelectedOffer.NightPrice, SelectedOffer.WeekendPrice, SelectedOffer.WeekendNightPrice,
                start, end, _nightStartsHour, _nightEndsHour);
        }
        else
        {
            hours = SelectedOffer.DurationHours <= 0 ? 1 : SelectedOffer.DurationHours;
            var rate = SelectedOffer.RateFor(start, _nightStartsHour, _nightEndsHour);
            amount = rate;
            end = start.AddHours((double)hours);
            band = GroundBookingKinds.BandTitle(start, _nightStartsHour, _nightEndsHour)
                   + (rate > 0 ? $" · Rs {rate:0}" : string.Empty);
        }

        var extras = ServiceChoices.Where(s => s.IsSelected).ToList();
        amount += extras.Sum(s => s.Amount);
        var shop = ShopChoices.Where(s => s.Qty > 0).ToList();
        amount += shop.Sum(s => s.LineTotal);

        WindowLine = $"{start:dd MMM yyyy HH:mm} – {end:HH:mm} ({hours:0} hr)";
        BandLine = band;
        var extraBits = extras.Select(s => s.Display)
            .Concat(shop.Select(s => $"{s.Name} ×{s.Qty} · Rs {s.LineTotal:0} ({s.KindLabel})"))
            .ToList();
        ExtrasLine = extraBits.Count == 0
            ? string.Empty
            : "Extras: " + string.Join(" · ", extraBits);
        QuoteLine = amount <= 0
            ? "Amount: confirm the rate after saving"
            : $"Amount: Rs {amount:0}";
    }

    private decimal ParseHours()
        => int.TryParse(SelectedHoursText, out var n) && n > 0 ? n : 2;
}
