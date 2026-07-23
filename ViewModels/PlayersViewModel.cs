using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public partial class PlayersViewModel : ObservableObject
{
    private readonly IPlayerService _players;

    public ObservableCollection<Player> Items { get; } = new();

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    public PlayersViewModel(IPlayerService players)
    {
        _players = players;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var list = await _players.GetAllAsync().ConfigureAwait(true);
            Items.Clear();
            foreach (var p in list)
                Items.Add(p);
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
    private async Task AddAsync()
    {
        await Shell.Current.GoToAsync(nameof(Pages.PlayerEditPage)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync(Player? player)
    {
        if (player is null)
            return;

        await Shell.Current.GoToAsync($"{nameof(Pages.PlayerEditPage)}?id={player.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(Player? player)
    {
        if (player is null)
            return;

        var ok = await Shell.Current.DisplayAlert(
            "Delete player",
            $"Remove {player.FullName}? They will be removed from teams as captain if needed.",
            "Delete",
            "Cancel").ConfigureAwait(true);

        if (!ok)
            return;

        await _players.SoftDeleteAsync(player.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }
}
