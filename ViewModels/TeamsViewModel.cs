using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class TeamsViewModel : ObservableObject
{
    private readonly ITeamService _teams;

    public ObservableCollection<TeamListItem> Items { get; } = new();

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    public TeamsViewModel(ITeamService teams)
    {
        _teams = teams;
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
            var list = await _teams.GetAllAsync().ConfigureAwait(true);
            Items.Clear();
            foreach (var t in list)
                Items.Add(t);
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
        => await Shell.Current.GoToAsync(nameof(Pages.TeamEditPage)).ConfigureAwait(true);

    [RelayCommand]
    private async Task EditAsync(TeamListItem? team)
    {
        if (team is null)
            return;

        await Shell.Current.GoToAsync($"{nameof(Pages.TeamEditPage)}?id={team.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(TeamListItem? team)
    {
        if (team is null)
            return;

        var ok = await Shell.Current.DisplayAlert(
            "Delete team",
            $"Remove team {team.Name}?",
            "Delete",
            "Cancel").ConfigureAwait(true);

        if (!ok)
            return;

        await _teams.SoftDeleteAsync(team.Id).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }
}
