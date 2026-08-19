using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class SelectOption<T>
{
    public required string Label { get; init; }
    public required T Value { get; init; }
}

[QueryProperty(nameof(MatchId), "id")]
public partial class ScoringViewModel : ObservableObject
{
    private readonly IMatchService _matches;
    private readonly IAuthService _auth;
    private readonly IScoringFeedback _feedback;

    public string? MatchId { get; set; }

    public ObservableCollection<SquadPlayer> Batters { get; } = new();
    public ObservableCollection<SquadPlayer> Bowlers { get; } = new();
    public ObservableCollection<SquadPlayer> Fielders { get; } = new();
    /// <summary>Striker / non-striker only — run-out "who is out" picker.</summary>
    public ObservableCollection<SquadPlayer> CreaseBatters { get; } = new();
    public ObservableCollection<BattingCardRow> BattingCard { get; } = new();
    public ObservableCollection<BowlingCardRow> BowlingCard { get; } = new();
    public ObservableCollection<SelectOption<ExtraType>> ExtraOptions { get; } = new();
    public ObservableCollection<SelectOption<WicketType>> WicketOptions { get; } = new();

    [ObservableProperty] private SquadPlayer? striker;
    [ObservableProperty] private SquadPlayer? nonStriker;
    [ObservableProperty] private SquadPlayer? bowler;
    [ObservableProperty] private SquadPlayer? fielder;
    [ObservableProperty] private SquadPlayer? dismissedPlayer;
    [ObservableProperty] private SelectOption<ExtraType>? selectedExtraOption;
    [ObservableProperty] private SelectOption<WicketType>? selectedWicketOption;
    [ObservableProperty] private string scoreText = "0/0 (0.0)";
    [ObservableProperty] private string formatHint = string.Empty;
    [ObservableProperty] private string situationText = string.Empty;
    [ObservableProperty] private string battingTeamName = string.Empty;
    [ObservableProperty] private string bowlingTeamName = string.Empty;
    [ObservableProperty] private string sidesText = string.Empty;
    [ObservableProperty] private string squadHint = string.Empty;
    [ObservableProperty] private string duelText = "Select striker & bowler";
    [ObservableProperty] private string extrasText = "Extras 0";
    [ObservableProperty] private string scorecardTotalText = "0/0";
    [ObservableProperty] private string didNotBatText = string.Empty;
    [ObservableProperty] private string didNotBowlText = string.Empty;
    [ObservableProperty] private string scorecardHeaderText = "Innings";
    [ObservableProperty] private string scorecardSubHeaderText = string.Empty;
    [ObservableProperty] private int inningsNumber = 1;
    [ObservableProperty] private string chaseNeedText = string.Empty;
    [ObservableProperty] private string currentRunRateText = "-";
    [ObservableProperty] private string requiredRunRateText = "-";
    [ObservableProperty] private string projectedScoreText = "-";
    [ObservableProperty] private bool isChase;
    [ObservableProperty] private bool showRatePanel;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string syncHint = "Balls save locally; Sync pushes match data only. Players/teams use direct API.";
    [ObservableProperty] private string completeMatchButtonText = "Complete match + Sync";
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isWicket;
    [ObservableProperty] private bool needsFielder;
    [ObservableProperty] private bool needsDismissedChoice;
    [ObservableProperty] private bool canEndInnings;
    [ObservableProperty] private bool awaitingNextInnings;
    [ObservableProperty] private bool lastBatsmanCanBatAsSingleWicket;
    [ObservableProperty] private bool isLastBatsmanAlone;
    [ObservableProperty] private string lastBatsmanHint = string.Empty;
    [ObservableProperty] private bool isTargetAchieved;

    public bool ShowNonStrikerPicker => !IsLastBatsmanAlone;
    public bool CanScoreBalls => CanWrite && !IsTargetAchieved && !IsBusy;
    public bool HasDidNotBat => !string.IsNullOrWhiteSpace(DidNotBatText);
    public bool HasDidNotBowl => !string.IsNullOrWhiteSpace(DidNotBowlText);

    private Guid _matchId;
    private bool _isTournamentMatch;

    partial void OnDidNotBatTextChanged(string value)
        => OnPropertyChanged(nameof(HasDidNotBat));

    partial void OnDidNotBowlTextChanged(string value)
        => OnPropertyChanged(nameof(HasDidNotBowl));

    partial void OnIsLastBatsmanAloneChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNonStrikerPicker));
        LastBatsmanHint = value
            ? "Last batsman batting alone — no strike change at over end."
            : string.Empty;
    }

    partial void OnIsTargetAchievedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanScoreBalls));
        ScoreCommand.NotifyCanExecuteChanged();
        if (value)
        {
            ErrorMessage = _isTournamentMatch
                ? "Target achieved — scoring stopped. Choose End innings or Complete match."
                : "Target achieved — scoring stopped. Choose End innings or Complete match + Sync.";
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanScoreBalls));
        ScoreCommand.NotifyCanExecuteChanged();
    }

    public ScoringViewModel(IMatchService matches, IAuthService auth, IScoringFeedback feedback)
    {
        _matches = matches;
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
        if (!Guid.TryParse(MatchId, out _matchId))
        {
            ErrorMessage = "Invalid match.";
            return;
        }

        CanWrite = _auth.CanWrite;
        if (!CanWrite)
        {
            await Shell.Current.GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={_matchId}").ConfigureAwait(true);
            return;
        }

        var active = await _matches.GetActiveMatchAsync().ConfigureAwait(true);
        if (active is not null && active.Id != _matchId)
        {
            await Shell.Current.DisplayAlert(
                "Match in progress",
                $"Only one match can run at a time in this league on this device. Finish or complete \"{active.Title}\" first.",
                "Continue").ConfigureAwait(true);
            MatchId = active.Id.ToString("D");
            _matchId = active.Id;
        }

        AwaitingNextInnings = false;
        // Loads from in-memory match squad cache (warmed at match start) — no API.
        var ctx = await _matches.GetScoringContextAsync(_matchId).ConfigureAwait(true);
        if (ctx is null)
        {
            ErrorMessage = "No active innings. End previous turn or complete match.";
            CanEndInnings = false;
            FormatHint = string.Empty;
            BattingTeamName = string.Empty;
            BowlingTeamName = string.Empty;
            SidesText = string.Empty;
            return;
        }

        _isTournamentMatch = ctx.IsTournamentMatch;
        CompleteMatchButtonText = _isTournamentMatch ? "Complete match" : "Complete match + Sync";
        SyncHint = _isTournamentMatch
            ? "Tournament match — sync from Tournaments list after the tournament is finished."
            : "Balls save locally; Sync pushes match data only. Players/teams use direct API.";

        Batters.Clear();
        foreach (var p in ctx.Batters) Batters.Add(p);
        ApplyBowlersFromContext(ctx);

        LastBatsmanCanBatAsSingleWicket = ctx.LastBatsmanCanBatAsSingleWicket;
        FormatHint = ctx.FormatHint;
        BattingTeamName = ctx.BattingTeamName;
        BowlingTeamName = ctx.BowlingTeamName;
        InningsNumber = ctx.InningsNumber;
        ScorecardHeaderText = $"Innings {ctx.InningsNumber} — {ctx.BattingTeamName}";
        ScorecardSubHeaderText = $"vs {ctx.BowlingTeamName}";
        SidesText = $"Batting: {ctx.BattingTeamName}  ·  Bowling: {ctx.BowlingTeamName}";
        SquadHint = $"{ctx.TeamAName} vs {ctx.TeamBName} · available batters {Batters.Count} · bowlers {Bowlers.Count} · fielders {Fielders.Count}";
        CanEndInnings = CanWrite;

        Striker = Batters.Count > 0 ? Batters[0] : null;
        if (Batters.Count > 1)
            NonStriker = Batters[1];
        else if (Batters.Count == 1 && LastBatsmanCanBatAsSingleWicket)
            NonStriker = null;
        else
            NonStriker = Batters.Count > 0 ? Batters[0] : null;
        ApplyLoneBatsmanState();
        if (Bowler is null && Bowlers.Count > 0)
            Bowler = Bowlers[0];
        if (Fielder is null && Fielders.Count > 0)
            Fielder = Fielders[0];
        RebuildCreaseBatters();
        DismissedPlayer = Striker;

        UpdateWicketUiFlags();
        await RefreshScoreAsync().ConfigureAwait(true);
        await RefreshScorecardAsync().ConfigureAwait(true);
        await RefreshDuelAsync().ConfigureAwait(true);

        var snap = await _matches.GetLiveSnapshotAsync(_matchId).ConfigureAwait(true);
        ApplyTargetAchievedFromSnapshot(snap);
        if (IsTargetAchieved)
            await PromptTargetAchievedAsync().ConfigureAwait(true);
    }

    private void ApplyLoneBatsmanState()
    {
        IsLastBatsmanAlone = LastBatsmanCanBatAsSingleWicket
                             && Striker is not null
                             && (NonStriker is null || NonStriker.Id == Striker.Id)
                             && Batters.Count <= 1;
        if (IsLastBatsmanAlone)
            NonStriker = null;
    }

    private void RebuildCreaseBatters()
    {
        CreaseBatters.Clear();
        if (Striker is not null)
            CreaseBatters.Add(Striker);
        if (NonStriker is not null && NonStriker.Id != Striker?.Id)
            CreaseBatters.Add(NonStriker);
    }

    /// <summary>Reload not-out batters into the dropdown; keep crease picks if still available.</summary>
    private async Task RefreshAvailableBattersAsync(Guid? justDismissedId = null, bool autoFillEmptyEnd = true)
    {
        var ctx = await _matches.GetScoringContextAsync(_matchId).ConfigureAwait(true);
        if (ctx is null) return;

        var keepStrikerId = Striker?.Id;
        var keepNonStrikerId = NonStriker?.Id;
        if (justDismissedId is Guid outId)
        {
            if (keepStrikerId == outId) keepStrikerId = null;
            if (keepNonStrikerId == outId) keepNonStrikerId = null;
        }

        LastBatsmanCanBatAsSingleWicket = ctx.LastBatsmanCanBatAsSingleWicket;
        Batters.Clear();
        foreach (var p in ctx.Batters) Batters.Add(p);
        ApplyBowlersFromContext(ctx);
        FormatHint = ctx.FormatHint;
        BattingTeamName = ctx.BattingTeamName;
        BowlingTeamName = ctx.BowlingTeamName;
        InningsNumber = ctx.InningsNumber;
        ScorecardHeaderText = $"Innings {ctx.InningsNumber} — {ctx.BattingTeamName}";
        ScorecardSubHeaderText = $"vs {ctx.BowlingTeamName}";
        SquadHint = $"{ctx.TeamAName} vs {ctx.TeamBName} · available batters {Batters.Count} · bowlers {Bowlers.Count} · fielders {Fielders.Count}";

        Striker = keepStrikerId is Guid sid
            ? Batters.FirstOrDefault(p => p.Id == sid)
            : null;
        NonStriker = keepNonStrikerId is Guid nid
            ? Batters.FirstOrDefault(p => p.Id == nid)
            : null;

        if (LastBatsmanCanBatAsSingleWicket && Batters.Count == 1)
        {
            // Last man stands alone — no second batsman to partner.
            Striker ??= Batters[0];
            NonStriker = null;
        }
        else if (autoFillEmptyEnd)
        {
            // After a wicket, fill the empty crease end with the next not-out batter.
            if (Striker is null)
                Striker = Batters.FirstOrDefault(p => p.Id != NonStriker?.Id);
            if (NonStriker is null)
                NonStriker = Batters.FirstOrDefault(p => p.Id != Striker?.Id);
        }

        ApplyLoneBatsmanState();
        RebuildCreaseBatters();
        if (DismissedPlayer is null || Batters.All(p => p.Id != DismissedPlayer.Id))
            DismissedPlayer = Striker;
    }

    /// <summary>
    /// Refresh bowler picker from scoring context — bowlers who hit max overs are omitted
    /// (same idea as dismissed batsmen leaving the batter list).
    /// </summary>
    private void ApplyBowlersFromContext(ScoringContext ctx)
    {
        var keepBowlerId = Bowler?.Id;
        Bowlers.Clear();
        foreach (var p in ctx.Bowlers) Bowlers.Add(p);

        // Fielders stay full side (catches/run-outs) even if a bowler is overs-capped.
        Fielders.Clear();
        foreach (var p in ctx.Fielders) Fielders.Add(p);

        // Keep current bowler only while still eligible; overs-capped bowlers drop off the list.
        Bowler = keepBowlerId is Guid id
            ? Bowlers.FirstOrDefault(p => p.Id == id)
            : null;

        if (Fielder is not null)
            Fielder = Fielders.FirstOrDefault(p => p.Id == Fielder.Id) ?? Fielder;
    }

    partial void OnStrikerChanged(SquadPlayer? value)
    {
        RebuildCreaseBatters();
        if (!NeedsDismissedChoice)
            DismissedPlayer = value;
        _ = RefreshDuelAsync();
        _ = RefreshScorecardAsync();
    }

    partial void OnNonStrikerChanged(SquadPlayer? value)
    {
        RebuildCreaseBatters();
        _ = RefreshScorecardAsync();
    }
    partial void OnBowlerChanged(SquadPlayer? value) => _ = RefreshDuelAsync();
    partial void OnIsWicketChanged(bool value) => UpdateWicketUiFlags();
    partial void OnSelectedWicketOptionChanged(SelectOption<WicketType>? value) => UpdateWicketUiFlags();

    private void UpdateWicketUiFlags()
    {
        var type = SelectedWicketOption?.Value ?? WicketType.None;
        NeedsFielder = IsWicket && type is WicketType.Caught or WicketType.RunOut or WicketType.Stumped;
        NeedsDismissedChoice = IsWicket && type == WicketType.RunOut;
        RebuildCreaseBatters();
        if (!NeedsDismissedChoice)
            DismissedPlayer = Striker;
        else if (DismissedPlayer is null || CreaseBatters.All(p => p.Id != DismissedPlayer.Id))
            DismissedPlayer = Striker;
    }

    [RelayCommand(CanExecute = nameof(CanScoreBalls))]
    private async Task ScoreAsync(string? runsText)
    {
        if (!CanScoreBalls) return;
        if (IsTargetAchieved)
        {
            await PromptTargetAchievedAsync().ConfigureAwait(true);
            return;
        }
        if (!int.TryParse(runsText, out var runs)) return;

        ApplyLoneBatsmanState();
        var alone = IsLastBatsmanAlone;
        if (Striker is null || Bowler is null || (!alone && NonStriker is null))
        {
            ErrorMessage = alone
                ? "Select striker and bowler."
                : "Select striker, non-striker and bowler.";
            return;
        }

        var wicketType = SelectedWicketOption?.Value ?? WicketType.Bowled;
        if (IsWicket && NeedsFielder && Fielder is null)
        {
            ErrorMessage = "Select fielder (catch / run-out / stumped).";
            return;
        }

        if (IsWicket && NeedsDismissedChoice && DismissedPlayer is null)
        {
            ErrorMessage = "Select who was run out.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var extra = SelectedExtraOption?.Value ?? ExtraType.None;
            var needsFielder = IsWicket && wicketType is WicketType.Caught or WicketType.RunOut or WicketType.Stumped;
            // DB NonStrikerId is required — while alone, store striker id on both ends.
            var nonStrikerId = alone ? Striker.Id : NonStriker!.Id;

            var input = new DeliveryInput(
                Striker.Id,
                nonStrikerId,
                Bowler.Id,
                runs,
                0,
                extra,
                IsWicket,
                IsWicket ? wicketType : WicketType.None,
                IsWicket ? (DismissedPlayer?.Id ?? Striker.Id) : null,
                needsFielder ? Fielder?.Id : null);

            input = extra switch
            {
                ExtraType.Bye or ExtraType.LegBye => input with { RunsOffBat = 0, ExtraRuns = runs },
                ExtraType.Wide => input with { RunsOffBat = 0, ExtraRuns = 1 + runs },
                ExtraType.NoBall => input with { RunsOffBat = runs, ExtraRuns = 1 },
                _ => input with { RunsOffBat = runs, ExtraRuns = 0 }
            };

            // Local SQLite only — pickers already use cached squad.
            _feedback.PlayBallEntered();
            var previousBowlerId = Bowler.Id;
            // Capture crease before list refresh (Picker rebuild can drop SelectedItem).
            var creaseStrikerId = Striker.Id;
            var creaseNonStrikerId = alone ? (Guid?)null : NonStriker!.Id;
            await _matches.RecordDeliveryAsync(_matchId, input).ConfigureAwait(true);

            var dismissedId = IsWicket ? (DismissedPlayer?.Id ?? Striker.Id) : (Guid?)null;

            SelectedExtraOption = ExtraOptions[0];
            IsWicket = false;

            // Drop dismissed batters; leave empty crease end for popup pick after a wicket.
            await RefreshAvailableBattersAsync(dismissedId, autoFillEmptyEnd: dismissedId is null)
                .ConfigureAwait(true);

            // All out: no not-out batters left.
            if (Batters.Count == 0)
            {
                await RefreshScoreAsync().ConfigureAwait(true);
                await RefreshScorecardAsync().ConfigureAwait(true);
                var allOut = await Shell.Current.DisplayAlert(
                    "All out",
                    "No batsmen left. End innings now?",
                    "End innings",
                    "Later").ConfigureAwait(true);
                if (allOut)
                    await EndInningsCoreAsync(askConfirm: false).ConfigureAwait(true);
                return;
            }

            // Classic mode: one batter left but setting off → cannot continue without partner.
            ApplyLoneBatsmanState();
            if (!LastBatsmanCanBatAsSingleWicket && Batters.Count == 1 &&
                (Striker is null || NonStriker is null))
            {
                // Keep the remaining not-out batter on strike for display.
                Striker ??= Batters[0];
                NonStriker = null;
                await RefreshScoreAsync().ConfigureAwait(true);
                await RefreshScorecardAsync().ConfigureAwait(true);
                var stop = await Shell.Current.DisplayAlert(
                    "Last wicket fallen",
                    "Only one batsman remains. End innings now?",
                    "End innings",
                    "Later").ConfigureAwait(true);
                if (stop)
                    await EndInningsCoreAsync(askConfirm: false).ConfigureAwait(true);
                return;
            }

            // Wicket with batters still available → pick who comes in.
            if (dismissedId is not null && !IsLastBatsmanAlone)
            {
                if (Striker is null)
                    await PromptNextBatsmanAsync(fillStrikerEnd: true).ConfigureAwait(true);
                else if (NonStriker is null)
                    await PromptNextBatsmanAsync(fillStrikerEnd: false).ConfigureAwait(true);
            }
            else if (dismissedId is null)
            {
                // Re-apply crease after Batters list rebuild so strike rotate has correct ends.
                Striker = Batters.FirstOrDefault(p => p.Id == creaseStrikerId) ?? Striker;
                if (creaseNonStrikerId is Guid nsId)
                    NonStriker = Batters.FirstOrDefault(p => p.Id == nsId) ?? NonStriker;
            }

            ApplyLoneBatsmanState();

            // 1) Odd-run strike change — only when both crease ends are distinct batters.
            if (!IsLastBatsmanAlone
                && Striker is not null
                && NonStriker is not null
                && Striker.Id != NonStriker.Id)
            {
                var rotate = extra switch
                {
                    ExtraType.Wide => runs % 2 == 1,
                    ExtraType.NoBall => runs % 2 == 1,
                    _ => runs % 2 == 1
                };
                if (rotate)
                    (Striker, NonStriker) = (NonStriker, Striker);
            }

            await RefreshScoreAsync().ConfigureAwait(true);
            await RefreshScorecardAsync().ConfigureAwait(true);
            await RefreshDuelAsync().ConfigureAwait(true);

            var snap = await _matches.GetLiveSnapshotAsync(_matchId).ConfigureAwait(true);

            // Target chased — stop the game and offer End innings / Complete + Sync.
            ApplyTargetAchievedFromSnapshot(snap);
            if (IsTargetAchieved)
            {
                await PromptTargetAchievedAsync().ConfigureAwait(true);
                return;
            }

            // 2) End of over: change ends — NOT while last batsman bats alone.
            // Wides/no-balls leave score at x.0 without completing a new over — do not swap again.
            // Start of innings is 0.0 (Overs=0) — do not swap there.
            ApplyLoneBatsmanState();
            var justCompletedOver = extra is not ExtraType.Wide and not ExtraType.NoBall
                                    && snap is { BallsInCurrentOver: 0, Overs: > 0 };
            if (justCompletedOver
                && !IsLastBatsmanAlone
                && Striker is not null
                && NonStriker is not null
                && Striker.Id != NonStriker.Id)
            {
                (Striker, NonStriker) = (NonStriker, Striker);
                await RefreshScorecardAsync().ConfigureAwait(true);
                await RefreshDuelAsync().ConfigureAwait(true);
            }

            if (snap?.InningsOversComplete == true)
            {
                var auto = await Shell.Current.DisplayAlert(
                    "Overs complete",
                    $"{snap.OversLimit} overs done for this turn. End innings now?",
                    "End innings",
                    "Later").ConfigureAwait(true);
                if (auto)
                    await EndInningsCoreAsync(askConfirm: false).ConfigureAwait(true);
            }
            else if (justCompletedOver)
            {
                // Next over needs a bowler pick (previous bowler excluded when alternatives exist).
                await PromptNextBowlerAsync(previousBowlerId).ConfigureAwait(true);
            }
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private void ApplyTargetAchievedFromSnapshot(LiveScoreSnapshot? snap)
        => IsTargetAchieved = snap is { IsChase: true, RunsRequired: 0 };

    /// <summary>Target reached — stop scoring and let user end innings or complete+sync, then scorecard.</summary>
    private async Task PromptTargetAchievedAsync()
    {
        if (!CanWrite) return;

        var title = string.IsNullOrWhiteSpace(ChaseNeedText) ? "Target achieved" : ChaseNeedText;
        var completeLabel = _isTournamentMatch ? "Complete match" : "Complete match + Sync";
        var pick = await Shell.Current.DisplayActionSheet(
                title,
                "Later",
                null,
                "End innings",
                completeLabel)
            .ConfigureAwait(true);

        if (string.Equals(pick, "End innings", StringComparison.Ordinal))
        {
            await EndInningsCoreAsync(askConfirm: false).ConfigureAwait(true);
            return;
        }

        if (string.Equals(pick, completeLabel, StringComparison.Ordinal))
            await CompleteMatchAndShowScorecardAsync(askConfirm: false).ConfigureAwait(true);
    }

    /// <summary>After a wicket, force selection of the incoming batsman via action sheet.</summary>
    private async Task PromptNextBatsmanAsync(bool fillStrikerEnd)
    {
        var partnerId = fillStrikerEnd ? NonStriker?.Id : Striker?.Id;
        var candidates = Batters
            .Where(b => partnerId is null || b.Id != partnerId)
            .ToList();

        if (candidates.Count == 0)
        {
            if (fillStrikerEnd) Striker = null;
            else NonStriker = null;
            ErrorMessage = "No batsman available — select a batsman to continue.";
            return;
        }

        var endLabel = fillStrikerEnd ? "striker" : "non-striker";
        var labels = candidates.Select(b => b.DisplayName).ToArray();
        var pick = await Shell.Current.DisplayActionSheet(
                $"Wicket — select new {endLabel}",
                "Cancel",
                null,
                labels)
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(pick) || string.Equals(pick, "Cancel", StringComparison.Ordinal))
        {
            if (fillStrikerEnd) Striker = null;
            else NonStriker = null;
            ErrorMessage = "Select the new batsman to continue.";
            RebuildCreaseBatters();
            await RefreshDuelAsync().ConfigureAwait(true);
            return;
        }

        var selected = candidates.FirstOrDefault(b =>
                            string.Equals(b.DisplayName, pick, StringComparison.Ordinal))
                        ?? Batters.FirstOrDefault(b =>
                            string.Equals(b.DisplayName, pick, StringComparison.Ordinal));

        if (selected is null)
        {
            if (fillStrikerEnd) Striker = null;
            else NonStriker = null;
            ErrorMessage = "Select the new batsman to continue.";
            return;
        }

        if (fillStrikerEnd) Striker = selected;
        else NonStriker = selected;

        ErrorMessage = null;
        ApplyLoneBatsmanState();
        RebuildCreaseBatters();
        DismissedPlayer = Striker;
        await RefreshDuelAsync().ConfigureAwait(true);
        await RefreshScorecardAsync().ConfigureAwait(true);
    }

    /// <summary>After a completed over, force selection of the next bowler via action sheet.</summary>
    private async Task PromptNextBowlerAsync(Guid previousBowlerId)
    {
        if (Bowlers.Count == 0)
        {
            Bowler = null;
            ErrorMessage = "No bowlers available — select a bowler to continue.";
            return;
        }

        // Prefer a different bowler for the next over (standard consecutive-overs rule).
        var candidates = Bowlers.Where(b => b.Id != previousBowlerId).ToList();
        if (candidates.Count == 0)
            candidates = Bowlers.ToList();

        var labels = candidates.Select(b => b.DisplayName).ToArray();
        var oversText = ScoreText;
        var pick = await Shell.Current.DisplayActionSheet(
                $"Over complete ({oversText}) — select next bowler",
                "Cancel",
                null,
                labels)
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(pick) || string.Equals(pick, "Cancel", StringComparison.Ordinal))
        {
            // Clear so the next ball cannot be scored until a bowler is chosen.
            Bowler = null;
            ErrorMessage = "Select the next bowler to continue.";
            await RefreshDuelAsync().ConfigureAwait(true);
            return;
        }

        var selected = candidates.FirstOrDefault(b =>
                            string.Equals(b.DisplayName, pick, StringComparison.Ordinal))
                        ?? Bowlers.FirstOrDefault(b =>
                            string.Equals(b.DisplayName, pick, StringComparison.Ordinal));

        if (selected is null)
        {
            Bowler = null;
            ErrorMessage = "Select the next bowler to continue.";
            return;
        }

        Bowler = selected;
        ErrorMessage = null;
        await RefreshDuelAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task UndoAsync()
    {
        if (!CanWrite || IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var undone = await _matches.UndoLastDeliveryAsync(_matchId).ConfigureAwait(true);

            // Reload not-out batters, then force crease back to the deleted ball's ends
            // (score path rotates for odd runs / end-of-over — undo must reverse that).
            await RefreshAvailableBattersAsync().ConfigureAwait(true);
            RestoreCreaseFromUndone(undone);

            await RefreshScoreAsync().ConfigureAwait(true);
            await RefreshScorecardAsync().ConfigureAwait(true);
            await RefreshDuelAsync().ConfigureAwait(true);

            var snap = await _matches.GetLiveSnapshotAsync(_matchId).ConfigureAwait(true);
            ApplyTargetAchievedFromSnapshot(snap);
            if (!IsTargetAchieved
                && ErrorMessage is not null
                && ErrorMessage.StartsWith("Target achieved", StringComparison.Ordinal))
                ErrorMessage = null;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private void RestoreCreaseFromUndone(UndoneDelivery undone)
    {
        SquadPlayer? FindBatter(Guid id) =>
            Batters.FirstOrDefault(p => p.Id == id)
            ?? CreaseBatters.FirstOrDefault(p => p.Id == id)
            ?? Fielders.FirstOrDefault(p => p.Id == id);

        var restoredStriker = FindBatter(undone.BatterId);
        var restoredNonStriker = FindBatter(undone.NonStrikerId);

        // If still missing (cache gap), keep current pick rather than clearing crease.
        if (restoredStriker is not null)
            Striker = restoredStriker;
        if (restoredNonStriker is not null)
            NonStriker = restoredNonStriker;

        // Alone deliveries store the same id on both ends — keep single-wicket mode.
        if (LastBatsmanCanBatAsSingleWicket
            && Striker is not null
            && (NonStriker is null || NonStriker.Id == Striker.Id)
            && Batters.Count <= 1)
        {
            NonStriker = null;
        }
        else if (Striker is not null && NonStriker is not null && Striker.Id == NonStriker.Id)
        {
            // Avoid same player on both ends after partial restore (classic mode).
            NonStriker = Batters.FirstOrDefault(p => p.Id != Striker.Id) ?? NonStriker;
        }

        ApplyLoneBatsmanState();

        var restoredBowler = Bowlers.FirstOrDefault(p => p.Id == undone.BowlerId);
        if (restoredBowler is not null)
            Bowler = restoredBowler;

        RebuildCreaseBatters();
        DismissedPlayer = Striker;
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task EndInningsAsync() => EndInningsCoreAsync(askConfirm: true);

    private async Task EndInningsCoreAsync(bool askConfirm)
    {
        if (!CanWrite) return;
        if (askConfirm)
        {
            var ok = await Shell.Current.DisplayAlert(
                "End innings",
                "Close this side's turn and continue the match?",
                "End innings",
                "Cancel").ConfigureAwait(true);
            if (!ok) return;
        }

        var ownedBusy = !IsBusy;
        try
        {
            if (ownedBusy)
                IsBusy = true;
            ErrorMessage = null;
            var result = await _matches.EndInningsAsync(_matchId).ConfigureAwait(true);

            if (result.MatchCompleted)
            {
                await Shell.Current.DisplayAlert(
                        "Match complete",
                        string.IsNullOrWhiteSpace(result.Message)
                            ? "Match finished. Opening full scorecard."
                            : result.Message,
                        "View scorecard")
                    .ConfigureAwait(true);
                await OpenCompletedScorecardAsync().ConfigureAwait(true);
                return;
            }

            if (!result.CanStartNext || result.NextBattingTeamId is not Guid bat || result.NextBowlingTeamId is not Guid bowl)
            {
                ErrorMessage = result.Message;
                CanEndInnings = false;
                AwaitingNextInnings = true;
                return;
            }

            var enforceFollowOn = false;
            if (result.FollowOnAvailable)
            {
                enforceFollowOn = await Shell.Current.DisplayAlert(
                    "Follow-on",
                    $"Lead by {result.LeadBy} runs. Enforce follow-on (trailing side bats again)?",
                    "Follow-on",
                    "Bat again").ConfigureAwait(true);
            }

            if (enforceFollowOn)
                await _matches.StartInningsAsync(_matchId, bowl, bat, isFollowOn: true).ConfigureAwait(true);
            else
                await _matches.StartInningsAsync(_matchId, bat, bowl, isFollowOn: false).ConfigureAwait(true);

            Striker = null;
            NonStriker = null;
            Bowler = null;
            await InitializeAsync().ConfigureAwait(true);
            await Shell.Current.DisplayAlert("Next innings", result.Message, "OK").ConfigureAwait(true);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally
        {
            if (ownedBusy)
                IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task CompleteAsync() => CompleteMatchAndShowScorecardAsync(askConfirm: true);

    private async Task CompleteMatchAndShowScorecardAsync(bool askConfirm)
    {
        if (!CanWrite) return;
        if (askConfirm)
        {
            var ok = await Shell.Current.DisplayAlert(
                "Complete match",
                "Mark completed and sync this match to server? You will see the full scorecard next.",
                "Yes",
                "No").ConfigureAwait(true);
            if (!ok) return;
        }

        var ownedBusy = !IsBusy;
        try
        {
            if (ownedBusy)
                IsBusy = true;
            ErrorMessage = null;
            await _matches.CompleteMatchAsync(_matchId).ConfigureAwait(true);
            var card = await _matches.GetMatchScorecardAsync(_matchId).ConfigureAwait(true);
            var potmLine = card?.HasPlayerOfTheMatch == true
                ? $"Player of the Match: {card.PlayerOfTheMatchName}."
                : string.Empty;
            await Shell.Current.DisplayAlert(
                    "Match complete",
                    string.IsNullOrWhiteSpace(potmLine)
                        ? "Match marked complete. Opening full scorecard."
                        : $"Match marked complete. {potmLine} Opening full scorecard.",
                    "View scorecard")
                .ConfigureAwait(true);
            await OpenCompletedScorecardAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            if (ownedBusy)
                IsBusy = false;
        }
    }

    private async Task OpenCompletedScorecardAsync()
    {
        // Replace live scoring with the same full scorecard used from Matches list.
        var id = _matchId;
        try
        {
            await Shell.Current
                .GoToAsync($"../{nameof(Pages.MatchScorecardPage)}?id={id:D}")
                .ConfigureAwait(true);
        }
        catch
        {
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            await Shell.Current
                .GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={id:D}")
                .ConfigureAwait(true);
        }
    }

    private async Task RefreshScoreAsync()
    {
        var snap = await _matches.GetLiveSnapshotAsync(_matchId).ConfigureAwait(true);
        ScoreText = snap?.ScoreText ?? "0/0 (0.0)";
        SituationText = snap?.SituationText ?? string.Empty;
        ShowRatePanel = snap is not null;
        IsChase = snap?.IsChase == true;
        CurrentRunRateText = snap is null ? "-" : snap.CurrentRunRate.ToString("0.##");
        ProjectedScoreText = snap is null ? "-" : snap.ProjectedScore.ToString();

        if (snap is { IsChase: true, RunsRequired: int need })
        {
            var oversLeft = $"{snap.BallsRemaining / 6}.{snap.BallsRemaining % 6}";
            ChaseNeedText = need == 0
                ? $"Target {snap.Target} achieved"
                : $"Need {need} runs from {snap.BallsRemaining} balls ({oversLeft} ov)";
            RequiredRunRateText = snap.RequiredRunRate is double rrr
                ? rrr.ToString("0.##")
                : "-";
        }
        else
        {
            ChaseNeedText = string.Empty;
            RequiredRunRateText = "-";
        }
    }

    private async Task RefreshScorecardAsync()
    {
        var card = await _matches.GetLiveScorecardAsync(
            _matchId,
            Striker?.Id,
            NonStriker?.Id).ConfigureAwait(true);

        BattingCard.Clear();
        BowlingCard.Clear();
        if (card is null)
        {
            ExtrasText = "Extras 0";
            ScorecardTotalText = "0/0";
            DidNotBatText = string.Empty;
            DidNotBowlText = string.Empty;
            return;
        }

        foreach (var row in card.Batting)
            BattingCard.Add(row);
        foreach (var row in card.Bowling)
            BowlingCard.Add(row);
        ExtrasText = $"Extras {card.Extras}";
        ScorecardTotalText = $"{card.TotalRuns}/{card.Wickets}";
        DidNotBatText = card.DidNotBatText;
        DidNotBowlText = card.DidNotBowlText;
    }

    private Task RefreshDuelAsync()
    {
        DuelText = Striker is null || Bowler is null
            ? "Select striker & bowler"
            : $"{Striker.DisplayName}  vs  {Bowler.DisplayName}";
        return Task.CompletedTask;
    }

    partial void OnCanWriteChanged(bool value)
    {
        OnPropertyChanged(nameof(CanScoreBalls));
        ScoreCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        CompleteCommand.NotifyCanExecuteChanged();
        EndInningsCommand.NotifyCanExecuteChanged();
    }
}
