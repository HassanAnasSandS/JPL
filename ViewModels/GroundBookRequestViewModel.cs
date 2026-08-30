using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class BookableServiceChoice : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string AmountLabel => Amount > 0 ? $"Rs {Amount:0}" : "Free";
    public string Display => $"{Name} · {AmountLabel}";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                SelectionChanged?.Invoke();
        }
    }

    public Action? SelectionChanged { get; set; }
}

public sealed partial class BookableShopChoice : ObservableObject
{
    public const int MaxQty = 99;

    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Kind { get; init; } = "Rent";
    public string KindLabel => string.Equals(Kind, "Sell", StringComparison.OrdinalIgnoreCase) ? "Sell" : "Rent";
    public string PriceLabel => Price > 0 ? $"Rs {Price:0}" : "Free";
    public decimal LineTotal => Qty * Price;
    public string LineLabel => Qty <= 0
        ? $"{PriceLabel} · {KindLabel}"
        : $"{Qty} × {PriceLabel} = Rs {LineTotal:0}";

    private int _qty;
    public int Qty
    {
        get => _qty;
        set
        {
            var next = Math.Clamp(value, 0, MaxQty);
            if (!SetProperty(ref _qty, next))
                return;
            OnPropertyChanged(nameof(LineLabel));
            OnPropertyChanged(nameof(LineTotal));
            QtyChanged?.Invoke();
        }
    }

    public Action? QtyChanged { get; set; }

    [RelayCommand]
    private void Increase() => Qty++;

    [RelayCommand]
    private void Decrease() => Qty--;
}

[QueryProperty(nameof(GroundId), "id")]
public partial class GroundBookRequestViewModel : ObservableObject
{
    private readonly IGroundsApiClient _api;
    private readonly IAuthService _auth;
    private readonly IAppNavigator _navigator;

    public string? GroundId { get; set; }

    public ObservableCollection<GroundBookingOfferDto> OfferChoices { get; } = new();
    public ObservableCollection<BookableServiceChoice> ServiceChoices { get; } = new();
    public ObservableCollection<BookableShopChoice> ShopChoices { get; } = new();
    public IReadOnlyList<string> HourChoices { get; } = ["1", "2", "3", "4", "5", "6", "8"];

    public const string TabBooking = "booking";
    public const string TabServices = "services";
    public const string TabShop = "shop";
    public const string TabConfirm = "confirm";

    [ObservableProperty] private string groundName = "Request booking";
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
    [ObservableProperty] private string selectedTab = TabBooking;

    public bool ShowHours => SelectedOffer?.IsHourly == true;
    public bool HasPaidServices => ServiceChoices.Count > 0;
    public bool HasShopItems => ShopChoices.Count > 0;
    public bool HasNoServices => !HasPaidServices;
    public bool HasNoShopItems => !HasShopItems;
    public bool HasExtras => !string.IsNullOrWhiteSpace(ExtrasLine);
    public bool IsBookingTab => SelectedTab == TabBooking;
    public bool IsServicesTab => SelectedTab == TabServices;
    public bool IsShopTab => SelectedTab == TabShop;
    public bool IsConfirmTab => SelectedTab == TabConfirm;

    private int _nightStartsHour = 18;
    private int _nightEndsHour = 6;

    public GroundBookRequestViewModel(IGroundsApiClient api, IAuthService auth, IAppNavigator navigator)
    {
        _api = api;
        _auth = auth;
        _navigator = navigator;
        ApplyLoggedInContact();
    }

    public async Task LoadAsync()
    {
        ApplyLoggedInContact();
        if (string.IsNullOrWhiteSpace(CustomerPhone))
        {
            try
            {
                await _auth.ValidateRemoteSessionAsync().ConfigureAwait(true);
            }
            catch
            {
                /* keep local session name / WhatsApp if GetMe fails */
            }
            ApplyLoggedInContact();
        }
        if (!await GroundBookingGate.EnsureCanBookAsync(_auth, _navigator).ConfigureAwait(true))
        {
            if (Shell.Current is not null)
            {
                try
                {
                    await Shell.Current.GoToAsync("..").ConfigureAwait(true);
                }
                catch
                {
                    /* window already switched to signup */
                }
            }
            return;
        }
        if (!Guid.TryParse(GroundId, out var id))
            return;
        var g = await _api.GetGroundAsync(id).ConfigureAwait(true);
        if (g is null)
            return;

        GroundName = g.Name;
        _nightStartsHour = GroundBookingKinds.ClampNightHour(g.NightStartsHour);
        _nightEndsHour = GroundBookingKinds.ClampNightEndHour(g.NightEndsHour);
        OfferChoices.Clear();
        foreach (var offer in g.Offers ?? [])
        {
            if (offer.IsEnabled)
                OfferChoices.Add(offer);
        }

        ServiceChoices.Clear();
        foreach (var service in g.Services ?? [])
        {
            ServiceChoices.Add(new BookableServiceChoice
            {
                Name = service.Name,
                Amount = service.Amount,
                SelectionChanged = RefreshQuote
            });
        }

        ShopChoices.Clear();
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
        NotifyExtrasVisibility();
        RefreshQuote();
        SelectedTab = TabBooking;
        if (OfferChoices.Count == 0)
            ErrorMessage = "This ground has no booking types enabled.";
    }

    partial void OnSelectedTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsBookingTab));
        OnPropertyChanged(nameof(IsServicesTab));
        OnPropertyChanged(nameof(IsShopTab));
        OnPropertyChanged(nameof(IsConfirmTab));
    }

    [RelayCommand]
    private void SelectBookingTab() => SelectedTab = TabBooking;

    [RelayCommand]
    private void SelectServicesTab() => SelectedTab = TabServices;

    [RelayCommand]
    private void SelectShopTab() => SelectedTab = TabShop;

    [RelayCommand]
    private void SelectConfirmTab() => SelectedTab = TabConfirm;

    private void NotifyExtrasVisibility()
    {
        OnPropertyChanged(nameof(HasPaidServices));
        OnPropertyChanged(nameof(HasShopItems));
        OnPropertyChanged(nameof(HasNoServices));
        OnPropertyChanged(nameof(HasNoShopItems));
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
        if (!Guid.TryParse(GroundId, out var id) || IsBusy)
            return;
        if (!await GroundBookingGate.EnsureCanBookAsync(_auth, _navigator).ConfigureAwait(true))
            return;
        if (SelectedOffer is null)
        {
            ErrorMessage = "Pick a booking type.";
            return;
        }
        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            ErrorMessage = "Your account name is missing. Sign in again.";
            return;
        }
        if (!PakistanWhatsApp.TryNormalize(CustomerPhone, out var customerPhone))
        {
            ErrorMessage = "Your account has no WhatsApp number. Sign in again after updating it.";
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
            await _api.RequestBookingAsync(id, new
            {
                startAt = start,
                endAt = end,
                bookingKind = SelectedOffer.Kind,
                hours,
                customerName = CustomerName,
                customerPhone = customerPhone,
                notes = Notes,
                services = ServiceChoices.Where(s => s.IsSelected).Select(s => s.Name).ToList(),
                shop = ShopChoices.Where(s => s.Qty > 0).Select(s => new { name = s.Name, qty = s.Qty }).ToList()
            }).ConfigureAwait(true);
            await Shell.Current.DisplayAlert("Requested", "Booking sent. The ground owner will confirm.", "OK")
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

    private void ApplyLoggedInContact()
    {
        var session = _auth.Current;
        CustomerName = session?.Username ?? string.Empty;
        CustomerPhone = session?.WhatsAppNumber ?? string.Empty;
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
            ? "Amount: owner will confirm the rate"
            : $"Amount: Rs {amount:0}";
    }

    private decimal ParseHours()
        => int.TryParse(SelectedHoursText, out var n) && n > 0 ? n : 2;
}
