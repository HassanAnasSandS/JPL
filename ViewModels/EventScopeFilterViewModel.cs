using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

/// <summary>
/// Exclusive Tournament / Series / Match filter: pick a mode, then multi-select in that mode only.
/// Shared by Statistics, Rankings, Hall of Fame, and Strength Meter.
/// </summary>
public sealed partial class EventScopeFilterViewModel : ObservableObject
{
    private readonly IStatsService _stats;
    private readonly ILeagueContext _league;
    private Guid? _loadedLeagueId;

    public NamedIdMultiSelectFilterViewModel TournamentFilter { get; }
    public NamedIdMultiSelectFilterViewModel SeriesFilter { get; }
    public NamedIdMultiSelectFilterViewModel MatchFilter { get; }

    /// <summary>0=none, 1=tournament, 2=series, 3=matches.</summary>
    [ObservableProperty] private int mode;

    public bool IsModeNone => Mode == 0;
    public bool IsModeTournament => Mode == 1;
    public bool IsModeSeries => Mode == 2;
    public bool IsModeMatch => Mode == 3;
    public bool ShowTournamentDropdown => IsModeTournament;
    public bool ShowSeriesDropdown => IsModeSeries;
    public bool ShowMatchDropdown => IsModeMatch;

    public bool HasSelection =>
        (IsModeTournament && TournamentFilter.HasSelection)
        || (IsModeSeries && SeriesFilter.HasSelection)
        || (IsModeMatch && MatchFilter.HasSelection);

    public IReadOnlyList<Guid>? TournamentIds => IsModeTournament ? TournamentFilter.SelectedIds : null;
    public IReadOnlyList<Guid>? SeriesIds => IsModeSeries ? SeriesFilter.SelectedIds : null;
    public IReadOnlyList<Guid>? MatchIds => IsModeMatch ? MatchFilter.SelectedIds : null;

    public event EventHandler? Changed;

    public EventScopeFilterViewModel(IStatsService stats, ILeagueContext league)
    {
        _stats = stats;
        _league = league;
        TournamentFilter = new NamedIdMultiSelectFilterViewModel(
            "Tournament", "All tournaments", "tournament", "tournaments",
            "Select one or more tournaments");
        SeriesFilter = new NamedIdMultiSelectFilterViewModel(
            "Series", "All series", "series", "series",
            "Select one or more series");
        MatchFilter = new NamedIdMultiSelectFilterViewModel(
            "Match", "All matches", "match", "matches",
            "Select one or more completed matches");

        Wire(TournamentFilter);
        Wire(SeriesFilter);
        Wire(MatchFilter);
    }

    private void Wire(NamedIdMultiSelectFilterViewModel filter)
    {
        filter.Committed += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSelection));
            Changed?.Invoke(this, EventArgs.Empty);
        };
        filter.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NamedIdMultiSelectFilterViewModel.HasSelection) or null)
                OnPropertyChanged(nameof(HasSelection));
        };
    }

    partial void OnModeChanged(int value)
    {
        OnPropertyChanged(nameof(IsModeNone));
        OnPropertyChanged(nameof(IsModeTournament));
        OnPropertyChanged(nameof(IsModeSeries));
        OnPropertyChanged(nameof(IsModeMatch));
        OnPropertyChanged(nameof(ShowTournamentDropdown));
        OnPropertyChanged(nameof(ShowSeriesDropdown));
        OnPropertyChanged(nameof(ShowMatchDropdown));

        if (value != 1)
            TournamentFilter.ClearSelection();
        if (value != 2)
            SeriesFilter.ClearSelection();
        if (value != 3)
            MatchFilter.ClearSelection();

        OnPropertyChanged(nameof(HasSelection));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectMode(string? mode)
    {
        Mode = mode?.Trim().ToLowerInvariant() switch
        {
            "tournament" => 1,
            "series" => 2,
            "match" or "matches" => 3,
            _ => 0
        };
    }

    public void Clear()
    {
        Mode = 0;
        TournamentFilter.ClearSelection();
        SeriesFilter.ClearSelection();
        MatchFilter.ClearSelection();
        OnPropertyChanged(nameof(HasSelection));
    }

    public async Task EnsureLoadedAsync()
    {
        var leagueId = _league.CurrentLeagueId;
        if (_loadedLeagueId == leagueId)
            return;

        Mode = 0;
        TournamentFilter.ClearSelection();
        SeriesFilter.ClearSelection();
        MatchFilter.ClearSelection();
        TournamentFilter.SetOptions(Array.Empty<(Guid, string)>());
        SeriesFilter.SetOptions(Array.Empty<(Guid, string)>());
        MatchFilter.SetOptions(Array.Empty<(Guid, string)>());
        OnPropertyChanged(nameof(HasSelection));

        try
        {
            var options = await _stats.GetFilterOptionsAsync().ConfigureAwait(true);
            TournamentFilter.SetOptions(options.Tournaments.Select(t => (t.Id, t.Name)));
            SeriesFilter.SetOptions(options.Series.Select(s => (s.Id, s.Name)));
            MatchFilter.SetOptions(options.Matches.Select(m => (m.Id, m.Name)));
            _loadedLeagueId = leagueId;
        }
        catch
        {
            /* dropdowns optional */
        }
    }
}
