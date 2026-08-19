using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using CommunityToolkit.Mvvm.Input;

using JPL.Domain;

using JPL.Services;



namespace JPL.ViewModels;



public partial class PlayersViewModel : ObservableObject

{

    private readonly IPlayerService _players;

    private readonly IAuthService _auth;

    private List<Player> _all = new();



    public ObservableCollection<Player> Items { get; } = new();



    /// <summary>0 = All, 1 = Active, 2 = Deactive</summary>

    [ObservableProperty] private int statusFilter;

    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private string? errorMessage;

    [ObservableProperty] private bool canWrite;

    [ObservableProperty] private string nameFilter = string.Empty;

    [ObservableProperty] private int totalCount;

    [ObservableProperty] private int activeCount;

    [ObservableProperty] private int inactiveCount;



    public bool HasActiveFilters =>

        !string.IsNullOrWhiteSpace(NameFilter) || StatusFilter != 0;



    public bool IsAllFilter => StatusFilter == 0;

    public bool IsActiveFilter => StatusFilter == 1;

    public bool IsInactiveFilter => StatusFilter == 2;



    public string AllFilterLabel => $"All ({TotalCount})";

    public string ActiveFilterLabel => $"Active ({ActiveCount})";

    public string InactiveFilterLabel => $"Deactive ({InactiveCount})";



    public PlayersViewModel(IPlayerService players, IAuthService auth)

    {

        _players = players;

        _auth = auth;

        CanWrite = _auth.CanWrite;

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

    public async Task LoadAsync()

    {

        try

        {

            IsBusy = true;

            ErrorMessage = null;

            CanWrite = _auth.CanWrite;

            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);

            _all = (await _players.GetAllAsync().ConfigureAwait(true)).ToList();
            RefreshCounts();
            ApplyFilters();

        }

        catch (Exception ex)

        {

            ErrorMessage = ex.Message;

            _all = new List<Player>();

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

        ActiveCount = _all.Count(p => p.IsActive);

        InactiveCount = _all.Count(p => !p.IsActive);

    }



    private void ApplyFilters()

    {

        IEnumerable<Player> q = _all;



        q = StatusFilter switch

        {

            1 => q.Where(p => p.IsActive),

            2 => q.Where(p => !p.IsActive),

            _ => q

        };



        var name = NameFilter?.Trim();

        if (!string.IsNullOrWhiteSpace(name))

            q = q.Where(p => p.FullName.Contains(name, StringComparison.OrdinalIgnoreCase));



        Items.Clear();

        foreach (var p in q)

            Items.Add(p);

    }



    [RelayCommand]

    private async Task AddAsync()

    {

        if (!CanWrite)

            return;

        await Shell.Current.GoToAsync(nameof(Pages.PlayerEditPage)).ConfigureAwait(true);

    }



    [RelayCommand]

    private async Task EditAsync(Player? player)

    {

        if (!CanWrite || player is null)

            return;



        await Shell.Current.GoToAsync($"{nameof(Pages.PlayerEditPage)}?id={player.Id}").ConfigureAwait(true);

    }



    [RelayCommand]

    private async Task ToggleActiveAsync(Player? player)

    {

        if (!CanWrite || player is null)

            return;



        var next = !player.IsActive;

        var verb = next ? "Activate" : "Deactivate";

        var ok = await Shell.Current.DisplayAlert(

            $"{verb} player",

            $"{verb} {player.FullName}? Inactive players will not appear when creating or updating teams.",

            verb,

            "Cancel").ConfigureAwait(true);

        if (!ok)

            return;



        try

        {

            await _players.SetActiveAsync(player.Id, next).ConfigureAwait(true);

            await LoadAsync().ConfigureAwait(true);

        }

        catch (Exception ex)

        {

            ErrorMessage = ex.Message;

        }

    }



    [RelayCommand]

    private async Task DeleteAsync(Player? player)

    {

        if (!CanWrite || player is null)

            return;



        var ok = await Shell.Current.DisplayAlert(

            "Delete player",

            $"Remove {player.FullName}?",

            "Delete",

            "Cancel").ConfigureAwait(true);



        if (!ok)

            return;



        try

        {

            await _players.SoftDeleteAsync(player.Id).ConfigureAwait(true);

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


