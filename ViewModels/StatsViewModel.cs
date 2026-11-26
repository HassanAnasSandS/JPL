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
    public ExpensesViewModel Expenses { get; }
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
    private int _partnershipLoadGen;
    private int _partnershipDetailsLoadGen;
    private int _partnershipPageBusy;
    private bool _partnershipLoadPending;
    private int _netsAwardsLoadGen;
    private int _netsAwardsPageBusy;
    private bool _netsAwardsLoadPending;

    public ObservableCollection<PlayerCareerSummary> Leaderboard { get; } = new();
    public ObservableCollection<NetSessionAwardsRow> NetSessionAwards { get; } = new();
    public ObservableCollection<TeamCareerSummary> TeamLeaderboard { get; } = new();
    public ObservableCollection<TeamVsStat> TeamVs { get; } = new();
    public ObservableCollection<TeamSquadPlayerRow> TeamSquad { get; } = new();
    public ObservableCollection<ConfrontationStat> Confrontations { get; } = new();
    public ObservableCollection<FieldingVsStat> FieldingVs { get; } = new();
    public ObservableCollection<CaptainVsStat> CaptainVs { get; } = new();
    public ObservableCollection<PartnershipStat> Partnerships { get; } = new();
    public ObservableCollection<PartnershipMatchStat> PartnershipMatchDetails { get; } = new();
    public ObservableCollection<ApiMatchSummaryDto> PlayerMatchHistory { get; } = new();
    public ObservableCollection<PlayerTournamentHistoryItem> PlayerTournamentHistory { get; } = new();
    public ObservableCollection<PlayerSeriesHistoryItem> PlayerSeriesHistory { get; } = new();
    public ObservableCollection<VenueListItem> VenueOptions { get; } = new();
    public IReadOnlyList<string> PlayerRoleOptions { get; } = new[] { "All", "As a Captain", "As a Player" };

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
    [ObservableProperty] private StatsFilterOptionDto? selectedCaptain;
    [ObservableProperty] private string captainPlayerScope = "own";
    [ObservableProperty] private string selectedPlayerRole = "All";
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
    [ObservableProperty] private bool showPartnershipModal;
    [ObservableProperty] private bool isLoadingPartnershipDetails;
    [ObservableProperty] private string? partnershipDetailsError;
    [ObservableProperty] private PartnershipStat? selectedPartnershipPair;
    [ObservableProperty] private bool showPlayerMatchHistory;
    [ObservableProperty] private bool isLoadingPlayerMatchHistory;
    [ObservableProperty] private string? playerMatchHistoryError;
    [ObservableProperty] private string playerMatchHistoryTitle = string.Empty;
    [ObservableProperty] private int bestWinningStreak;
    [ObservableProperty] private int currentWinningStreak;
    [ObservableProperty] private int worstLosingStreak;
    [ObservableProperty] private int currentLosingStreak;
    [ObservableProperty] private int bestTournamentWinningStreak;
    [ObservableProperty] private int currentTournamentWinningStreak;
    [ObservableProperty] private int worstTournamentLosingStreak;
    [ObservableProperty] private int currentTournamentLosingStreak;
    [ObservableProperty] private int playerTournamentPlayed;
    [ObservableProperty] private int playerTournamentWon;
    [ObservableProperty] private int playerTournamentLoss;
    [ObservableProperty] private int playerTournamentTied;
    [ObservableProperty] private int bestSeriesWinningStreak;
    [ObservableProperty] private int currentSeriesWinningStreak;
    [ObservableProperty] private int worstSeriesLosingStreak;
    [ObservableProperty] private int currentSeriesLosingStreak;
    [ObservableProperty] private int playerSeriesPlayed;
    [ObservableProperty] private int playerSeriesWon;
    [ObservableProperty] private int playerSeriesLoss;
    [ObservableProperty] private int playerSeriesTied;
    [ObservableProperty] private bool showPlayerTournamentHistory;
    [ObservableProperty] private bool isLoadingPlayerTournamentHistory;
    [ObservableProperty] private string? playerTournamentHistoryError;
    [ObservableProperty] private string playerTournamentHistoryTitle = string.Empty;
    [ObservableProperty] private bool showPlayerSeriesHistory;
    [ObservableProperty] private bool isLoadingPlayerSeriesHistory;
    [ObservableProperty] private string? playerSeriesHistoryError;
    [ObservableProperty] private string playerSeriesHistoryTitle = string.Empty;

    public bool ShowTable => !ShowDetail;
    public bool ShowLoading => IsBusy || IsSorting;
    public string LoadingMessage => IsSorting ? "Sorting…" : "Loading…";
    public bool HasPartnershipDetailsError => !string.IsNullOrWhiteSpace(PartnershipDetailsError);
    public bool ShowPartnershipDetailsList => !IsLoadingPartnershipDetails && !HasPartnershipDetailsError;
    public string PartnershipDetailsCountText => PartnershipMatchDetails.Count switch
    {
        0 => "No partnership innings",
        1 => "1 partnership innings",
        var count => $"{count} partnership innings"
    };
    public string CountLabel => TotalCount <= 0
        ? "No stats"
        : $"{(IsNetsAwardsStats ? NetSessionAwards.Count : IsPartnershipStats ? Partnerships.Count : IsTeamStats ? TeamLeaderboard.Count : Leaderboard.Count)} of {TotalCount}";
    public bool IsBattingTab => HeadToHeadMode == 0;
    public bool IsBowlingTab => HeadToHeadMode == 1;
    public bool IsFieldingTab => HeadToHeadMode == 2;
    public bool IsCaptainTab => HeadToHeadMode == 3;
    public bool IsPartnershipTab => HeadToHeadMode == 4;
    public bool ShowConfrontationsList => HeadToHeadMode is 0 or 1;
    public bool IsMatchStats => SelectedCategory == 0;
    /// <summary>Match/Tournament/Series use two-line streak headers; keep name + stats bars the same height.</summary>
    public bool HasTallStatsHeader => IsMatchStats || IsTournamentStats || IsSeriesStats;
    public double StatsHeaderBarHeight => HasTallStatsHeader ? 50 : 42;
    /// <summary>Body spacer under the frozen header = bar height + gold underline (2).</summary>
    public double StatsHeaderSpacer => StatsHeaderBarHeight + 2;
    public bool IsTournamentStats => SelectedCategory == 1;
    public bool IsSeriesStats => SelectedCategory == 2;
    public bool IsBattingStats => SelectedCategory == 3;
    public bool IsNetsAwardsStats => IsNetsSource && SelectedCategory == 12;
    public bool IsPartnershipStats => SelectedCategory == 11;
    public bool IsMatchBattingStats => IsBattingStats && IsMatchesSource;
    public bool IsNetsBattingStats => IsBattingStats && IsNetsSource;
    public bool IsBowlingStats => SelectedCategory == 4;
    public bool IsFieldingStats => SelectedCategory == 5;
    public bool IsCaptaincyStats => SelectedCategory == 6;
    public bool IsTeamStats => SelectedCategory == 7;
    public bool IsAttendanceStats => SelectedCategory == 8;
    public bool IsPaymentsStats => SelectedCategory == 10;
    public bool IsAttendanceOrPaymentsStats => IsAttendanceStats || IsPaymentsStats;
    public bool IsExpenseStats => SelectedCategory == 9;
    /// <summary>Teams / Captaincy / Attendance / Expenses use an extra group-header row above column sort buttons.</summary>
    public bool HasGroupedColumnHeader => IsTeamStats || IsCaptaincyStats || IsPartnershipStats;
    public bool HasFlatColumnHeader => !HasGroupedColumnHeader;
    public bool ShowColumnPicker => ShowPlayerLeaderboard || IsPartnershipStats;
    public bool ShowPartnershipGroup => Columns.IsVisible("PartnershipInnings")
        || Columns.IsVisible("PartnershipRuns") || Columns.IsVisible("PartnershipBalls")
        || Columns.IsVisible("PartnershipAverage") || Columns.IsVisible("PartnershipStrikeRate")
        || Columns.IsVisible("PartnershipOuts");
    public bool ShowPlayerContributionGroup => Columns.IsVisible("PlayerRuns")
        || Columns.IsVisible("PlayerBalls") || Columns.IsVisible("PlayerStrikeRate")
        || Columns.IsVisible("PlayerAverage") || Columns.IsVisible("PlayerOuts");
    public bool ShowPartnerContributionGroup => Columns.IsVisible("PartnerRuns")
        || Columns.IsVisible("PartnerBalls") || Columns.IsVisible("PartnerStrikeRate")
        || Columns.IsVisible("PartnerAverage") || Columns.IsVisible("PartnerOuts");
    public bool ShowPlayerDetail => ShowDetail && SelectedCareer is not null && SelectedTeam is null;
    public bool ShowTeamDetail => ShowDetail && SelectedTeam is not null;
    public bool ShowPlayerLeaderboard => !IsTeamStats && !IsExpenseStats && !IsPartnershipStats && !IsNetsAwardsStats;
    public bool ShowMatchCategories => !IsNetsSource;
    public bool ShowMatchSpecificFilters => ShowMatchCategories && !IsExpenseStats;
    public bool ShowAsCaptainFilter => !IsNetsSource && (SelectedCategory is >= 0 and <= 8 or 10);
    public bool ShowCaptainFilter => ShowAsCaptainFilter;
    public bool ShowCaptainPlayerScope => ShowCaptainFilter && SelectedCaptain is { Id: var captainId } && captainId != Guid.Empty;
    public bool CaptainOpponentSide => string.Equals(CaptainPlayerScope, "opposition", StringComparison.Ordinal);
    private bool FilterAsCaptain => string.Equals(SelectedPlayerRole, "As a Captain", StringComparison.Ordinal);
    private bool FilterAsPlayer => string.Equals(SelectedPlayerRole, "As a Player", StringComparison.Ordinal);
    public bool IsMatchesSource => !IsNetsSource;
    public string SessionOrMatchLabel => IsNetsSource ? "Session" : "Match";
    public string SessionOrMatchSortParameter => IsNetsSource ? "Sessions" : "Matches";
    public string FieldingRateLabel => IsNetsSource ? "Per Session" : "Per Match";
    public string FieldingRateSortParameter => IsNetsSource ? "FieldingPerSession" : "FieldingPerMatch";
    public string StatsSourceLabel => IsNetsSource ? "Nets" : "Matches";
    public string ConfrontationSectionTitle => IsNetsSource ? "Nets 1 vs 1" : "Head to head";
    public string FiltersToggleText => ShowFilters
        ? "Hide filters"
        : HasActiveFilters ? "Filters on" : "Filters";
    public string EmptyStatsHint => IsNetsAwardsStats
        ? "No completed nets sessions yet."
        : IsNetsSource
        ? "No nets stats yet — complete a session first."
        : IsPartnershipStats
            ? "No partnership records match these filters."
        : IsExpenseStats
            ? "No expense records yet — add a purchase and player contributions first."
            : IsTeamStats
            ? "No team stats yet — complete a match first."
            : "No stats yet — complete a match first.";
    public string SearchPlaceholder => IsNetsAwardsStats ? "Search session" : IsExpenseStats ? "Filter expense or purchaser" : IsTeamStats ? "Search team" : IsPartnershipStats ? "Search player or pair" : "Search player";
    public string NameColumnTitle => IsTeamStats ? "Team" : IsPartnershipStats ? "Partnership" : "Player";
    public string NameSortParameter => IsTeamStats ? "TeamName" : IsPartnershipStats ? "PartnershipName" : "PlayerName";
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
        || (ShowAsCaptainFilter && !string.Equals(SelectedPlayerRole, "All", StringComparison.Ordinal))
        || (ShowCaptainFilter && SelectedCaptain is { Id: var captainId } && captainId != Guid.Empty)
        || FormatFilter.HasSelection
        || EventScope.HasSelection;

    public StatsViewModel(IStatsService stats, ITeamService teams, IVenueService venues, IFormatService formats, ILeagueContext league, ExpensesViewModel expenses)
    {
        _stats = stats;
        _teams = teams;
        _venues = venues;
        _league = league;
        Expenses = expenses;
        FormatFilter = new FormatFilterViewModel(formats);
        EventScope = new EventScopeFilterViewModel(stats, league);
        SelectedCaptain = EventScope.Captains.FirstOrDefault();
        Columns = new StatsColumnVisibilityViewModel();
        Columns.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowPartnershipGroup));
            OnPropertyChanged(nameof(ShowPlayerContributionGroup));
            OnPropertyChanged(nameof(ShowPartnerContributionGroup));
        };
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
        VenueOptions.Add(new VenueListItem { Id = Guid.Empty, Name = "All" });
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
        if (IsExpenseStats)
        {
            Expenses.NameFilter = value;
            return;
        }
        _ = ReloadDebouncedAsync();
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));

    partial void OnIsLoadingPartnershipDetailsChanged(bool value)
        => OnPropertyChanged(nameof(ShowPartnershipDetailsList));

    partial void OnPartnershipDetailsErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPartnershipDetailsError));
        OnPropertyChanged(nameof(ShowPartnershipDetailsList));
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

    partial void OnSelectedPlayerRoleChanged(string value)
    {
        if (!string.Equals(value, "All", StringComparison.Ordinal) && SelectedCaptain is { Id: var captainId } && captainId != Guid.Empty)
            SelectedCaptain = EventScope.Captains.FirstOrDefault(c => c.Id == Guid.Empty);
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(FiltersToggleText));
    }

    partial void OnSelectedCaptainChanged(StatsFilterOptionDto? value)
    {
        if (value is { Id: var captainId } && captainId != Guid.Empty && !string.Equals(SelectedPlayerRole, "All", StringComparison.Ordinal))
            SelectedPlayerRole = "All";
        OnPropertyChanged(nameof(ShowCaptainPlayerScope));
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
        OnPropertyChanged(nameof(ShowMatchSpecificFilters));
        OnPropertyChanged(nameof(ShowAsCaptainFilter));
        OnPropertyChanged(nameof(ShowCaptainFilter));
        OnPropertyChanged(nameof(IsMatchesSource));
        OnPropertyChanged(nameof(SessionOrMatchLabel));
        OnPropertyChanged(nameof(SessionOrMatchSortParameter));
        OnPropertyChanged(nameof(FieldingRateLabel));
        OnPropertyChanged(nameof(FieldingRateSortParameter));
        OnPropertyChanged(nameof(StatsSourceLabel));
        OnPropertyChanged(nameof(ConfrontationSectionTitle));
        OnPropertyChanged(nameof(EmptyStatsHint));
        OnPropertyChanged(nameof(CareerSubtitle));
        OnPropertyChanged(nameof(IsMatchBattingStats));
        OnPropertyChanged(nameof(IsNetsBattingStats));
        OnPropertyChanged(nameof(IsNetsAwardsStats));
        OnPropertyChanged(nameof(ShowPlayerLeaderboard));
        OnPropertyChanged(nameof(ShowColumnPicker));
        if (value && HeadToHeadMode == 3)
            HeadToHeadMode = 0;
        if (value && SelectedCategory is not (3 or 4 or 5 or 12))
            SelectedCategory = 3;
        else if (!value && SelectedCategory == 12)
            SelectedCategory = 3;
        else
            ApplyCategory();
        if (value)
        {
            SelectedPlayerRole = "All";
            SelectedCaptain = EventScope.Captains.FirstOrDefault(c => c.Id == Guid.Empty);
            EventScope.Clear();
        }
        _ = ReloadAsync();
    }

    partial void OnSelectedCategoryChanged(int oldValue, int newValue)
    {
        if (oldValue == 12 && newValue != 12)
        {
            Interlocked.Increment(ref _netsAwardsLoadGen);
            IsBusy = false;
            IsLoadingMore = false;
        }
        if (oldValue == 11 && newValue != 11)
            Interlocked.Increment(ref _partnershipLoadGen);

        OnPropertyChanged(nameof(IsMatchStats));
        OnPropertyChanged(nameof(HasTallStatsHeader));
        OnPropertyChanged(nameof(StatsHeaderBarHeight));
        OnPropertyChanged(nameof(StatsHeaderSpacer));
        OnPropertyChanged(nameof(IsTournamentStats));
        OnPropertyChanged(nameof(IsSeriesStats));
        OnPropertyChanged(nameof(IsBattingStats));
        OnPropertyChanged(nameof(IsMatchBattingStats));
        OnPropertyChanged(nameof(IsNetsBattingStats));
        OnPropertyChanged(nameof(IsNetsAwardsStats));
        OnPropertyChanged(nameof(IsBowlingStats));
        OnPropertyChanged(nameof(IsFieldingStats));
        OnPropertyChanged(nameof(IsCaptaincyStats));
        OnPropertyChanged(nameof(IsAttendanceStats));
        OnPropertyChanged(nameof(IsPaymentsStats));
        OnPropertyChanged(nameof(IsAttendanceOrPaymentsStats));
        OnPropertyChanged(nameof(IsExpenseStats));
        OnPropertyChanged(nameof(IsPartnershipStats));
        OnPropertyChanged(nameof(ShowPlayerLeaderboard));
        OnPropertyChanged(nameof(ShowColumnPicker));
        OnPropertyChanged(nameof(ShowMatchSpecificFilters));
        OnPropertyChanged(nameof(ShowAsCaptainFilter));
        OnPropertyChanged(nameof(ShowCaptainFilter));
        OnPropertyChanged(nameof(IsTeamStats));
        OnPropertyChanged(nameof(HasGroupedColumnHeader));
        OnPropertyChanged(nameof(HasFlatColumnHeader));
        OnPropertyChanged(nameof(ShowColumnPicker));
        OnPropertyChanged(nameof(ShowPartnershipGroup));
        OnPropertyChanged(nameof(ShowPlayerContributionGroup));
        OnPropertyChanged(nameof(ShowPartnerContributionGroup));
        OnPropertyChanged(nameof(ShowPlayerLeaderboard));
        OnPropertyChanged(nameof(EmptyStatsHint));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(NameColumnTitle));
        OnPropertyChanged(nameof(NameSortParameter));
        OnPropertyChanged(nameof(TableHint));
        ApplyCategory();
        if (!ShowAsCaptainFilter)
        {
            SelectedPlayerRole = "All";
            SelectedCaptain = EventScope.Captains.FirstOrDefault(c => c.Id == Guid.Empty);
        }
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
            "awards" => 12,
            "partnerships" => 11,
            "bowling" => 4,
            "fielding" => 5,
            "captaincy" => 6,
            "teams" => 7,
            "attendance" => 8,
            "expenses" => 9,
            "payments" => 10,
            _ => 0
        };
    }

    private void ApplyCategory()
    {
        switch (SelectedCategory)
        {
            case 1:
                CategoryTitle = "Tournament";
                CategoryHint = "Tournaments played, won, lost, tied, streaks";
                SetDefaultSort("Tournaments");
                break;
            case 2:
                CategoryTitle = "Series";
                CategoryHint = "Series played, won, lost, tied, streaks";
                SetDefaultSort("Series");
                break;
            case 3:
                CategoryTitle = "Batting";
                CategoryHint = IsNetsSource
                    ? "Runs, ducks, golden ducks, sessions, dismissals, partner RO, SR, avg per session, avg per out"
                    : "Runs, highest score, ducks, golden ducks, balls, scoring shots, dismissals, partner RO, SR, average";
                SetDefaultSort("Runs");
                break;
            case 11:
                CategoryTitle = "Partnerships";
                CategoryHint = "Combined pairs, batting contributions and partnership record";
                SetDefaultSort("PartnershipRuns");
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
            case 12:
                CategoryTitle = "Awards";
                CategoryHint = "Player of the Session, Best Batsman, Best Bowler, Best Fielder";
                SetDefaultSort("SessionDateUtc");
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
            case 10:
                CategoryTitle = "Payments";
                CategoryHint = "Total, paid, remaining";
                SetDefaultSort("PaymentTotal");
                break;
            case 9:
                CategoryTitle = "Expenses";
                CategoryHint = "Player who purchased, player contributions, and remaining amount";
                SetDefaultSort("ExpensePurchased");
                break;
            default:
                CategoryTitle = "Match";
                CategoryHint = "Played, won, lost, tied, streaks";
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
        if (IsNetsAwardsStats)
        {
            await ReloadNetsAwardsAsync().ConfigureAwait(true);
            return;
        }
        if (IsPartnershipStats)
        {
            await ReloadPartnershipsAsync().ConfigureAwait(true);
            return;
        }
        if (IsExpenseStats)
        {
            Expenses.NameFilter = PlayerNameFilter;
            await Expenses.LoadAsync().ConfigureAwait(true);
            return;
        }
        HasMore = true;
        Leaderboard.Clear();
        TeamLeaderboard.Clear();
        OnPropertyChanged(nameof(CountLabel));
        await LoadMoreAsync().ConfigureAwait(true);
    }

    private async Task ReloadNetsAwardsAsync()
    {
        Interlocked.Increment(ref _netsAwardsLoadGen);
        NetSessionAwards.Clear();
        TotalCount = 0;
        HasMore = true;
        OnPropertyChanged(nameof(CountLabel));
        await LoadMoreNetsAwardsAsync().ConfigureAwait(true);
    }

    private async Task LoadMoreNetsAwardsAsync()
    {
        if (!IsNetsAwardsStats || !HasMore)
            return;

        if (Interlocked.Exchange(ref _netsAwardsPageBusy, 1) == 1)
        {
            _netsAwardsLoadPending = true;
            return;
        }

        var generation = Volatile.Read(ref _netsAwardsLoadGen);
        try
        {
            _netsAwardsLoadPending = false;
            if (NetSessionAwards.Count == 0)
                IsBusy = true;
            else
                IsLoadingMore = true;
            ErrorMessage = null;

            var page = await _stats.GetNetSessionAwardsPageAsync(NetSessionAwards.Count, PlayerNameFilter)
                .ConfigureAwait(true);
            if (generation != Volatile.Read(ref _netsAwardsLoadGen) || !IsNetsAwardsStats)
                return;

            TotalCount = page.TotalCount;
            foreach (var row in page.Items)
            {
                row.IsAltRow = NetSessionAwards.Count % 2 == 1;
                NetSessionAwards.Add(row);
            }
            HasMore = page.HasMore;
            OnPropertyChanged(nameof(CountLabel));
        }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _netsAwardsLoadGen))
            {
                ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                    ? "Offline — Nets awards need internet."
                    : $"Nets awards require API. {ex.Message}";
                HasMore = false;
            }
        }
        finally
        {
            if (generation == Volatile.Read(ref _netsAwardsLoadGen) || IsNetsAwardsStats)
            {
                IsBusy = false;
                IsLoadingMore = false;
            }
            Interlocked.Exchange(ref _netsAwardsPageBusy, 0);
            var reloadAfterCurrentPage = generation != Volatile.Read(ref _netsAwardsLoadGen)
                && IsNetsAwardsStats && HasMore;
            var loadQueuedPage = _netsAwardsLoadPending && IsNetsAwardsStats && HasMore;
            _netsAwardsLoadPending = false;
            if (reloadAfterCurrentPage || loadQueuedPage)
                await LoadMoreNetsAwardsAsync().ConfigureAwait(true);
        }
    }

    private async Task ReloadPartnershipsAsync()
    {
        Interlocked.Increment(ref _partnershipLoadGen);
        Partnerships.Clear();
        TotalCount = 0;
        HasMore = true;
        OnPropertyChanged(nameof(CountLabel));
        await LoadMorePartnershipsAsync().ConfigureAwait(true);
    }

    private async Task LoadMorePartnershipsAsync()
    {
        if (!IsPartnershipStats || !HasMore)
            return;

        if (Interlocked.Exchange(ref _partnershipPageBusy, 1) == 1)
        {
            _partnershipLoadPending = true;
            return;
        }

        var generation = Volatile.Read(ref _partnershipLoadGen);
        try
        {
            _partnershipLoadPending = false;
            if (Partnerships.Count == 0)
                IsBusy = true;
            else
                IsLoadingMore = true;
            ErrorMessage = null;
            await FormatFilter.EnsureLoadedAsync().ConfigureAwait(true);
            await EventScope.EnsureLoadedAsync().ConfigureAwait(true);
            await EnsureVenuesLoadedForCurrentLeagueAsync().ConfigureAwait(true);

            if (!TryDateRange(out var from, out var toExclusive, out var dateError))
            {
                ErrorMessage = dateError;
                HasMore = false;
                return;
            }

            Guid? venueId = SelectedVenue is { Id: var id } && id != Guid.Empty ? id : null;
            var page = await _stats.GetPartnershipLeaderboardPageAsync(
                    Partnerships.Count,
                    PlayerNameFilter,
                    _sortColumn,
                    _sortAscending,
                    from,
                    toExclusive,
                    venueId,
                    FormatFilter.SelectedIds,
                    EventScope.TournamentIds,
                    EventScope.SeriesIds,
                    EventScope.MatchIds)
                .ConfigureAwait(true);

            if (generation != Volatile.Read(ref _partnershipLoadGen) || !IsPartnershipStats)
                return;

            TotalCount = page.TotalCount;
            foreach (var pair in page.Items)
            {
                pair.IsAltRow = pair.Rank % 2 == 0;
                Partnerships.Add(pair);
            }
            HasMore = page.HasMore;
            OnPropertyChanged(nameof(CountLabel));
            UpdateSortedByText();
        }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _partnershipLoadGen))
            {
                ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                    ? "Offline — partnership stats need internet."
                    : ex.Message;
                HasMore = false;
            }
        }
        finally
        {
            if (generation == Volatile.Read(ref _partnershipLoadGen) || IsPartnershipStats)
            {
                IsBusy = false;
                IsLoadingMore = false;
            }
            Interlocked.Exchange(ref _partnershipPageBusy, 0);
            var reloadAfterCurrentPage = generation != Volatile.Read(ref _partnershipLoadGen)
                && IsPartnershipStats && HasMore;
            var loadQueuedPage = _partnershipLoadPending && IsPartnershipStats && HasMore;
            _partnershipLoadPending = false;
            if (reloadAfterCurrentPage || loadQueuedPage)
                await LoadMorePartnershipsAsync().ConfigureAwait(true);
        }
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
        if (IsPartnershipStats)
        {
            await LoadMorePartnershipsAsync().ConfigureAwait(true);
            return;
        }
        if (IsNetsAwardsStats)
        {
            await LoadMoreNetsAwardsAsync().ConfigureAwait(true);
            return;
        }
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
                    attendanceFromDebut: IsAttendanceStats && AttendanceFromDebut,
                    asCaptain: ShowAsCaptainFilter && FilterAsCaptain,
                    captainPlayerId: ShowCaptainFilter && SelectedCaptain is { Id: var captainId } && captainId != Guid.Empty ? captainId : null,
                    captainOpponentSide: ShowCaptainPlayerScope && CaptainOpponentSide,
                    asPlayer: ShowAsCaptainFilter && FilterAsPlayer)
                .ConfigureAwait(true);
            if (IsTeamStats)
                return;
            TotalCount = board.TotalCount;
            var pageItems = board.Items as IList<PlayerCareerSummary> ?? board.Items.ToList();
            if (IsMatchesSource && IsMatchStats)
                await FillMissingMatchStreaksAsync(pageItems).ConfigureAwait(true);
            else if (IsMatchesSource && IsTournamentStats)
                await FillMissingTournamentStreaksAsync(pageItems).ConfigureAwait(true);
            else if (IsMatchesSource && IsSeriesStats)
                await FillMissingSeriesStreaksAsync(pageItems).ConfigureAwait(true);
            // Hosted API may sort streak columns as all-zeros; re-order after client fill.
            pageItems = ReorderStreakPageIfNeeded(pageItems, skip);
            foreach (var row in pageItems)
                row.IsAltRow = row.Rank % 2 == 0;
            ListPaging.Append(Leaderboard, pageItems, p => p.PlayerId);
            HasMore = board.HasMore;
            OnPropertyChanged(nameof(CountLabel));
            Columns.NotifyLayoutChanged();

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
        SelectedPlayerRole = "All";
        SelectedCaptain = EventScope.Captains.FirstOrDefault(c => c.Id == Guid.Empty);
        CaptainPlayerScope = "own";
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
            VenueOptions.Add(new VenueListItem { Id = Guid.Empty, Name = "All" });
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
                VenueOptions.Add(new VenueListItem { Id = Guid.Empty, Name = "All" });
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

        if (IsPartnershipStats)
        {
            IsSorting = true;
            try
            {
                await ReloadAsync().ConfigureAwait(true);
            }
            finally
            {
                IsSorting = false;
            }
            return;
        }

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
        "PartnershipName" => "Partnership",
        "PartnershipInnings" => "Partnership innings",
        "PartnershipRuns" => "Partnership runs",
        "PartnershipBalls" => "Partnership balls",
        "PartnershipAverage" => "Partnership average",
        "PartnershipStrikeRate" => "Partnership SR",
        "PartnershipOuts" => "Partnership outs",
        "PlayerRuns" => "Player runs",
        "PlayerBalls" => "Player balls",
        "PlayerStrikeRate" => "Player SR",
        "PlayerAverage" => "Player average",
        "PlayerOuts" => "Player outs",
        "PartnerRuns" => "Partner runs",
        "PartnerBalls" => "Partner balls",
        "PartnerStrikeRate" => "Partner SR",
        "PartnerAverage" => "Partner average",
        "PartnerOuts" => "Partner outs",
        "PlayerName" => "Player",
        "TeamName" => "Team",
        "Sessions" => "Sessions",
        "Matches" => "Match",
        "MatchWinPct" => "Win %",
        "BestWinningStreak" => "Best winning streak",
        "CurrentWinningStreak" => "Current winning streak",
        "WorstLosingStreak" => "Worst losing streak",
        "CurrentLosingStreak" => "Current losing streak",
        "BestTournamentWinningStreak" => "Best winning streak",
        "CurrentTournamentWinningStreak" => "Current winning streak",
        "WorstTournamentLosingStreak" => "Worst losing streak",
        "CurrentTournamentLosingStreak" => "Current losing streak",
        "BestSeriesWinningStreak" => "Best winning streak",
        "CurrentSeriesWinningStreak" => "Current winning streak",
        "WorstSeriesLosingStreak" => "Worst losing streak",
        "CurrentSeriesLosingStreak" => "Current losing streak",
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
    private async Task OpenPartnershipDetailsAsync(PartnershipStat? pair)
    {
        if (pair is null || pair.PlayerId == Guid.Empty || pair.PartnerId == Guid.Empty)
            return;

        var generation = Interlocked.Increment(ref _partnershipDetailsLoadGen);
        SelectedPartnershipPair = pair;
        PartnershipMatchDetails.Clear();
        OnPropertyChanged(nameof(PartnershipDetailsCountText));
        PartnershipDetailsError = null;
        ShowPartnershipModal = true;
        IsLoadingPartnershipDetails = true;
        try
        {
            await FormatFilter.EnsureLoadedAsync().ConfigureAwait(true);
            await EventScope.EnsureLoadedAsync().ConfigureAwait(true);
            await EnsureVenuesLoadedForCurrentLeagueAsync().ConfigureAwait(true);

            if (!TryDateRange(out var from, out var toExclusive, out var dateError))
            {
                PartnershipDetailsError = dateError;
                return;
            }

            if (generation != Volatile.Read(ref _partnershipDetailsLoadGen) || !ShowPartnershipModal)
                return;

            Guid? venueId = SelectedVenue is { Id: var id } && id != Guid.Empty ? id : null;
            var details = await _stats.GetPartnershipMatchesAsync(
                    pair.PlayerId,
                    pair.PartnerId,
                    from,
                    toExclusive,
                    venueId,
                    FormatFilter.SelectedIds,
                    EventScope.TournamentIds,
                    EventScope.SeriesIds,
                    EventScope.MatchIds)
                .ConfigureAwait(true);

            if (generation != Volatile.Read(ref _partnershipDetailsLoadGen) || !ShowPartnershipModal)
                return;

            foreach (var item in details)
                PartnershipMatchDetails.Add(item);
            OnPropertyChanged(nameof(PartnershipDetailsCountText));
        }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _partnershipDetailsLoadGen))
                PartnershipDetailsError = NetworkUi.IsNetworkFailure(ex)
                    ? "Offline — partnership details need internet."
                    : ex.Message;
        }
        finally
        {
            if (generation == Volatile.Read(ref _partnershipDetailsLoadGen))
                IsLoadingPartnershipDetails = false;
        }
    }

    [RelayCommand]
    private void ClosePartnershipDetails()
    {
        Interlocked.Increment(ref _partnershipDetailsLoadGen);
        ShowPartnershipModal = false;
        IsLoadingPartnershipDetails = false;
        PartnershipDetailsError = null;
        SelectedPartnershipPair = null;
    }

    [RelayCommand]
    private async Task OpenPlayerAsync(PlayerCareerSummary? row)
    {
        if (row is null) return;
        if (IsSeriesStats && IsMatchesSource)
        {
            PlayerSeriesHistory.Clear();
            ApplySeriesStreaksFromCareer(row);
            PlayerSeriesHistoryTitle = $"{row.PlayerName} · Series";
            PlayerSeriesHistoryError = null;
            ShowPlayerSeriesHistory = true;
            IsLoadingPlayerSeriesHistory = true;
            try
            {
                foreach (var series in await _stats.GetPlayerSeriesHistoryAsync(row.PlayerId).ConfigureAwait(true))
                    PlayerSeriesHistory.Add(series);
            }
            catch (Exception ex)
            {
                PlayerSeriesHistoryError = NetworkUi.IsNetworkFailure(ex)
                    ? "Offline — series history needs internet."
                    : ex.Message;
            }
            finally { IsLoadingPlayerSeriesHistory = false; }
            return;
        }
        if (IsTournamentStats && IsMatchesSource)
        {
            PlayerTournamentHistory.Clear();
            ApplyTournamentStreaksFromCareer(row);
            PlayerTournamentHistoryTitle = $"{row.PlayerName} · Tournaments";
            PlayerTournamentHistoryError = null;
            ShowPlayerTournamentHistory = true;
            IsLoadingPlayerTournamentHistory = true;
            try
            {
                foreach (var tournament in await _stats.GetPlayerTournamentHistoryAsync(row.PlayerId).ConfigureAwait(true))
                    PlayerTournamentHistory.Add(tournament);
            }
            catch (Exception ex)
            {
                PlayerTournamentHistoryError = NetworkUi.IsNetworkFailure(ex)
                    ? "Offline — tournament history needs internet."
                    : ex.Message;
            }
            finally { IsLoadingPlayerTournamentHistory = false; }
            return;
        }
        if (IsMatchStats && IsMatchesSource)
        {
            PlayerMatchHistory.Clear();
            ApplyMatchStreaksFromCareer(row);
            PlayerMatchHistoryTitle = $"{row.PlayerName} · All Matches";
            PlayerMatchHistoryError = null;
            ShowPlayerMatchHistory = true;
            IsLoadingPlayerMatchHistory = true;
            try
            {
                var matches = await _stats.GetPlayerMatchesAsync(row.PlayerId).ConfigureAwait(true);
                foreach (var match in matches)
                    PlayerMatchHistory.Add(match);
            }
            catch (Exception ex)
            {
                PlayerMatchHistoryError = NetworkUi.IsNetworkFailure(ex)
                    ? "Offline — match history needs internet."
                    : ex.Message;
            }
            finally { IsLoadingPlayerMatchHistory = false; }
            return;
        }
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

    public string BestWinningStreakText => MatchStreakText(BestWinningStreak);
    public string CurrentWinningStreakText => MatchStreakText(CurrentWinningStreak);
    public string WorstLosingStreakText => MatchStreakText(WorstLosingStreak);
    public string CurrentLosingStreakText => MatchStreakText(CurrentLosingStreak);
    public string BestTournamentWinningStreakText => TournamentStreakText(BestTournamentWinningStreak);
    public string CurrentTournamentWinningStreakText => TournamentStreakText(CurrentTournamentWinningStreak);
    public string WorstTournamentLosingStreakText => TournamentStreakText(WorstTournamentLosingStreak);
    public string CurrentTournamentLosingStreakText => TournamentStreakText(CurrentTournamentLosingStreak);
    public string BestSeriesWinningStreakText => SeriesStreakText(BestSeriesWinningStreak);
    public string CurrentSeriesWinningStreakText => SeriesStreakText(CurrentSeriesWinningStreak);
    public string WorstSeriesLosingStreakText => SeriesStreakText(WorstSeriesLosingStreak);
    public string CurrentSeriesLosingStreakText => SeriesStreakText(CurrentSeriesLosingStreak);

    partial void OnBestWinningStreakChanged(int value) => OnPropertyChanged(nameof(BestWinningStreakText));
    partial void OnCurrentWinningStreakChanged(int value) => OnPropertyChanged(nameof(CurrentWinningStreakText));
    partial void OnWorstLosingStreakChanged(int value) => OnPropertyChanged(nameof(WorstLosingStreakText));
    partial void OnCurrentLosingStreakChanged(int value) => OnPropertyChanged(nameof(CurrentLosingStreakText));
    partial void OnBestTournamentWinningStreakChanged(int value) => OnPropertyChanged(nameof(BestTournamentWinningStreakText));
    partial void OnCurrentTournamentWinningStreakChanged(int value) => OnPropertyChanged(nameof(CurrentTournamentWinningStreakText));
    partial void OnWorstTournamentLosingStreakChanged(int value) => OnPropertyChanged(nameof(WorstTournamentLosingStreakText));
    partial void OnCurrentTournamentLosingStreakChanged(int value) => OnPropertyChanged(nameof(CurrentTournamentLosingStreakText));
    partial void OnBestSeriesWinningStreakChanged(int value) => OnPropertyChanged(nameof(BestSeriesWinningStreakText));
    partial void OnCurrentSeriesWinningStreakChanged(int value) => OnPropertyChanged(nameof(CurrentSeriesWinningStreakText));
    partial void OnWorstSeriesLosingStreakChanged(int value) => OnPropertyChanged(nameof(WorstSeriesLosingStreakText));
    partial void OnCurrentSeriesLosingStreakChanged(int value) => OnPropertyChanged(nameof(CurrentSeriesLosingStreakText));

    private static string MatchStreakText(int value) => $"{value} match{(value == 1 ? "" : "es")}";
    private static string TournamentStreakText(int value) => $"{value} tournament{(value == 1 ? "" : "s")}";
    private static string SeriesStreakText(int value) => $"{value} series";

    private void ApplyMatchStreaksFromCareer(PlayerCareerSummary row)
    {
        BestWinningStreak = row.BestWinningStreak;
        CurrentWinningStreak = row.CurrentWinningStreak;
        WorstLosingStreak = row.WorstLosingStreak;
        CurrentLosingStreak = row.CurrentLosingStreak;
    }

    private static bool IsStreakSortColumn(string? column) => column is
        "BestWinningStreak" or "CurrentWinningStreak" or "WorstLosingStreak" or "CurrentLosingStreak"
        or "BestTournamentWinningStreak" or "CurrentTournamentWinningStreak"
        or "WorstTournamentLosingStreak" or "CurrentTournamentLosingStreak"
        or "BestSeriesWinningStreak" or "CurrentSeriesWinningStreak"
        or "WorstSeriesLosingStreak" or "CurrentSeriesLosingStreak";

    private static int StreakSortValue(PlayerCareerSummary row, string column) => column switch
    {
        "BestWinningStreak" => row.BestWinningStreak,
        "CurrentWinningStreak" => row.CurrentWinningStreak,
        "WorstLosingStreak" => row.WorstLosingStreak,
        "CurrentLosingStreak" => row.CurrentLosingStreak,
        "BestTournamentWinningStreak" => row.BestTournamentWinningStreak,
        "CurrentTournamentWinningStreak" => row.CurrentTournamentWinningStreak,
        "WorstTournamentLosingStreak" => row.WorstTournamentLosingStreak,
        "CurrentTournamentLosingStreak" => row.CurrentTournamentLosingStreak,
        "BestSeriesWinningStreak" => row.BestSeriesWinningStreak,
        "CurrentSeriesWinningStreak" => row.CurrentSeriesWinningStreak,
        "WorstSeriesLosingStreak" => row.WorstSeriesLosingStreak,
        "CurrentSeriesLosingStreak" => row.CurrentSeriesLosingStreak,
        _ => 0
    };

    private IList<PlayerCareerSummary> ReorderStreakPageIfNeeded(IList<PlayerCareerSummary> items, int skip)
    {
        if (items.Count == 0 || !IsStreakSortColumn(_sortColumn))
        {
            for (var i = 0; i < items.Count; i++)
                items[i].Rank = skip + i + 1;
            return items;
        }

        IEnumerable<PlayerCareerSummary> ordered = _sortAscending
            ? items.OrderBy(r => StreakSortValue(r, _sortColumn)).ThenBy(r => r.PlayerName, StringComparer.OrdinalIgnoreCase)
            : items.OrderByDescending(r => StreakSortValue(r, _sortColumn)).ThenBy(r => r.PlayerName, StringComparer.OrdinalIgnoreCase);
        var list = ordered.ToList();
        for (var i = 0; i < list.Count; i++)
            list[i].Rank = skip + i + 1;
        return list;
    }

    /// <summary>
    /// Hosted API may not expose streak fields yet — fill from history before rows render
    /// so columns are not stuck at 0 until a later sort/reload.
    /// </summary>
    private async Task FillMissingMatchStreaksAsync(IEnumerable<PlayerCareerSummary> rows)
    {
        var missing = rows.Where(StreakStats.NeedsMatchStreakFill).ToList();
        if (missing.Count == 0)
            return;

        using var gate = new SemaphoreSlim(4);
        var tasks = missing.Select(async row =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var matches = await _stats.GetPlayerMatchesAsync(row.PlayerId).ConfigureAwait(false);
                StreakStats.ApplyMatchStreaks(row, matches);
            }
            catch
            {
                /* keep zeros if history is unavailable */
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task FillMissingTournamentStreaksAsync(IEnumerable<PlayerCareerSummary> rows)
    {
        var missing = rows.Where(StreakStats.NeedsTournamentStreakFill).ToList();
        if (missing.Count == 0)
            return;

        using var gate = new SemaphoreSlim(4);
        var tasks = missing.Select(async row =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var history = await _stats.GetPlayerTournamentHistoryAsync(row.PlayerId).ConfigureAwait(false);
                ApplyTournamentStreaksFromHistory(row, history);
            }
            catch
            {
                /* keep zeros if history is unavailable */
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task FillMissingSeriesStreaksAsync(IEnumerable<PlayerCareerSummary> rows)
    {
        var missing = rows.Where(StreakStats.NeedsSeriesStreakFill).ToList();
        if (missing.Count == 0)
            return;

        using var gate = new SemaphoreSlim(4);
        var tasks = missing.Select(async row =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var history = await _stats.GetPlayerSeriesHistoryAsync(row.PlayerId).ConfigureAwait(false);
                ApplySeriesStreaksFromHistory(row, history);
            }
            catch
            {
                /* keep zeros if history is unavailable */
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static void ApplyTournamentStreaksFromHistory(
        PlayerCareerSummary row, IReadOnlyList<PlayerTournamentHistoryItem> history)
    {
        var outcomes = history
            .Select(t =>
            {
                var date = t.Matches.Count == 0
                    ? DateTime.MinValue
                    : t.Matches.Max(m => m.MatchDateUtc);
                var wins = t.Matches.Count(m => StreakStats.MatchOutcome(m) > 0);
                var losses = t.Matches.Count(m => StreakStats.MatchOutcome(m) < 0);
                // Approximate tournament result from that player's match results in the event.
                var outcome = wins == losses ? 0 : wins > losses ? 1 : -1;
                return (date, t.TournamentId, outcome);
            })
            .Where(x => x.date != DateTime.MinValue);
        var (bestWin, currentWin, worstLoss, currentLoss) = StreakStats.Compute(outcomes);
        row.BestTournamentWinningStreak = bestWin;
        row.CurrentTournamentWinningStreak = currentWin;
        row.WorstTournamentLosingStreak = worstLoss;
        row.CurrentTournamentLosingStreak = currentLoss;
    }

    private static void ApplySeriesStreaksFromHistory(
        PlayerCareerSummary row, IReadOnlyList<PlayerSeriesHistoryItem> history)
    {
        var outcomes = history.Select(s =>
        {
            var date = s.Matches.Count == 0
                ? DateTime.MinValue
                : s.Matches.Max(m => m.MatchDateUtc);
            // Series list already has win counts for each side when available.
            var outcome = s.TeamAWins == s.TeamBWins
                ? 0
                : PlayerSeriesOutcome(s);
            return (date, s.SeriesId, outcome);
        }).Where(x => x.date != DateTime.MinValue);
        var (bestWin, currentWin, worstLoss, currentLoss) = StreakStats.Compute(outcomes);
        row.BestSeriesWinningStreak = bestWin;
        row.CurrentSeriesWinningStreak = currentWin;
        row.WorstSeriesLosingStreak = worstLoss;
        row.CurrentSeriesLosingStreak = currentLoss;
    }

    private static int PlayerSeriesOutcome(PlayerSeriesHistoryItem series)
    {
        // Infer the player's side from match (You) labels when possible.
        var sample = series.Matches.FirstOrDefault(m => m.PlayerTeamId is Guid);
        if (sample?.PlayerTeamId is Guid teamId)
        {
            if (teamId == sample.TeamAId)
                return series.TeamAWins == series.TeamBWins ? 0
                    : series.TeamAWins > series.TeamBWins ? 1 : -1;
            if (teamId == sample.TeamBId)
                return series.TeamAWins == series.TeamBWins ? 0
                    : series.TeamBWins > series.TeamAWins ? 1 : -1;
        }

        var wins = series.Matches.Count(m => StreakStats.MatchOutcome(m) > 0);
        var losses = series.Matches.Count(m => StreakStats.MatchOutcome(m) < 0);
        return wins == losses ? 0 : wins > losses ? 1 : -1;
    }

    private void ApplyTournamentStreaksFromCareer(PlayerCareerSummary row)
    {
        BestTournamentWinningStreak = row.BestTournamentWinningStreak;
        CurrentTournamentWinningStreak = row.CurrentTournamentWinningStreak;
        WorstTournamentLosingStreak = row.WorstTournamentLosingStreak;
        CurrentTournamentLosingStreak = row.CurrentTournamentLosingStreak;
        PlayerTournamentPlayed = row.Tournaments;
        PlayerTournamentWon = row.TournamentWon;
        PlayerTournamentLoss = row.TournamentLoss;
        PlayerTournamentTied = row.TournamentTied;
    }

    private void ApplySeriesStreaksFromCareer(PlayerCareerSummary row)
    {
        BestSeriesWinningStreak = row.BestSeriesWinningStreak;
        CurrentSeriesWinningStreak = row.CurrentSeriesWinningStreak;
        WorstSeriesLosingStreak = row.WorstSeriesLosingStreak;
        CurrentSeriesLosingStreak = row.CurrentSeriesLosingStreak;
        PlayerSeriesPlayed = row.Series;
        PlayerSeriesWon = row.SeriesWon;
        PlayerSeriesLoss = row.SeriesLoss;
        PlayerSeriesTied = row.SeriesTied;
    }

    [RelayCommand]
    private async Task OpenNetsAwardPlayerAsync(Guid? playerId)
    {
        if (playerId is not Guid id || id == Guid.Empty)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var career = await _stats.GetCareerAsync(id, "nets").ConfigureAwait(true);
            if (career is null)
            {
                ErrorMessage = "Nets player stats not found.";
                return;
            }

            await OpenPlayerAsync(career).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — Nets player stats need internet."
                : $"Nets player stats require API. {ex.Message}";
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClosePlayerMatchHistory()
    {
        ShowPlayerMatchHistory = false;
        IsLoadingPlayerMatchHistory = false;
        PlayerMatchHistoryError = null;
    }

    [RelayCommand]
    private void ClosePlayerTournamentHistory()
    {
        ShowPlayerTournamentHistory = false;
        IsLoadingPlayerTournamentHistory = false;
        PlayerTournamentHistoryError = null;
    }

    [RelayCommand]
    private void TogglePlayerTournament(PlayerTournamentHistoryItem? tournament)
    {
        if (tournament is not null) tournament.IsExpanded = !tournament.IsExpanded;
    }

    [RelayCommand]
    private void ClosePlayerSeriesHistory()
    {
        ShowPlayerSeriesHistory = false;
        IsLoadingPlayerSeriesHistory = false;
        PlayerSeriesHistoryError = null;
    }

    [RelayCommand]
    private void TogglePlayerSeries(PlayerSeriesHistoryItem? series)
    {
        if (series is not null) series.IsExpanded = !series.IsExpanded;
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
        OnPropertyChanged(nameof(IsPartnershipTab));
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
            Partnerships.Clear();
            if (IsPartnershipTab)
            {
                ConfrontationHeader = $"Partnerships — {SelectedCareer.PlayerName}";
                var partnershipRows = await _stats.GetPartnershipsAsync(playerId).ConfigureAwait(true);
                if (gen != _detailGen) return;
                foreach (var row in partnershipRows) Partnerships.Add(row);
                return;
            }
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
            Partnerships.Clear();
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

    [RelayCommand]
    private void ShowPartnerships() => HeadToHeadMode = 4;

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
