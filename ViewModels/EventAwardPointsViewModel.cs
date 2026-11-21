using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class EventAwardPointsViewModel : ObservableObject
{
    private readonly IMatchService _matches;
    private readonly string _eventType;
    private IReadOnlyList<MatchPointsRow> _allRows = Array.Empty<MatchPointsRow>();
    private readonly Dictionary<EventAwardPointsCategory, Guid?> _winners = new();
    private EventAwardPointsCategory _currentCategory;
    private int _completedMatchCount;

    public ObservableCollection<MatchPointsRow> Rows { get; } = new();

    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private bool isDetailOpen;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string subtitle = string.Empty;
    [ObservableProperty] private string errorText = string.Empty;
    [ObservableProperty] private string emptyText = "No match points are available yet.";
    [ObservableProperty] private string selectedPointsLabel = string.Empty;
    [ObservableProperty] private string categoryColumnsLabel = "PLAYER · BAT / BOWL / FIELD";
    [ObservableProperty] private MatchPointsRow? selectedPlayer;

    public bool HasRows => Rows.Count > 0;
    public bool ShowEmpty => !IsBusy && !HasRows && string.IsNullOrWhiteSpace(ErrorText);
    public bool ShowError => !IsBusy && !string.IsNullOrWhiteSpace(ErrorText);
    public string RulesText => MatchPointsCalculator.RulesText;

    public EventAwardPointsViewModel(IMatchService matches, string eventType)
    {
        _matches = matches;
        _eventType = eventType;
    }

    public async Task LoadAsync(
        IEnumerable<(Guid? MatchId, string Label, bool HasResult)> fixtures,
        Guid? overallWinner,
        Guid? battingWinner,
        Guid? bowlingWinner,
        Guid? fieldingWinner)
    {
        IsBusy = true;
        ErrorText = string.Empty;
        Rows.Clear();
        _allRows = Array.Empty<MatchPointsRow>();
        _winners[EventAwardPointsCategory.Overall] = overallWinner;
        _winners[EventAwardPointsCategory.Batting] = battingWinner;
        _winners[EventAwardPointsCategory.Bowling] = bowlingWinner;
        _winners[EventAwardPointsCategory.Fielding] = fieldingWinner;
        NotifyListState();

        var completedFixtures = fixtures
            .Where(f => f.MatchId.HasValue && f.HasResult)
            .GroupBy(f => f.MatchId!.Value)
            .Select(g => g.First())
            .ToList();
        _completedMatchCount = completedFixtures.Count;
        var matchPoints = new List<(string MatchTitle, IReadOnlyList<MatchPointsRow> Players)>();
        using var throttler = new SemaphoreSlim(4);
        var tasks = completedFixtures.Select(async (fixture, index) =>
        {
            await throttler.WaitAsync().ConfigureAwait(false);
            try
            {
                var points = await _matches.GetMatchPointsAsync(fixture.MatchId!.Value).ConfigureAwait(false);
                var title = string.IsNullOrWhiteSpace(fixture.Label)
                            || fixture.Label.Equals("Custom", StringComparison.OrdinalIgnoreCase)
                    ? $"Match {index + 1}"
                    : fixture.Label;
                return (Title: title, Players: points, Failed: false);
            }
            catch
            {
                var title = string.IsNullOrWhiteSpace(fixture.Label)
                            || fixture.Label.Equals("Custom", StringComparison.OrdinalIgnoreCase)
                    ? $"Match {index + 1}"
                    : fixture.Label;
                return (Title: title, Players: (IReadOnlyList<MatchPointsRow>)Array.Empty<MatchPointsRow>(), Failed: true);
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        try
        {
            var results = await Task.WhenAll(tasks).ConfigureAwait(true);
            matchPoints.AddRange(results.Select(r => (r.Title, r.Players)));
            var failed = results.Count(r => r.Failed);
            _allRows = EventAwardPointsCalculator.Aggregate(matchPoints, $"{_eventType} total");
            if (failed > 0)
                ErrorText = $"Points could not be loaded for {failed} match{(failed == 1 ? "" : "es")}. Showing available results.";
            if (IsOpen)
                Open(_currentCategory);
        }
        finally
        {
            IsBusy = false;
            NotifyListState();
        }
    }

    [RelayCommand]
    private void Open(EventAwardPointsCategory category)
    {
        _currentCategory = category;
        var winnerId = _winners.GetValueOrDefault(category);
        var points = EventAwardPointsCalculator.ForCategory(_allRows, category, winnerId);
        Rows.Clear();
        foreach (var row in points) Rows.Add(row);

        var categoryName = category switch
        {
            EventAwardPointsCategory.Batting => "Best Batsman",
            EventAwardPointsCategory.Bowling => "Best Bowler",
            EventAwardPointsCategory.Fielding => "Best Fielder",
            _ => $"Player of the {_eventType}"
        };
        Title = $"{categoryName} · Points";
        CategoryColumnsLabel = category switch
        {
            EventAwardPointsCategory.Batting => "PLAYER · BATTING",
            EventAwardPointsCategory.Bowling => "PLAYER · BOWLING",
            EventAwardPointsCategory.Fielding => "PLAYER · FIELDING",
            _ => "PLAYER · BAT / BOWL / FIELD"
        };
        Subtitle = category == EventAwardPointsCategory.Overall
            ? $"Batting + bowling + fielding · {CountMatches()} completed matches"
            : $"{categoryName} points · {CountMatches()} completed matches";
        SelectedPointsLabel = category == EventAwardPointsCategory.Overall
            ? $"TOTAL {_eventType.ToUpperInvariant()} POINTS"
            : $"{categoryName.ToUpperInvariant()} POINTS";
        EmptyText = "No player points are available for this award yet.";
        SelectedPlayer = null;
        IsDetailOpen = false;
        IsOpen = true;
        NotifyListState();
    }

    [RelayCommand]
    private void SelectPlayer(MatchPointsRow? row)
    {
        if (row is null) return;
        SelectedPlayer = row;
        IsDetailOpen = true;
    }

    [RelayCommand]
    private void CloseDetail()
    {
        IsDetailOpen = false;
        SelectedPlayer = null;
    }

    [RelayCommand]
    private void Close()
    {
        IsDetailOpen = false;
        IsOpen = false;
        SelectedPlayer = null;
        Rows.Clear();
        NotifyListState();
    }

    private int CountMatches()
        => _completedMatchCount;

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowError));
    }
}
