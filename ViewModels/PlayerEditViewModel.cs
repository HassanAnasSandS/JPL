using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

[QueryProperty(nameof(PlayerId), "id")]
public partial class PlayerEditViewModel : ObservableObject
{
    private readonly IPlayerService _players;
    private readonly IAuthService _auth;

    public string? PlayerId { get; set; }

    [ObservableProperty]
    private string fullName = string.Empty;

    [ObservableProperty]
    private string title = "Register player";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool canWrite;

    [ObservableProperty]
    private bool isActive = true;

    private Guid? _existingId;

    public PlayerEditViewModel(IPlayerService players, IAuthService auth)
    {
        _players = players;
        _auth = auth;
        CanWrite = _auth.CanWrite;
    }

    public async Task InitializeAsync()
    {
        CanWrite = _auth.CanWrite;
        if (!CanWrite)
        {
            ErrorMessage = "Read-only user cannot edit players.";
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        if (!Guid.TryParse(PlayerId, out var id))
            return;

        _existingId = id;
        Title = "Edit player";
        var player = await _players.GetAsync(id).ConfigureAwait(true);
        if (player is null)
        {
            ErrorMessage = "Player not found.";
            return;
        }

        FullName = player.FullName;
        IsActive = player.IsActive;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy || !CanWrite)
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

            var draft = new PlayerDraft(name, IsActive);
            if (_existingId is Guid id)
                await _players.UpdateAsync(id, draft).ConfigureAwait(true);
            else
                await _players.CreateAsync(draft).ConfigureAwait(true);

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
}
