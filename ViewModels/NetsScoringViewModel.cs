using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

[QueryProperty(nameof(SessionId), "id")]
public partial class NetsScoringViewModel : ObservableObject
{
    private readonly INetSessionService _sessions;
    private readonly IAuthService _auth;
    private readonly IScoringFeedback _feedback;

    public string? SessionId { get; set; }

    public ObservableCollection<SquadPlayer> Players { get; } = new();
    public ObservableCollection<BattingCardRow> BattingCard { get; } = new();
    public ObservableCollection<BowlingCardRow> BowlingCard { get; } = new();
    public ObservableCollection<SelectOption<ExtraType>> ExtraOptions { get; } = new();
    public ObservableCollection<SelectOption<WicketType>> WicketOptions { get; } = new();

    [ObservableProperty] private SquadPlayer? batter;
    [ObservableProperty] private SquadPlayer? bowler;
    [ObservableProperty] private SquadPlayer? fielder;
    [ObservableProperty] private SelectOption<ExtraType>? selectedExtraOption;
    [ObservableProperty] private SelectOption<WicketType>? selectedWicketOption;
    [ObservableProperty] private string scoreText = "0/0 (0.0)";
    [ObservableProperty] private string sessionName = string.Empty;
    [ObservableProperty] private string duelText = "Select batter & bowler";
    [ObservableProperty] private string extrasText = "Extras 0";
    [ObservableProperty] private string scorecardTotalText = "0/0";
    [ObservableProperty] private string scorecardHeaderText = "Nets session";
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isWicket;
    [ObservableProperty] private bool isDroppedCatch;
    [ObservableProperty] private bool needsFielder;
    private bool _suppressFieldingToggle;

    public bool CanScoreBalls => CanWrite && !IsBusy && Batter is not null && Bowler is not null;

    private Guid _sessionId;

    public NetsScoringViewModel(INetSessionService sessions, IAuthService auth, IScoringFeedback feedback)
    {
        _sessions = sessions;
        _auth = auth;
        _feedback = feedback;
        CanWrite = _auth.CanWrite;

        ExtraOptions.Add(new SelectOption<ExtraType> { Label = "None", Value = ExtraType.None });
        ExtraOptions.Add(new SelectOption<ExtraType> { Label = "Wide", Value = ExtraType.Wide });
        ExtraOptions.Add(new SelectOption<ExtraType> { Label = "No Ball", Value = ExtraType.NoBall });
        ExtraOptions.Add(new SelectOption<ExtraType> { Label = "Bye", Value = ExtraType.Bye });
        ExtraOptions.Add(new SelectOption<ExtraType> { Label = "Leg Bye", Value = ExtraType.LegBye });
        SelectedExtraOption = ExtraOptions[0];

        WicketOptions.Add(new SelectOption<WicketType> { Label = "Bowled", Value = WicketType.Bowled });
        WicketOptions.Add(new SelectOption<WicketType> { Label = "Caught", Value = WicketType.Caught });
        WicketOptions.Add(new SelectOption<WicketType> { Label = "LBW", Value = WicketType.Lbw });
        WicketOptions.Add(new SelectOption<WicketType> { Label = "Run Out", Value = WicketType.RunOut });
        WicketOptions.Add(new SelectOption<WicketType> { Label = "Stumped", Value = WicketType.Stumped });
        WicketOptions.Add(new SelectOption<WicketType> { Label = "Hit Wicket", Value = WicketType.HitWicket });
        SelectedWicketOption = WicketOptions[0];
    }

    public async Task InitializeAsync()
    {
        if (!Guid.TryParse(SessionId, out _sessionId))
        {
            ErrorMessage = "Invalid session.";
            return;
        }

        CanWrite = _auth.CanWrite;
        if (!CanWrite)
        {
            await Shell.Current.GoToAsync($"{nameof(Pages.NetSessionScorecardPage)}?id={_sessionId}")
                .ConfigureAwait(true);
            return;
        }

        var active = await _sessions.GetActiveAsync().ConfigureAwait(true);
        if (active is not null && active.Id != _sessionId)
        {
            await Shell.Current.DisplayAlert(
                "Session in progress",
                $"Only one nets session can run at a time. Finish or complete \"{active.Name}\" first.",
                "Continue").ConfigureAwait(true);
            SessionId = active.Id.ToString("D");
            _sessionId = active.Id;
        }

        var session = await _sessions.GetAsync(_sessionId).ConfigureAwait(true);
        if (session is null)
        {
            ErrorMessage = "Session not found.";
            return;
        }

        if (session.Status == NetSessionStatus.Completed)
        {
            await Shell.Current.GoToAsync($"{nameof(Pages.NetSessionScorecardPage)}?id={_sessionId}")
                .ConfigureAwait(true);
            return;
        }

        var ctx = await _sessions.GetScoringContextAsync(_sessionId).ConfigureAwait(true);
        if (ctx is null)
        {
            ErrorMessage = "Session not found.";
            return;
        }

        SessionName = ctx.Name;
        ScorecardHeaderText = ctx.Name;
        Players.Clear();
        foreach (var p in ctx.Players)
            Players.Add(p);

        var snap = await _sessions.GetLiveSnapshotAsync(_sessionId).ConfigureAwait(true);
        if (snap?.BatterId is Guid bid)
            Batter = Players.FirstOrDefault(p => p.Id == bid) ?? Batter;
        if (snap?.BowlerId is Guid bwid)
            Bowler = Players.FirstOrDefault(p => p.Id == bwid) ?? Bowler;

        if (Batter is null && Players.Count > 0)
            ErrorMessage = "Select a batter and bowler to start.";

        await RefreshScoreAsync().ConfigureAwait(true);
        await RefreshScorecardAsync().ConfigureAwait(true);
        await RefreshDuelAsync().ConfigureAwait(true);
        NotifyScoreCanExecute();
    }

    partial void OnBatterChanged(SquadPlayer? value)
    {
        _ = RefreshDuelAsync();
        _ = RefreshScorecardAsync();
        NotifyScoreCanExecute();
    }

    partial void OnBowlerChanged(SquadPlayer? value)
    {
        _ = RefreshDuelAsync();
        NotifyScoreCanExecute();
    }

    partial void OnIsBusyChanged(bool value) => NotifyScoreCanExecute();
    partial void OnCanWriteChanged(bool value) => NotifyScoreCanExecute();
    partial void OnIsWicketChanged(bool value)
    {
        if (!_suppressFieldingToggle && value && IsDroppedCatch)
        {
            _suppressFieldingToggle = true;
            IsDroppedCatch = false;
            _suppressFieldingToggle = false;
        }
        UpdateWicketUiFlags();
    }

    partial void OnIsDroppedCatchChanged(bool value)
    {
        if (!_suppressFieldingToggle && value && IsWicket)
        {
            _suppressFieldingToggle = true;
            IsWicket = false;
            _suppressFieldingToggle = false;
        }
        UpdateWicketUiFlags();
    }

    partial void OnSelectedWicketOptionChanged(SelectOption<WicketType>? value) => UpdateWicketUiFlags();

    private void NotifyScoreCanExecute()
    {
        OnPropertyChanged(nameof(CanScoreBalls));
        ScoreCommand.NotifyCanExecuteChanged();
    }

    private void UpdateWicketUiFlags()
    {
        var type = SelectedWicketOption?.Value ?? WicketType.None;
        NeedsFielder = IsDroppedCatch
            || (IsWicket && type is WicketType.Caught or WicketType.RunOut or WicketType.Stumped);
    }

    [RelayCommand(CanExecute = nameof(CanScoreBalls))]
    private async Task ScoreAsync(string? runsText)
    {
        if (!CanScoreBalls)
            return;
        if (!int.TryParse(runsText, out var runs))
            return;
        if (runs is < 0 or 5 or > 6)
            return;
        if (Batter is null || Bowler is null)
        {
            ErrorMessage = "Select batter and bowler.";
            return;
        }

        var wicketType = SelectedWicketOption?.Value ?? WicketType.Bowled;
        if (NeedsFielder && Fielder is null)
        {
            ErrorMessage = IsDroppedCatch
                ? "Select fielder who dropped the catch."
                : "Select fielder (catch / run-out / stumped).";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var extra = SelectedExtraOption?.Value ?? ExtraType.None;
            var dropCatch = IsDroppedCatch && !IsWicket;
            var needsFielder = dropCatch
                || (IsWicket && wicketType is WicketType.Caught or WicketType.RunOut or WicketType.Stumped);
            var input = new DeliveryInput(
                Batter.Id,
                Batter.Id,
                Bowler.Id,
                runs,
                0,
                extra,
                IsWicket,
                IsWicket ? wicketType : WicketType.None,
                IsWicket ? Batter.Id : null,
                needsFielder ? Fielder?.Id : null,
                dropCatch);

            input = extra switch
            {
                ExtraType.Bye or ExtraType.LegBye => input with { RunsOffBat = 0, ExtraRuns = runs },
                ExtraType.Wide => input with { RunsOffBat = 0, ExtraRuns = 1 + runs },
                ExtraType.NoBall => input with { RunsOffBat = runs, ExtraRuns = 1 },
                _ => input with { RunsOffBat = runs, ExtraRuns = 0 }
            };

            _feedback.PlayBallEntered();
            var previousBowlerId = Bowler.Id;
            var wasWicket = IsWicket;
            await _sessions.RecordDeliveryAsync(_sessionId, input).ConfigureAwait(true);

            SelectedExtraOption = ExtraOptions[0];
            IsWicket = false;
            IsDroppedCatch = false;

            await RefreshScoreAsync().ConfigureAwait(true);
            await RefreshScorecardAsync().ConfigureAwait(true);
            await RefreshDuelAsync().ConfigureAwait(true);

            if (wasWicket)
                await PromptNextBatterAsync().ConfigureAwait(true);

            var snap = await _sessions.GetLiveSnapshotAsync(_sessionId).ConfigureAwait(true);
            var justCompletedOver = extra is not ExtraType.Wide and not ExtraType.NoBall
                                    && snap is { BallsInCurrentOver: 0, Overs: > 0 };
            if (justCompletedOver)
                await PromptNextBowlerAsync(previousBowlerId).ConfigureAwait(true);
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
    private async Task ChangeBatterAsync()
        => await PromptNextBatterAsync(title: "Change batter").ConfigureAwait(true);

    [RelayCommand]
    private async Task ChangeBowlerAsync()
        => await PromptNextBowlerAsync(Bowler?.Id ?? Guid.Empty, title: "Change bowler").ConfigureAwait(true);

    private async Task PromptNextBatterAsync(string title = "Wicket — select batter")
    {
        if (Players.Count == 0)
        {
            Batter = null;
            ErrorMessage = "No players in this session.";
            NotifyScoreCanExecute();
            return;
        }

        var labels = Players.Select(p => p.DisplayName).ToArray();
        var pick = await Shell.Current.DisplayActionSheet(title, "Cancel", null, labels).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(pick) || string.Equals(pick, "Cancel", StringComparison.Ordinal))
        {
            ErrorMessage = "Select a batter to continue.";
            NotifyScoreCanExecute();
            return;
        }

        Batter = Players.FirstOrDefault(p => string.Equals(p.DisplayName, pick, StringComparison.Ordinal));
        ErrorMessage = Batter is null ? "Select a batter to continue." : null;
        NotifyScoreCanExecute();
        await RefreshDuelAsync().ConfigureAwait(true);
        await RefreshScorecardAsync().ConfigureAwait(true);
    }

    private async Task PromptNextBowlerAsync(Guid previousBowlerId, string? title = null)
    {
        if (Players.Count == 0)
        {
            Bowler = null;
            ErrorMessage = "No players in this session.";
            NotifyScoreCanExecute();
            return;
        }

        var labels = Players.Select(p => p.DisplayName).ToArray();
        var header = title ?? $"Over complete ({ScoreText}) — select next bowler";
        var pick = await Shell.Current.DisplayActionSheet(header, "Cancel", null, labels).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(pick) || string.Equals(pick, "Cancel", StringComparison.Ordinal))
        {
            Bowler = null;
            ErrorMessage = "Select the next bowler to continue.";
            NotifyScoreCanExecute();
            await RefreshDuelAsync().ConfigureAwait(true);
            return;
        }

        Bowler = Players.FirstOrDefault(p => string.Equals(p.DisplayName, pick, StringComparison.Ordinal));
        ErrorMessage = Bowler is null ? "Select the next bowler to continue." : null;
        NotifyScoreCanExecute();
        await RefreshDuelAsync().ConfigureAwait(true);
        _ = previousBowlerId;
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task UndoAsync()
    {
        if (!CanWrite || IsBusy)
            return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var undone = await _sessions.UndoLastDeliveryAsync(_sessionId).ConfigureAwait(true);
            Batter = Players.FirstOrDefault(p => p.Id == undone.BatterId) ?? Batter;
            Bowler = Players.FirstOrDefault(p => p.Id == undone.BowlerId) ?? Bowler;
            await RefreshScoreAsync().ConfigureAwait(true);
            await RefreshScorecardAsync().ConfigureAwait(true);
            await RefreshDuelAsync().ConfigureAwait(true);
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
    private async Task CompleteAsync()
    {
        if (!CanWrite)
            return;
        var ok = await Shell.Current.DisplayAlert(
            "Complete session",
            "End this nets session and sync stats?",
            "Complete + Sync",
            "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            IsBusy = true;
            await _sessions.CompleteAsync(_sessionId).ConfigureAwait(true);
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            await Shell.Current.GoToAsync($"{nameof(Pages.NetSessionScorecardPage)}?id={_sessionId}")
                .ConfigureAwait(true);
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

    private async Task RefreshScoreAsync()
    {
        var snap = await _sessions.GetLiveSnapshotAsync(_sessionId).ConfigureAwait(true);
        ScoreText = snap?.ScoreText ?? "0/0 (0.0)";
        ScorecardTotalText = snap is null ? "0/0" : $"{snap.Runs}/{snap.Wickets}";
    }

    private async Task RefreshScorecardAsync()
    {
        var card = await _sessions.GetScorecardAsync(_sessionId, Batter?.Id).ConfigureAwait(true);
        BattingCard.Clear();
        BowlingCard.Clear();
        if (card is null)
            return;
        foreach (var row in card.Batting)
            BattingCard.Add(row);
        foreach (var row in card.Bowling)
            BowlingCard.Add(row);
        ExtrasText = card.ExtrasText;
        ScorecardTotalText = card.TotalText;
    }

    private async Task RefreshDuelAsync()
    {
        if (Batter is null || Bowler is null)
        {
            DuelText = "Select batter & bowler";
            return;
        }

        var card = await _sessions.GetScorecardAsync(_sessionId, Batter.Id).ConfigureAwait(true);
        var bat = card?.Batting.FirstOrDefault(r => r.PlayerId == Batter.Id);
        var bowl = card?.Bowling.FirstOrDefault(r => r.PlayerId == Bowler.Id);
        var batBit = bat is null ? "—" : $"{bat.Runs} ({bat.Balls})";
        var bowlBit = bowl is null ? "—" : bowl.FiguresText;
        DuelText = $"{Batter.DisplayName} {batBit}  ·  {Bowler.DisplayName} {bowlBit}";
    }
}
