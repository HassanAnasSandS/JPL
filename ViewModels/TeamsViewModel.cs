using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using CommunityToolkit.Mvvm.Input;

using JPL.Services;



namespace JPL.ViewModels;



public partial class TeamsViewModel : ObservableObject

{

    private readonly ITeamService _teams;

    private readonly IAuthService _auth;

    private List<TeamListItem> _all = new();



    public ObservableCollection<TeamListItem> Items { get; } = new();



    /// <summary>0 = All, 1 = Active, 2 = Deactive</summary>

    [ObservableProperty] private int statusFilter;

    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private string? errorMessage;

    [ObservableProperty] private bool canWrite;

    [ObservableProperty] private string nameFilter = string.Empty;

    [ObservableProperty] private string captainFilter = string.Empty;

    [ObservableProperty] private int totalCount;

    [ObservableProperty] private int activeCount;

    [ObservableProperty] private int inactiveCount;



    public bool HasActiveFilters =>

        !string.IsNullOrWhiteSpace(NameFilter)

        || !string.IsNullOrWhiteSpace(CaptainFilter)

        || StatusFilter != 0;



    public bool IsAllFilter => StatusFilter == 0;

    public bool IsActiveFilter => StatusFilter == 1;

    public bool IsInactiveFilter => StatusFilter == 2;



    public string AllFilterLabel => $"All ({TotalCount})";

    public string ActiveFilterLabel => $"Active ({ActiveCount})";

    public string InactiveFilterLabel => $"Deactive ({InactiveCount})";



    public TeamsViewModel(ITeamService teams, IAuthService auth)

    {

        _teams = teams;

        _auth = auth;

        CanWrite = _auth.CanWrite;

    }



    public void RefreshAuthState() => CanWrite = _auth.CanWrite;



    partial void OnNameFilterChanged(string value)

    {

        OnPropertyChanged(nameof(HasActiveFilters));

        ApplyFilters();

    }



    partial void OnCaptainFilterChanged(string value)

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

    public async Task LoadAsync()

    {

        try

        {

            IsBusy = true;

            ErrorMessage = null;

            RefreshAuthState();

            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);

            _all = (await _teams.GetAllAsync().ConfigureAwait(true)).ToList();
            RefreshCounts();
            ApplyFilters();

        }

        catch (Exception ex)

        {

            ErrorMessage = ex.Message;

            _all = new List<TeamListItem>();

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

        CaptainFilter = string.Empty;

        StatusFilter = 0;

    }



    private void RefreshCounts()

    {

        TotalCount = _all.Count;

        ActiveCount = _all.Count(t => t.IsActive);

        InactiveCount = _all.Count(t => !t.IsActive);

    }



    private void ApplyFilters()

    {

        IEnumerable<TeamListItem> q = _all;



        q = StatusFilter switch

        {

            1 => q.Where(t => t.IsActive),

            2 => q.Where(t => !t.IsActive),

            _ => q

        };



        var name = NameFilter?.Trim();

        var captain = CaptainFilter?.Trim();

        if (!string.IsNullOrWhiteSpace(name))

            q = q.Where(t => t.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(captain))

            q = q.Where(t => (t.CaptainName ?? string.Empty)

                .Contains(captain, StringComparison.OrdinalIgnoreCase));



        Items.Clear();

        foreach (var t in q)

            Items.Add(t);

    }



    [RelayCommand]

    private async Task AddAsync()

    {

        if (!CanWrite)

            return;

        await Shell.Current.GoToAsync(nameof(Pages.TeamEditPage)).ConfigureAwait(true);

    }



    [RelayCommand]

    private async Task ViewAsync(TeamListItem? team)

    {

        if (team is null)

            return;



        await Shell.Current.GoToAsync($"{nameof(Pages.TeamEditPage)}?id={team.Id}&mode=view").ConfigureAwait(true);

    }



    [RelayCommand]

    private async Task EditAsync(TeamListItem? team)

    {

        if (!CanWrite || team is null)

            return;



        await Shell.Current.GoToAsync($"{nameof(Pages.TeamEditPage)}?id={team.Id}").ConfigureAwait(true);

    }



    [RelayCommand]

    private async Task ToggleActiveAsync(TeamListItem? team)

    {

        if (!CanWrite || team is null)

            return;



        var next = !team.IsActive;

        var verb = next ? "Activate" : "Deactivate";

        var ok = await Shell.Current.DisplayAlert(

            $"{verb} team",

            $"{verb} {team.Name}? Inactive teams will not appear in new match dropdowns.",

            verb,

            "Cancel").ConfigureAwait(true);

        if (!ok)

            return;



        try

        {

            await _teams.SetActiveAsync(team.Id, next).ConfigureAwait(true);

            await LoadAsync().ConfigureAwait(true);

        }

        catch (Exception ex)

        {

            ErrorMessage = ex.Message;

        }

    }



    [RelayCommand]

    private async Task DeleteAsync(TeamListItem? team)

    {

        if (!CanWrite || team is null)

            return;



        var ok = await Shell.Current.DisplayAlert(

            "Delete team",

            $"Remove team {team.Name}?",

            "Delete",

            "Cancel").ConfigureAwait(true);



        if (!ok)

            return;



        try

        {

            await _teams.SoftDeleteAsync(team.Id).ConfigureAwait(true);

            await LoadAsync().ConfigureAwait(true);

        }

        catch (Exception ex)

        {

            ErrorMessage = NetworkUi.UserMessage(ex);

            if (NetworkUi.IsNetworkFailure(ex))

                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);

        }

    }

}


