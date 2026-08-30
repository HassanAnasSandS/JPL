using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class UserRow
{
    public Guid Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public AppRole Role { get; init; }
    public string RoleLabel { get; init; } = string.Empty;
    public string LeagueName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string StatusText => IsActive ? "Active" : "Deactive";
    public Color StatusColor => StatusColors.For(IsActive);
    public string StatusLabel => StatusText;
    public string ToggleActiveLabel => IsActive ? "Deactivate" : "Activate";
}

public sealed class UserRoleOption
{
    public AppRole? Role { get; init; }
    public string Name { get; init; } = string.Empty;
}

public partial class UsersViewModel : ObservableObject
{
    private static readonly UserLeagueOption AllLeaguesOption = new() { Id = Guid.Empty, Name = "All leagues" };
    private static readonly UserRoleOption AllRolesOption = new() { Role = null, Name = "All roles" };

    private static readonly UserRoleOption[] RoleOptions =
    [
        AllRolesOption,
        new() { Role = AppRole.User, Name = "League User" },
        new() { Role = AppRole.Admin, Name = "League Admin" },
        new() { Role = AppRole.GroundOwner, Name = "Ground Owner" },
        new() { Role = AppRole.PublicUser, Name = "Public User" },
        new() { Role = AppRole.Guest, Name = "Guest" }
    ];

    private readonly IUserService _users;
    private readonly IAuthService _auth;
    private readonly IUsersModuleGate _gate;
    private readonly ILeagueService _leagues;
    private int _loadGen;
    private int _loadBusy;
    private int _filterGen;
    private int _suppressFilter;

    public ObservableCollection<UserRow> Items { get; } = new();
    public ObservableCollection<UserLeagueOption> LeagueFilterOptions { get; } = new();
    public ObservableCollection<UserRoleOption> RoleFilterOptions { get; } = new();

    /// <summary>0 = All, 1 = Active, 2 = Deactive</summary>
    [ObservableProperty] private int statusFilter;
    [ObservableProperty] private UserLeagueOption? selectedLeagueFilter;
    [ObservableProperty] private UserRoleOption? selectedRoleFilter;
    [ObservableProperty] private bool isUnlocked;
    [ObservableProperty] private string modulePassword = string.Empty;
    [ObservableProperty] private string? unlockError;
    [ObservableProperty] private bool isPasswordHidden = true;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private string nameFilter = string.Empty;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private int activeCount;
    [ObservableProperty] private int inactiveCount;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;

    public string CountLabel => TotalCount <= 0 ? "No users" : $"{Items.Count} of {TotalCount}";

    public string PasswordToggleIcon => IsPasswordHidden ? "👁" : "🙈";
    public bool ShowManagement => IsUnlocked;
    public bool ShowLockPanel => !IsUnlocked;

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(NameFilter) || StatusFilter != 0 || HasLeagueFilter || HasRoleFilter;

    private bool HasLeagueFilter =>
        SelectedLeagueFilter is { } league && league.Id != Guid.Empty;

    private bool HasRoleFilter => SelectedRoleFilter?.Role is not null;

    public bool IsAllFilter => StatusFilter == 0;
    public bool IsActiveFilter => StatusFilter == 1;
    public bool IsInactiveFilter => StatusFilter == 2;

    public string AllFilterLabel => $"All ({TotalCount})";
    public string ActiveFilterLabel => $"Active ({ActiveCount})";
    public string InactiveFilterLabel => $"Deactive ({InactiveCount})";

    public UsersViewModel(IUserService users, IAuthService auth, IUsersModuleGate gate, ILeagueService leagues)
    {
        _users = users;
        _auth = auth;
        _gate = gate;
        _leagues = leagues;
        CanWrite = _auth.Current?.CanManageUsers == true;
        IsUnlocked = _gate.IsUnlocked;
        LeagueFilterOptions.Add(AllLeaguesOption);
        SelectedLeagueFilter = AllLeaguesOption;
        foreach (var role in RoleOptions)
            RoleFilterOptions.Add(role);
        SelectedRoleFilter = AllRolesOption;
    }

    public void RefreshAuthState()
    {
        CanWrite = _auth.Current?.CanManageUsers == true;
        IsUnlocked = _gate.IsUnlocked;
    }

    partial void OnIsPasswordHiddenChanged(bool value) => OnPropertyChanged(nameof(PasswordToggleIcon));
    partial void OnIsUnlockedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowManagement));
        OnPropertyChanged(nameof(ShowLockPanel));
    }

    partial void OnNameFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        if (_suppressFilter == 0)
            _ = ReloadDebouncedAsync();
    }

    partial void OnStatusFilterChanged(int value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsActiveFilter));
        OnPropertyChanged(nameof(IsInactiveFilter));
        if (_suppressFilter == 0)
            _ = ReloadAsync();
    }

    partial void OnSelectedLeagueFilterChanged(UserLeagueOption? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        if (_suppressFilter == 0)
            _ = ReloadAsync();
    }

    partial void OnSelectedRoleFilterChanged(UserRoleOption? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        if (_suppressFilter == 0)
            _ = ReloadAsync();
    }

    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(AllFilterLabel));
        OnPropertyChanged(nameof(CountLabel));
    }
    partial void OnActiveCountChanged(int value) => OnPropertyChanged(nameof(ActiveFilterLabel));
    partial void OnInactiveCountChanged(int value) => OnPropertyChanged(nameof(InactiveFilterLabel));

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordHidden = !IsPasswordHidden;

    [RelayCommand]
    private void Unlock()
    {
        UnlockError = null;
        if (_gate.TryUnlock(ModulePassword))
        {
            IsUnlocked = true;
            ModulePassword = string.Empty;
            _ = LoadAsync();
            return;
        }

        IsUnlocked = false;
        UnlockError = "Wrong password. Access denied.";
        ModulePassword = string.Empty;
    }

    [RelayCommand]
    private void LockModule()
    {
        _gate.Lock();
        IsUnlocked = false;
        ModulePassword = string.Empty;
        UnlockError = null;
        ErrorMessage = null;
        Items.Clear();
    }

    [RelayCommand]
    public Task LoadAsync() => ReloadAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        CanWrite = _auth.Current?.CanManageUsers == true;
        if (!CanWrite)
        {
            ErrorMessage = "Only HassanAnas can manage accounts.";
            Items.Clear();
            return;
        }

        if (!_gate.IsUnlocked)
        {
            IsUnlocked = false;
            return;
        }

        HasMore = true;
        Items.Clear();
        OnPropertyChanged(nameof(CountLabel));
        await LoadMoreAsync().ConfigureAwait(true);
    }

    private async Task ReloadDebouncedAsync()
    {
        var gen = Interlocked.Increment(ref _filterGen);
        await Task.Delay(350).ConfigureAwait(true);
        if (gen != _filterGen)
            return;
        await ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        CanWrite = _auth.Current?.CanManageUsers == true;
        if (!CanWrite || !_gate.IsUnlocked)
            return;
        if (!HasMore || Interlocked.Exchange(ref _loadBusy, 1) == 1)
            return;

        var gen = Interlocked.Increment(ref _loadGen);
        try
        {
            if (Items.Count == 0)
                IsBusy = true;
            else
                IsLoadingMore = true;
            ErrorMessage = null;
            IsUnlocked = true;

            if (Items.Count == 0)
                await LoadLeagueFilterOptionsAsync().ConfigureAwait(true);
            if (gen != Volatile.Read(ref _loadGen))
                return;

            var leagueId = SelectedLeagueFilter is { } league && league.Id != Guid.Empty ? league.Id : (Guid?)null;
            var role = SelectedRoleFilter?.Role?.ToString();
            var page = await _users.GetPageAsync(Items.Count, NameFilter, StatusFilter, leagueId, role)
                .ConfigureAwait(true);
            if (gen != Volatile.Read(ref _loadGen))
                return;

            TotalCount = page.TotalCount;
            ActiveCount = page.ActiveCount ?? ActiveCount;
            InactiveCount = page.InactiveCount ?? InactiveCount;
            ListPaging.Append(Items, page.Items.Select(ToRow), r => r.Id);
            HasMore = page.HasMore;
            OnPropertyChanged(nameof(CountLabel));
        }
        catch (Exception ex)
        {
            if (gen != Volatile.Read(ref _loadGen))
                return;
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — Users module needs internet / API."
                : ex.Message;
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
            if (Items.Count == 0)
            {
                TotalCount = 0;
                ActiveCount = 0;
                InactiveCount = 0;
            }
        }
        finally
        {
            if (gen == Volatile.Read(ref _loadGen))
            {
                IsBusy = false;
                IsLoadingMore = false;
            }
            Interlocked.Exchange(ref _loadBusy, 0);
        }
    }

    [RelayCommand]
    private void SelectStatus(string? status)
    {
        StatusFilter = status switch
        {
            "active" => 1,
            "inactive" or "deactive" => 2,
            _ => 0
        };
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Interlocked.Exchange(ref _suppressFilter, 1);
        NameFilter = string.Empty;
        StatusFilter = 0;
        SelectedLeagueFilter = LeagueFilterOptions.FirstOrDefault(l => l.Id == Guid.Empty)
            ?? AllLeaguesOption;
        SelectedRoleFilter = RoleFilterOptions.FirstOrDefault(r => r.Role is null)
            ?? AllRolesOption;
        Interlocked.Exchange(ref _suppressFilter, 0);
        _ = ReloadAsync();
    }

    private async Task LoadLeagueFilterOptionsAsync()
    {
        var previousId = SelectedLeagueFilter?.Id ?? Guid.Empty;
        IReadOnlyList<League> list = [];
        try
        {
            list = await _leagues.GetAllAsync().ConfigureAwait(true);
        }
        catch
        {
            // Keep All leagues even if catalog fetch fails; users still list by name search.
        }

        var rows = list
            .DistinctBy(l => l.Id)
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(l => new UserLeagueOption { Id = l.Id, Name = l.Name })
            .ToList();

        LeagueFilterOptions.Clear();
        LeagueFilterOptions.Add(AllLeaguesOption);
        foreach (var row in rows)
            LeagueFilterOptions.Add(row);

        SelectedLeagueFilter = LeagueFilterOptions.FirstOrDefault(l => l.Id == previousId)
            ?? AllLeaguesOption;
    }

    private static UserRow ToRow(AppUser u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        Role = u.Role,
        RoleLabel = AppRoleLabels.Of(u.Role),
        LeagueName = u.Role is AppRole.PublicUser or AppRole.Guest or AppRole.GroundOwner || string.IsNullOrWhiteSpace(u.LeagueName)
            ? (u.Role == AppRole.PublicUser ? "Public"
                : u.Role == AppRole.Guest ? "Guest"
                : string.IsNullOrWhiteSpace(u.LeagueName) ? "No league" : u.LeagueName)
            : u.LeagueName,
        IsActive = u.IsActive
    };

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanWrite || !_gate.IsUnlocked)
            return;
        await Shell.Current.GoToAsync(nameof(Pages.UserEditPage)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync(UserRow? user)
    {
        if (!CanWrite || !_gate.IsUnlocked || user is null)
            return;
        await Shell.Current.GoToAsync($"{nameof(Pages.UserEditPage)}?id={user.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(UserRow? user)
    {
        if (!CanWrite || !_gate.IsUnlocked || user is null)
            return;

        var next = !user.IsActive;
        var verb = next ? "Activate" : "Deactivate";
        var ok = await Shell.Current.DisplayAlert(
            $"{verb} user",
            $"{verb} account \"{user.Username}\"? Inactive users cannot sign in.",
            verb,
            "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            await _users.SetActiveAsync(user.Id, next).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(UserRow? user)
    {
        if (!CanWrite || !_gate.IsUnlocked || user is null)
            return;

        var ok = await Shell.Current.DisplayAlert(
            "Delete user",
            $"Remove account \"{user.Username}\"?",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            await _users.DeleteAsync(user.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
