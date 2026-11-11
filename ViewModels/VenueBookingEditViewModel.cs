using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public partial class VenueParticipantEditor : ObservableObject
{
    public Guid PlayerId { get; init; }
    public string PlayerName { get; init; } = string.Empty;

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string status = VenueBookingPaymentStatus.Pending;
    [ObservableProperty] private string amountText = "0";

    public bool IsPaid => string.Equals(Status, VenueBookingPaymentStatus.Paid, StringComparison.OrdinalIgnoreCase);
    public string StatusToggleLabel => IsPaid ? "Paid" : "Pending";
    public string DisplayName => PlayerName;
    public string AmountDisplay => decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var inv)
            ? inv.ToString("0.##", CultureInfo.InvariantCulture)
            : decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.CurrentCulture, out var loc)
                ? loc.ToString("0.##", CultureInfo.CurrentCulture)
                : "0";

    partial void OnStatusChanged(string value)
    {
        OnPropertyChanged(nameof(IsPaid));
        OnPropertyChanged(nameof(StatusToggleLabel));
    }

    partial void OnAmountTextChanged(string value)
        => OnPropertyChanged(nameof(AmountDisplay));

    public void ToggleStatus()
        => Status = IsPaid ? VenueBookingPaymentStatus.Pending : VenueBookingPaymentStatus.Paid;
}

public sealed class PlayerOption
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string DisplayName => FullName;
}

public partial class VenueBookingEditViewModel : ObservableObject
{
    private readonly IVenueBookingService _bookings;
    private readonly IVenueService _venues;
    private readonly IPlayerService _players;
    private readonly IAuthService _auth;
    private Guid? _existingId;
    private int _loadGate;

    /// <summary>Edit mode: full player roster to add/remove.</summary>
    public ObservableCollection<VenueParticipantEditor> ParticipantCandidates { get; } = new();

    /// <summary>View mode: only players already on this booking.</summary>
    public ObservableCollection<VenueParticipantEditor> AddedParticipants { get; } = new();

    public ObservableCollection<PlayerOption> BookedByOptions { get; } = new();
    public ObservableCollection<VenueListItem> VenueOptions { get; } = new();

    public IReadOnlyList<string> StatusOptions { get; } = new[]
    {
        VenueBookingPaymentStatus.Pending,
        VenueBookingPaymentStatus.Paid
    };

    [ObservableProperty] private VenueListItem? selectedVenue;
    [ObservableProperty] private DateTime bookingDate = DateTime.Today;
    [ObservableProperty] private PlayerOption? selectedBookedBy;
    [ObservableProperty] private string bookingCostText = "0";
    [ObservableProperty] private string title = "New venue booking";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private bool isViewMode;
    [ObservableProperty] private string ownerLine = string.Empty;

    public bool ShowEditParticipants => !IsViewMode;
    public bool ShowViewParticipants => IsViewMode;

    public VenueBookingEditViewModel(
        IVenueBookingService bookings,
        IVenueService venues,
        IPlayerService players,
        IAuthService auth)
    {
        _bookings = bookings;
        _venues = venues;
        _players = players;
        _auth = auth;
        CanWrite = _auth.CanWrite;
        CanEdit = CanWrite;
    }

    partial void OnIsViewModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEditParticipants));
        OnPropertyChanged(nameof(ShowViewParticipants));
    }

    public void ApplyQuery(string? bookingId, string? mode)
    {
        _existingId = null;
        if (Guid.TryParse(bookingId, out var id) && id != Guid.Empty)
            _existingId = id;

        IsViewMode = string.Equals(mode, "view", StringComparison.OrdinalIgnoreCase);
    }

    public async Task InitializeAsync()
    {
        var gate = Interlocked.Increment(ref _loadGate);

        CanWrite = _auth.CanWrite;

        if (!CanWrite && !IsViewMode)
        {
            ErrorMessage = "Read-only user cannot edit venue bookings.";
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        var allPlayers = await _players.GetActiveAsync().ConfigureAwait(true);
        if (gate != _loadGate) return;

        VenueOptions.Clear();
        try
        {
            foreach (var v in await _venues.GetAllAsync().ConfigureAwait(true))
                VenueOptions.Add(v);
        }
        catch (Exception venueEx) when (NetworkUi.IsNetworkFailure(venueEx) || venueEx is SessionExpiredException)
        {
            ErrorMessage = "Could not load venues. Check connection.";
        }

        if (gate != _loadGate) return;

        BookedByOptions.Clear();
        ParticipantCandidates.Clear();
        AddedParticipants.Clear();

        foreach (var p in allPlayers)
            BookedByOptions.Add(new PlayerOption { Id = p.Id, FullName = p.FullName });

        if (_existingId is null)
        {
            Title = "New venue booking";
            IsViewMode = false;
            CanEdit = CanWrite;
            OwnerLine = CanWrite ? $"Will be owned by {_auth.Current?.Username}" : string.Empty;
            SelectedVenue = VenueOptions.FirstOrDefault();
            foreach (var p in allPlayers)
            {
                ParticipantCandidates.Add(new VenueParticipantEditor
                {
                    PlayerId = p.Id,
                    PlayerName = p.FullName
                });
            }
            return;
        }

        var detail = await _bookings.GetAsync(_existingId.Value).ConfigureAwait(true);
        if (gate != _loadGate) return;

        if (detail is null)
        {
            ErrorMessage = "Venue booking not found.";
            return;
        }

        // Always force view-only when caller asked for view, or when this admin cannot edit.
        if (IsViewMode || !detail.CanEdit)
        {
            if (!IsViewMode && !detail.CanEdit)
                ErrorMessage = "Only the admin who created this booking can edit it.";
            IsViewMode = true;
        }

        CanEdit = detail.CanEdit && !IsViewMode;
        Title = IsViewMode ? "View venue booking" : "Edit venue booking";
        SelectedVenue = VenueOptions.FirstOrDefault(v => v.Id == detail.VenueId)
                        ?? new VenueListItem { Id = detail.VenueId, Name = detail.VenueName };
        if (VenueOptions.All(v => v.Id != SelectedVenue.Id))
            VenueOptions.Insert(0, SelectedVenue);
        BookingDate = detail.BookingDateUtc.Date;
        BookingCostText = detail.BookingCost.ToString("0.##", CultureInfo.InvariantCulture);
        OwnerLine = $"Created by {detail.CreatedByUsername}";
        SelectedBookedBy = BookedByOptions.FirstOrDefault(p => p.Id == detail.BookedByPlayerId);

        // View list: ONLY added participants (never the full roster).
        foreach (var row in detail.Participants.OrderBy(p => p.PlayerName, StringComparer.OrdinalIgnoreCase))
        {
            AddedParticipants.Add(new VenueParticipantEditor
            {
                PlayerId = row.PlayerId,
                PlayerName = row.PlayerName,
                IsSelected = true,
                Status = VenueBookingPaymentStatus.Normalize(row.Status),
                AmountText = FormatAmount(row.Amount)
            });
        }

        if (!IsViewMode)
        {
            var byId = detail.Participants.ToDictionary(p => p.PlayerId);
            foreach (var p in allPlayers)
            {
                var editor = new VenueParticipantEditor
                {
                    PlayerId = p.Id,
                    PlayerName = p.FullName
                };
                if (byId.TryGetValue(p.Id, out var row))
                {
                    editor.IsSelected = true;
                    editor.Status = VenueBookingPaymentStatus.Normalize(row.Status);
                    editor.AmountText = FormatAmount(row.Amount);
                }
                ParticipantCandidates.Add(editor);
            }

            foreach (var row in detail.Participants)
            {
                if (ParticipantCandidates.Any(c => c.PlayerId == row.PlayerId))
                    continue;
                ParticipantCandidates.Add(new VenueParticipantEditor
                {
                    PlayerId = row.PlayerId,
                    PlayerName = row.PlayerName,
                    IsSelected = true,
                    Status = VenueBookingPaymentStatus.Normalize(row.Status),
                    AmountText = FormatAmount(row.Amount)
                });
                if (BookedByOptions.All(p => p.Id != row.PlayerId))
                    BookedByOptions.Add(new PlayerOption { Id = row.PlayerId, FullName = row.PlayerName });
            }
        }

        if (SelectedBookedBy is null && detail.BookedByPlayerId != Guid.Empty)
        {
            var opt = new PlayerOption { Id = detail.BookedByPlayerId, FullName = detail.BookedByPlayerName };
            BookedByOptions.Add(opt);
            SelectedBookedBy = opt;
        }
    }

    [RelayCommand]
    private void ToggleParticipant(VenueParticipantEditor? player)
    {
        if (!CanEdit || player is null) return;
        player.IsSelected = !player.IsSelected;
        if (!player.IsSelected)
        {
            player.Status = VenueBookingPaymentStatus.Pending;
            player.AmountText = "0";
        }
    }

    [RelayCommand]
    private void TogglePaymentStatus(VenueParticipantEditor? player)
    {
        if (!CanEdit || player is null || !player.IsSelected) return;
        player.ToggleStatus();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy || !CanEdit) return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            if (!decimal.TryParse(BookingCostText?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var cost)
                && !decimal.TryParse(BookingCostText?.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out cost))
                throw new ArgumentException("Booking cost must be a number.");

            if (SelectedVenue is null)
                throw new ArgumentException("Select a venue.");
            if (SelectedBookedBy is null)
                throw new ArgumentException("Select Booked By player.");

            var participants = new List<VenueBookingParticipantDraft>();
            foreach (var p in ParticipantCandidates.Where(x => x.IsSelected))
            {
                participants.Add(new VenueBookingParticipantDraft(
                    p.PlayerId,
                    p.Status,
                    ParseAmount(p.AmountText, p.PlayerName)));
            }

            var draft = new VenueBookingDraft(
                SelectedVenue.Id,
                BookingDate.Date,
                SelectedBookedBy.Id,
                cost,
                participants);

            if (_existingId is Guid id)
                await _bookings.UpdateAsync(id, draft).ConfigureAwait(true);
            else
                await _bookings.CreateAsync(draft).ConfigureAwait(true);

            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
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

    private static string FormatAmount(decimal amount)
        => amount.ToString("0.##", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(string? text, string playerName)
    {
        var raw = text?.Trim();
        if (string.IsNullOrEmpty(raw))
            return 0;
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            && !decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
            throw new ArgumentException($"Amount for {playerName} must be a number.");
        if (amount < 0)
            throw new ArgumentException($"Amount for {playerName} cannot be negative.");
        return amount;
    }
}
