using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class StatsViewModel : ObservableObject
{
    private readonly IStatsService _stats;
    private readonly ITeamService _teams;
    private readonly IVenueService _venues;
    private readonly ILeagueContext _league;
    public FormatFilterViewModel FormatFilter { get; }
    public EventScopeFilterViewModel EventScope { get; }
    public StatsColumnVisibilityViewModel Columns { get; }
    private Guid? _venuesLeagueId;
    private string _sortColumn = "PlayerName";
    private bool _sortAscending = true;
    /// <summary>Shell route to return to when leaving player detail (e.g. "//rankings"). Null = stay on Statistics table.</summary>
    private string? _detailReturnRoute;
    private int _loadBusy;
    private int _filterGen;
    private int _detailGen;

    public ObservableCollection<PlayerCareerSummary> Leaderboard { get; } = new();
    public ObservableCollection<TeamCareerSummary> TeamLeaderboard { get; } = new();
    public ObservableCollection<TeamVsStat> TeamVs { get; } = new();
    public ObservableCollection<TeamSquadPlayerRow> TeamSquad { get; } = new();
    public ObservableCollection<ConfrontationStat> Confrontations { get; } = new();
    public ObservableCollection<FieldingVsStat> FieldingVs { get; } = new();
    public ObservableCollection<CaptainVsStat> CaptainVs { get; } = new();
    public ObservableCollection<VenueListItem> VenueOptions { get; } = new();

    [ObservableProperty] private PlayerCareerSummary? selectedCareer;
    [ObservableProperty] private TeamCareerSummary? selectedTeam;
    [ObservableProperty] private bool showDetail;
    [ObservableProperty] private int headToHeadMode;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private bool isSorting;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string confrontationHeader = "Batting vs bowlers";
    [ObservableProperty] private string detailTitle = "Player details";
    [ObservableProperty] private string sortedByText = "Player ↑  ·  tap a column to sort";
    [ObservableProperty] private string backButtonText = "\u2190 Back to leaderboard";
    [ObservableProperty] private string playerNameFilter = string.Empty;
    [ObservableProperty] private VenueListItem? selectedVenue;
    [ObservableProperty] private bool filterByDate;
    [ObservableProperty] private DateTime fromDate = DateTime.Today.AddMonths(-1);
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private int selectedCategory;
    [ObservableProperty] private string categoryTitle = "Match";
    [ObservableProperty] private string categoryHint = "Played, won, lost, tied";
    [ObservableProperty] private bool isNetsSource;
    [ObservableProperty] private bool showFilters;
    /// <summary>Attendance only: Total/Absent counted from each player's debut date.</summary>
    [ObservableProperty] private bool attendanceFromDebut;

    public bool ShowTable => !ShowDetail;
    public bool ShowLoading => IsBusy || IsSorting;
    public string LoadingMessage => IsSorting ? "Sorting…" : "Loading…";
    public string CountLabel => TotalCount <= 0 ? "No stats" : $"{(IsTeamStats ? TeamLeaderboard.Count : Leaderboard.Count)} of {TotalCount}";
    public bool IsBattingTab => HeadToHeadMode == 0;
    public bool IsBowlingTab => HeadToHeadMode == 1;
    public bool IsFieldingTab => HeadToHeadMode == 2;
    public bool IsCaptainTab => HeadToHeadMode == 3;
    public bool ShowConfrontationsList => HeadToHeadMode is 0 or 1;
    public bool IsMatchStats => SelectedCategory == 0;
    public bool IsTournamentStats => SelectedCategory == 1;
    public bool IsSeriesStats => SelectedCategory == 2;
    public bool IsBattingStats => SelectedCategory == 3;
    public bool IsMatchBattingStats => IsBattingStats && IsMatchesSource;
    public bool IsNetsBattingStats => IsBattingStats && IsNetsSource;
    public bool IsBowlingStats => SelectedCategory == 4;
    public bool IsFieldingStats => SelectedCategory == 5;
    public bool IsCaptaincyStats => SelectedCategory == 6;
    public bool IsTeamStats => SelectedCategory == 7;
    public bool IsAttendanceStats => SelectedCategory == 8;
    /// <summary>Teams / Captaincy / Attendance use an extra group-header row above column sort buttons.</summary>
    public bool HasGroupedColumnHeader => IsTeamStats || IsCaptaincyStats || IsAttendanceStats;
    public bool HasFlatColumnHeader => !HasGroupedColumnHeader;
    public bool ShowPlayerDetail => ShowDetail && SelectedCareer is not null && SelectedTeam is null;
    public bool ShowTeamDetail => ShowDetail && SelectedTeam is not null;
    public bool ShowPlayerLeaderboard => !IsTeamStats;
    public bool ShowMatchCategories => !IsNetsSource;
    public bool IsMatchesSource => !IsNetsSource;
    public string StatsSourceLabel => IsNetsSource ? "Nets" : "Matches";
    public string ConfrontationSectionTitle => IsNetsSource ? "Nets 1 vs 1" : "Head to head";
    public string FiltersToggleText => ShowFilters
        ? "Hide filters"
        : HasActiveFilters ? "Filters on" : "Filters";
    public string EmptyStatsHint => IsNetsSource
        ? "No nets stats yet — complete a session first."
        : IsTeamStats
            ? "No team stats yet — complete a match first."
            : "No stats yet — complete a match first.";
    public string SearchPlaceholder => IsTeamStats ? "Search team" : "Search player";
    public string NameColumnTitle => IsTeamStats ? "Team" : "Player";
    public string NameSortParameter => IsTeamStats ? "TeamName" : "PlayerName";
    public string TableHint => IsTeamStats
        ? "Tap a team for 1 vs 1 stats"
        : "Tap a player for 1 vs 1 stats";
    public string DetailBadgeText => ShowTeamDetail ? "Team" : StatsSourceLabel;
    public string CareerSubtitle
    {
        get
        {
            if (SelectedTeam is not null)
            {
                var cap = SelectedTeam.HasCaptain ? $"Captain: {SelectedTeam.CaptainName}" : "No captain";
                var n = SelectedTeam.Played;
                if (n <= 0)
                    n = SelectedTeam.Series;
                if (n <= 0)
                    return cap;
                var career = n == 1 ? "Career across 1 match" : $"Career across {n} matches";
                return $"{cap}  ·  {career}";
            }
            if (SelectedCareer is null)
                return string.Empty;
            if (IsNetsSource)
            {
                var n = SelectedCareer.Sessions;
                if (n <= 0)
                    n = SelectedCareer.InningsBatted;
                return n == 1 ? "Career across 1 session" : $"Career across {n} sessions";
            }
            return $"Career across {SelectedCareer.Matches} matches";
        }
    }
    public string SortColumn => _sortColumn;
    public bool SortAscending => _sortAscending;
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(PlayerNameFilter)
        || (SelectedVenue is not null && SelectedVenue.Id != Guid.Empty)
        || FilterByDate
        || FormatFilter.HasSelection
        || EventScope.HasSelection;

    public StatsViewModel(IStatsService stats, ITeamService teams, IVenueService venues, IFormatService formats, ILeagueContext league)
    {
        _stats = stats;
        _teams = teams;
        _venues = venues;
        _league = league;
        FormatFilter = new FormatFilterViewModel(formats);
        EventScope = new EventScopeFilterViewModel(stats, league);
        Columns = new StatsColumnVisibilityViewModel();
        FormatFilter.Committed += (_, _) => _ = LoadAsync();
        FormatFilter.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FormatFilterViewModel.HasSelection) or nameof(FormatFilterViewModel.SummaryText) or null)
            {
                OnPropertyChanged(nameof(HasActiveFilters));
                OnPropertyChanged(nameof(FiltersToggleText));
            }
        };
        EventScope.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(HasActiveFilters));
            OnPropertyChanged(nameof(FiltersToggleText));
            _ = LoadAsync();
        };
        EventScope.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EventScopeFilterViewModel.HasSelection) or null)
            {
                OnPropertyChanged(nameof(HasActiveFilters));
                OnPropertyChanged(nameof(FiltersToggleText));
            }
        };
        VenueOptions.Add(new VenueListItem { Id = Guid.Empty, Name = "All venues" });
        SelectedVenue = VenueOptions[0];
        ApplyCategory();
    }

    partial void OnShowDetailChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowTable));
        OnPropertyChanged(nameof(ShowPlayerDetail));
        OnPropertyChanged(nameof(ShowTeamDetail));
        OnPropertyChanged(nameof(DetailBadgeText));
        SortCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTeamChanged(TeamCareerSummary? value)
    {
        OnPropertyChanged(nameof(ShowPlayerDetail));
        OnPropertyChanged(nameof(ShowTeamDetail));
        OnPropertyChanged(nameof(CareerSubtitle));
        OnPropertyChanged(nameof(DetailBadgeText));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(LoadingMessage));
        SortCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSortingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(LoadingMessage));
        SortCommand.NotifyCanExecuteChanged();
    }

    partial void OnPlayerNameFilterChanged(string value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(FiltersToggleText));
        _ = ReloadDebouncedAsync();
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));

    partial void OnSelectedVenueChanged(VenueListItem? value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(FiltersToggleText));
    }

    partial void OnFilterByDateChanged(bool value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(FiltersToggleText));
    }

    partial void OnShowFiltersChanged(bool value)
        => OnPropertyChanged(nameof(FiltersToggleText));

    partial void OnSelectedCareerChanged(PlayerCareerSummary? value)
    {
        OnPropertyChanged(nameof(CareerSubtitle));
        OnPropertyChanged(nameof(ShowPlayerDetail));
        OnPropertyChanged(nameof(ShowTeamDetail));
        OnPropertyChanged(nameof(DetailBadgeText));
    }

    partial void OnIsNetsSourceChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowMatchCategories));
        OnPropertyChanged(nameof(IsMatchesSource));
        OnPropertyChanged(nameof(StatsSourceLabel));
        OnPropertyChanged(nameof(ConfrontationSectionTitle));
        OnPropertyChanged(nameof(EmptyStatsHint));
        OnPropertyChanged(nameof(CareerSubtitle));
        OnPropertyChanged(nameof(IsMatchBattingStats));
        OnPropertyChanged(nameof(IsNetsBattingStats));
        if (value && HeadToHeadMode == 3)
            HeadToHeadMode = 0;
        if (value && SelectedCategory is 0 or 1 or 2 or 6 or 7 or 8)
            SelectedCategory = 3;
        else
            ApplyCategory();
        if (value)
            EventScope.Clear();
        _ = ReloadAsync();
    }

    partial void OnSelectedCategoryChanged(int oldValue, int newValue)
    {
        OnPropertyChanged(nameof(IsMatchStats));
        OnPropertyChanged(nameof(IsTournamentStats));
        OnPropertyChanged(nameof(IsSeriesStats));
        OnPropertyChanged(nameof(IsBattingStats));
        OnPropertyChanged(nameof(IsMatchBattingStats));
        OnPropertyChanged(nameof(IsNetsBattingStats));
        OnPropertyChanged(nameof(IsBowlingStats));
        OnPropertyChanged(nameof(IsFieldingStats));
        OnPropertyChanged(nameof(IsCaptaincyStats));
        OnPropertyChanged(nameof(IsAttendanceStats));
        OnPropertyChanged(nameof(IsTeamStats));
        OnPropertyChanged(nameof(HasGroupedColumnHeader));
        OnPropertyChanged(nameof(HasFlatColumnHeader));
        OnPropertyChanged(nameof(ShowPlayerLeaderboard));
        OnPropertyChanged(nameof(EmptyStatsHint));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(NameColumnTitle));
        OnPropertyChanged(nameof(NameSortParameter));
        OnPropertyChanged(nameof(TableHint));
        ApplyCategory();
        if ((oldValue == 7 || newValue == 7) && ShowDetail)
        {
            ShowDetail = false;
            SelectedCareer = null;
            SelectedTeam = null;
            Confrontations.Clear();
            FieldingVs.Clear();
            CaptainVs.Clear();
            TeamVs.Clear();
            TeamSquad.Clear();
        }
        if (newValue != 8 && AttendanceFromDebut)
            AttendanceFromDebut = false;
        _ = ReloadAsync();
    }

    partial void OnAttendanceFromDebutChanged(bool value)
    {
        if (IsAttendanceStats)
            _ = ReloadAsync();
    }

    [RelayCommand]
    private void ToggleFilters() => ShowFilters = !ShowFilters;

    [RelayCommand]
    private void SelectSource(string? source)
        => IsNetsSource = string.Equals(source, "nets", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void SelectCategory(string? category)
    {
        SelectedCategory = category switch
        {
            "tournament" => 1,
            "series" => 2,
            "batting" => 3,
            "bowling" => 4,
            "fielding" => 5,
            "captaincy" => 6,
            "teams" => 7,
            "attendance" => 8,
            _ => 0
        };
    }

    private void ApplyCategory()
    {
        switch (SelectedCategory)
        {
            case 1:
                CategoryTitle = "Tournament";
                CategoryHint = "Tournaments played, won, lost, tied";
                SetDefaultSort("Tournaments");
                break;
            case 2:
                CategoryTitle = "Series";
                CategoryHint = "Series played, won, lost, tied";
                SetDefaultSort("Series");
                break;
            case 3:
                CategoryTitle = "Batting";
                CategoryHint = IsNetsSource
                    ? "Runs, ducks, golden ducks, sessions, dismissals, partner RO, SR, avg per session, avg per out"
                    : "Runs, highest score, ducks, golden ducks, balls, scoring shots, dismissals, partner RO, SR, average";
                SetDefaultSort("Runs");
                break;
            case 4:
                CategoryTitle = "Bowling";
                CategoryHint = "Wickets, best figures, hat-tricks, duck / golden duck wickets, 4s/6s conceded, runs, extras, SR, economy";
                SetDefaultSort("Wickets");
                break;
            case 5:
                CategoryTitle = "Fielding";
                CategoryHint = "Catches, run outs, stumpings, dropped catches";
                SetDefaultSort("Catches");
                break;
            case 6:
                CategoryTitle = "Captaincy";
                CategoryHint = "Matches, series, and tournaments as captain";
                SetDefaultSort("CaptainMatches");
                break;
            case 7:
                CategoryTitle = "Teams";
                CategoryHint = "Matches, win %, NRR, tournaments, and series";
                SetDefaultSort("Played");
                break;
            case 8:
                CategoryTitle = "Attendance";
                CategoryHint = "Present, absent · total, paid, remaining";
                SetDefaultSort("AttendancePct");
                break;
            default:
                CategoryTitle = "Match";
                CategoryHint = "Played, won, lost, tied";
                SetDefaultSort("Matches");
                break;
        }

        Columns.ApplyScope(SelectedCategory, IsNetsSource);
    }

    private void SetDefaultSort(string column)
    {
        if (string.Equals(_sortColumn, column, StringComparison.Ordinal))
            return;

        _sortColumn = column;
        _sortAscending = false;
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortAscending));
    }

    [RelayCommand]
    public Task LoadAsync() => ReloadAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (ShowDetail)
            return;
        HasMore = true;
        Leaderboard.Clear();
        TeamLeaderboard.Clear();
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
        if (ShowDetail)
            return;
        if (!HasMore || Interlocked.Exchange(ref _loadBusy, 1) == 1)
            return;

        try
        {
            var existing = IsTeamStats ? TeamLeaderboard.Count : Leaderboard.Count;
            if (existing == 0)
                IsBusy = true;
            else
                IsLoadingMore = true;
            ErrorMessage = null;

            await FormatFilter.EnsureLoadedAsync().ConfigureAwait(true);
            if (!IsNetsSource)
                await EventScope.EnsureLoadedAsync().ConfigureAwait(true);

            await EnsureVenuesLoadedForCurrentLeagueAsync().ConfigureAwait(true);

            if (!TryDateRange(out var from, out var toExclusive, out var dateError))
            {
                ErrorMessage = dateError;
                return;
            }

            Guid? venueId = IsNetsSource
                ? null
                : SelectedVenue is { Id: var id } && id != Guid.Empty ? id : null;
            var source = IsNetsSource ? "nets" : "matches";
            var formatIds = IsNetsSource ? null : FormatFilter.SelectedIds;
            var tournamentIds = IsNetsSource ? null : EventScope.TournamentIds;
            var seriesIds = IsNetsSource ? null : EventScope.SeriesIds;
            var matchIds = IsNetsSource ? null : EventScope.MatchIds;

            var skip = IsTeamStats ? TeamLeaderboard.Count : Leaderboard.Count;

            if (IsTeamStats)
            {
                var page = await _stats.GetTeamLeaderboardPageAsync(
                        skip, PlayerNameFilter, _sortColumn, _sortAscending, from, toExclusive, venueId, formatIds,
                        tournamentIds, seriesIds, matchIds)
                    .ConfigureAwait(true);
                if (!IsTeamStats)
                    return;
                TotalCount = page.TotalCount;
                foreach (var row in page.Items)
                    row.IsAltRow = row.Rank % 2 == 0;
                ListPaging.Append(TeamLeaderboard, page.Items, t => t.TeamId);
                HasMore = page.HasMore;
                OnPropertyChanged(nameof(CountLabel));
                UpdateSortedByText();
                return;
            }

            var board = await _stats.GetLeaderboardPageAsync(
                    skip, PlayerNameFilter, _sortColumn, _sortAscending, from, toExclusive, venueId, source, formatIds,
                    tournamentIds, seriesIds, matchIds,
                    attendanceFromDebut: IsAttendanceStats && AttendanceFromDebut)
                .ConfigureAwait(true);
            if (IsTeamStats)
                return;
            TotalCount = board.TotalCount;
            foreach (var row in board.Items)
                row.IsAltRow = row.Rank % 2 == 0;
            ListPaging.Append(Leaderboard, board.Items, p => p.PlayerId);
            HasMore = board.HasMore;
            OnPropertyChanged(nameof(CountLabel));

            UpdateSortedByText();
        }
        catch (Exception ex)
        {
            if ((IsTeamStats ? TeamLeaderboard.Count : Leaderboard.Count) == 0)
            {
                Leaderboard.Clear();
                TeamLeaderboard.Clear();
                TotalCount = 0;
                Confrontations.Clear();
                FieldingVs.Clear();
                CaptainVs.Clear();
                TeamVs.Clear();
                TeamSquad.Clear();
            }
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — statistics need internet."
                : $"Stats require API (server DB). {ex.Message}";
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
        finally
        {
            if (!ShowDetail)
                IsBusy = false;
            IsLoadingMore = false;
            Interlocked.Exchange(ref _loadBusy, 0);
        }
    }

    private bool TryDateRange(out DateTime? from, out DateTime? toExclusive, out string? error)
    {
        from = null;
        toExclusive = null;
        error = null;
        if (!FilterByDate)
            return true;
        var fromLocal = FromDate.Date;
        if (fromLocal > ToDate.Date)
        {
            error = "From date must be on or before To date.";
            return false;
        }
        from = fromLocal;
        toExclusive = ToDate.Date.AddDays(1);
        return true;
    }

    private void UpdateSortedByText()
    {
        var dir = _sortAscending ? "↑" : "↓";
        SortedByText = $"{DisplayName(_sortColumn)} {dir}  ·  tap a column to sort";
    }

    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        ShowFilters = false;
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        PlayerNameFilter = string.Empty;
        FilterByDate = false;
        FromDate = DateTime.Today.AddMonths(-1);
        ToDate = DateTime.Today;
        SelectedVenue = VenueOptions.FirstOrDefault(v => v.Id == Guid.Empty) ?? VenueOptions.FirstOrDefault();
        FormatFilter.ClearSelection();
        EventScope.Clear();
        ShowFilters = false;
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task EnsureVenuesLoadedForCurrentLeagueAsync()
    {
        var leagueId = _league.CurrentLeagueId;
        if (_venuesLeagueId == leagueId && VenueOptions.Count > 1)
            return;

        try
        {
            var venues = await _venues.GetAllAsync().ConfigureAwait(true);
            var keep = _venuesLeagueId == leagueId ? SelectedVenue?.Id ?? Guid.Empty : Guid.Empty;
            VenueOptions.Clear();
            VenueOptions.Add(new VenueListItem { Id = Guid.Empty, Name = "All venues" });
            foreach (var v in venues)
                VenueOptions.Add(v);
            SelectedVenue = VenueOptions.FirstOrDefault(v => v.Id == keep) ?? VenueOptions[0];
            _venuesLeagueId = leagueId;
        }
        catch
        {
            /* venue list optional offline */
            if (VenueOptions.Count == 0)
            {
                VenueOptions.Add(new VenueListItem { Id = Guid.Empty, Name = "All venues" });
                SelectedVenue = VenueOptions[0];
            }
        }
    }

    /// <summary>Tap column header: first click sorts desc for numbers / asc for name; tap again toggles.</summary>
    [RelayCommand(CanExecute = nameof(CanSort))]
    private async Task Sort(string? column)
    {
        if (string.IsNullOrWhiteSpace(column) || !ShowTable)
            return;

        if (column is "PlayerName" or "TeamName")
            column = NameSortParameter;

        if (string.Equals(_sortColumn, column, StringComparison.Ordinal))
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = column;
            // Name: A→Z first; numeric stats: high→low first.
            _sortAscending = column is "PlayerName" or "TeamName";
        }

        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortAscending));

        IsSorting = true;
        try
        {
            await ReloadAsync().ConfigureAwait(true);
        }
        finally
        {
            IsSorting = false;
        }
    }

    private bool CanSort(string? column) => !IsSorting && !IsBusy && ShowTable;

    private static string DisplayName(string column) => column switch
    {
        "PlayerName" => "Player",
        "TeamName" => "Team",
        "Sessions" => "Sessions",
        "Matches" => "Match",
        "MatchWinPct" => "Win %",
        "TournamentWinPct" => "Win %",
        "SeriesWinPct" => "Win %",
        "CaptainWinPct" => "Win %",
        "CaptainTournamentWinPct" => "Tour win %",
        "CaptainSeriesWinPct" => "Series win %",
        "CaptainPoints" => "Points",
        "CaptainNrr" => "NRR",
        "CaptainRunsFor" => "Runs for",
        "CaptainRunsAgainst" => "Runs against",
        "Played" => "Played",
        "Won" => "Won",
        "Lost" => "Lost",
        "Loss" => "Lost",
        "Tied" => "Tied",
        "Points" => "Points",
        "WinPct" => "Win %",
        "Nrr" => "NRR",
        "RunsFor" => "Runs for",
        "RunsAgainst" => "Runs against",
        "CaptainMatches" => "Captain played",
        "CaptainWon" => "Captain won",
        "CaptainLoss" => "Captain lost",
        "CaptainTied" => "Captain tied",
        "Tournaments" => "Tournaments",
        "TournamentWon" => "Titles",
        "TournamentLoss" => "Tour lost",
        "TournamentTied" => "Tour tied",
        "CaptainTournaments" => "Captain tournaments",
        "CaptainTournamentWon" => "Captain titles",
        "CaptainTournamentLoss" => "Captain tour lost",
        "CaptainTournamentTied" => "Captain tour tied",
        "Series" => "Series",
        "SeriesWon" => "Series won",
        "SeriesLoss" => "Series lost",
        "SeriesTied" => "Series tied",
        "CaptainSeries" => "Captain series",
        "CaptainSeriesWon" => "Captain series won",
        "CaptainSeriesLoss" => "Captain series lost",
        "CaptainSeriesTied" => "Captain series tied",
        "InningsBatted" => "Innings",
        "InningsBowled" => "Innings",
        "BallsFaced" => "Balls faced",
        "Runs" => "Runs",
        "Dots" => "Dots",
        "Ones" => "Ones",
        "Twos" => "Twos",
        "Threes" => "Threes",
        "Fours" => "Fours",
        "Fives" => "Fives",
        "Sixes" => "Sixes",
        "WidesFaced" => "Wides faced",
        "NoBallsFaced" => "No-balls faced",
        "Dismissals" => "Dismissals",
        "NotOuts" => "Not out",
        "Fifties" => "Fifties",
        "Hundreds" => "Hundreds",
        "BoundaryPct" => "Boundary %",
        "BowledOut" => "Bowled",
        "CaughtOut" => "Caught",
        "LbwOut" => "LBW",
        "RunOut" => "Run out",
        "PartnerRunOuts" => "Partner RO",
        "StumpedOut" => "Stumped",
        "HitWicketOut" => "Hit wicket",
        "BattingStrikeRate" => "Strike rate",
        "BattingAverage" => "Average",
        "AvgPerSession" => "Avg per session",
        "AvgPerOut" => "Avg per out",
        "HighestScore" => "Highest",
        "Ducks" => "Ducks",
        "GoldenDucks" => "Golden ducks",
        "OversBowled" => "Overs",
        "OversBowledBalls" => "Balls bowled",
        "RunsConceded" => "Runs conceded",
        "Wickets" => "Wickets",
        "BestBowling" => "Best bowling",
        "FiveWicketHauls" => "5 wickets",
        "Maidens" => "Maidens",
        "HatTricks" => "Hat-tricks",
        "DuckWkts" => "Duck wickets",
        "GoldenDuckWkts" => "Golden duck wickets",
        "DotBallsBowled" => "Dots bowled",
        "FoursConceded" => "4s conceded",
        "SixesConceded" => "6s conceded",
        "WidesBowled" => "Wides",
        "NoBallsBowled" => "No-balls",
        "ByesConceded" => "Byes",
        "LegByesConceded" => "Leg byes",
        "BowledWkts" => "Bowled wickets",
        "CaughtWkts" => "Caught wickets",
        "LbwWkts" => "LBW wickets",
        "StumpedWkts" => "Stumped wickets",
        "HitWicketWkts" => "Hit-wicket wickets",
        "RunOutWkts" => "Run-out wickets",
        "BowlingStrikeRate" => "Bowl SR",
        "BowlingAverage" => "Bowl avg",
        "BowlingEconomy" => "Economy",
        "Catches" => "Catches",
        "RunOutsEffected" => "Run outs",
        "Stumpings" => "Stumpings",
        "FieldingTotal" => "Total",
        "FieldingPerMatch" => "Per match",
        "DroppedCatches" => "Dropped catches",
        "AttendanceTotal" => "Total",
        "AttendancePresent" => "Present",
        "AttendanceAbsent" => "Absent",
        "AttendancePct" => "Attendance %",
        "PaymentTotal" => "Payment total",
        "PaymentPaid" => "Paid",
        "PaymentRemaining" => "Remaining",
        "PaymentPaidPct" => "Paid %",
        _ => column
    };

    [RelayCommand]
    private async Task OpenPlayerAsync(PlayerCareerSummary? row)
    {
        if (row is null) return;
        SelectedTeam = null;
        TeamVs.Clear();
        TeamSquad.Clear();
        FieldingVs.Clear();
        CaptainVs.Clear();
        SetDetailSource(returnRoute: null);
        SelectedCareer = row;
        DetailTitle = row.PlayerName;
        if (HeadToHeadMode != 0)
            HeadToHeadMode = 0;
        ShowDetail = true;
        await LoadPlayerDetailsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenTeamAsync(TeamCareerSummary? row)
    {
        if (row is null) return;
        SelectedCareer = null;
        Confrontations.Clear();
        FieldingVs.Clear();
        CaptainVs.Clear();
        TeamSquad.Clear();
        SetDetailSource(returnRoute: null);
        SelectedTeam = row;
        ShowDetail = true;
        DetailTitle = row.TeamName;
        await LoadTeamSquadAsync(row.TeamId).ConfigureAwait(true);
        await LoadTeamVsAsync().ConfigureAwait(true);
    }

    /// <summary>Open the same career detail UI used from the Statistics table (e.g. from Rankings).</summary>
    /// <param name="returnRoute">Shell route when Back is pressed, e.g. "//rankings". Null keeps Statistics table.</param>
    public async Task OpenPlayerByIdAsync(Guid playerId, string? returnRoute = null)
    {
        if (playerId == Guid.Empty)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            SetDetailSource(returnRoute);

            SelectedTeam = null;
            TeamVs.Clear();
            TeamSquad.Clear();
            FieldingVs.Clear();
            CaptainVs.Clear();

            var career = Leaderboard.FirstOrDefault(p => p.PlayerId == playerId)
                         ?? await _stats.GetCareerAsync(playerId).ConfigureAwait(true);
            if (career is null)
            {
                ErrorMessage = "Player stats not found.";
                return;
            }

            SelectedCareer = career;
            DetailTitle = career.PlayerName;
            if (HeadToHeadMode != 0)
                HeadToHeadMode = 0;
            ShowDetail = true;
            await LoadPlayerDetailsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — statistics need internet."
                : $"Stats require API (server DB). {ex.Message}";
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetDetailSource(string? returnRoute)
    {
        _detailReturnRoute = returnRoute;
        BackButtonText = string.Equals(returnRoute, "//rankings", StringComparison.OrdinalIgnoreCase)
            ? "\u2190 Back to rankings"
            : "\u2190 Back to leaderboard";
    }

    [RelayCommand]
    private async Task BackToTableAsync()
    {
        Interlocked.Increment(ref _detailGen);
        IsBusy = false;
        ShowDetail = false;
        Confrontations.Clear();
        FieldingVs.Clear();
        CaptainVs.Clear();
        TeamVs.Clear();
        TeamSquad.Clear();
        SelectedCareer = null;
        SelectedTeam = null;

        var route = _detailReturnRoute;
        _detailReturnRoute = null;
        BackButtonText = "\u2190 Back to leaderboard";

        if (!string.IsNullOrWhiteSpace(route) && Shell.Current is not null)
            await Shell.Current.GoToAsync(route).ConfigureAwait(true);
    }

    partial void OnHeadToHeadModeChanged(int value)
    {
        OnPropertyChanged(nameof(IsBattingTab));
        OnPropertyChanged(nameof(IsBowlingTab));
        OnPropertyChanged(nameof(IsFieldingTab));
        OnPropertyChanged(nameof(IsCaptainTab));
        OnPropertyChanged(nameof(ShowConfrontationsList));
        if (ShowDetail)
            _ = LoadPlayerDetailsAsync();
    }

    private async Task LoadPlayerDetailsAsync()
    {
        if (SelectedCareer is null || !ShowDetail || SelectedTeam is not null) return;
        var gen = Interlocked.Increment(ref _detailGen);
        var playerId = SelectedCareer.PlayerId;
        var batting = IsBattingTab;
        var fielding = IsFieldingTab;
        var captain = IsCaptainTab;
        var nets = IsNetsSource;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            Confrontations.Clear();
            FieldingVs.Clear();
            CaptainVs.Clear();
            if (captain)
            {
                ConfrontationHeader = $"Captain vs captain — {SelectedCareer.PlayerName}";
                var rows = await _stats.GetCaptainVsAsync(playerId).ConfigureAwait(true);
                if (gen != _detailGen) return;
                foreach (var r in rows)
                    CaptainVs.Add(r);
                return;
            }
            if (fielding)
            {
                ConfrontationHeader = nets
                    ? $"Nets 1 vs 1 — fielding vs batters — {SelectedCareer.PlayerName}"
                    : $"Fielding vs batters — {SelectedCareer.PlayerName}";
                var rows = await _stats.GetFieldingVsAsync(playerId, nets ? "nets" : "matches").ConfigureAwait(true);
                if (gen != _detailGen) return;
                foreach (var r in rows)
                    FieldingVs.Add(r);
                return;
            }
            if (batting)
            {
                ConfrontationHeader = nets
                    ? $"Nets 1 vs 1 — batting vs bowlers — {SelectedCareer.PlayerName}"
                    : $"Batting vs bowlers — {SelectedCareer.PlayerName}";
                var rows = await _stats.GetBattingVsBowlersAsync(playerId, nets ? "nets" : "matches").ConfigureAwait(true);
                if (gen != _detailGen) return;
                foreach (var r in rows) Confrontations.Add(r);
            }
            else
            {
                ConfrontationHeader = nets
                    ? $"Nets 1 vs 1 — bowling vs batters — {SelectedCareer.PlayerName}"
                    : $"Bowling vs batters — {SelectedCareer.PlayerName}";
                var rows = await _stats.GetBowlingVsBattersAsync(playerId, nets ? "nets" : "matches").ConfigureAwait(true);
                if (gen != _detailGen) return;
                foreach (var r in rows) Confrontations.Add(r);
            }
        }
        catch (Exception ex)
        {
            if (gen != _detailGen) return;
            Confrontations.Clear();
            FieldingVs.Clear();
            CaptainVs.Clear();
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — statistics need internet."
                : $"Stats require API (server DB). {ex.Message}";
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
        finally
        {
            if (gen == _detailGen)
                IsBusy = false;
        }
    }

    [RelayCommand]
    private void ShowBatting() => HeadToHeadMode = 0;

    [RelayCommand]
    private void ShowBowling() => HeadToHeadMode = 1;

    [RelayCommand]
    private void ShowFieldingVs() => HeadToHeadMode = 2;

    [RelayCommand]
    private void ShowCaptainVs() => HeadToHeadMode = 3;

    private async Task LoadTeamSquadAsync(Guid teamId)
    {
        TeamSquad.Clear();
        try
        {
            var detail = await _teams.GetAsync(teamId).ConfigureAwait(true);
            if (SelectedTeam is null || SelectedTeam.TeamId != teamId || !ShowDetail || detail is null)
                return;

            if (!SelectedTeam.HasCaptain && detail.CaptainPlayerId is Guid capId)
            {
                var cap = detail.Players.FirstOrDefault(p => p.Id == capId);
                if (cap is not null)
                {
                    SelectedTeam.CaptainPlayerId = capId;
                    SelectedTeam.CaptainName = cap.FullName;
                    OnPropertyChanged(nameof(CareerSubtitle));
                }
            }

            foreach (var p in detail.Players
                         .OrderByDescending(p => detail.CaptainPlayerId == p.Id)
                         .ThenBy(p => p.FullName, StringComparer.OrdinalIgnoreCase))
            {
                TeamSquad.Add(new TeamSquadPlayerRow
                {
                    PlayerId = p.Id,
                    PlayerName = p.FullName,
                    IsCaptain = detail.CaptainPlayerId == p.Id
                });
            }
        }
        catch
        {
            // Squad is optional — keep detail usable without it.
        }
    }

    private async Task LoadTeamVsAsync()
    {
        if (SelectedTeam is null || !ShowDetail) return;
        var gen = Interlocked.Increment(ref _detailGen);
        var teamId = SelectedTeam.TeamId;
        var teamName = SelectedTeam.TeamName;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            TeamVs.Clear();
            DateTime? from = null;
            DateTime? toExclusive = null;
            if (FilterByDate)
            {
                from = FromDate.Date;
                toExclusive = ToDate.Date.AddDays(1);
            }
            Guid? venueId = SelectedVenue is { Id: var id } && id != Guid.Empty ? id : null;
            var rows = await _stats.GetTeamVsAsync(
                    teamId,
                    from,
                    toExclusive,
                    venueId,
                    FormatFilter.SelectedIds,
                    EventScope.TournamentIds,
                    EventScope.SeriesIds,
                    EventScope.MatchIds)
                .ConfigureAwait(true);
            if (gen != _detailGen || SelectedTeam is null || !ShowDetail)
                return;
            foreach (var r in rows)
            {
                r.TeamName = teamName;
                TeamVs.Add(r);
            }
        }
        catch (Exception ex)
        {
            if (gen != _detailGen) return;
            TeamVs.Clear();
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — statistics need internet."
                : $"Stats require API (server DB). {ex.Message}";
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
        finally
        {
            if (gen == _detailGen)
                IsBusy = false;
        }
    }

    private string StatsSource() => IsNetsSource ? "nets" : "matches";
}
