using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

[QueryProperty(nameof(PlayerId), "id")]
public partial class PlayerEditViewModel : ObservableObject
{
    private readonly IPlayerService _players;

    public string? PlayerId { get; set; }

    [ObservableProperty]
    private string fullName = string.Empty;

    [ObservableProperty]
    private string? nickname;

    [ObservableProperty]
    private string? phone;

    [ObservableProperty]
    private string? battingStyle;

    [ObservableProperty]
    private string? bowlingStyle;

    [ObservableProperty]
    private string? notes;

    [ObservableProperty]
    private string title = "Register player";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    private Guid? _existingId;

    public PlayerEditViewModel(IPlayerService players)
    {
        _players = players;
    }

    public async Task InitializeAsync()
    {
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
        Nickname = player.Nickname;
        Phone = player.Phone;
        BattingStyle = player.BattingStyle;
        BowlingStyle = player.BowlingStyle;
        Notes = player.Notes;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var draft = new PlayerDraft(FullName, Nickname, Phone, BattingStyle, BowlingStyle, Notes);
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
