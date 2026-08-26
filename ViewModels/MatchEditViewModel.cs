using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public partial class MatchEditViewModel : ObservableObject, IQueryAttributable
{
    private readonly IMatchService _matches;
    private readonly ITeamService _teams;
    private readonly IAuthService _auth;
    private readonly ITournamentService _tournaments;
    private readonly ISeriesService _series;
    private readonly IVenueService _venues;
    private readonly IFormatService _formats;

    private Guid? _tournamentId;
    private Guid? _tournamentFixtureId;
    private Guid? _seriesId;
    private Guid? _seriesFixtureId;
    private Guid? _pendingTeamAId;
    private Guid? _pendingTeamBId;
    private Guid? _pendingVenueId;
    private Guid? _pendingFormatId;
    private byte? _pendingFormat;
    private int? _pendingOvers;
    private byte? _pendingOversMode;
    private bool? _pendingLastBat;
    private int? _pendingBowlerMax;

    public ObservableCollection<TeamListItem> Teams { get; } = new();
    public ObservableCollection<TeamListItem> BatFirstOptions { get; } = new();
    public ObservableCollection<FormatListItem> FormatOptions { get; } = new();
    public ObservableCollection<VenueListItem> VenueOptions { get; } = new();

    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private VenueListItem? selectedVenue;
    [ObservableProperty] private int oversLimit = 8;
    [ObservableProperty] private bool noOversLimit;
    [ObservableProperty] private bool oversAreMatchTotal = true;
    [ObservableProperty] private FormatListItem? selectedFormat;
    [ObservableProperty] private TeamListItem? teamA;
    [ObservableProperty] private TeamListItem? teamB;
    /// <summary>True = Team 1 called Heads (Team 2 gets Tails). False = Team 1 Tails, Team 2 Heads.</summary>
    [ObservableProperty] private bool teamAIsHeads = true;
    [ObservableProperty] private TeamListItem? tossWinner;
    [ObservableProperty] private TeamListItem? battingFirst;
    [ObservableProperty] private string? tossResultText;
    [ObservableProperty] private string coinFaceLabel = "H";
    [ObservableProperty] private string coinFaceTitle = "HEADS";
    [ObservableProperty] private string tossOverlayStatus = "Flip the coin";
    [ObservableProperty] private bool isTossOverlayVisible;
    [ObservableProperty] private bool isCoinFlipping;
    [ObservableProperty] private bool showTossWinnerBanner;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string formatHint = string.Empty;
    [ObservableProperty] private bool lastBatsmanCanBatAsSingleWicket;
    [ObservableProperty] private int bowlerMaxOvers;
    [ObservableProperty] private bool isTournamentMatch;
    [ObservableProperty] private bool isSeriesMatch;
    [ObservableProperty] private bool lockMatchSettings;
    [ObservableProperty] private string pageTitle = "New match";
    [ObservableProperty] private bool isLiveTelecast;
    [ObservableProperty] private string liveStreamUrl = string.Empty;

    public bool CanChangeTeams => !IsTournamentMatch && !IsSeriesMatch;
    public bool CanEditMatchSettings => !LockMatchSettings;
    public bool ShowLiveStreamUrl => IsLiveTelecast;
    public bool ShowOversFields => !NoOversLimit;
    public string OversFieldLabel => OversAreMatchTotal ? "Match overs *" : "Overs per innings *";
    public OversMode CurrentOversMode => NoOversLimit
        ? OversMode.Unlimited
        : OversAreMatchTotal
            ? OversMode.MatchTotal
            : OversMode.PerInnings;

    public string TeamAPlayingHint => PlayingHint(TeamA);
    public string TeamBPlayingHint => PlayingHint(TeamB);
    public bool HasTeamAPlayingHint => !string.IsNullOrEmpty(TeamAPlayingHint);
    public bool HasTeamBPlayingHint => !string.IsNullOrEmpty(TeamBPlayingHint);

    /// <summary>True when the coin landed on Heads.</summary>
    public bool LastCoinWasHeads { get; private set; } = true;
    public bool TeamBIsHeads => !TeamAIsHeads;
    public double TeamAHeadsOpacity => TeamAIsHeads ? 1 : 0.45;
    public double TeamATailsOpacity => TeamAIsHeads ? 0.45 : 1;
    public double TeamBHeadsOpacity => TeamAIsHeads ? 0.45 : 1;
    public double TeamBTailsOpacity => TeamAIsHeads ? 1 : 0.45;
    public string HeadsTeamName => TeamAIsHeads
        ? (TeamA?.Name ?? "Team 1")
        : (TeamB?.Name ?? "Team 2");
    public string TailsTeamName => TeamAIsHeads
        ? (TeamB?.Name ?? "Team 2")
        : (TeamA?.Name ?? "Team 1");

    /// <summary>False while toss overlay is open — blocks form / background interaction.</summary>
    public bool IsMatchFormEnabled => !IsTossOverlayVisible;

    public bool CanToss => TeamA is not null && TeamB is not null && TeamA.Id != TeamB.Id && !IsCoinFlipping;
    public bool HasTossWinner => TossWinner is not null;
    public bool CanPickBatFirst => HasTossWinner && !IsCoinFlipping;
    public bool HasBatFirstPick => BattingFirst is not null;
    public bool CanElectTossDecision => ShowTossWinnerBanner && !IsCoinFlipping && TossWinner is not null;
    public bool CanCloseTossOverlay => ShowTossWinnerBanner && !IsCoinFlipping && BattingFirst is not null;
    public bool CanStartMatch => HasTossWinner && BattingFirst is not null;
    public bool TossWinnerBats => TossWinner is not null && BattingFirst is not null && TossWinner.Id == BattingFirst.Id;
    public bool TossWinnerBowls => TossWinner is not null && BattingFirst is not null && TossWinner.Id != BattingFirst.Id;
    public double BatButtonOpacity => BattingFirst is null || TossWinnerBats ? 1 : 0.45;
    public double BowlButtonOpacity => BattingFirst is null || TossWinnerBowls ? 1 : 0.45;
    public string TossDecisionHint => TossWinner is null
        ? string.Empty
        : BattingFirst is null
            ? $"{TossWinner.Name} — choose Bat or Bowl"
            : TossWinnerBats
                ? $"{TossWinner.Name} elected to bat"
                : $"{TossWinner.Name} elected to bowl";

    public MatchEditViewModel(
        IMatchService matches,
        ITeamService teams,
        IAuthService auth,
        ITournamentService tournaments,
        ISeriesService series,
        IVenueService venues,
        IFormatService formats)
    {
        _matches = matches;
        _teams = teams;
        _auth = auth;
        _tournaments = tournaments;
        _series = series;
        _venues = venues;
        _formats = formats;
    }

    partial void OnSelectedFormatChanged(FormatListItem? value)
    {
        if (value is not null)
        {
            OversLimit = value.OversLimit > 0 ? value.OversLimit : 8;
            ApplyOversMode(value.OversMode);
            FormatHint = value.Hint;
        }
        else
            UpdateHint();
    }
    partial void OnOversLimitChanged(int value) => UpdateHint();
    partial void OnNoOversLimitChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowOversFields));
        OnPropertyChanged(nameof(OversFieldLabel));
        UpdateHint();
    }
    partial void OnOversAreMatchTotalChanged(bool value)
    {
        OnPropertyChanged(nameof(OversFieldLabel));
        UpdateHint();
    }
    partial void OnLockMatchSettingsChanged(bool value)
        => OnPropertyChanged(nameof(CanEditMatchSettings));
    partial void OnIsLiveTelecastChanged(bool value)
        => OnPropertyChanged(nameof(ShowLiveStreamUrl));
    partial void OnIsTossOverlayVisibleChanged(bool value)
        => OnPropertyChanged(nameof(IsMatchFormEnabled));

    partial void OnTeamAChanged(TeamListItem? value)
    {
        ClearToss();
        RefreshTossAvailability();
        NotifyCallSides();
        OnPropertyChanged(nameof(TeamAPlayingHint));
        OnPropertyChanged(nameof(HasTeamAPlayingHint));
    }

    partial void OnTeamBChanged(TeamListItem? value)
    {
        ClearToss();
        RefreshTossAvailability();
        NotifyCallSides();
        OnPropertyChanged(nameof(TeamBPlayingHint));
        OnPropertyChanged(nameof(HasTeamBPlayingHint));
    }

    partial void OnTeamAIsHeadsChanged(bool value)
    {
        ClearToss();
        NotifyCallSides();
    }

    private void NotifyCallSides()
    {
        OnPropertyChanged(nameof(TeamBIsHeads));
        OnPropertyChanged(nameof(TeamAHeadsOpacity));
        OnPropertyChanged(nameof(TeamATailsOpacity));
        OnPropertyChanged(nameof(TeamBHeadsOpacity));
        OnPropertyChanged(nameof(TeamBTailsOpacity));
        OnPropertyChanged(nameof(HeadsTeamName));
        OnPropertyChanged(nameof(TailsTeamName));
    }

    partial void OnTossWinnerChanged(TeamListItem? value)
    {
        OnPropertyChanged(nameof(HasTossWinner));
        OnPropertyChanged(nameof(CanPickBatFirst));
        OnPropertyChanged(nameof(CanElectTossDecision));
        OnPropertyChanged(nameof(CanStartMatch));
        OnPropertyChanged(nameof(CanCloseTossOverlay));
        OnPropertyChanged(nameof(TossDecisionHint));
        OnPropertyChanged(nameof(TossWinnerBats));
        OnPropertyChanged(nameof(TossWinnerBowls));
        OnPropertyChanged(nameof(BatButtonOpacity));
        OnPropertyChanged(nameof(BowlButtonOpacity));
        RebuildBatFirstOptions();
    }

    partial void OnBattingFirstChanged(TeamListItem? value)
    {
        OnPropertyChanged(nameof(CanStartMatch));
        OnPropertyChanged(nameof(HasBatFirstPick));
        OnPropertyChanged(nameof(CanCloseTossOverlay));
        OnPropertyChanged(nameof(TossDecisionHint));
        OnPropertyChanged(nameof(TossWinnerBats));
        OnPropertyChanged(nameof(TossWinnerBowls));
        OnPropertyChanged(nameof(BatButtonOpacity));
        OnPropertyChanged(nameof(BowlButtonOpacity));
        ElectBatCommand.NotifyCanExecuteChanged();
        ElectBowlCommand.NotifyCanExecuteChanged();
        CloseTossOverlayCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCoinFlippingChanged(bool value)
    {
        RefreshTossAvailability();
        OnPropertyChanged(nameof(CanPickBatFirst));
        OnPropertyChanged(nameof(CanElectTossDecision));
        OnPropertyChanged(nameof(CanCloseTossOverlay));
        ElectBatCommand.NotifyCanExecuteChanged();
        ElectBowlCommand.NotifyCanExecuteChanged();
        CloseTossOverlayCommand.NotifyCanExecuteChanged();
    }

    partial void OnShowTossWinnerBannerChanged(bool value)
    {
        OnPropertyChanged(nameof(CanElectTossDecision));
        OnPropertyChanged(nameof(CanCloseTossOverlay));
        OnPropertyChanged(nameof(TossDecisionHint));
        ElectBatCommand.NotifyCanExecuteChanged();
        ElectBowlCommand.NotifyCanExecuteChanged();
        CloseTossOverlayCommand.NotifyCanExecuteChanged();
    }

    private void RefreshTossAvailability()
    {
        OnPropertyChanged(nameof(CanToss));
        TossCommand.NotifyCanExecuteChanged();
    }

    private void ClearToss()
    {
        TossWinner = null;
        BattingFirst = null;
        TossResultText = null;
        ShowTossWinnerBanner = false;
        IsTossOverlayVisible = false;
        IsCoinFlipping = false;
        TossOverlayStatus = "Flip the coin";
        CoinFaceLabel = "H";
        CoinFaceTitle = "HEADS";
        BatFirstOptions.Clear();
    }

    private void RebuildBatFirstOptions()
    {
        var keep = BattingFirst?.Id;
        BatFirstOptions.Clear();
        if (TeamA is not null) BatFirstOptions.Add(TeamA);
        if (TeamB is not null && TeamB.Id != TeamA?.Id) BatFirstOptions.Add(TeamB);

        BattingFirst = keep is Guid id
            ? BatFirstOptions.FirstOrDefault(t => t.Id == id)
            : null;
    }

    private void UpdateHint()
    {
        FormatHint = SelectedFormat?.Hint
            ?? "No formats in this league yet. Open Formats and add one first.";
    }

    private void ApplyOversMode(OversMode mode)
    {
        NoOversLimit = mode == OversMode.Unlimited;
        OversAreMatchTotal = mode != OversMode.PerInnings;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _tournamentId = null;
        _tournamentFixtureId = null;
        _seriesId = null;
        _seriesFixtureId = null;
        _pendingTeamAId = null;
        _pendingTeamBId = null;
        _pendingVenueId = null;
        _pendingFormatId = null;
        _pendingFormat = null;
        _pendingOvers = null;
        _pendingOversMode = null;
        _pendingLastBat = null;
        _pendingBowlerMax = null;
        IsTournamentMatch = false;
        IsSeriesMatch = false;
        LockMatchSettings = false;
        PageTitle = "New match";

        if (query.TryGetValue("tournamentId", out var tid) && Guid.TryParse($"{tid}", out var tournamentId))
            _tournamentId = tournamentId;
        if (query.TryGetValue("seriesId", out var sid) && Guid.TryParse($"{sid}", out var seriesId))
            _seriesId = seriesId;
        if (query.TryGetValue("fixtureId", out var fid) && Guid.TryParse($"{fid}", out var fixtureId))
        {
            if (_seriesId is not null)
                _seriesFixtureId = fixtureId;
            else
                _tournamentFixtureId = fixtureId;
        }

        if (_tournamentId is not null && _tournamentFixtureId is not null)
        {
            IsTournamentMatch = true;
            PageTitle = "Tournament match";
        }

        if (_seriesId is not null && _seriesFixtureId is not null)
        {
            IsSeriesMatch = true;
            PageTitle = "Series match";
        }

        if (query.TryGetValue("lockSettings", out var lockRaw)
            && ($"{lockRaw}" == "1" || string.Equals($"{lockRaw}", "true", StringComparison.OrdinalIgnoreCase)))
            LockMatchSettings = true;

        if (query.TryGetValue("title", out var titleObj))
        {
            var raw = $"{titleObj}";
            Title = Uri.UnescapeDataString(raw).Replace('+', ' ');
        }
        if (query.TryGetValue("teamAId", out var aObj) && Guid.TryParse($"{aObj}", out var teamAId))
            _pendingTeamAId = teamAId;
        if (query.TryGetValue("teamBId", out var bObj) && Guid.TryParse($"{bObj}", out var teamBId))
            _pendingTeamBId = teamBId;
        if (query.TryGetValue("venueId", out var vObj) && Guid.TryParse($"{vObj}", out var venueId))
            _pendingVenueId = venueId;
        if (query.TryGetValue("formatId", out var formatIdObj) && Guid.TryParse($"{formatIdObj}", out var formatId))
            _pendingFormatId = formatId;
        if (query.TryGetValue("format", out var fObj) && byte.TryParse($"{fObj}", out var format))
            _pendingFormat = format;
        if (query.TryGetValue("overs", out var oObj) && int.TryParse($"{oObj}", out var overs))
            _pendingOvers = overs;
        if (query.TryGetValue("oversMode", out var omObj) && byte.TryParse($"{omObj}", out var oversMode))
            _pendingOversMode = oversMode;
        if (query.TryGetValue("lastBat", out var lbObj) && int.TryParse($"{lbObj}", out var lastBat))
            _pendingLastBat = lastBat != 0;
        if (query.TryGetValue("bowlerMax", out var bmObj) && int.TryParse($"{bmObj}", out var bowlerMax))
            _pendingBowlerMax = bowlerMax;
    }

    public async Task InitializeAsync()
    {
        if (!_auth.CanWrite)
        {
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        var active = await _matches.GetActiveMatchAsync().ConfigureAwait(true);
        if (active is not null)
        {
            if (!active.IsScoringOwner)
            {
                await Shell.Current.DisplayAlert(
                    "View only",
                    ScoringOwnership.OtherAccountMessage,
                    "OK").ConfigureAwait(true);
                await Shell.Current.GoToAsync("..").ConfigureAwait(true);
                await Shell.Current.GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={active.Id}").ConfigureAwait(true);
                return;
            }

            await Shell.Current.DisplayAlert(
                "Match in progress",
                $"Only one match can run at a time in this league on this device. Finish or complete \"{active.Title}\" first.",
                "Continue").ConfigureAwait(true);
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={active.Id}").ConfigureAwait(true);
            return;
        }

        UpdateHint();

        await MauiProgram.WhenReadyAsync().ConfigureAwait(true);

        VenueOptions.Clear();
        try
        {
            var venues = await _venues.GetAllAsync().ConfigureAwait(true);
            foreach (var v in venues)
                VenueOptions.Add(v);
        }
        catch
        {
            /* offline — venue list empty until online */
        }
        if (SelectedVenue is not null)
            SelectedVenue = VenueOptions.FirstOrDefault(v => v.Id == SelectedVenue.Id);
        if (_pendingVenueId is Guid pendingVenue)
            SelectedVenue = VenueOptions.FirstOrDefault(v => v.Id == pendingVenue) ?? SelectedVenue;

        FormatOptions.Clear();
        try
        {
            foreach (var f in await _formats.GetAllAsync().ConfigureAwait(true))
                FormatOptions.Add(f);
        }
        catch
        {
            /* offline — format list empty until online */
        }

        if (_pendingFormatId is Guid pendingFormatId)
            SelectedFormat = FormatOptions.FirstOrDefault(f => f.Id == pendingFormatId);
        if (SelectedFormat is null && _pendingFormat is byte pendingFormat)
        {
            var mode = _pendingOversMode is byte pendingMode ? (OversMode)pendingMode : OversMode.MatchTotal;
            var overs = _pendingOvers is int pendingOvers && pendingOvers > 0 ? pendingOvers : 8;
            SelectedFormat = FormatOptions.FirstOrDefault(f =>
                                 (byte)f.Format == pendingFormat
                                 && f.OversMode == mode
                                 && f.OversLimit == overs)
                             ?? FormatOptions.FirstOrDefault(f => (byte)f.Format == pendingFormat);
        }
        SelectedFormat ??= FormatOptions.FirstOrDefault();
        if (SelectedFormat is null)
            FormatHint = "No formats in this league yet. Open Formats and add one first.";

        if (_pendingLastBat is bool pendingLastBat)
            LastBatsmanCanBatAsSingleWicket = pendingLastBat;
        if (_pendingBowlerMax is int pendingBowlerMax)
            BowlerMaxOvers = pendingBowlerMax;

        var list = await _teams.GetActiveAsync().ConfigureAwait(true);
        Teams.Clear();
        foreach (var t in list) Teams.Add(t);

        if (_pendingTeamAId is Guid pa)
            TeamA = Teams.FirstOrDefault(t => t.Id == pa);
        if (_pendingTeamBId is Guid pb)
            TeamB = Teams.FirstOrDefault(t => t.Id == pb);
        OnPropertyChanged(nameof(CanChangeTeams));
    }

    [RelayCommand]
    private void SetTeamAHeads() => TeamAIsHeads = true;

    [RelayCommand]
    private void SetTeamATails() => TeamAIsHeads = false;

    [RelayCommand]
    private void SetTeamBHeads() => TeamAIsHeads = false;

    [RelayCommand]
    private void SetTeamBTails() => TeamAIsHeads = true;

    [RelayCommand(CanExecute = nameof(CanToss))]
    private void Toss()
    {
        ErrorMessage = null;
        if (TeamA is null || TeamB is null)
        {
            ErrorMessage = "Select both teams before toss.";
            return;
        }

        if (TeamA.Id == TeamB.Id)
        {
            ErrorMessage = "Select two different teams for toss.";
            return;
        }

        // Open overlay — page code-behind runs the coin animation, then calls FinishTossAsync.
        BattingFirst = null;
        TossWinner = null;
        ShowTossWinnerBanner = false;
        TossResultText = null;
        TossOverlayStatus = "Calling heads or tails…";
        CoinFaceLabel = "H";
        CoinFaceTitle = "HEADS";
        IsTossOverlayVisible = true;
        IsCoinFlipping = true;
    }

    /// <summary>Called by the page after the coin animation lands.</summary>
    public void FinishToss(bool heads)
    {
        if (TeamA is null || TeamB is null)
        {
            IsCoinFlipping = false;
            IsTossOverlayVisible = false;
            return;
        }

        LastCoinWasHeads = heads;
        if (heads)
        {
            CoinFaceLabel = "H";
            CoinFaceTitle = "HEADS";
            TossWinner = TeamAIsHeads ? TeamA : TeamB;
        }
        else
        {
            CoinFaceLabel = "T";
            CoinFaceTitle = "TAILS";
            TossWinner = TeamAIsHeads ? TeamB : TeamA;
        }

        TossResultText = $"{TossWinner.Name} won the toss";
        TossOverlayStatus = TossResultText;
        ShowTossWinnerBanner = true;
        IsCoinFlipping = false;
        RebuildBatFirstOptions();
        OnPropertyChanged(nameof(TossDecisionHint));
        ElectBatCommand.NotifyCanExecuteChanged();
        ElectBowlCommand.NotifyCanExecuteChanged();
        CloseTossOverlayCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanElectTossDecision))]
    private void ElectBat()
    {
        if (TossWinner is null) return;
        BattingFirst = TossWinner;
    }

    [RelayCommand(CanExecute = nameof(CanElectTossDecision))]
    private void ElectBowl()
    {
        if (TossWinner is null || TeamA is null || TeamB is null) return;
        BattingFirst = TossWinner.Id == TeamA.Id ? TeamB : TeamA;
    }

    [RelayCommand(CanExecute = nameof(CanCloseTossOverlay))]
    private void CloseTossOverlay()
    {
        if (IsCoinFlipping || BattingFirst is null) return;
        IsTossOverlayVisible = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var matchTitle = Title?.Trim() ?? string.Empty;
            if (matchTitle.Length == 0)
                throw new ArgumentException("Match title is required.");
            if (matchTitle.Length > 160)
                throw new ArgumentException("Match title must be 160 characters or fewer.");
            if (SelectedVenue is null || string.IsNullOrWhiteSpace(SelectedVenue.Name))
                throw new ArgumentException("Venue is required.");
            var venueName = SelectedVenue.Name.Trim();
            if (venueName.Length > 120)
                throw new ArgumentException("Venue must be 120 characters or fewer.");
            if (TeamA is null || TeamB is null)
                throw new InvalidOperationException("Select both teams.");
            if (TeamA.Id == TeamB.Id)
                throw new InvalidOperationException("Select two different teams.");
            if (SelectedFormat is null)
                throw new InvalidOperationException("Select a format. Add one in Formats if the list is empty.");
            OversRules.Validate(SelectedFormat.OversMode, SelectedFormat.OversLimit, BowlerMaxOvers);
            if (TossWinner is null)
                throw new InvalidOperationException("Flip the coin to decide the toss winner.");
            if (BattingFirst is null)
                throw new InvalidOperationException("Select which team bats first.");

            // Playing lists must be disjoint — same player cannot be in both XIs.
            if (!await EnsureNoSharedPlayersAsync(TeamA, TeamB).ConfigureAwait(true))
                return;

            var bowlingFirst = BattingFirst.Id == TeamA.Id ? TeamB : TeamA;
            var tossWinnerBatsFirst = TossWinner.Id == BattingFirst.Id;
            var format = SelectedFormat.Format;

            var match = await _matches.CreateAsync(new MatchDraft(
                matchTitle,
                TeamA.Id,
                TeamB.Id,
                null,
                null,
                SelectedFormat.OversLimit,
                format,
                venueName,
                TossWinner.Id,
                tossWinnerBatsFirst,
                LastBatsmanCanBatAsSingleWicket,
                BowlerMaxOvers,
                _tournamentId,
                _tournamentFixtureId,
                _seriesId,
                _seriesFixtureId,
                SelectedFormat.OversMode,
                IsLiveTelecast ? LiveStreamUrl : null,
                IsLiveTelecast,
                SelectedFormat.Id)).ConfigureAwait(true);

            if (_tournamentId is Guid tid && _tournamentFixtureId is Guid fid)
            {
                try
                {
                    await _tournaments.LinkMatchAsync(tid, fid, match.Id).ConfigureAwait(true);
                }
                catch
                {
                    /* offline — sync payload still carries TournamentFixtureId */
                }
            }

            if (_seriesId is Guid seriesId && _seriesFixtureId is Guid seriesFid)
            {
                try
                {
                    await _series.LinkMatchAsync(seriesId, seriesFid, match.Id).ConfigureAwait(true);
                }
                catch
                {
                    /* offline — sync payload still carries SeriesFixtureId */
                }
            }

            await _matches.StartInningsAsync(match.Id, BattingFirst.Id, bowlingFirst.Id).ConfigureAwait(true);
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={match.Id}").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Returns false (and shows a warning) when the two playing lists share any player.
    /// Bench/squad overlap is allowed; only the playing XI must be different.
    /// </summary>
    private async Task<bool> EnsureNoSharedPlayersAsync(TeamListItem teamA, TeamListItem teamB)
    {
        var detailA = await _teams.GetAsync(teamA.Id).ConfigureAwait(true);
        var detailB = await _teams.GetAsync(teamB.Id).ConfigureAwait(true);
        if (detailA is null || detailB is null)
            throw new InvalidOperationException("Could not load team squads. Open Teams while online and try again.");

        var squadA = detailA.Players.Select(p => p.Id).ToList();
        var squadB = detailB.Players.Select(p => p.Id).ToList();
        var playingA = TeamRoster.PlayingListOnly(squadA, detailA.PlayingPlayerIds);
        var playingB = TeamRoster.PlayingListOnly(squadB, detailB.PlayingPlayerIds);

        if (playingA.Count == 0)
        {
            ErrorMessage = $"{teamA.Name} has no playing players. Edit the team and set the playing list.";
            await Shell.Current.DisplayAlert("Playing list required", ErrorMessage, "OK").ConfigureAwait(true);
            return false;
        }

        if (playingB.Count == 0)
        {
            ErrorMessage = $"{teamB.Name} has no playing players. Edit the team and set the playing list.";
            await Shell.Current.DisplayAlert("Playing list required", ErrorMessage, "OK").ConfigureAwait(true);
            return false;
        }

        var sharedIds = playingA.Intersect(playingB).ToHashSet();
        if (sharedIds.Count == 0)
            return true;

        var byId = detailA.Players.Concat(detailB.Players)
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First().FullName);
        var shared = sharedIds
            .Select(id => byId.GetValueOrDefault(id, id.ToString("N")[..8]))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var names = string.Join("\n• ", shared);
        await Shell.Current.DisplayAlert(
                "Same players in both playing lists",
                $"These player(s) are in both {teamA.Name} and {teamB.Name} playing lists:\n\n• {names}\n\n" +
                "Remove them from one playing list. Squad/bench overlap is allowed. Match cannot start until playing lists are different.",
                "OK")
            .ConfigureAwait(true);

        ErrorMessage = "Both playing lists have the same player(s). Fix the playing lists before starting.";
        return false;
    }

    private static string PlayingHint(TeamListItem? team)
    {
        if (team is null)
            return string.Empty;
        if (team.PlayingCount > 0 && team.PlayingCount != team.PlayerCount)
            return $"{team.PlayingCount} playing from {team.PlayerCount} squad. Edit the team to change this for the next match.";
        if (team.PlayerCount > 0)
            return $"{team.PlayerCount} playing. Edit the team to set a smaller playing list for the next match.";
        return "This team has no players.";
    }
}
