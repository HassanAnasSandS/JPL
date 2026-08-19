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
    public string StatusLabel => StatusText;
    public string ToggleActiveLabel => IsActive ? "Deactivate" : "Activate";
}

public partial class UsersViewModel : ObservableObject
{
    private readonly IUserService _users;
    private readonly IAuthService _auth;
    private readonly IUsersModuleGate _gate;
    private List<AppUser> _all = new();

    public ObservableCollection<UserRow> Items { get; } = new();

    /// <summary>0 = All, 1 = Active, 2 = Deactive</summary>
    [ObservableProperty] private int statusFilter;
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

    public string PasswordToggleIcon => IsPasswordHidden ? "👁" : "🙈";
    public bool ShowManagement => IsUnlocked;
    public bool ShowLockPanel => !IsUnlocked;

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(NameFilter) || StatusFilter != 0;

    public bool IsAllFilter => StatusFilter == 0;
    public bool IsActiveFilter => StatusFilter == 1;
    public bool IsInactiveFilter => StatusFilter == 2;

    public string AllFilterLabel => $"All ({TotalCount})";
    public string ActiveFilterLabel => $"Active ({ActiveCount})";
    public string InactiveFilterLabel => $"Deactive ({InactiveCount})";

    public UsersViewModel(IUserService users, IAuthService auth, IUsersModuleGate gate)
    {
        _users = users;
        _auth = auth;
        _gate = gate;
        CanWrite = _auth.Current?.CanManageUsers == true;
        IsUnlocked = _gate.IsUnlocked;
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
        ApplyFilters();
    }

    partial void OnStatusFilterChanged(int value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsActiveFilter));
        OnPropertyChanged(nameof(IsInactiveFilter));
        ApplyFilters();
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(AllFilterLabel));
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
    public async Task LoadAsync()
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

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            CanWrite = _auth.Current?.CanManageUsers == true;
            IsUnlocked = true;

            _all = (await _users.GetAllAsync().ConfigureAwait(true)).ToList();
            RefreshCounts();
            ApplyFilters();
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — Users module needs internet / API."
                : ex.Message;
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
            _all = new List<AppUser>();
            RefreshCounts();
            ApplyFilters();
        }
        finally
        {
            IsBusy = false;
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
        NameFilter = string.Empty;
        StatusFilter = 0;
    }

    private void RefreshCounts()
    {
        TotalCount = _all.Count;
        ActiveCount = _all.Count(u => u.IsActive);
        InactiveCount = _all.Count(u => !u.IsActive);
    }

    private void ApplyFilters()
    {
        IEnumerable<AppUser> q = _all;

        q = StatusFilter switch
        {
            1 => q.Where(u => u.IsActive),
            2 => q.Where(u => !u.IsActive),
            _ => q
        };

        var name = NameFilter?.Trim();
        if (!string.IsNullOrWhiteSpace(name))
            q = q.Where(u =>
                u.Username.Contains(name, StringComparison.OrdinalIgnoreCase)
                || u.LeagueName.Contains(name, StringComparison.OrdinalIgnoreCase));

        Items.Clear();
        foreach (var u in q)
        {
            Items.Add(new UserRow
            {
                Id = u.Id,
                Username = u.Username,
                Role = u.Role,
                RoleLabel = u.Role.ToString(),
                LeagueName = string.IsNullOrWhiteSpace(u.LeagueName) ? "No league" : u.LeagueName,
                IsActive = u.IsActive
            });
        }
    }

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
