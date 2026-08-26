using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using JPL.Services;

namespace JPL.ViewModels;

[QueryProperty(nameof(PlayerId), "id")]
[QueryProperty(nameof(Mode), "mode")]
public partial class PlayerEditViewModel : ObservableObject
{
    private readonly IPlayerService _players;
    private readonly IPlayerImageService _images;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly IOptions<ApiOptions> _api;

    public string? PlayerId { get; set; }

    public string? Mode { get; set; }

    [ObservableProperty] private string fullName = string.Empty;
    [ObservableProperty] private string nickName = string.Empty;
    [ObservableProperty] private string playingRole = string.Empty;
    [ObservableProperty] private string battingStyle = string.Empty;
    [ObservableProperty] private string bowlingStyle = string.Empty;
    [ObservableProperty] private string bio = string.Empty;
    [ObservableProperty] private string title = "Register player";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private bool isViewMode;
    [ObservableProperty] private bool canEdit;
    [ObservableProperty] private bool isActive = true;
    [ObservableProperty] private ImageSource? photoPreview;
    [ObservableProperty] private string initials = "?";
    [ObservableProperty] private string photoFileName = "No file selected";
    [ObservableProperty] private string photoHint = "Optional · JPEG, PNG or WebP · one photo";
    [ObservableProperty] private bool hasPhoto;
    [ObservableProperty] private bool isPhotoModalOpen;

    private Guid? _existingId;
    private byte[]? _pendingJpeg;
    private bool _removePhoto;
    private bool _loadedExistingPhoto;

    public string PhotoActionLabel => HasPhoto ? "Change photo" : "Choose photo";
    public bool CanRemovePhoto => HasPhoto && CanEdit;
    public bool ShowInitials => !HasPhoto;
    public string StatusText => IsActive ? "Active" : "Inactive";
    public Color StatusColor => StatusColors.For(IsActive);
    public string IdentityLine
    {
        get
        {
            var bits = new[] { PlayingRole, BattingStyle, BowlingStyle }
                .Select(s => s?.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s));
            return string.Join(" · ", bits);
        }
    }
    public bool HasIdentityLine => !string.IsNullOrWhiteSpace(IdentityLine);
    public bool HasNickName => !string.IsNullOrWhiteSpace(NickName);
    public bool HasBio => !string.IsNullOrWhiteSpace(Bio);
    public bool HasPublicProfile => TryPublicProfileUrl() is not null;
    public string PublicProfileHint => "Anyone can open this URL without login. Full career and ranks — no PRO lock.";

    public PlayerEditViewModel(
        IPlayerService players,
        IPlayerImageService images,
        IAuthService auth,
        ILeagueContext league,
        IOptions<ApiOptions> api)
    {
        _players = players;
        _images = images;
        _auth = auth;
        _league = league;
        _api = api;
        CanWrite = _auth.CanWrite;
        CanEdit = CanWrite;
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

    partial void OnCanWriteChanged(bool value)
        => OnPropertyChanged(nameof(CanRemovePhoto));

    partial void OnCanEditChanged(bool value)
        => OnPropertyChanged(nameof(CanRemovePhoto));

    partial void OnFullNameChanged(string value)
        => Initials = PlayerPhotos.Initials(value);

    partial void OnPlayingRoleChanged(string value) => NotifyIdentity();
    partial void OnBattingStyleChanged(string value) => NotifyIdentity();
    partial void OnBowlingStyleChanged(string value) => NotifyIdentity();
    partial void OnNickNameChanged(string value) => OnPropertyChanged(nameof(HasNickName));
    partial void OnBioChanged(string value) => OnPropertyChanged(nameof(HasBio));

    private void NotifyIdentity()
    {
        OnPropertyChanged(nameof(IdentityLine));
        OnPropertyChanged(nameof(HasIdentityLine));
    }

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(StatusText));
    }

    public async Task InitializeAsync()
    {
        CanWrite = _auth.CanWrite;
        IsViewMode = string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase);
        CanEdit = CanWrite && !IsViewMode;

        if (!CanWrite && !IsViewMode)
        {
            ErrorMessage = "Read-only user cannot edit players.";
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        if (!Guid.TryParse(PlayerId, out var id))
        {
            if (IsViewMode)
            {
                ErrorMessage = "Player not found.";
                return;
            }

            ResetPhoto();
            return;
        }

        _existingId = id;
        Title = IsViewMode ? "View player" : "Edit player";
        var player = await _players.GetAsync(id).ConfigureAwait(true);
        if (player is null)
        {
            ErrorMessage = "Player not found.";
            return;
        }

        FullName = player.FullName;
        NickName = player.NickName ?? string.Empty;
        PlayingRole = player.PlayingRole ?? string.Empty;
        BattingStyle = player.BattingStyle ?? string.Empty;
        BowlingStyle = player.BowlingStyle ?? string.Empty;
        Bio = player.Bio ?? string.Empty;
        IsActive = player.IsActive;
        Initials = PlayerPhotos.Initials(player.FullName);
        OnPropertyChanged(nameof(HasPublicProfile));
        _pendingJpeg = null;
        _removePhoto = false;
        _loadedExistingPhoto = false;

        try
        {
            var photo = await _images.GetPhotoAsync(id).ConfigureAwait(true);
            if (photo is not null)
            {
                PhotoPreview = photo;
                HasPhoto = true;
                PhotoFileName = "Current photo";
                PhotoHint = "Optional · one photo per player";
                _loadedExistingPhoto = true;
            }
            else
            {
                ResetPhoto();
            }
        }
        catch
        {
            ResetPhoto();
        }
    }

    [RelayCommand]
    private async Task PickPhotoAsync()
    {
        if (!CanEdit)
            return;

        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose player photo",
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
            PhotoHint = "Photo will be removed when you save";
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

            var name = FullName?.Trim() ?? string.Empty;
            if (name.Length == 0)
                throw new ArgumentException("Player name is required.");
            if (name.Length > 120)
                throw new ArgumentException("Player name must be 120 characters or fewer.");

            var draft = new PlayerDraft(
                name,
                IsActive,
                NickName,
                PlayingRole,
                BattingStyle,
                BowlingStyle,
                Bio);
            Guid id;
            if (_existingId is Guid existing)
            {
                await _players.UpdateAsync(existing, draft).ConfigureAwait(true);
                id = existing;
            }
            else
            {
                var created = await _players.CreateAsync(draft).ConfigureAwait(true);
                id = created.Id;
                _existingId = id;
                Title = "Edit player";
                OnPropertyChanged(nameof(HasPublicProfile));
            }

            if (_pendingJpeg is { Length: > 0 })
                await _images.UploadAsync(id, _pendingJpeg, "photo.jpg").ConfigureAwait(true);
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

    [RelayCommand]
    private async Task OpenPublicProfileAsync()
    {
        var url = TryPublicProfileUrl();
        if (url is null)
        {
            ErrorMessage = "Public profile URL is not ready yet. Save the player first.";
            return;
        }

        try
        {
            await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.UserMessage(ex);
        }
    }

    [RelayCommand]
    private async Task CopyPublicLinkAsync()
    {
        var url = TryPublicProfileUrl();
        if (url is null)
        {
            ErrorMessage = "Public profile URL is not ready yet. Save the player first.";
            return;
        }

        await Clipboard.Default.SetTextAsync(url).ConfigureAwait(true);
        ErrorMessage = null;
        await Shell.Current.DisplayAlert("Public link copied", url, "OK").ConfigureAwait(true);
    }

    private string? TryPublicProfileUrl()
    {
        if (_existingId is not Guid playerId || playerId == Guid.Empty)
            return null;
        if (_league.CurrentLeagueId is not Guid leagueId || leagueId == Guid.Empty)
            return null;
        var root = (_api.Value.BaseUrl ?? string.Empty).TrimEnd('/');
        return string.IsNullOrWhiteSpace(root) ? null : $"{root}/p/{leagueId:D}/{playerId:D}";
    }

    private void ResetPhoto()
    {
        PhotoPreview = null;
        HasPhoto = false;
        PhotoFileName = "No file selected";
        PhotoHint = "Optional · JPEG, PNG or WebP · one photo";
        Initials = PlayerPhotos.Initials(FullName);
        OnPropertyChanged(nameof(PhotoActionLabel));
        OnPropertyChanged(nameof(CanRemovePhoto));
    }
}
