using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;
using System.Collections.ObjectModel;

namespace JPL.ViewModels;

[QueryProperty(nameof(SessionId), "id")]
public partial class NetSessionScorecardViewModel : ObservableObject
{
    private readonly INetSessionService _sessions;

    public string? SessionId { get; set; }

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private NetSessionScorecard? card;
    [ObservableProperty] private string title = "Nets scorecard";
    [ObservableProperty] private string metaLine = string.Empty;
    [ObservableProperty] private string totalText = "0/0";
    [ObservableProperty] private string extrasText = "Extras 0";
    [ObservableProperty] private string oversText = "0.0";
    [ObservableProperty] private bool isPointsListOpen;
    [ObservableProperty] private bool isSelectedPlayerPointsOpen;
    [ObservableProperty] private MatchPointsRow? selectedPlayerPoints;
    [ObservableProperty] private string playerPointsSelectionTitle = "SELECTED PLAYER";
    [ObservableProperty] private string pointsListTitle = "SESSION POINTS";
    [ObservableProperty] private string pointsAwardName = "—";
    [ObservableProperty] private string pointsAwardPoints = "—";
    [ObservableProperty] private string pointsAwardBreakdown = string.Empty;
    [ObservableProperty] private bool isPointsListEmpty;

    public ObservableCollection<MatchPointsRow> AwardPointsRows { get; } = new();

    public IReadOnlyList<BattingCardRow> Batting => Card?.Batting ?? Array.Empty<BattingCardRow>();
    public IReadOnlyList<BattingCardRow> CombinedBatting
    {
        get
        {
            if (Card?.CombinedBatting is { Count: > 0 } combined)
                return combined;
            // Fallback: roll spell rows up if CombinedBatting was not populated (older API / cache).
            if (Card?.Batting is not { Count: > 0 } spells)
                return Array.Empty<BattingCardRow>();
            return spells
                .GroupBy(row => row.PlayerId)
                .Select(group =>
                {
                    var rows = group.ToList();
                    var first = rows[0];
                    var last = rows[^1];
                    var outs = rows.Sum(r => r.Outs);
                    return new BattingCardRow
                    {
                        PlayerId = first.PlayerId,
                        Name = first.Name,
                        Runs = rows.Sum(r => r.Runs),
                        Balls = rows.Sum(r => r.Balls),
                        Fours = rows.Sum(r => r.Fours),
                        Sixes = rows.Sum(r => r.Sixes),
                        Outs = outs,
                        IsOut = last.IsOut,
                        IsOnStrike = last.IsOnStrike,
                        IsCaptain = first.IsCaptain,
                        Dismissal = outs == 0 ? string.Empty : $"{outs} out{(outs == 1 ? string.Empty : "s")}"
                    };
                })
                .ToList();
        }
    }
    public IReadOnlyList<BowlingCardRow> Bowling => Card?.Bowling ?? Array.Empty<BowlingCardRow>();
    public bool HasScorecard => Card is not null;
    public bool HasCombinedBatting => CombinedBatting.Count > 0;
    public bool ShowPlaceholder => Card is null;
    public bool HasSessionAwards => Card?.Deliveries.Count > 0;
    public string PlayerOfSessionName => AwardName(Card?.PlayerOfTheSessionPlayerId);
    public string BestBatsmanName => AwardName(Card?.BestBatsmanPlayerId);
    public string BestBowlerName => AwardName(Card?.BestBowlerPlayerId);
    public string BestFielderName => AwardName(Card?.BestFielderPlayerId);
    public string PlayerOfSessionPoints => AwardPoints(Card?.PlayerOfTheSessionPlayerId, EventAwardPointsCategory.Overall);
    public string BestBatsmanPoints => AwardPoints(Card?.BestBatsmanPlayerId, EventAwardPointsCategory.Batting);
    public string BestBowlerPoints => AwardPoints(Card?.BestBowlerPlayerId, EventAwardPointsCategory.Bowling);
    public string BestFielderPoints => AwardPoints(Card?.BestFielderPlayerId, EventAwardPointsCategory.Fielding);

    public NetSessionScorecardViewModel(INetSessionService sessions)
    {
        _sessions = sessions;
    }

    partial void OnCardChanged(NetSessionScorecard? value)
    {
        OnPropertyChanged(nameof(Batting));
        OnPropertyChanged(nameof(CombinedBatting));
        OnPropertyChanged(nameof(HasCombinedBatting));
        OnPropertyChanged(nameof(Bowling));
        OnPropertyChanged(nameof(HasScorecard));
        OnPropertyChanged(nameof(ShowPlaceholder));
        OnPropertyChanged(nameof(HasSessionAwards));
        OnPropertyChanged(nameof(PlayerOfSessionName));
        OnPropertyChanged(nameof(BestBatsmanName));
        OnPropertyChanged(nameof(BestBowlerName));
        OnPropertyChanged(nameof(BestFielderName));
        OnPropertyChanged(nameof(PlayerOfSessionPoints));
        OnPropertyChanged(nameof(BestBatsmanPoints));
        OnPropertyChanged(nameof(BestBowlerPoints));
        OnPropertyChanged(nameof(BestFielderPoints));
    }

    private string AwardName(Guid? playerId)
    {
        if (playerId is not Guid id)
            return "No award";
        return Card?.Points.FirstOrDefault(p => p.PlayerId == id)?.PlayerName ?? "No award";
    }

    private string AwardPoints(Guid? playerId, EventAwardPointsCategory category)
    {
        if (playerId is not Guid id)
            return "—";
        var row = Card?.Points.FirstOrDefault(p => p.PlayerId == id);
        if (row is null)
            return "—";
        var points = category switch
        {
            EventAwardPointsCategory.Batting => row.BattingPoints,
            EventAwardPointsCategory.Bowling => row.BowlingPoints,
            EventAwardPointsCategory.Fielding => row.FieldingPoints,
            _ => row.TotalPoints
        };
        return $"{points:+#;-#;0} pts";
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (!Guid.TryParse(SessionId, out var id))
        {
            ErrorMessage = "Invalid session.";
            return;
        }

        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            Card = await _sessions.GetScorecardAsync(id).ConfigureAwait(true);
            if (Card is null)
            {
                ErrorMessage = "Session not found.";
                return;
            }

            Title = Card.Name;
            MetaLine = Card.MetaLine;
            TotalText = Card.TotalText;
            ExtrasText = Card.ExtrasText;
            OversText = Card.OversText;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void OpenAwardPoints(EventAwardPointsCategory category)
    {
        if (Card is null)
            return;

        var award = category switch
        {
            EventAwardPointsCategory.Batting => ("BEST BATSMAN", Card.BestBatsmanPlayerId),
            EventAwardPointsCategory.Bowling => ("BEST BOWLER", Card.BestBowlerPlayerId),
            EventAwardPointsCategory.Fielding => ("BEST FIELDER", Card.BestFielderPlayerId),
            _ => ("PLAYER OF THE SESSION", Card.PlayerOfTheSessionPlayerId)
        };
        var winner = award.Item2 is Guid winnerId
            ? Card.Points.FirstOrDefault(p => p.PlayerId == winnerId)
            : null;

        PointsListTitle = award.Item1;
        PointsAwardName = winner?.PlayerName ?? "No award available";
        PointsAwardPoints = winner is null ? "—" : $"{winner.TotalPoints:+#;-#;0} pts";
        PointsAwardBreakdown = winner?.BreakdownText ?? "No player recorded a contribution in this category.";
        AwardPointsRows.Clear();
        var ordered = category switch
        {
            EventAwardPointsCategory.Batting => Card.Points.OrderByDescending(p => p.BattingPoints),
            EventAwardPointsCategory.Bowling => Card.Points.OrderByDescending(p => p.BowlingPoints),
            EventAwardPointsCategory.Fielding => Card.Points.OrderByDescending(p => p.FieldingPoints),
            _ => Card.Points.OrderByDescending(p => p.TotalPoints)
        };
        foreach (var row in ordered
                     .ThenByDescending(p => p.TotalPoints)
                     .ThenBy(p => p.Order))
        {
            row.Rank = AwardPointsRows.Count + 1;
            AwardPointsRows.Add(row);
        }

        IsPointsListEmpty = AwardPointsRows.Count == 0;
        IsPointsListOpen = true;
    }

    [RelayCommand]
    private void CloseAwardPoints()
    {
        CloseSelectedPlayerPoints();
        IsPointsListOpen = false;
        AwardPointsRows.Clear();
    }

    [RelayCommand]
    private void SelectPlayerPoints(MatchPointsRow? row)
    {
        if (row is null)
            return;

        SelectedPlayerPoints = row;
        PlayerPointsSelectionTitle = row.IsPlayerOfTheMatch ? "PLAYER OF THE SESSION" : "SELECTED PLAYER";
        IsSelectedPlayerPointsOpen = true;
    }

    [RelayCommand]
    private void CloseSelectedPlayerPoints()
    {
        IsSelectedPlayerPointsOpen = false;
        SelectedPlayerPoints = null;
    }
}
