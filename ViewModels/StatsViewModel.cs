using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class StatsViewModel : ObservableObject
{
    private readonly IStatsService _stats;
    private readonly IVenueService _venues;
    public FormatFilterViewModel FormatFilter { get; }
    private List<PlayerCareerSummary> _all = new();
    private List<TeamCareerSummary> _allTeams = new();
    private string _sortColumn = "PlayerName";
    private bool _sortAscending = true;
    /// <summary>Shell route to return to when leaving player detail (e.g. "//rankings"). Null = stay on Statistics table.</summary>
    private string? _detailReturnRoute;

    public ObservableCollection<PlayerCareerSummary> Leaderboard { get; } = new();
    public ObservableCollection<TeamCareerSummary> TeamLeaderboard { get; } = new();
    public ObservableCollection<TeamVsStat> TeamVs { get; } = new();
    public ObservableCollection<ConfrontationStat> Confrontations { get; } = new();
    public ObservableCollection<CaptainVsStat> CaptainVs { get; } = new();
    public ObservableCollection<VenueListItem> VenueOptions { get; } = new();

    [ObservableProperty] private PlayerCareerSummary? selectedCareer;
    [ObservableProperty] private TeamCareerSummary? selectedTeam;
    [ObservableProperty] private bool showDetail;
    [ObservableProperty] private int headToHeadMode;
    [ObservableProperty] private bool isBusy;
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

    public bool ShowTable => !ShowDetail;
    public bool ShowLoading => IsBusy || IsSorting;
    public string LoadingMessage => IsSorting ? "Sorting…" : "Loading…";
    public bool IsBattingTab => HeadToHeadMode == 0;
    public bool IsBowlingTab => HeadToHeadMode == 1;
    public bool IsCaptainTab => HeadToHeadMode == 2;
    public bool ShowConfrontationsList => HeadToHeadMode != 2;
    public bool IsMatchStats => SelectedCategory == 0;
    public bool IsTournamentStats => SelectedCategory == 1;
    public bool IsSeriesStats => SelectedCategory == 2;
    public bool IsBattingStats => SelectedCategory == 3;
    public bool IsMatchBattingStats => IsBattingStats && IsMatchesSource;
    public bool IsNetsBattingStats => IsBattingStats && IsNetsSource;
    public bool IsBowlingStats => SelectedCategory == 4;
    public bool IsFieldingStats => SelectedCategory == 5;
    public bool IsCaptaincyStats => SelectedCategory == 6;
    public bool IsAttendanceStats => SelectedCategory == 7;
    public bool IsTeamStats => SelectedCategory == 8;
    public bool ShowPlayerDetail => ShowDetail && SelectedCareer is not null && SelectedTeam is null;
    public bool ShowTeamDetail => ShowDetail && SelectedTeam is not null;
    public bool ShowPlayerLeaderboard => !IsTeamStats;
    public bool ShowMatchCategories => !IsNetsSource;
    public bool IsMatchesSource => !IsNetsSource;
    public string StatsSourceLabel => IsNetsSource ? "Nets" : "Matches";
    public string ConfrontationSectionTitle => IsNetsSource ? "Nets 1 vs 1" : "Head to head";
    public string TableScopeText => $"{StatsSourceLabel}  ·  {CategoryTitle}";
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
                var n = SelectedTeam.Played;
                if (n <= 0)
                    n = SelectedTeam.Series;
                if (n <= 0)
                    return "No completed matches yet";
                return n == 1 ? "Career across 1 match" : $"Career across {n} matches";
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
        || FormatFilter.HasSelection;

    public StatsViewModel(IStatsService stats, IVenueService venues, IFormatService formats)
    {
        _stats = stats;
        _venues = venues;
        FormatFilter = new FormatFilterViewModel(formats);
        FormatFilter.Committed += (_, _) => _ = LoadAsync();
        FormatFilter.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FormatFilterViewModel.HasSelection) or nameof(FormatFilterViewModel.SummaryText) or null)
            {
                OnPropertyChanged(nameof(HasActiveFilters));
                OnPropertyChanged(nameof(FiltersToggleText));
            }
        };
        VenueOptions.Add(new VenueListItem { Id = Guid.Empty, Name = "All venues" });
        SelectedVenue = VenueOptions[0];
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
        ApplySort();
    }

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
        OnPropertyChanged(nameof(TableScopeText));
        OnPropertyChanged(nameof(IsMatchBattingStats));
        OnPropertyChanged(nameof(IsNetsBattingStats));
        if (value && HeadToHeadMode == 2)
            HeadToHeadMode = 0;
        if (value && SelectedCategory is 0 or 1 or 2 or 6 or 7 or 8)
            SelectedCategory = 3;
        else
            ApplyCategory();
        _ = LoadAsync();
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
        OnPropertyChanged(nameof(ShowPlayerLeaderboard));
        OnPropertyChanged(nameof(EmptyStatsHint));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(NameColumnTitle));
        OnPropertyChanged(nameof(NameSortParameter));
        OnPropertyChanged(nameof(TableHint));
        ApplyCategory();
        if ((oldValue == 8 || newValue == 8) && ShowDetail)
        {
            ShowDetail = false;
            SelectedCareer = null;
            SelectedTeam = null;
            Confrontations.Clear();
            CaptainVs.Clear();
            TeamVs.Clear();
        }
        if (oldValue == 8 || newValue == 8)
            _ = LoadAsync();
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
            "attendance" => 7,
            "teams" => 8,
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
                    ? "Runs, sessions, dismissals, SR, avg per session, avg per out"
                    : "Runs, highest score, balls, scoring shots, dismissals, SR, average";
                SetDefaultSort("Runs");
                break;
            case 4:
                CategoryTitle = "Bowling";
                CategoryHint = "Wickets, best figures, hat-tricks, runs conceded, extras, SR, economy";
                SetDefaultSort("Wickets");
                break;
            case 5:
                CategoryTitle = "Fielding";
                CategoryHint = "Catches, run outs, stumpings";
                SetDefaultSort("Catches");
                break;
            case 6:
                CategoryTitle = "Captaincy";
                CategoryHint = "Matches, series, and tournaments as captain";
                SetDefaultSort("CaptainMatches");
                break;
            case 7:
                CategoryTitle = "Attendance";
                CategoryHint = "Present, absent, attendance %";
                SetDefaultSort("AttendancePct");
                break;
            case 8:
                CategoryTitle = "Teams";
                CategoryHint = "Matches, win %, NRR, tournaments, and series";
                SetDefaultSort("Played");
                break;
            default:
                CategoryTitle = "Match";
                CategoryHint = "Played, won, lost, tied";
                SetDefaultSort("Matches");
                break;
        }

        OnPropertyChanged(nameof(TableScopeText));
    }

    private void SetDefaultSort(string column)
    {
        if (string.Equals(_sortColumn, column, StringComparison.Ordinal))
        {
            ApplySort();
            return;
        }

        _sortColumn = column;
        _sortAscending = false;
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortAscending));
        ApplySort();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;

            await FormatFilter.EnsureLoadedAsync().ConfigureAwait(true);

            if (VenueOptions.Count <= 1)
            {
                try
                {
                    var venues = await _venues.GetAllAsync().ConfigureAwait(true);
                    var keep = SelectedVenue?.Id ?? Guid.Empty;
                    VenueOptions.Clear();
                    VenueOptions.Add(new VenueListItem { Id = Guid.Empty, Name = "All venues" });
                    foreach (var v in venues)
                        VenueOptions.Add(v);
                    SelectedVenue = VenueOptions.FirstOrDefault(v => v.Id == keep) ?? VenueOptions[0];
                }
                catch
                {
                    /* venue list optional offline */
                }
            }

            DateTime? from = null;
            DateTime? toExclusive = null;
            if (FilterByDate)
            {
                var fromLocal = FromDate.Date;
                var toLocalExclusive = ToDate.Date.AddDays(1);
                if (fromLocal > ToDate.Date)
                {
                    ErrorMessage = "From date must be on or before To date.";
                    return;
                }
                from = fromLocal;
                toExclusive = toLocalExclusive;
            }

            Guid? venueId = IsNetsSource
                ? null
                : SelectedVenue is { Id: var id } && id != Guid.Empty ? id : null;
            var source = IsNetsSource ? "nets" : "matches";
            var formatIds = IsNetsSource ? null : FormatFilter.SelectedIds;
            var loadTeams = IsTeamStats;
            if (loadTeams)
            {
                var teams = await _stats.GetTeamLeaderboardAsync(from, toExclusive, venueId, formatIds)
                    .ConfigureAwait(true);
                if (!IsTeamStats)
                    return;
                _allTeams = teams.ToList();
                _all.Clear();
                Leaderboard.Clear();
                ApplySort();
                if (ShowDetail && SelectedTeam is not null)
                {
                    var refreshed = _allTeams.FirstOrDefault(t => t.TeamId == SelectedTeam.TeamId);
                    if (refreshed is not null)
                        SelectedTeam = refreshed;
                    await LoadTeamVsAsync().ConfigureAwait(true);
                }
                return;
            }

            var board = await _stats.GetLeaderboardAsync(from, toExclusive, venueId, source, formatIds).ConfigureAwait(true);
            if (IsTeamStats)
                return;
            _all = board.ToList();
            _allTeams.Clear();
            TeamLeaderboard.Clear();
            ApplySort();

            if (ShowDetail && SelectedCareer is not null)
            {
                var refreshed = _all.FirstOrDefault(p => p.PlayerId == SelectedCareer.PlayerId);
                if (refreshed is not null)
                    SelectedCareer = refreshed;
                await LoadPlayerDetailsAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _all.Clear();
            Leaderboard.Clear();
            _allTeams.Clear();
            TeamLeaderboard.Clear();
            Confrontations.Clear();
            CaptainVs.Clear();
            TeamVs.Clear();
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
        ShowFilters = false;
        await LoadAsync().ConfigureAwait(true);
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
            // Let the spinner paint so the tap is visibly acknowledged.
            await Task.Delay(50).ConfigureAwait(true);

            var sortColumn = _sortColumn;
            var ascending = _sortAscending;
            if (IsTeamStats)
            {
                var source = FilteredTeamRows();
                var ordered = await Task.Run(() => BuildSortedTeamRows(source, sortColumn, ascending)).ConfigureAwait(true);
                NumberTeamRows(ordered);
                TeamLeaderboard.Clear();
                foreach (var row in ordered)
                    TeamLeaderboard.Add(row);
            }
            else
            {
                var source = FilteredRows();
                var ordered = await Task.Run(() => BuildSortedRows(source, sortColumn, ascending)).ConfigureAwait(true);
                NumberRows(ordered);
                Leaderboard.Clear();
                foreach (var row in ordered)
                    Leaderboard.Add(row);
            }

            var dir = _sortAscending ? "↑" : "↓";
            SortedByText = $"{DisplayName(_sortColumn)} {dir}  ·  tap a column to sort";
        }
        finally
        {
            IsSorting = false;
        }
    }

    private bool CanSort(string? column) => !IsSorting && !IsBusy && ShowTable;

    private void ApplySort()
    {
        if (IsTeamStats)
        {
            var ordered = BuildSortedTeamRows(FilteredTeamRows(), _sortColumn, _sortAscending);
            NumberTeamRows(ordered);
            TeamLeaderboard.Clear();
            foreach (var row in ordered)
                TeamLeaderboard.Add(row);
        }
        else
        {
            var ordered = BuildSortedRows(FilteredRows(), _sortColumn, _sortAscending);
            NumberRows(ordered);
            Leaderboard.Clear();
            foreach (var row in ordered)
                Leaderboard.Add(row);
        }

        var dir = _sortAscending ? "↑" : "↓";
        SortedByText = $"{DisplayName(_sortColumn)} {dir}  ·  tap a column to sort";
    }

    private static void NumberRows(IReadOnlyList<PlayerCareerSummary> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Rank = i + 1;
            rows[i].IsAltRow = i % 2 == 1;
        }
    }

    private static void NumberTeamRows(IReadOnlyList<TeamCareerSummary> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Rank = i + 1;
            rows[i].IsAltRow = i % 2 == 1;
        }
    }

    private List<PlayerCareerSummary> FilteredRows()
    {
        IEnumerable<PlayerCareerSummary> q = _all;
        if (!string.IsNullOrWhiteSpace(PlayerNameFilter))
        {
            var f = PlayerNameFilter.Trim();
            q = q.Where(p => p.PlayerName.Contains(f, StringComparison.OrdinalIgnoreCase));
        }
        return q.ToList();
    }

    private List<TeamCareerSummary> FilteredTeamRows()
    {
        IEnumerable<TeamCareerSummary> q = _allTeams;
        if (!string.IsNullOrWhiteSpace(PlayerNameFilter))
        {
            var f = PlayerNameFilter.Trim();
            q = q.Where(t => t.TeamName.Contains(f, StringComparison.OrdinalIgnoreCase));
        }
        return q.ToList();
    }

    private static List<PlayerCareerSummary> BuildSortedRows(
        IReadOnlyList<PlayerCareerSummary> source,
        string sortColumn,
        bool ascending)
    {
        IOrderedEnumerable<PlayerCareerSummary> ordered = ascending
            ? source.OrderBy(r => SortKey(r, sortColumn), Comparer<object?>.Create(CompareKeys))
            : source.OrderByDescending(r => SortKey(r, sortColumn), Comparer<object?>.Create(CompareKeys));

        return ordered.ThenBy(r => r.PlayerName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<TeamCareerSummary> BuildSortedTeamRows(
        IReadOnlyList<TeamCareerSummary> source,
        string sortColumn,
        bool ascending)
    {
        IOrderedEnumerable<TeamCareerSummary> ordered = ascending
            ? source.OrderBy(r => TeamSortKey(r, sortColumn), Comparer<object?>.Create(CompareKeys))
            : source.OrderByDescending(r => TeamSortKey(r, sortColumn), Comparer<object?>.Create(CompareKeys));

        return ordered.ThenBy(r => r.TeamName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int CompareKeys(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        if (a is IComparable ca && a.GetType() == b.GetType())
            return ca.CompareTo(b);
        return string.CompareOrdinal(a.ToString(), b.ToString());
    }

    private static object SortKey(PlayerCareerSummary r, string column) => column switch
    {
        "PlayerName" => r.PlayerName,
        "Sessions" => r.Sessions,
        "Matches" => r.Matches,
        "Won" => r.Won,
        "Loss" => r.Loss,
        "Tied" => r.Tied,
        "CaptainMatches" => r.CaptainMatches,
        "CaptainWon" => r.CaptainWon,
        "CaptainLoss" => r.CaptainLoss,
        "CaptainTied" => r.CaptainTied,
        "Tournaments" => r.Tournaments,
        "TournamentWon" => r.TournamentWon,
        "TournamentLoss" => r.TournamentLoss,
        "TournamentTied" => r.TournamentTied,
        "CaptainTournaments" => r.CaptainTournaments,
        "CaptainTournamentWon" => r.CaptainTournamentWon,
        "CaptainTournamentLoss" => r.CaptainTournamentLoss,
        "CaptainTournamentTied" => r.CaptainTournamentTied,
        "Series" => r.Series,
        "SeriesWon" => r.SeriesWon,
        "SeriesLoss" => r.SeriesLoss,
        "SeriesTied" => r.SeriesTied,
        "CaptainSeries" => r.CaptainSeries,
        "CaptainSeriesWon" => r.CaptainSeriesWon,
        "CaptainSeriesLoss" => r.CaptainSeriesLoss,
        "CaptainSeriesTied" => r.CaptainSeriesTied,
        "BallsFaced" => r.BallsFaced,
        "Runs" => r.Runs,
        "Dots" => r.Dots,
        "Ones" => r.Ones,
        "Twos" => r.Twos,
        "Threes" => r.Threes,
        "Fours" => r.Fours,
        "Fives" => r.Fives,
        "Sixes" => r.Sixes,
        "WidesFaced" => r.WidesFaced,
        "NoBallsFaced" => r.NoBallsFaced,
        "Dismissals" => r.Dismissals,
        "BowledOut" => r.BowledOut,
        "CaughtOut" => r.CaughtOut,
        "LbwOut" => r.LbwOut,
        "RunOut" => r.RunOut,
        "StumpedOut" => r.StumpedOut,
        "HitWicketOut" => r.HitWicketOut,
        "BattingStrikeRate" => r.BallsFaced == 0 ? double.MinValue : r.BattingStrikeRate,
        "BattingAverage" => r.BattingAverage,
        "AvgPerSession" => r.Sessions <= 0 ? double.MinValue : r.AvgPerSession,
        "AvgPerOut" => r.Dismissals <= 0 ? double.MinValue : r.AvgPerOut,
        "HighestScore" => r.InningsBatted <= 0
            ? (int.MinValue, 0)
            : (r.HighestScore, r.HighestScoreNotOut ? 1 : 0),
        "OversBowledBalls" => r.OversBowledBalls,
        "RunsConceded" => r.RunsConceded,
        "Wickets" => r.Wickets,
        "BestBowling" => r.BestBowlingText == "-"
            ? (int.MinValue, 0)
            : (r.BestBowlingWickets, -r.BestBowlingRuns),
        "HatTricks" => r.HatTricks,
        "DotBallsBowled" => r.DotBallsBowled,
        "WidesBowled" => r.WidesBowled,
        "NoBallsBowled" => r.NoBallsBowled,
        "ByesConceded" => r.ByesConceded,
        "LegByesConceded" => r.LegByesConceded,
        "BowledWkts" => r.BowledWkts,
        "CaughtWkts" => r.CaughtWkts,
        "LbwWkts" => r.LbwWkts,
        "StumpedWkts" => r.StumpedWkts,
        "HitWicketWkts" => r.HitWicketWkts,
        "RunOutWkts" => r.RunOutWkts,
        "BowlingStrikeRate" => r.Wickets == 0 ? double.MaxValue : r.BowlingStrikeRate,
        "BowlingEconomy" => r.OversBowledBalls == 0 ? double.MaxValue : r.BowlingEconomy,
        "Catches" => r.Catches,
        "RunOutsEffected" => r.RunOutsEffected,
        "Stumpings" => r.Stumpings,
        "AttendanceTotal" => r.AttendanceTotal,
        "AttendancePresent" => r.AttendancePresent,
        "AttendanceAbsent" => r.AttendanceAbsent,
        "AttendancePct" => r.AttendanceTotal == 0 ? double.MinValue : r.AttendancePct,
        _ => r.PlayerName
    };

    private static object TeamSortKey(TeamCareerSummary r, string column) => column switch
    {
        "TeamName" => r.TeamName,
        "Played" => r.Played,
        "Won" => r.Won,
        "Lost" => r.Lost,
        "Tied" => r.Tied,
        "Points" => r.Points,
        "WinPct" => r.Played == 0 ? double.MinValue : r.WinPct,
        "Nrr" => r.Played == 0 ? double.MinValue : r.Nrr,
        "RunsFor" => r.RunsFor,
        "RunsAgainst" => r.RunsAgainst,
        "Tournaments" => r.Tournaments,
        "TournamentWon" => r.TournamentWon,
        "TournamentLoss" => r.TournamentLoss,
        "Series" => r.Series,
        "SeriesWon" => r.SeriesWon,
        "SeriesLoss" => r.SeriesLoss,
        "SeriesTied" => r.SeriesTied,
        _ => r.TeamName
    };

    private static string DisplayName(string column) => column switch
    {
        "PlayerName" => "Player",
        "TeamName" => "Team",
        "Sessions" => "Sessions",
        "Matches" => "Played",
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
        "TournamentLoss" => "Tournament lost",
        "TournamentTied" => "Tournament tied",
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
        "BowledOut" => "Bowled",
        "CaughtOut" => "Caught",
        "LbwOut" => "LBW",
        "RunOut" => "Run out",
        "StumpedOut" => "Stumped",
        "HitWicketOut" => "Hit wicket",
        "BattingStrikeRate" => "Strike rate",
        "BattingAverage" => "Average",
        "AvgPerSession" => "Avg per session",
        "AvgPerOut" => "Avg per out",
        "HighestScore" => "Highest",
        "OversBowledBalls" => "Balls bowled",
        "RunsConceded" => "Runs conceded",
        "Wickets" => "Wickets",
        "BestBowling" => "Best bowling",
        "HatTricks" => "Hat-tricks",
        "DotBallsBowled" => "Dots bowled",
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
        "BowlingEconomy" => "Economy",
        "Catches" => "Catches",
        "RunOutsEffected" => "Run outs",
        "Stumpings" => "Stumpings",
        "AttendanceTotal" => "Total",
        "AttendancePresent" => "Present",
        "AttendanceAbsent" => "Absent",
        "AttendancePct" => "Attendance %",
        _ => column
    };

    [RelayCommand]
    private async Task OpenPlayerAsync(PlayerCareerSummary? row)
    {
        if (row is null) return;
        SelectedTeam = null;
        TeamVs.Clear();
        CaptainVs.Clear();
        SetDetailSource(returnRoute: null);
        SelectedCareer = row;
        ShowDetail = true;
        DetailTitle = row.PlayerName;
        if (HeadToHeadMode == 0)
            await LoadPlayerDetailsAsync().ConfigureAwait(true);
        else
            HeadToHeadMode = 0;
    }

    [RelayCommand]
    private async Task OpenTeamAsync(TeamCareerSummary? row)
    {
        if (row is null) return;
        SelectedCareer = null;
        Confrontations.Clear();
        CaptainVs.Clear();
        SetDetailSource(returnRoute: null);
        SelectedTeam = row;
        ShowDetail = true;
        DetailTitle = row.TeamName;
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
            CaptainVs.Clear();

            if (_all.Count == 0)
            {
                var board = await _stats.GetLeaderboardAsync().ConfigureAwait(true);
                _all = board.ToList();
                ApplySort();
            }

            var career = _all.FirstOrDefault(p => p.PlayerId == playerId)
                         ?? await _stats.GetCareerAsync(playerId).ConfigureAwait(true);
            if (career is null)
            {
                ErrorMessage = "Player stats not found.";
                return;
            }

            if (_all.All(p => p.PlayerId != career.PlayerId))
                _all.Add(career);

            SelectedCareer = career;
            ShowDetail = true;
            DetailTitle = career.PlayerName;
            if (HeadToHeadMode == 0)
                await LoadPlayerDetailsAsync().ConfigureAwait(true);
            else
                HeadToHeadMode = 0;
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
        ShowDetail = false;
        Confrontations.Clear();
        CaptainVs.Clear();
        TeamVs.Clear();
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
        OnPropertyChanged(nameof(IsCaptainTab));
        OnPropertyChanged(nameof(ShowConfrontationsList));
        _ = LoadPlayerDetailsAsync();
    }

    private async Task LoadPlayerDetailsAsync()
    {
        if (SelectedCareer is null || !ShowDetail || SelectedTeam is not null) return;
        try
        {
            Confrontations.Clear();
            CaptainVs.Clear();
            if (IsCaptainTab)
            {
                ConfrontationHeader = $"Captain vs captain — {SelectedCareer.PlayerName}";
                var rows = await _stats.GetCaptainVsAsync(SelectedCareer.PlayerId).ConfigureAwait(true);
                foreach (var r in rows)
                    CaptainVs.Add(r);
                return;
            }
            if (IsBattingTab)
            {
                ConfrontationHeader = IsNetsSource
                    ? $"Nets 1 vs 1 — batting vs bowlers — {SelectedCareer.PlayerName}"
                    : $"Batting vs bowlers — {SelectedCareer.PlayerName}";
                var rows = await _stats.GetBattingVsBowlersAsync(SelectedCareer.PlayerId, StatsSource()).ConfigureAwait(true);
                foreach (var r in rows) Confrontations.Add(r);
            }
            else
            {
                ConfrontationHeader = IsNetsSource
                    ? $"Nets 1 vs 1 — bowling vs batters — {SelectedCareer.PlayerName}"
                    : $"Bowling vs batters — {SelectedCareer.PlayerName}";
                var rows = await _stats.GetBowlingVsBattersAsync(SelectedCareer.PlayerId, StatsSource()).ConfigureAwait(true);
                foreach (var r in rows) Confrontations.Add(r);
            }
        }
        catch (Exception ex)
        {
            Confrontations.Clear();
            CaptainVs.Clear();
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — statistics need internet."
                : $"Stats require API (server DB). {ex.Message}";
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void ShowBatting() => HeadToHeadMode = 0;

    [RelayCommand]
    private void ShowBowling() => HeadToHeadMode = 1;

    [RelayCommand]
    private void ShowCaptainVs() => HeadToHeadMode = 2;

    private async Task LoadTeamVsAsync()
    {
        if (SelectedTeam is null || !ShowDetail) return;
        try
        {
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
                    SelectedTeam.TeamId,
                    from,
                    toExclusive,
                    venueId,
                    FormatFilter.SelectedIds)
                .ConfigureAwait(true);
            if (SelectedTeam is null || !ShowDetail)
                return;
            foreach (var r in rows)
            {
                r.TeamName = SelectedTeam.TeamName;
                TeamVs.Add(r);
            }
        }
        catch (Exception ex)
        {
            TeamVs.Clear();
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — statistics need internet."
                : $"Stats require API (server DB). {ex.Message}";
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
    }

    private string StatsSource() => IsNetsSource ? "nets" : "matches";
}
