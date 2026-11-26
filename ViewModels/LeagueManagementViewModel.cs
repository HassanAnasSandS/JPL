using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class ManagedLeagueRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public ImageSource Image { get; init; } = "cric360_logo.png";
    public bool IsActive { get; init; }
    public int AdminCount { get; init; }
    public int UserCount { get; init; }
    public int LiveMatchCount { get; init; }
    public DateTime CreatedAtUtc { get; init; }

    public string StatusText => IsActive ? "Active" : "Deactive";
    public Color StatusColor => StatusColors.For(IsActive);
    public string ToggleActiveLabel => IsActive ? "Deactivate" : "Activate";
    public bool CanDelete => !IsActive;
    /// <summary>Delete stays tappable (explains "deactivate first") but looks muted while active.</summary>
    public double DeleteOpacity => CanDelete ? 1.0 : 0.45;
    public bool HasLive => LiveMatchCount > 0;
    public string LiveText => LiveMatchCount == 1 ? "1 live match" : $"{LiveMatchCount} live matches";

    public string MembersText
    {
        get
        {
            var admins = AdminCount == 1 ? "1 admin" : $"{AdminCount} admins";
            var users = UserCount == 1 ? "1 user" : $"{UserCount} users";
            return $"{admins}  ·  {users}";
        }
    }

    public string CreatedText => $"Created {CreatedAtUtc:dd MMM yyyy}";
}

/// <summary>
/// SuperAdmin (HassanAnas) League management: league records only — list / search / filter,
/// create, edit, activate / deactivate, delete (soft). Shares the Users module unlock.
/// </summary>
public partial class LeagueManagementViewModel : ObservableObject
{
    private readonly ILeagueAdminService _leagues;
    private readonly IAuthService _auth;
    private readonly IUsersModuleGate _gate;
    private int _loadGen;
    private int _loadBusy;
    private int _filterGen;
    private int _suppressFilter;

    public ObservableCollection<ManagedLeagueRow> Items { get; } = new();

    /// <summary>0 = All, 1 = Active, 2 = Deactive</summary>
    [ObservableProperty] private int statusFilter;
    [ObservableProperty] private bool isUnlocked;
    [ObservableProperty] private string modulePassword = string.Empty;
    [ObservableProperty] private string? unlockError;
    [ObservableProperty] private bool isPasswordHidden = true;
    [ObservableProperty] private bool isBusy;
    /// <summary>Activate / deactivate / delete in progress (separate from IsBusy so RefreshView does not re-trigger).</summary>
    [ObservableProperty] private bool isWorking;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private string nameFilter = string.Empty;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private int activeCount;
    [ObservableProperty] private int inactiveCount;

    public LeagueManagementViewModel(ILeagueAdminService leagues, IAuthService auth, IUsersModuleGate gate)
    {
        _leagues = leagues;
        _auth = auth;
        _gate = gate;
        CanWrite = _auth.Current?.CanManageLeagues == true;
        IsUnlocked = _gate.IsUnlocked;
    }

    public string CountLabel => TotalCount <= 0 ? "No leagues" : $"{Items.Count} of {TotalCount}";
    public string PasswordToggleIcon => IsPasswordHidden ? "👁" : "🙈";
    public bool ShowManagement => IsUnlocked;
    public bool ShowSpinner => IsBusy || IsWorking;
    public bool ShowLockPanel => !IsUnlocked;
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(NameFilter) || StatusFilter != 0;
    public bool IsAllFilter => StatusFilter == 0;
    public bool IsActiveFilter => StatusFilter == 1;
    public bool IsInactiveFilter => StatusFilter == 2;
    public string AllFilterLabel => $"All ({ActiveCount + InactiveCount})";
    public string ActiveFilterLabel => $"Active ({ActiveCount})";
    public string InactiveFilterLabel => $"Deactive ({InactiveCount})";
    public string EmptyText => HasActiveFilters ? "No leagues match these filters." : "No leagues yet. Tap + Add to create one.";

    public void RefreshAuthState()
    {
        CanWrite = _auth.Current?.CanManageLeagues == true;
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
        OnPropertyChanged(nameof(EmptyText));
        if (_suppressFilter == 0)
            _ = ReloadDebouncedAsync();
    }

    partial void OnStatusFilterChanged(int value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsActiveFilter));
        OnPropertyChanged(nameof(IsInactiveFilter));
        if (_suppressFilter == 0)
            _ = ReloadAsync();
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(ShowSpinner));
    partial void OnIsWorkingChanged(bool value) => OnPropertyChanged(nameof(ShowSpinner));

    partial void OnActiveCountChanged(int value)
    {
        OnPropertyChanged(nameof(ActiveFilterLabel));
        OnPropertyChanged(nameof(AllFilterLabel));
    }

    partial void OnInactiveCountChanged(int value)
    {
        OnPropertyChanged(nameof(InactiveFilterLabel));
        OnPropertyChanged(nameof(AllFilterLabel));
    }

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
        CanWrite = _auth.Current?.CanManageLeagues == true;
        if (!CanWrite)
        {
            ErrorMessage = "Only HassanAnas can manage leagues.";
            Items.Clear();
            return;
        }

        if (!_gate.IsUnlocked)
        {
            IsUnlocked = false;
            return;
        }

        // New generation first so an older in-flight page cannot append into the fresh list.
        Interlocked.Increment(ref _loadGen);
        Interlocked.Exchange(ref _loadBusy, 0);
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
        if (!CanWrite || !_gate.IsUnlocked)
            return;
        if (!HasMore || Interlocked.Exchange(ref _loadBusy, 1) == 1)
            return;

        var gen = Volatile.Read(ref _loadGen);
        try
        {
            if (Items.Count == 0)
                IsBusy = true;
            else
                IsLoadingMore = true;
            ErrorMessage = null;

            var page = await _leagues.GetPageAsync(Items.Count, NameFilter, StatusFilter).ConfigureAwait(true);
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
            var offline = NetworkUi.IsNetworkFailure(ex);
            ErrorMessage = offline ? "Offline — League management needs internet / API." : ex.Message;
            if (offline)
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
                Interlocked.Exchange(ref _loadBusy, 0);
            }
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
        Interlocked.Exchange(ref _suppressFilter, 0);
        _ = ReloadAsync();
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanWrite || !_gate.IsUnlocked)
            return;
        await Shell.Current.GoToAsync(nameof(Pages.LeagueManageEditPage)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync(ManagedLeagueRow? row)
    {
        if (!CanWrite || !_gate.IsUnlocked || row is null)
            return;
        await Shell.Current.GoToAsync($"{nameof(Pages.LeagueManageEditPage)}?id={row.Id:D}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(ManagedLeagueRow? row)
    {
        if (!CanWrite || !_gate.IsUnlocked || row is null || IsWorking)
            return;

        var next = !row.IsActive;
        string message;
        if (next)
        {
            message = $"Activate \"{row.Name}\"? Its League Admins and Users can sign in again.";
        }
        else
        {
            message = $"Deactivate \"{row.Name}\"?\n\n"
                      + $"{row.MembersText} will be signed out and blocked from login until you activate it again. "
                      + "League data is kept.";
            if (row.HasLive)
                message += $"\n\n⚠ This league has {row.LiveText} right now.";
        }

        var verb = next ? "Activate" : "Deactivate";
        var ok = await Shell.Current.DisplayAlert($"{verb} league", message, verb, "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            IsWorking = true;
            ErrorMessage = null;
            await _leagues.SetActiveAsync(row.Id, next).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await ShowFailureAsync($"{verb} failed", ex).ConfigureAwait(true);
            return;
        }
        finally
        {
            IsWorking = false;
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(ManagedLeagueRow? row)
    {
        if (!CanWrite || !_gate.IsUnlocked || row is null || IsWorking)
            return;

        if (row.IsActive)
        {
            await Shell.Current.DisplayAlert(
                "Delete league",
                $"Deactivate \"{row.Name}\" first, then delete it.",
                "OK").ConfigureAwait(true);
            return;
        }

        var ok = await Shell.Current.DisplayAlert(
            "Delete league",
            $"Delete \"{row.Name}\"?\n\n"
            + $"It will be removed from every list. {row.MembersText} stay blocked from login. "
            + "The league database is kept on the server (nothing is erased).",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            IsWorking = true;
            ErrorMessage = null;
            await _leagues.DeleteAsync(row.Id).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await ShowFailureAsync("Delete failed", ex).ConfigureAwait(true);
            return;
        }
        finally
        {
            IsWorking = false;
        }

        await ReloadAsync().ConfigureAwait(true);
    }

    private async Task ShowFailureAsync(string title, Exception ex)
    {
        if (NetworkUi.IsNetworkFailure(ex))
        {
            ErrorMessage = NetworkUi.OfflineMessage;
            await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
            return;
        }

        ErrorMessage = ex.Message;
        await Shell.Current.DisplayAlert(title, ex.Message, "OK").ConfigureAwait(true);
    }

    private static ManagedLeagueRow ToRow(ManagedLeague l) => new()
    {
        Id = l.Id,
        Name = l.Name,
        Image = LeagueImages.ToImageSource(l.ImageBytes),
        IsActive = l.IsActive,
        AdminCount = l.AdminCount,
        UserCount = l.UserCount,
        LiveMatchCount = l.LiveMatchCount,
        CreatedAtUtc = l.CreatedAtUtc
    };
}
