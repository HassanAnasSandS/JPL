using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class OwnerBookingsViewModel : ObservableObject
{
    private readonly IGroundsApiClient _api;
    private int _loadBusy;
    private string? _cursor;
    private bool _suppressGroundChange;

    public ObservableCollection<GroundBookingDto> Items { get; } = new();
    public ObservableCollection<GroundPick> GroundPicks { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private GroundPick? selectedGround;
    [ObservableProperty] private bool hasGroundPicker;

    public string CountLabel => TotalCount <= 0 ? "No bookings" : $"{Items.Count} of {TotalCount}";

    public OwnerBookingsViewModel(IGroundsApiClient api) => _api = api;

    partial void OnSelectedGroundChanged(GroundPick? value)
    {
        if (_suppressGroundChange)
            return;
        _ = ReloadAsync();
    }

    [RelayCommand]
    private async Task AddAsync()
        => await Shell.Current.GoToAsync(nameof(Pages.OwnerBookingCreatePage)).ConfigureAwait(true);

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
            if (_cursor is null)
                await EnsureGroundsAsync().ConfigureAwait(true);
            if (SelectedGround?.Id is not Guid groundId)
            {
                TotalCount = 0;
                HasMore = false;
                ErrorMessage = GroundPicks.Count == 0
                    ? "Add a ground first, then bookings will show here."
                    : "Select a ground to see its bookings.";
                OnPropertyChanged(nameof(CountLabel));
                return;
            }

            var page = await _api.GetOwnerBookingsAsync(groundId, _cursor).ConfigureAwait(true);
            foreach (var row in page.Items)
                Items.Add(row);
            TotalCount = page.TotalCount;
            HasMore = page.HasMore;
            _cursor = page.NextCursor;
            OnPropertyChanged(nameof(CountLabel));
            if (Items.Count == 0)
                ErrorMessage = "No bookings for this ground. Tap + Book to confirm a slot.";
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
    private async Task OpenAsync(GroundBookingDto? row)
    {
        if (row is null)
            return;
        await Shell.Current.GoToAsync($"{nameof(Pages.OwnerBookingEditPage)}?id={row.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private Task ChatCustomerAsync(GroundBookingDto? row)
        => PakistanWhatsApp.OpenChatAsync(
            row?.CustomerPhone,
            row is null ? null : $"Assalam o Alaikum {row.CustomerName}, regarding your booking at {row.GroundName}.");

    private async Task EnsureGroundsAsync()
    {
        var page = await _api.GetOwnerGroundsAsync(null, 50).ConfigureAwait(true);
        var selectedId = SelectedGround?.Id;
        _suppressGroundChange = true;
        try
        {
            GroundPicks.Clear();
            foreach (var g in page.Items)
                GroundPicks.Add(new GroundPick(g.Id, g.Name));
            HasGroundPicker = GroundPicks.Count > 0;
            SelectedGround = GroundPicks.FirstOrDefault(p => p.Id == selectedId)
                             ?? GroundPicks.FirstOrDefault();
        }
        finally
        {
            _suppressGroundChange = false;
        }
    }
}

[QueryProperty(nameof(BookingId), "id")]
public partial class OwnerBookingEditViewModel : ObservableObject
{
    private readonly IGroundsApiClient _api;

    public string? BookingId { get; set; }
    public IReadOnlyList<string> StatusOptions { get; } =
        ["Pending", "Confirmed", "Completed", "Cancelled", "Rejected"];

    [ObservableProperty] private string title = "Booking";
    [ObservableProperty] private string customerLine = string.Empty;
    [ObservableProperty] private string whenLine = string.Empty;
    [ObservableProperty] private string servicesLine = string.Empty;
    [ObservableProperty] private string shopLine = string.Empty;
    [ObservableProperty] private string selectedStatus = "Pending";

    public bool HasServicesLine => !string.IsNullOrWhiteSpace(ServicesLine);
    public bool HasShopLine => !string.IsNullOrWhiteSpace(ShopLine);
    [ObservableProperty] private string bookingAmountText = "0";
    [ObservableProperty] private string discountText = "0";
    [ObservableProperty] private string advanceText = "0";
    [ObservableProperty] private string remainingText = "0";
    [ObservableProperty] private string paymentNote = string.Empty;
    [ObservableProperty] private string notes = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canChatCustomer;
    [ObservableProperty] private string? customerPhone;
    [ObservableProperty] private string? customerName;

    public OwnerBookingEditViewModel(IGroundsApiClient api) => _api = api;

    partial void OnServicesLineChanged(string value) => OnPropertyChanged(nameof(HasServicesLine));
    partial void OnShopLineChanged(string value) => OnPropertyChanged(nameof(HasShopLine));

    public async Task LoadAsync()
    {
        if (!Guid.TryParse(BookingId, out var id))
            return;
        var b = await _api.GetBookingAsync(id).ConfigureAwait(true);
        if (b is null)
        {
            ErrorMessage = "Booking not found.";
            return;
        }

        Title = b.GroundName;
        CustomerName = b.CustomerName;
        CustomerPhone = b.CustomerPhone;
        CanChatCustomer = b.HasCustomerWhatsApp;
        CustomerLine = $"{b.CustomerName}  {b.CustomerPhone}";
        WhenLine = b.WhenLabel;
        ServicesLine = b.ServicesLine;
        ShopLine = b.ShopLine;
        SelectedStatus = b.Status;
        BookingAmountText = b.BookingAmount.ToString("0");
        DiscountText = b.Discount.ToString("0");
        AdvanceText = b.Advance.ToString("0");
        PaymentNote = b.PaymentNote ?? string.Empty;
        Notes = b.Notes ?? string.Empty;
        RecalcRemaining();
    }

    partial void OnBookingAmountTextChanged(string value) => RecalcRemaining();
    partial void OnDiscountTextChanged(string value) => RecalcRemaining();
    partial void OnAdvanceTextChanged(string value) => RecalcRemaining();

    private void RecalcRemaining()
    {
        decimal.TryParse(BookingAmountText, out var amount);
        decimal.TryParse(DiscountText, out var disc);
        decimal.TryParse(AdvanceText, out var adv);
        RemainingText = Math.Max(0, amount - Math.Max(0, disc) - Math.Max(0, adv)).ToString("0");
    }

    private bool TryReadMoney(out decimal amount, out decimal discount, out decimal advance, out string? error)
    {
        amount = discount = advance = 0;
        if (!decimal.TryParse(BookingAmountText, out amount) || amount < 0)
        {
            error = "Total must be 0 or more.";
            return false;
        }
        if (!decimal.TryParse(DiscountText, out discount) || discount < 0)
        {
            error = "Discount must be 0 or more.";
            return false;
        }
        if (!decimal.TryParse(AdvanceText, out advance) || advance < 0)
        {
            error = "Advance must be 0 or more.";
            return false;
        }
        if (discount > amount)
        {
            error = "Discount cannot exceed total.";
            return false;
        }
        if (advance > amount - discount)
        {
            error = "Advance cannot exceed remaining after discount.";
            return false;
        }
        error = null;
        return true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Guid.TryParse(BookingId, out var id) || IsBusy)
            return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            if (!TryReadMoney(out var amount, out var disc, out var adv, out var moneyError))
            {
                ErrorMessage = moneyError;
                await Shell.Current.DisplayAlert("Could not save", moneyError!, "OK").ConfigureAwait(true);
                return;
            }
            await _api.UpdateBookingAsync(id, new
            {
                status = SelectedStatus,
                discount = disc,
                advance = adv,
                paymentNote = PaymentNote,
                notes = Notes
            }).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            await Shell.Current.DisplayAlert("Saved", "Booking updated.", "OK").ConfigureAwait(true);
        }
        catch (BookingConflictException ex)
        {
            ErrorMessage = ex.Message;
            await Shell.Current.DisplayAlert("Time not available", ex.Message, "OK").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await Shell.Current.DisplayAlert("Could not save", ex.Message, "OK").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task ChatCustomerAsync()
        => PakistanWhatsApp.OpenChatAsync(
            CustomerPhone,
            string.IsNullOrWhiteSpace(CustomerName)
                ? $"Assalam o Alaikum, regarding your booking at {Title}."
                : $"Assalam o Alaikum {CustomerName}, regarding your booking at {Title}.");
}
