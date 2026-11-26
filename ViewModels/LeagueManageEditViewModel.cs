using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

/// <summary>SuperAdmin create / edit league record: name, image, active.</summary>
[QueryProperty(nameof(LeagueId), "id")]
public partial class LeagueManageEditViewModel : ObservableObject
{
    private readonly ILeagueAdminService _leagues;
    private readonly IAuthService _auth;
    private readonly IUsersModuleGate _gate;
    private Guid? _existingId;
    private byte[]? _imageBytes;
    private bool _imageChanged;
    private bool _removeImage;
    private string? _initializedFor;

    public string? LeagueId { get; set; }

    [ObservableProperty] private string title = "Add league";
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string? nameError;
    [ObservableProperty] private ImageSource imagePreview = "cric360_logo.png";
    [ObservableProperty] private bool hasImage;
    [ObservableProperty] private bool isActive = true;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoaded;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? infoText;

    public LeagueManageEditViewModel(ILeagueAdminService leagues, IAuthService auth, IUsersModuleGate gate)
    {
        _leagues = leagues;
        _auth = auth;
        _gate = gate;
    }

    public bool IsEdit => _existingId is not null;
    public bool HasNameError => !string.IsNullOrEmpty(NameError);

    partial void OnNameErrorChanged(string? value) => OnPropertyChanged(nameof(HasNameError));
    public Color StatusColor => StatusColors.For(IsActive);
    public string ActiveHint => IsActive
        ? "Active — League Admins and Users of this league can sign in."
        : "Deactive — League Admins and Users of this league are blocked from login.";
    public string SaveLabel => IsEdit ? "Save changes" : "Create league";
    public bool CanSave => IsLoaded && !IsBusy;

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(ActiveHint));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSave));
    partial void OnIsLoadedChanged(bool value) => OnPropertyChanged(nameof(CanSave));

    partial void OnNameChanged(string value)
    {
        // Live validation once the user has typed; cleared as soon as the name is valid.
        if (NameError is null)
            return;
        NameError = TryValidate(value, out _);
    }

    public async Task InitializeAsync()
    {
        // OnAppearing also fires when returning from the photo picker — keep the form as typed.
        if (IsLoaded && string.Equals(_initializedFor, LeagueId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            return;

        ErrorMessage = null;
        NameError = null;
        InfoText = null;
        IsLoaded = false;

        if (_auth.Current?.CanManageLeagues != true || !_gate.IsUnlocked)
        {
            ErrorMessage = _auth.Current?.CanManageLeagues != true
                ? "Only HassanAnas can manage leagues."
                : "Users module is locked.";
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        _imageChanged = false;
        _removeImage = false;

        if (!Guid.TryParse(LeagueId, out var id) || id == Guid.Empty)
        {
            _existingId = null;
            Title = "Add league";
            Name = string.Empty;
            IsActive = true;
            SetImage(null);
            InfoText = "A new league gets its own database automatically.";
            NotifyMode();
            _initializedFor = string.Empty;
            IsLoaded = true;
            return;
        }

        _existingId = id;
        Title = "Edit league";
        NotifyMode();
        try
        {
            IsBusy = true;
            var league = await _leagues.GetAsync(id).ConfigureAwait(true);
            if (league is null)
            {
                ErrorMessage = "League not found. It may have been deleted.";
                return;
            }

            Name = league.Name;
            IsActive = league.IsActive;
            SetImage(league.ImageBytes);
            InfoText = $"{league.AdminCount} admin(s) · {league.UserCount} user(s) · created {league.CreatedAtUtc:dd MMM yyyy}"
                       + (league.UpdatedAtUtc is DateTime u ? $" · edited {u:dd MMM yyyy}" : string.Empty);
            _initializedFor = LeagueId ?? string.Empty;
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex) ? NetworkUi.OfflineMessage : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyMode()
    {
        OnPropertyChanged(nameof(IsEdit));
        OnPropertyChanged(nameof(SaveLabel));
    }

    private void SetImage(byte[]? bytes)
    {
        _imageBytes = bytes is { Length: > 0 } ? bytes : null;
        HasImage = _imageBytes is not null;
        ImagePreview = LeagueImages.ToImageSource(_imageBytes);
    }

    [RelayCommand]
    private async Task PickImageAsync()
    {
        try
        {
            var result = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "League image"
            }).ConfigureAwait(true);
            if (result is null)
                return;

            await using var stream = await result.OpenReadAsync().ConfigureAwait(true);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(true);
            if (ms.Length > LeagueAdminService.ImageMaxBytes)
            {
                ErrorMessage = "Image must be under 2 MB.";
                return;
            }

            SetImage(ms.ToArray());
            _imageChanged = true;
            _removeImage = false;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void RemoveImage()
    {
        SetImage(null);
        _imageChanged = false;
        _removeImage = _existingId is not null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanSave)
            return;

        ErrorMessage = null;
        NameError = TryValidate(Name, out var cleanName);
        if (NameError is not null)
            return;

        try
        {
            IsBusy = true;
            // Edit: send the image only when it changed (null = keep the server copy).
            var image = _existingId is null || _imageChanged ? _imageBytes : null;
            var draft = new ManagedLeagueDraft(cleanName, image, IsActive, _removeImage);
            if (_existingId is Guid id)
                await _leagues.UpdateAsync(id, draft).ConfigureAwait(true);
            else
                await _leagues.CreateAsync(draft).ConfigureAwait(true);

            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
        }
        catch (ArgumentException ex)
        {
            NameError = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex) ? NetworkUi.OfflineMessage : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string? TryValidate(string? raw, out string clean)
    {
        clean = string.Empty;
        try
        {
            clean = LeagueAdminService.ValidateName(raw);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
