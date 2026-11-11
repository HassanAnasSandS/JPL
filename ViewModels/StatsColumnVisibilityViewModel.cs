using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JPL.ViewModels;

public sealed partial class StatsColumnOption : ObservableObject
{
    public string Key { get; }
    public string Title { get; }
    public double Width { get; }

    [ObservableProperty] private bool isVisible = true;

    public StatsColumnOption(string key, string title, double width, bool isVisible = true)
    {
        Key = key;
        Title = title;
        Width = width;
        IsVisible = isVisible;
    }
}

/// <summary>
/// Per-category Statistics column show/hide. Persists hidden keys in Preferences.
/// </summary>
public sealed partial class StatsColumnVisibilityViewModel : ObservableObject
{
    private const string PrefPrefix = "stats.cols.hidden.";
    private string _scopeKey = "match";
    private bool _suppress;

    public ObservableCollection<StatsColumnOption> Options { get; } = new();

    [ObservableProperty] private bool isDropdownOpen;
    [ObservableProperty] private string summaryText = "All columns";

    public bool HasHiddenColumns => Options.Any(o => !o.IsVisible);
    public string ToggleText => HasHiddenColumns ? "Columns · Custom" : "Columns";

    public event EventHandler? Changed;

    public bool IsVisible(string key)
    {
        foreach (var opt in Options)
        {
            if (string.Equals(opt.Key, key, StringComparison.Ordinal))
                return opt.IsVisible;
        }
        return true;
    }

    public void ApplyScope(int category, bool isNetsSource)
    {
        _scopeKey = ScopeKey(category, isNetsSource);
        var defs = DefinitionsFor(category, isNetsSource);
        var hidden = LoadHidden(_scopeKey);

        _suppress = true;
        Options.Clear();
        foreach (var (key, title, width) in defs)
        {
            var opt = new StatsColumnOption(key, title, width, isVisible: !hidden.Contains(key));
            opt.PropertyChanged += OnOptionPropertyChanged;
            Options.Add(opt);
        }
        EnsureAtLeastOneVisible();
        _suppress = false;
        RefreshSummary();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleDropdown() => IsDropdownOpen = !IsDropdownOpen;

    [RelayCommand]
    private void CloseDropdown() => IsDropdownOpen = false;

    [RelayCommand]
    private void ShowAll()
    {
        _suppress = true;
        foreach (var opt in Options)
            opt.IsVisible = true;
        _suppress = false;
        Persist();
        RefreshSummary();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        Preferences.Default.Remove(PrefPrefix + _scopeKey);
        var defs = Options.Select(o => (o.Key, o.Title, o.Width)).ToList();
        // Re-apply current scope with empty hidden set
        _suppress = true;
        foreach (var opt in Options)
            opt.IsVisible = true;
        _suppress = false;
        Persist();
        RefreshSummary();
        Changed?.Invoke(this, EventArgs.Empty);
        _ = defs;
    }

    private void OnOptionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppress || e.PropertyName is not (nameof(StatsColumnOption.IsVisible) or null))
            return;
        if (sender is StatsColumnOption opt && !opt.IsVisible && Options.Count(o => o.IsVisible) == 0)
        {
            _suppress = true;
            opt.IsVisible = true;
            _suppress = false;
            return;
        }
        Persist();
        RefreshSummary();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureAtLeastOneVisible()
    {
        if (Options.Count == 0 || Options.Any(o => o.IsVisible))
            return;
        Options[0].IsVisible = true;
    }

    private void Persist()
    {
        var hidden = Options.Where(o => !o.IsVisible).Select(o => o.Key).ToList();
        if (hidden.Count == 0)
            Preferences.Default.Remove(PrefPrefix + _scopeKey);
        else
            Preferences.Default.Set(PrefPrefix + _scopeKey, string.Join(",", hidden));
    }

    private void RefreshSummary()
    {
        var total = Options.Count;
        var shown = Options.Count(o => o.IsVisible);
        SummaryText = shown >= total
            ? "All columns"
            : shown == 0
                ? "No columns"
                : $"{shown} of {total} columns";
        OnPropertyChanged(nameof(HasHiddenColumns));
        OnPropertyChanged(nameof(ToggleText));
    }

    private static HashSet<string> LoadHidden(string scopeKey)
    {
        var raw = Preferences.Default.Get(PrefPrefix + scopeKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return new HashSet<string>(StringComparer.Ordinal);
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string ScopeKey(int category, bool isNetsSource)
        => category switch
        {
            1 => "tournament",
            2 => "series",
            3 => isNetsSource ? "nets-batting" : "batting",
            4 => "bowling",
            5 => "fielding",
            6 => "captaincy",
            7 => "team",
            8 => "attendance",
            _ => "match"
        };

    private static IReadOnlyList<(string Key, string Title, double Width)> DefinitionsFor(
        int category, bool isNetsSource)
        => category switch
        {
            1 =>
            [
                ("Tournaments", "Played", 52),
                ("TournamentWon", "Won", 44),
                ("TournamentLoss", "Lost", 44),
                ("TournamentTied", "Tied", 44),
                ("TournamentWinPct", "Win%", 52)
            ],
            2 =>
            [
                ("Series", "Played", 52),
                ("SeriesWon", "Won", 44),
                ("SeriesLoss", "Lost", 44),
                ("SeriesTied", "Tied", 44),
                ("SeriesWinPct", "Win%", 52)
            ],
            3 when isNetsSource =>
            [
                ("BallsFaced", "Balls", 40),
                ("Runs", "Runs", 40),
                ("HighestScore", "HS", 80),
                ("Fifties", "50", 36),
                ("Hundreds", "100", 40),
                ("Ducks", "Duck", 40),
                ("GoldenDucks", "G.Duck", 48),
                ("Dots", "0", 32),
                ("Ones", "1", 32),
                ("Twos", "2", 32),
                ("Threes", "3", 32),
                ("Fours", "4", 32),
                ("Sixes", "6", 32),
                ("Dismissals", "Out", 36),
                ("NotOuts", "N.O", 40),
                ("BowledOut", "Bld", 36),
                ("CaughtOut", "Cgt", 36),
                ("LbwOut", "LBW", 36),
                ("RunOut", "RO", 36),
                ("PartnerRunOuts", "P.RO", 40),
                ("StumpedOut", "St", 36),
                ("HitWicketOut", "Hit wkt", 56),
                ("BattingStrikeRate", "SR", 48),
                ("AvgPerSession", "Avg/Sess", 72),
                ("AvgPerOut", "Avg/Out", 72),
                ("BoundaryPct", "Bound%", 56)
            ],
            3 =>
            [
                ("Matches", "Match", 48),
                ("InningsBatted", "Inns", 40),
                ("BallsFaced", "Balls", 40),
                ("Runs", "Runs", 40),
                ("HighestScore", "HS", 80),
                ("Fifties", "50", 36),
                ("Hundreds", "100", 40),
                ("Ducks", "Duck", 40),
                ("GoldenDucks", "G.Duck", 48),
                ("Dots", "0", 32),
                ("Ones", "1", 32),
                ("Twos", "2", 32),
                ("Threes", "3", 32),
                ("Fours", "4", 32),
                ("Sixes", "6", 32),
                ("Dismissals", "Out", 36),
                ("NotOuts", "N.O", 40),
                ("BowledOut", "Bld", 36),
                ("CaughtOut", "Cgt", 36),
                ("LbwOut", "LBW", 36),
                ("RunOut", "RO", 36),
                ("PartnerRunOuts", "P.RO", 40),
                ("StumpedOut", "St", 36),
                ("HitWicketOut", "Hit wkt", 56),
                ("BattingStrikeRate", "SR", 44),
                ("BattingAverage", "Avg", 44),
                ("BoundaryPct", "Bound%", 56)
            ],
            4 =>
            [
                ("Matches", "Match", 48),
                ("InningsBowled", "Inns", 40),
                ("OversBowled", "Overs", 48),
                ("OversBowledBalls", "Balls", 40),
                ("RunsConceded", "Runs", 40),
                ("Wickets", "Wkts", 36),
                ("BestBowling", "Best", 48),
                ("FiveWicketHauls", "5W", 36),
                ("Maidens", "Mdn", 40),
                ("HatTricks", "Hat", 40),
                ("DuckWkts", "Duck", 40),
                ("GoldenDuckWkts", "G.Duck", 48),
                ("DotBallsBowled", "Dot", 32),
                ("FoursConceded", "4s", 32),
                ("SixesConceded", "6s", 32),
                ("WidesBowled", "Wd", 36),
                ("NoBallsBowled", "NB", 36),
                ("ByesConceded", "Bye", 32),
                ("LegByesConceded", "LB", 32),
                ("BowledWkts", "Bld", 32),
                ("CaughtWkts", "Cgt", 32),
                ("LbwWkts", "LBW", 32),
                ("StumpedWkts", "St", 32),
                ("HitWicketWkts", "Hit wkt", 56),
                ("RunOutWkts", "RO", 32),
                ("BowlingStrikeRate", "SR", 44),
                ("BowlingAverage", "Avg", 44),
                ("BowlingEconomy", "Econ", 44)
            ],
            5 =>
            [
                ("Matches", "Match", 48),
                ("Catches", "Catches", 64),
                ("RunOutsEffected", "Run outs", 72),
                ("Stumpings", "Stumpings", 72),
                ("FieldingTotal", "Total", 52),
                ("FieldingPerMatch", "Per Match", 64),
                ("DroppedCatches", "Drops", 56)
            ],
            6 =>
            [
                ("CaptainMatches", "Pld", 44),
                ("CaptainWon", "Won", 44),
                ("CaptainLoss", "Lost", 44),
                ("CaptainTied", "Tied", 44),
                ("CaptainWinPct", "Win%", 52),
                ("CaptainTournaments", "Tours", 52),
                ("CaptainTournamentWon", "T.Won", 44),
                ("CaptainTournamentLoss", "T.Lost", 44),
                ("CaptainTournamentTied", "T.Tied", 44),
                ("CaptainTournamentWinPct", "Win%", 52),
                ("CaptainSeries", "Series", 52),
                ("CaptainSeriesWon", "S.Won", 44),
                ("CaptainSeriesLoss", "S.Lost", 44),
                ("CaptainSeriesTied", "S.Tied", 44),
                ("CaptainSeriesWinPct", "Win%", 52),
                ("CaptainPoints", "Pts", 44),
                ("CaptainNrr", "NRR", 56),
                ("CaptainRunsFor", "RF", 48),
                ("CaptainRunsAgainst", "RA", 48)
            ],
            7 =>
            [
                ("Played", "Pld", 44),
                ("Won", "Won", 44),
                ("Lost", "Lost", 44),
                ("Tied", "Tied", 44),
                ("WinPct", "Win%", 52),
                ("Tournaments", "Tours", 52),
                ("TournamentWon", "T.Won", 48),
                ("TournamentLoss", "T.Lost", 48),
                ("TournamentTied", "T.Tied", 48),
                ("TournamentWinPct", "Win%", 52),
                ("Series", "Series", 52),
                ("SeriesWon", "S.Won", 48),
                ("SeriesLoss", "S.Lost", 48),
                ("SeriesTied", "S.Tied", 48),
                ("SeriesWinPct", "Win%", 52),
                ("Points", "Pts", 44),
                ("Nrr", "NRR", 56),
                ("RunsFor", "RF", 48),
                ("RunsAgainst", "RA", 48)
            ],
            8 =>
            [
                ("AttendanceTotal", "Total", 56),
                ("AttendancePresent", "Present", 64),
                ("AttendanceAbsent", "Absent", 64),
                ("AttendancePct", "Att %", 56),
                ("PaymentTotal", "Total", 72),
                ("PaymentPaid", "Paid", 72),
                ("PaymentRemaining", "Remain", 72),
                ("PaymentPaidPct", "Paid%", 56)
            ],
            _ =>
            [
                ("Matches", "Played", 52),
                ("Won", "Won", 44),
                ("Loss", "Lost", 44),
                ("Tied", "Tied", 44),
                ("MatchWinPct", "Win%", 52)
            ]
        };
}
