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
    private bool isCaptain;

    public string DisplayName => IsCaptain ? $"{FullName} (C)" : FullName;

    partial void OnIsCaptainChanged(bool value)
        => OnPropertyChanged(nameof(DisplayName));
}

[QueryProperty(nameof(TeamId), "id")]
[QueryProperty(nameof(Mode), "mode")]
public partial class TeamEditViewModel : ObservableObject
{
    private readonly ITeamService _teams;
    private readonly IPlayerService _players;
    private readonly IAuthService _auth;

    public string? TeamId { get; set; }

    public string? Mode { get; set; }

    public ObservableCollection<SelectablePlayer> RosterCandidates { get; } = new();

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

    private Guid? _existingId;

    public TeamEditViewModel(ITeamService teams, IPlayerService players, IAuthService auth)
    {
        _teams = teams;
        _players = players;
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

        var allPlayers = await _players.GetActiveAsync().ConfigureAwait(true);

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
            Title = IsViewMode ? "View team" : "Edit team";
            var detail = await _teams.GetAsync(id).ConfigureAwait(true);
            if (detail is null)
            {
                ErrorMessage = "Team not found.";
                return;
            }

            Name = detail.Name;
            IsActive = detail.IsActive;
            captainId = detail.CaptainPlayerId;
            selected = detail.Players.Select(p => p.Id).ToHashSet();

            // View mode: only show current squad members (incl. inactive ones on the team).
            if (IsViewMode)
            {
                RosterCandidates.Clear();
                foreach (var p in detail.Players.OrderBy(x => x.FullName))
                    RosterCandidates.Add(new SelectablePlayer { Id = p.Id, FullName = p.FullName, IsSelected = true });
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
                row.IsSelected = selected.Contains(row.Id);
        }

        RebuildCaptainOptions(captainId);
    }

    partial void OnSelectedCaptainChanged(SelectablePlayer? value)
        => RefreshCaptainFlags();

    [RelayCommand]
    private void TogglePlayer(SelectablePlayer? player)
    {
        if (!CanEdit || player is null)
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

            if (SelectedCaptain is null || SelectedCaptain.Id == Guid.Empty)
                throw new InvalidOperationException("Captain is required. Select a captain from the squad.");
            if (!playerIds.Contains(SelectedCaptain.Id))
                throw new InvalidOperationException("Captain must be one of the selected squad players.");

            var draft = new TeamDraft(name, SelectedCaptain.Id, playerIds, IsActive);

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
        foreach (var p in selected)
            CaptainOptions.Add(p);

        if (preferredCaptainId is Guid cid && cid != Guid.Empty)
            SelectedCaptain = CaptainOptions.FirstOrDefault(c => c.Id == cid);
        else if (SelectedCaptain is null || !CaptainOptions.Any(c => c.Id == SelectedCaptain.Id))
            SelectedCaptain = CaptainOptions.FirstOrDefault();

        RefreshCaptainFlags();
    }

    private void RefreshCaptainFlags()
    {
        var capId = SelectedCaptain?.Id;
        foreach (var p in RosterCandidates)
            p.IsCaptain = capId is Guid id && p.Id == id;
    }
}
