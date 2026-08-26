using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public sealed partial class SelectablePlayer : ObservableObject
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private bool isCaptain;

    public string DisplayName => IsCaptain ? $"{FullName} (C)" : FullName;

    public string PlayingLabel => IsPlaying ? "Playing" : "Bench";

    partial void OnIsCaptainChanged(bool value)
        => OnPropertyChanged(nameof(DisplayName));

    partial void OnIsPlayingChanged(bool value)
        => OnPropertyChanged(nameof(PlayingLabel));
}

[QueryProperty(nameof(TeamId), "id")]
[QueryProperty(nameof(Mode), "mode")]
public partial class TeamEditViewModel : ObservableObject
{
    private readonly ITeamService _teams;
    private readonly IPlayerService _players;
    private readonly ITeamImageService _images;
    private readonly IAuthService _auth;

    public string? TeamId { get; set; }

    public string? Mode { get; set; }

    public ObservableCollection<SelectablePlayer> RosterCandidates { get; } = new();

    public ObservableCollection<SelectablePlayer> FilteredRoster { get; } = new();

    public ObservableCollection<SelectablePlayer> SquadSummary { get; } = new();

    public ObservableCollection<SelectablePlayer> PlayingSummary { get; } = new();

    public ObservableCollection<SelectablePlayer> CaptainOptions { get; } = new();

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private SelectablePlayer? selectedCaptain;

    [ObservableProperty]
    private string title = "New team";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string hint = string.Empty;

    [ObservableProperty]
    private bool canWrite;

    [ObservableProperty]
    private bool isViewMode;

    [ObservableProperty]
    private bool canEdit;

    [ObservableProperty]
    private bool isActive = true;

    [ObservableProperty]
    private string squadHint = "Select the full squad, then pick who will play the next match.";

    [ObservableProperty]
    private string playingHint = "Only playing players appear in the next match. Update this before each match.";

    [ObservableProperty]
    private string nameFilter = string.Empty;

    [ObservableProperty]
    private ImageSource? photoPreview;

    [ObservableProperty]
    private string initials = "?";

    [ObservableProperty]
    private string photoFileName = "No file selected";

    [ObservableProperty]
    private string photoHint = "Optional · JPEG, PNG or WebP · one logo";

    [ObservableProperty]
    private bool hasPhoto;

    [ObservableProperty]
    private bool isPhotoModalOpen;

    public bool ShowPlayerPicker => CanEdit;

    public bool HasFilter => !string.IsNullOrWhiteSpace(NameFilter);

    public bool HasSquadSummary => SquadSummary.Count > 0;

    public bool HasPlayingSummary => PlayingSummary.Count > 0;

    private Guid? _existingId;
    private byte[]? _pendingJpeg;
    private bool _removePhoto;
    private bool _loadedExistingPhoto;

    public string PhotoActionLabel => HasPhoto ? "Change logo" : "Choose logo";
    public bool CanRemovePhoto => HasPhoto && CanEdit;
    public bool ShowInitials => !HasPhoto;
    public string StatusText => IsActive ? "Active" : "Inactive";
    public Color StatusColor => StatusColors.For(IsActive);

    public TeamEditViewModel(ITeamService teams, IPlayerService players, ITeamImageService images, IAuthService auth)
    {
        _teams = teams;
        _players = players;
        _images = images;
        _auth = auth;
        CanWrite = _auth.CanWrite;
        CanEdit = CanWrite;
    }

    public async Task InitializeAsync()
    {
        CanWrite = _auth.CanWrite;
        IsViewMode = string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase);
        CanEdit = CanWrite && !IsViewMode;

        if (!CanWrite && !IsViewMode)
        {
            ErrorMessage = "Read-only user cannot edit teams.";
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        NameFilter = string.Empty;
        if (!Guid.TryParse(TeamId, out _))
            ResetPhoto();

        var allPlayers = await _players.GetActiveAsync().ConfigureAwait(true);

        RosterCandidates.Clear();
        foreach (var p in allPlayers)
        {
            RosterCandidates.Add(new SelectablePlayer { Id = p.Id, FullName = p.FullName });
        }

        HashSet<Guid> selected = new();
        HashSet<Guid> playing = new();
        Guid? captainId = null;

        if (Guid.TryParse(TeamId, out var id))
        {
            _existingId = id;
            Title = IsViewMode ? "View team" : "Edit team";
            var detail = await _teams.GetAsync(id).ConfigureAwait(true);
            if (detail is null)
            {
                ErrorMessage = "Team not found.";
                return;
            }

            Name = detail.Name;
            IsActive = detail.IsActive;
            Initials = PlayerPhotos.Initials(detail.Name);
            captainId = detail.CaptainPlayerId;
            selected = detail.Players.Select(p => p.Id).ToHashSet();
            playing = (detail.PlayingPlayerIds ?? Array.Empty<Guid>()).ToHashSet();
            if (playing.Count == 0)
                playing = selected;

            // View mode: only show current squad members (incl. inactive ones on the team).
            if (IsViewMode)
            {
                RosterCandidates.Clear();
                foreach (var p in detail.Players.OrderBy(x => x.FullName))
                {
                    RosterCandidates.Add(new SelectablePlayer
                    {
                        Id = p.Id,
                        FullName = p.FullName,
                        IsSelected = true,
                        IsPlaying = playing.Contains(p.Id)
                    });
                }
            }
        }
        else if (IsViewMode)
        {
            ErrorMessage = "Team not found.";
            return;
        }

        if (!IsViewMode)
        {
            foreach (var row in RosterCandidates)
            {
                row.IsSelected = selected.Contains(row.Id);
                row.IsPlaying = playing.Contains(row.Id);
            }
        }

        RebuildCaptainOptions(captainId);
        RebuildSummaries();
        ApplyFilter();
        RefreshHints();
        await LoadExistingPhotoAsync().ConfigureAwait(true);
    }

    partial void OnNameChanged(string value)
        => Initials = PlayerPhotos.Initials(value);

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnHasPhotoChanged(bool value)
    {
        OnPropertyChanged(nameof(PhotoActionLabel));
        OnPropertyChanged(nameof(CanRemovePhoto));
        OnPropertyChanged(nameof(ShowInitials));
        if (!value)
            IsPhotoModalOpen = false;
    }

    [RelayCommand]
    private void OpenPhoto()
    {
        if (PhotoPreview is null)
            return;
        IsPhotoModalOpen = true;
    }

    [RelayCommand]
    private void ClosePhoto() => IsPhotoModalOpen = false;

    partial void OnCanEditChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPlayerPicker));
        OnPropertyChanged(nameof(CanRemovePhoto));
    }

    partial void OnSelectedCaptainChanged(SelectablePlayer? value)
        => RefreshCaptainFlags();

    partial void OnNameFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasFilter));
        ApplyFilter();
    }

    [RelayCommand]
    private void TogglePlayer(SelectablePlayer? player)
    {
        if (!CanEdit || player is null)
            return;

        player.IsSelected = !player.IsSelected;
        if (player.IsSelected)
            player.IsPlaying = true;
        else
            player.IsPlaying = false;

        // If captain was deselected from team, clear captain.
        if (!player.IsSelected && SelectedCaptain?.Id == player.Id)
            SelectedCaptain = null;

        RebuildCaptainOptions(SelectedCaptain?.Id);
        RebuildSummaries();
        RefreshHints();
    }

    [RelayCommand]
    private void TogglePlaying(SelectablePlayer? player)
    {
        if (!CanEdit || player is null || !player.IsSelected)
            return;

        player.IsPlaying = !player.IsPlaying;
        RebuildSummaries();
        RefreshHints();
    }

    [RelayCommand]
    private void ClearFilter() => NameFilter = string.Empty;

    [RelayCommand]
    private async Task PickPhotoAsync()
    {
        if (!CanEdit)
            return;

        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose team logo",
                FileTypes = FilePickerFileType.Images
            }).ConfigureAwait(true);
            if (result is null)
                return;

            await using var stream = await result.OpenReadAsync().ConfigureAwait(true);
            var jpeg = await PlayerPhotos.ToJpegAsync(stream).ConfigureAwait(true);
            _pendingJpeg = jpeg;
            _removePhoto = false;
            PhotoPreview = PlayerPhotos.ToImageSource(jpeg);
            HasPhoto = true;
            PhotoFileName = result.FileName;
            PhotoHint = $"{Math.Max(1, jpeg.Length / 1024)} KB · will be saved as a small JPEG";
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void RemovePhoto()
    {
        if (!CanEdit)
            return;

        _pendingJpeg = null;
        _removePhoto = _existingId is not null && _loadedExistingPhoto;
        ResetPhoto();
        if (_removePhoto)
            PhotoHint = "Logo will be removed when you save";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy || !CanEdit)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var name = Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
                throw new ArgumentException("Team name is required.");
            if (name.Length > 120)
                throw new ArgumentException("Team name must be 120 characters or fewer.");

            var playerIds = RosterCandidates.Where(p => p.IsSelected).Select(p => p.Id).ToList();
            if (playerIds.Count == 0)
                throw new InvalidOperationException("Add at least one player to the squad.");

            var playingIds = RosterCandidates.Where(p => p.IsSelected && p.IsPlaying).Select(p => p.Id).ToList();
            if (playingIds.Count == 0)
                throw new InvalidOperationException("Select at least one playing player from the squad.");

            if (SelectedCaptain is null || SelectedCaptain.Id == Guid.Empty)
                throw new InvalidOperationException("Captain is required. Select a captain from the squad.");
            if (!playerIds.Contains(SelectedCaptain.Id))
                throw new InvalidOperationException("Captain must be one of the selected squad players.");

            var draft = new TeamDraft(name, SelectedCaptain.Id, playerIds, IsActive, playingIds);

            Guid id;
            if (_existingId is Guid existing)
            {
                await _teams.UpdateAsync(existing, draft).ConfigureAwait(true);
                id = existing;
            }
            else
            {
                var created = await _teams.CreateAsync(draft).ConfigureAwait(true);
                id = created.Id;
                _existingId = id;
            }

            if (_pendingJpeg is { Length: > 0 })
                await _images.UploadAsync(id, _pendingJpeg, "logo.jpg").ConfigureAwait(true);
            else if (_removePhoto)
                await _images.DeleteAsync(id).ConfigureAwait(true);

            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
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

    private void RebuildCaptainOptions(Guid? preferredCaptainId)
    {
        var selected = RosterCandidates.Where(p => p.IsSelected).OrderBy(p => p.FullName).ToList();
        CaptainOptions.Clear();
        foreach (var p in selected)
            CaptainOptions.Add(p);

        if (preferredCaptainId is Guid cid && cid != Guid.Empty)
            SelectedCaptain = CaptainOptions.FirstOrDefault(c => c.Id == cid);
        else if (SelectedCaptain is null || !CaptainOptions.Any(c => c.Id == SelectedCaptain.Id))
            SelectedCaptain = CaptainOptions.FirstOrDefault();

        RefreshCaptainFlags();
    }

    private void RebuildSummaries()
    {
        SquadSummary.Clear();
        foreach (var p in RosterCandidates.Where(x => x.IsSelected).OrderBy(x => x.FullName))
            SquadSummary.Add(p);

        PlayingSummary.Clear();
        foreach (var p in RosterCandidates.Where(x => x.IsSelected && x.IsPlaying).OrderBy(x => x.FullName))
            PlayingSummary.Add(p);

        OnPropertyChanged(nameof(HasSquadSummary));
        OnPropertyChanged(nameof(HasPlayingSummary));
    }

    private void ApplyFilter()
    {
        var q = NameFilter?.Trim() ?? string.Empty;
        IEnumerable<SelectablePlayer> source = RosterCandidates.OrderBy(p => p.FullName);
        if (q.Length > 0)
            source = source.Where(p => p.FullName.Contains(q, StringComparison.OrdinalIgnoreCase));

        FilteredRoster.Clear();
        foreach (var p in source)
            FilteredRoster.Add(p);
    }

    private void RefreshHints()
    {
        var squad = RosterCandidates.Count(p => p.IsSelected);
        var playing = RosterCandidates.Count(p => p.IsSelected && p.IsPlaying);
        SquadHint = squad == 0
            ? "Select the full squad, then pick who will play the next match."
            : $"Squad: {squad} player{(squad == 1 ? "" : "s")}";
        PlayingHint = playing == 0
            ? "Only playing players appear in the next match. Update this before each match."
            : $"Playing: {playing} of {squad}. Only these players will be in the next match.";
    }

    private void RefreshCaptainFlags()
    {
        var capId = SelectedCaptain?.Id;
        foreach (var p in RosterCandidates)
            p.IsCaptain = capId is Guid id && p.Id == id;
    }

    private async Task LoadExistingPhotoAsync()
    {
        _pendingJpeg = null;
        _removePhoto = false;
        _loadedExistingPhoto = false;
        if (_existingId is not Guid id)
            return;

        try
        {
            var photo = await _images.GetPhotoAsync(id).ConfigureAwait(true);
            if (photo is not null)
            {
                PhotoPreview = photo;
                HasPhoto = true;
                PhotoFileName = "Current logo";
                PhotoHint = "Optional · one logo per team";
                _loadedExistingPhoto = true;
            }
        }
        catch
        {
            ResetPhoto();
        }
    }

    private void ResetPhoto()
    {
        PhotoPreview = null;
        HasPhoto = false;
        PhotoFileName = "No file selected";
        PhotoHint = "Optional · JPEG, PNG or WebP · one logo";
        Initials = PlayerPhotos.Initials(Name);
        OnPropertyChanged(nameof(PhotoActionLabel));
        OnPropertyChanged(nameof(CanRemovePhoto));
    }
}
