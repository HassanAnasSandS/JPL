using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class GroundPick
{
    public Guid? Id { get; }
    public string Name { get; }

    public GroundPick(Guid? id, string name)
    {
        Id = id;
        Name = name;
    }

    public static GroundPick All { get; } = new(null, "All grounds");
}

public partial class CalendarDayItem : ObservableObject
{
    public DateTime Date { get; }
    public string DayNumber { get; }
    public bool IsCurrentMonth { get; }
    public bool IsToday { get; }

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool hasBookings;
    [ObservableProperty] private Color background = Colors.Transparent;
    [ObservableProperty] private Color textColor = Color.FromArgb("#0D1B2A");

    public CalendarDayItem(DateTime date, bool isCurrentMonth, bool isToday)
    {
        Date = date.Date;
        DayNumber = date.Day.ToString();
        IsCurrentMonth = isCurrentMonth;
        IsToday = isToday;
        RefreshColors();
    }

    partial void OnIsSelectedChanged(bool value) => RefreshColors();
    partial void OnHasBookingsChanged(bool value) => RefreshColors();

    public void RefreshColors()
    {
        if (IsSelected)
        {
            Background = Color.FromArgb("#0D1B2A");
            TextColor = Colors.White;
            return;
        }

        Background = IsToday ? Color.FromArgb("#FFF4CC") : Colors.Transparent;
        TextColor = IsCurrentMonth ? Color.FromArgb("#0D1B2A") : Color.FromArgb("#919191");
    }
}

public sealed class HourSlotItem
{
    public int Hour { get; init; }
    public string TimeLabel { get; init; } = string.Empty;
    public string SlotTitle { get; init; } = "Available";
    public bool IsBooked { get; init; }
    public Color SlotColor { get; init; } = Color.FromArgb("#D1FAE5");
    public Color SlotTextColor { get; init; } = Color.FromArgb("#15803D");
    public IReadOnlyList<GroundBookingDto> Bookings { get; init; } = [];
}

public partial class OwnerAvailabilityViewModel : ObservableObject
{
    private static readonly Color BookedBg = Color.FromArgb("#C62828");
    private static readonly Color BookedFg = Colors.White;
    private static readonly Color FreeBg = Color.FromArgb("#D1FAE5");
    private static readonly Color FreeFg = Color.FromArgb("#15803D");

    private readonly IGroundsApiClient _api;
    private readonly List<GroundBookingDto> _monthBookings = [];
    private DateTime _visibleMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private bool _suppressGroundChange;

    public ObservableCollection<GroundPick> GroundPicks { get; } = [];
    public ObservableCollection<CalendarDayItem> CalendarDays { get; } = [];
    public ObservableCollection<HourSlotItem> Hours { get; } = [];
    public ObservableCollection<GroundBookingDto> SelectedHourBookings { get; } = [];

    [ObservableProperty] private GroundPick? selectedGround;
    [ObservableProperty] private bool hasGroundPicker;
    [ObservableProperty] private string monthTitle = string.Empty;
    [ObservableProperty] private string selectedDateLabel = string.Empty;
    [ObservableProperty] private string daySummary = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool isDetailOpen;
    [ObservableProperty] private DateTime selectedDate = DateTime.Today;

    public OwnerAvailabilityViewModel(IGroundsApiClient api) => _api = api;

    partial void OnSelectedGroundChanged(GroundPick? value)
    {
        if (_suppressGroundChange || IsBusy)
            return;
        _ = LoadAsync();
    }

    [RelayCommand]
    private Task PrevMonthAsync()
    {
        _visibleMonth = _visibleMonth.AddMonths(-1);
        return LoadAsync();
    }

    [RelayCommand]
    private Task NextMonthAsync()
    {
        _visibleMonth = _visibleMonth.AddMonths(1);
        return LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            IsDetailOpen = false;
            await EnsureGroundsAsync().ConfigureAwait(true);
            if (SelectedGround?.Id is not Guid groundId)
            {
                _monthBookings.Clear();
                RebuildCalendar();
                RebuildHours();
                ErrorMessage = GroundPicks.Count == 0
                    ? "Add a ground first to see availability."
                    : "Select a ground to see its availability.";
                return;
            }

            var from = _visibleMonth.AddDays(-1);
            var to = _visibleMonth.AddMonths(1).AddDays(1);
            var page = await _api.GetOwnerBookingsAsync(groundId, null, 500, from, to).ConfigureAwait(true);

            _monthBookings.Clear();
            _monthBookings.AddRange(page.Items.Where(b => b.HoldsSlot));

            if (SelectedDate.Year != _visibleMonth.Year || SelectedDate.Month != _visibleMonth.Month)
                SelectedDate = ClampToMonth(SelectedDate, _visibleMonth);

            RebuildCalendar();
            RebuildHours();
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.UserMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectDay(CalendarDayItem? day)
    {
        if (day is null)
            return;

        if (day.Date.Year != _visibleMonth.Year || day.Date.Month != _visibleMonth.Month)
        {
            _visibleMonth = new DateTime(day.Date.Year, day.Date.Month, 1);
            SelectedDate = day.Date;
            _ = LoadAsync();
            return;
        }

        SelectedDate = day.Date;
        foreach (var item in CalendarDays)
            item.IsSelected = item.Date == SelectedDate;
        RebuildHours();
    }

    [RelayCommand]
    private void OpenSlot(HourSlotItem? slot)
    {
        if (slot is null || !slot.IsBooked || slot.Bookings.Count == 0)
            return;

        SelectedHourBookings.Clear();
        foreach (var row in slot.Bookings)
            SelectedHourBookings.Add(row);
        IsDetailOpen = true;
    }

    [RelayCommand]
    private void CloseDetail()
    {
        IsDetailOpen = false;
        SelectedHourBookings.Clear();
    }

    [RelayCommand]
    private Task ChatCustomerAsync(GroundBookingDto? row)
        => PakistanWhatsApp.OpenChatAsync(
            row?.CustomerPhone,
            row is null ? null : $"Assalam o Alaikum {row.CustomerName}, regarding your booking at {row.GroundName}.");

    private async Task EnsureGroundsAsync()
    {
        var page = await _api.GetOwnerGroundsAsync(null, 50).ConfigureAwait(true);
        var grounds = page.Items;
        var selectedId = SelectedGround?.Id;
        _suppressGroundChange = true;
        try
        {
            GroundPicks.Clear();
            foreach (var g in grounds)
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

    private IEnumerable<GroundBookingDto> VisibleBookings()
    {
        var groundId = SelectedGround?.Id;
        return groundId is Guid id
            ? _monthBookings.Where(b => b.GroundId == id)
            : _monthBookings;
    }

    private void RebuildCalendar()
    {
        MonthTitle = _visibleMonth.ToString("MMMM yyyy");
        var first = _visibleMonth;
        var offset = ((int)first.DayOfWeek + 6) % 7;
        var start = first.AddDays(-offset);
        var bookings = VisibleBookings().ToList();

        CalendarDays.Clear();
        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            var item = new CalendarDayItem(
                date,
                date.Month == _visibleMonth.Month,
                date == DateTime.Today)
            {
                IsSelected = date == SelectedDate.Date,
                HasBookings = bookings.Any(b => OverlapsDay(b, date))
            };
            CalendarDays.Add(item);
        }
    }

    private void RebuildHours()
    {
        SelectedDateLabel = SelectedDate.ToString("dddd, dd MMM yyyy");
        var dayStart = SelectedDate.Date;
        var dayEnd = dayStart.AddDays(1);
        var dayBookings = VisibleBookings()
            .Where(b => b.StartAt < dayEnd && b.EndAt > dayStart)
            .OrderBy(b => b.StartAt)
            .ToList();

        Hours.Clear();
        var bookedHours = 0;
        for (var hour = 0; hour < 24; hour++)
        {
            var slotStart = dayStart.AddHours(hour);
            var slotEnd = slotStart.AddHours(1);
            var hits = dayBookings
                .Where(b => b.StartAt < slotEnd && b.EndAt > slotStart)
                .ToList();
            var booked = hits.Count > 0;
            if (booked)
                bookedHours++;

            string title;
            if (!booked)
                title = "Available";
            else if (hits.Count == 1)
            {
                var b = hits[0];
                var kind = string.IsNullOrWhiteSpace(b.BookingTitle) ? b.Status : b.BookingTitle;
                title = $"{b.CustomerName} · {kind}";
            }
            else
                title = $"{hits.Count} bookings";

            Hours.Add(new HourSlotItem
            {
                Hour = hour,
                TimeLabel = slotStart.ToString("HH:mm"),
                SlotTitle = title,
                IsBooked = booked,
                SlotColor = booked ? BookedBg : FreeBg,
                SlotTextColor = booked ? BookedFg : FreeFg,
                Bookings = hits
            });
        }

        DaySummary = $"{bookedHours} booked · {24 - bookedHours} available";
    }

    private static DateTime ClampToMonth(DateTime date, DateTime month)
    {
        if (date.Year == month.Year && date.Month == month.Month)
            return date.Date;
        var last = DateTime.DaysInMonth(month.Year, month.Month);
        var day = Math.Min(date.Day, last);
        return new DateTime(month.Year, month.Month, day);
    }

    private static bool OverlapsDay(GroundBookingDto b, DateTime day)
    {
        var start = day.Date;
        var end = start.AddDays(1);
        return b.StartAt < end && b.EndAt > start;
    }
}
