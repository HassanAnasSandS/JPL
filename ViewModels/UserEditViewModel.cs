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

    private static readonly string[] AssignableRoles =
        ["League Admin", "League User", "Ground Owner"];

    public ObservableCollection<string> RoleOptions { get; } = new(AssignableRoles);
    public ObservableCollection<UserLeagueOption> LeagueOptions { get; } = new();

    [ObservableProperty] private string username = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string selectedRole = "League User";
    [ObservableProperty] private UserLeagueOption? selectedLeague;
    [ObservableProperty] private bool isActive = true;
    [ObservableProperty] private string title = "Add user";
    [ObservableProperty] private string passwordHint = "Password (required)";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private bool isPasswordHidden = true;

    public bool ShowLeaguePicker
    {
        get
        {
            var role = SelectedRole ?? string.Empty;
            if (role.Length == 0)
                return true;
            return !role.Equals("Ground Owner", StringComparison.OrdinalIgnoreCase)
                && !role.Equals(nameof(AppRole.GroundOwner), StringComparison.OrdinalIgnoreCase)
                && !role.Contains("Public", StringComparison.OrdinalIgnoreCase)
                && !role.Contains("Guest", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string LeagueLabel => ShowLeaguePicker ? "League *" : "League (not used for this role)";

    public Color StatusColor => StatusColors.For(IsActive);

    public string PasswordToggleIcon => IsPasswordHidden ? "👁" : "🙈";

    partial void OnSelectedRoleChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            selectedRole = "League User";
        OnPropertyChanged(nameof(ShowLeaguePicker));
        OnPropertyChanged(nameof(LeagueLabel));
    }

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
            SetRoleOptions(includeExisting: null);
            SelectedRole = "League User";
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
        SetRoleOptions(user.Role);
        SelectedRole = AppRoleLabels.Of(user.Role);
        IsActive = user.IsActive;
        Password = string.Empty;
        SelectLeague(user.LeagueId);
    }

    private void SetRoleOptions(AppRole? includeExisting)
    {
        RoleOptions.Clear();
        foreach (var role in AssignableRoles)
            RoleOptions.Add(role);

        if (includeExisting is AppRole.PublicUser)
            RoleOptions.Add("Public User");
        else if (includeExisting is AppRole.Guest)
            RoleOptions.Add("Guest");
    }

    private async Task LoadLeaguesAsync()
    {
        var list = (await _leagues.GetAllAsync().ConfigureAwait(true))
            .DistinctBy(l => l.Id)
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        LeagueOptions.Clear();
        foreach (var league in list)
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

            if (!AppRoleLabels.TryParseManaged(SelectedRole, out var role))
                throw new ArgumentException("Select League Admin, League User, or Ground Owner.");
            if (role == AppRole.PublicUser && _existingId is null)
                throw new ArgumentException("Public users sign up from the login screen.");
            if (role == AppRole.Guest && _existingId is null)
                throw new ArgumentException("Guest is a built-in account.");
            Guid? leagueId = null;
            if (role is not AppRole.GroundOwner and not AppRole.PublicUser and not AppRole.Guest)
            {
                if (SelectedLeague is null || SelectedLeague.Id == Guid.Empty)
                    throw new ArgumentException("Select a league for this user.");
                leagueId = SelectedLeague.Id;
            }

            var draft = new UserDraft(Username, Password, role, leagueId, IsActive);
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
