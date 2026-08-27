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

    public ObservableCollection<GroundBookingDto> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private string? errorMessage;

    public OwnerBookingsViewModel(IGroundsApiClient api) => _api = api;

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
            var page = await _api.GetOwnerBookingsAsync(null, _cursor).ConfigureAwait(true);
            foreach (var row in page)
                Items.Add(row);
            HasMore = page.Count >= 20;
            _cursor = HasMore && page.Count > 0
                ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{page[^1].CreatedAtUtc.Ticks:D20}|{page[^1].Id:N}"))
                : null;
            if (Items.Count == 0)
                ErrorMessage = "No booking requests yet.";
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
    [ObservableProperty] private string selectedStatus = "Pending";
    [ObservableProperty] private string bookingAmountText = "0";
    [ObservableProperty] private string advanceText = "0";
    [ObservableProperty] private string balanceText = "0";
    [ObservableProperty] private string paymentNote = string.Empty;
    [ObservableProperty] private string notes = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canChatCustomer;
    [ObservableProperty] private string? customerPhone;
    [ObservableProperty] private string? customerName;

    public OwnerBookingEditViewModel(IGroundsApiClient api) => _api = api;

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
        SelectedStatus = b.Status;
        BookingAmountText = b.BookingAmount.ToString("0");
        AdvanceText = b.Advance.ToString("0");
        BalanceText = b.Balance.ToString("0");
        PaymentNote = b.PaymentNote ?? string.Empty;
        Notes = b.Notes ?? string.Empty;
    }

    partial void OnBookingAmountTextChanged(string value) => RecalcBalance();
    partial void OnAdvanceTextChanged(string value) => RecalcBalance();

    private void RecalcBalance()
    {
        decimal.TryParse(BookingAmountText, out var amount);
        decimal.TryParse(AdvanceText, out var adv);
        BalanceText = Math.Max(0, amount - adv).ToString("0");
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
            decimal.TryParse(BookingAmountText, out var amount);
            decimal.TryParse(AdvanceText, out var adv);
            await _api.UpdateBookingAsync(id, new
            {
                status = SelectedStatus,
                bookingAmount = amount,
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

public partial class OwnerAvailabilityViewModel : ObservableObject
{
    private readonly IGroundsApiClient _api;

    public ObservableCollection<GroundBookingDto> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;

    public OwnerAvailabilityViewModel(IGroundsApiClient api) => _api = api;

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var page = await _api.GetOwnerBookingsAsync(null, null, 50).ConfigureAwait(true);
            Items.Clear();
            foreach (var row in page.Where(b =>
                         string.Equals(b.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(b.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(b => b.StartAt))
                Items.Add(row);
            if (Items.Count == 0)
                ErrorMessage = "No upcoming pending or confirmed slots.";
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

    [RelayCommand]
    private Task ChatCustomerAsync(GroundBookingDto? row)
        => PakistanWhatsApp.OpenChatAsync(
            row?.CustomerPhone,
            row is null ? null : $"Assalam o Alaikum {row.CustomerName}, regarding your booking at {row.GroundName}.");
}
