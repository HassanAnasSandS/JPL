using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

[QueryProperty(nameof(SessionId), "id")]
public partial class NetSessionEditViewModel : ObservableObject
{
    private readonly INetSessionService _sessions;
    private readonly IPlayerService _players;
    private readonly IAuthService _auth;

    public string? SessionId { get; set; }

    public ObservableCollection<SelectablePlayer> RosterCandidates { get; } = new();

    [ObservableProperty] private string title = "New nets session";
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private DateTime sessionDate = DateTime.Today;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private bool canEdit = true;
    [ObservableProperty] private string selectedCountText = "0 selected";

    public NetSessionEditViewModel(INetSessionService sessions, IPlayerService players, IAuthService auth)
    {
        _sessions = sessions;
        _players = players;
        _auth = auth;
        CanWrite = _auth.CanWrite;
        CanEdit = CanWrite;
    }

    public async Task InitializeAsync()
    {
        CanWrite = _auth.CanWrite;
        CanEdit = CanWrite;
        ErrorMessage = null;

        try
        {
            IsBusy = true;
            var active = await _players.GetActiveAsync().ConfigureAwait(true);
            var selected = new HashSet<Guid>();
            Guid? existingId = Guid.TryParse(SessionId, out var sid) ? sid : null;

            if (existingId is Guid id)
            {
                var session = await _sessions.GetAsync(id).ConfigureAwait(true);
                if (session is null)
                {
                    ErrorMessage = "Session not found.";
                    return;
                }

                Title = "Edit nets session";
                Name = session.Name;
                SessionDate = session.SessionDateUtc.Date;
                CanEdit = CanWrite && session.Status != NetSessionStatus.Completed;
                var roster = NetRosterPayload.TryParse(session.PlayerRosterJson);
                if (roster is not null)
                {
                    foreach (var p in roster.Players)
                        selected.Add(p.Id);
                }
            }
            else
            {
                Title = "New nets session";
                Name = string.Empty;
                SessionDate = DateTime.Today;
            }

            RosterCandidates.Clear();
            foreach (var p in active.OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase))
            {
                RosterCandidates.Add(new SelectablePlayer
                {
                    Id = p.Id,
                    FullName = p.FullName,
                    IsSelected = selected.Contains(p.Id)
                });
            }

            RefreshSelectedCount();
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
    private void TogglePlayer(SelectablePlayer? player)
    {
        if (!CanEdit || player is null)
            return;
        player.IsSelected = !player.IsSelected;
        RefreshSelectedCount();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveAsync()
    {
        if (!CanEdit)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var ids = RosterCandidates.Where(p => p.IsSelected).Select(p => p.Id).ToList();
            var draft = new NetSessionDraft(Name, ids, SessionDate.Date);

            Guid sessionId;
            if (Guid.TryParse(SessionId, out var existing))
            {
                await _sessions.UpdateSetupAsync(existing, draft).ConfigureAwait(true);
                sessionId = existing;
            }
            else
            {
                var created = await _sessions.CreateAsync(draft).ConfigureAwait(true);
                sessionId = created.Id;
            }

            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            await Shell.Current.GoToAsync($"{nameof(Pages.NetsScoringPage)}?id={sessionId}").ConfigureAwait(true);
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

    private void RefreshSelectedCount()
    {
        var n = RosterCandidates.Count(p => p.IsSelected);
        SelectedCountText = n == 1 ? "1 selected" : $"{n} selected";
    }

    partial void OnCanEditChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
}
