using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

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

    public IReadOnlyList<BattingCardRow> Batting => Card?.Batting ?? Array.Empty<BattingCardRow>();
    public IReadOnlyList<BowlingCardRow> Bowling => Card?.Bowling ?? Array.Empty<BowlingCardRow>();
    public bool HasScorecard => Card is not null;
    public bool ShowPlaceholder => Card is null;

    public NetSessionScorecardViewModel(INetSessionService sessions)
    {
        _sessions = sessions;
    }

    partial void OnCardChanged(NetSessionScorecard? value)
    {
        OnPropertyChanged(nameof(Batting));
        OnPropertyChanged(nameof(Bowling));
        OnPropertyChanged(nameof(HasScorecard));
        OnPropertyChanged(nameof(ShowPlaceholder));
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
}
