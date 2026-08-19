using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

[QueryProperty(nameof(MatchId), "id")]
public partial class MatchScorecardViewModel : ObservableObject
{
    private readonly IMatchService _matches;
    private string? _matchId;
    private Guid _loadedMatchId;

    public string? MatchId
    {
        get => _matchId;
        set
        {
            if (_matchId == value)
                return;

            _matchId = value;
            _loadedMatchId = Guid.Empty;
            // QueryProperty can arrive after/before OnAppearing — load as soon as id is known.
            _ = LoadAsync();
        }
    }

    public ObservableCollection<InningsScorecardSection> Innings { get; } = new();

    [ObservableProperty] private string title = "Scorecard";
    [ObservableProperty] private string teamAName = string.Empty;
    [ObservableProperty] private string teamBName = string.Empty;
    [ObservableProperty] private string teamAScore = string.Empty;
    [ObservableProperty] private string teamBScore = string.Empty;
    [ObservableProperty] private string resultText = string.Empty;
    [ObservableProperty] private string metaLine = string.Empty;
    [ObservableProperty] private string tossText = string.Empty;
    [ObservableProperty] private bool teamAWon;
    [ObservableProperty] private bool teamBWon;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool hasScorecard;
    [ObservableProperty] private string playerOfTheMatchText = string.Empty;

    public bool HasTossText => !string.IsNullOrWhiteSpace(TossText);
    public bool HasPlayerOfTheMatch => !string.IsNullOrWhiteSpace(PlayerOfTheMatchText);
    public bool ShowPlaceholder => !HasScorecard;
    public bool ShowIdlePlaceholder => !HasScorecard && !IsBusy;

    public MatchScorecardViewModel(IMatchService matches)
    {
        _matches = matches;
    }

    partial void OnTossTextChanged(string value)
        => OnPropertyChanged(nameof(HasTossText));

    partial void OnHasScorecardChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPlaceholder));
        OnPropertyChanged(nameof(ShowIdlePlaceholder));
    }

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(ShowIdlePlaceholder));

    public Task EnsureLoadedAsync()
    {
        if (HasScorecard || IsBusy)
            return Task.CompletedTask;
        return LoadAsync();
    }

    [RelayCommand]
    public Task RefreshAsync() => LoadCoreAsync(force: true);

    [RelayCommand]
    public Task LoadAsync() => LoadCoreAsync(force: false);

    private async Task LoadCoreAsync(bool force)
    {
        if (!Guid.TryParse(MatchId, out var id))
            return;

        if (IsBusy)
            return;

        if (!force && _loadedMatchId == id && HasScorecard)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var card = await _matches.GetMatchScorecardAsync(id).ConfigureAwait(true);
            if (card is null)
            {
                Innings.Clear();
                HasScorecard = false;
                ErrorMessage = "Scorecard not found on this device.";
                return;
            }

            Title = card.Title;
            TeamAName = card.TeamAName;
            TeamBName = card.TeamBName;
            TeamAScore = card.TeamAScore;
            TeamBScore = card.TeamBScore;
            ResultText = card.ResultText;
            MetaLine = card.MetaLine;
            TossText = card.TossText;
            TeamAWon = card.TeamAWon;
            TeamBWon = card.TeamBWon;
            PlayerOfTheMatchText = card.HasPlayerOfTheMatch
                ? (card.PlayerOfTheMatchName ?? string.Empty)
                : string.Empty;
            OnPropertyChanged(nameof(HasPlayerOfTheMatch));

            ReplaceInnings(card.Innings);
            HasScorecard = true;
            _loadedMatchId = id;

            if (card.Innings.Count == 0)
                ErrorMessage = "No innings detail yet for this match.";
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

    private void ReplaceInnings(IReadOnlyList<InningsScorecardSection> sections)
    {
        Innings.Clear();
        foreach (var inn in sections)
            Innings.Add(inn);
    }
}
