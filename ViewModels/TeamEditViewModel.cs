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
}

[QueryProperty(nameof(TeamId), "id")]
public partial class TeamEditViewModel : ObservableObject
{
    private readonly ITeamService _teams;
    private readonly IPlayerService _players;

    public string? TeamId { get; set; }

    public ObservableCollection<SelectablePlayer> RosterCandidates { get; } = new();

    public ObservableCollection<SelectablePlayer> CaptainOptions { get; } = new();

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string? shortName;

    [ObservableProperty]
    private SelectablePlayer? selectedCaptain;

    [ObservableProperty]
    private string title = "New team";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string hint = "Register players first, then add them here. Captain is chosen from team players only.";

    private Guid? _existingId;

    public TeamEditViewModel(ITeamService teams, IPlayerService players)
    {
        _teams = teams;
        _players = players;
    }

    public async Task InitializeAsync()
    {
        var allPlayers = await _players.GetAllAsync().ConfigureAwait(true);
        RosterCandidates.Clear();
        foreach (var p in allPlayers)
        {
            RosterCandidates.Add(new SelectablePlayer { Id = p.Id, FullName = p.FullName });
        }

        HashSet<Guid> selected = new();
        Guid? captainId = null;

        if (Guid.TryParse(TeamId, out var id))
        {
            _existingId = id;
            Title = "Edit team";
            var detail = await _teams.GetAsync(id).ConfigureAwait(true);
            if (detail is null)
            {
                ErrorMessage = "Team not found.";
                return;
            }

            Name = detail.Name;
            ShortName = detail.ShortName;
            captainId = detail.CaptainPlayerId;
            selected = detail.Players.Select(p => p.Id).ToHashSet();
        }

        foreach (var row in RosterCandidates)
            row.IsSelected = selected.Contains(row.Id);

        RebuildCaptainOptions(captainId);
    }

    partial void OnSelectedCaptainChanged(SelectablePlayer? value)
    {
        // no-op hook for future UI binding
    }

    [RelayCommand]
    private void TogglePlayer(SelectablePlayer? player)
    {
        if (player is null)
            return;

        player.IsSelected = !player.IsSelected;

        // If captain was deselected from team, clear captain.
        if (!player.IsSelected && SelectedCaptain?.Id == player.Id)
            SelectedCaptain = null;

        RebuildCaptainOptions(SelectedCaptain?.Id);
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

            var playerIds = RosterCandidates.Where(p => p.IsSelected).Select(p => p.Id).ToList();
            Guid? captainId = SelectedCaptain is null || SelectedCaptain.Id == Guid.Empty
                ? null
                : SelectedCaptain.Id;
            var draft = new TeamDraft(Name, ShortName, captainId, playerIds);

            if (_existingId is Guid id)
                await _teams.UpdateAsync(id, draft).ConfigureAwait(true);
            else
                await _teams.CreateAsync(draft).ConfigureAwait(true);

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

    private void RebuildCaptainOptions(Guid? preferredCaptainId)
    {
        var selected = RosterCandidates.Where(p => p.IsSelected).OrderBy(p => p.FullName).ToList();
        CaptainOptions.Clear();
        CaptainOptions.Add(new SelectablePlayer { Id = Guid.Empty, FullName = "(No captain)" });
        foreach (var p in selected)
            CaptainOptions.Add(p);

        if (preferredCaptainId is Guid cid && cid != Guid.Empty)
            SelectedCaptain = CaptainOptions.FirstOrDefault(c => c.Id == cid);
        else
            SelectedCaptain = CaptainOptions.FirstOrDefault();
    }
}
