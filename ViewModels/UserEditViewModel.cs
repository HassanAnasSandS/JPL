using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class UserLeagueOption
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

[QueryProperty(nameof(UserId), "id")]
public partial class UserEditViewModel : ObservableObject
{
    private readonly IUserService _users;
    private readonly IAuthService _auth;
    private readonly IUsersModuleGate _gate;
    private readonly ILeagueService _leagues;
    private readonly ILeagueContext _league;

    public string? UserId { get; set; }

    public IReadOnlyList<string> RoleOptions { get; } = new[] { "Admin", "User" };
    public ObservableCollection<UserLeagueOption> LeagueOptions { get; } = new();

    [ObservableProperty] private string username = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string selectedRole = "User";
    [ObservableProperty] private UserLeagueOption? selectedLeague;
    [ObservableProperty] private bool isActive = true;
    [ObservableProperty] private string title = "Add user";
    [ObservableProperty] private string passwordHint = "Password (required)";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private bool isPasswordHidden = true;

    public Color StatusColor => StatusColors.For(IsActive);

    public string PasswordToggleIcon => IsPasswordHidden ? "👁" : "🙈";

    partial void OnIsActiveChanged(bool value)
        => OnPropertyChanged(nameof(StatusColor));

    private Guid? _existingId;

    public UserEditViewModel(
        IUserService users,
        IAuthService auth,
        IUsersModuleGate gate,
        ILeagueService leagues,
        ILeagueContext league)
    {
        _users = users;
        _auth = auth;
        _gate = gate;
        _leagues = leagues;
        _league = league;
        CanWrite = _auth.Current?.CanManageUsers == true;
    }

    partial void OnIsPasswordHiddenChanged(bool value) => OnPropertyChanged(nameof(PasswordToggleIcon));

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordHidden = !IsPasswordHidden;

    public async Task InitializeAsync()
    {
        CanWrite = _auth.Current?.CanManageUsers == true;
        if (!_gate.IsUnlocked || !CanWrite)
        {
            ErrorMessage = !CanWrite
                ? "Only HassanAnas can manage accounts."
                : "Users module is locked.";
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        await LoadLeaguesAsync().ConfigureAwait(true);

        if (!Guid.TryParse(UserId, out var id))
        {
            Title = "Add user";
            PasswordHint = "Password (required)";
            SelectLeague(_league.CurrentLeagueId);
            return;
        }

        _existingId = id;
        Title = "Edit user";
        PasswordHint = "Password (leave blank to keep)";

        var user = await _users.GetAsync(id).ConfigureAwait(true);
        if (user is null)
        {
            ErrorMessage = "User not found.";
            return;
        }

        Username = user.Username;
        SelectedRole = user.Role.ToString();
        IsActive = user.IsActive;
        Password = string.Empty;
        SelectLeague(user.LeagueId);
    }

    private async Task LoadLeaguesAsync()
    {
        LeagueOptions.Clear();
        var list = await _leagues.GetAllAsync().ConfigureAwait(true);
        foreach (var league in list.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            LeagueOptions.Add(new UserLeagueOption { Id = league.Id, Name = league.Name });
    }

    private void SelectLeague(Guid? leagueId)
    {
        SelectedLeague = leagueId is Guid id
            ? LeagueOptions.FirstOrDefault(l => l.Id == id)
            : LeagueOptions.FirstOrDefault();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy || !CanWrite || !_gate.IsUnlocked)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            if (!Enum.TryParse<AppRole>(SelectedRole, ignoreCase: true, out var role))
                throw new ArgumentException("Select Admin or User role.");
            if (SelectedLeague is null || SelectedLeague.Id == Guid.Empty)
                throw new ArgumentException("Select a league for this user.");

            var draft = new UserDraft(Username, Password, role, SelectedLeague.Id, IsActive);
            if (_existingId is Guid id)
                await _users.UpdateAsync(id, draft).ConfigureAwait(true);
            else
                await _users.CreateAsync(draft).ConfigureAwait(true);

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
