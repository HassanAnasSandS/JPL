using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

[QueryProperty(nameof(MatchId), "id")]
public partial class MatchScorecardViewModel : ObservableObject
{
    private readonly IMatchService _matches;
    private readonly ILiveHubClient _hub;
    private readonly ILeagueContext _league;
    private readonly IOptions<ApiOptions> _api;
    private string? _matchId;
    private Guid _loadedMatchId;
    private CancellationTokenSource? _watchCts;
    private int _tickBusy;
    private string? _boundStreamUrl;
    private string? _rawStreamUrl;

    public string? MatchId
    {
        get => _matchId;
        set
        {
            if (_matchId == value)
                return;

            _matchId = value;
            _loadedMatchId = Guid.Empty;
            _boundStreamUrl = null;
            IsLiveStreamOpen = false;
            StreamSource = null;
            IsDetailScorecardModalOpen = false;
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
    [ObservableProperty] private bool isLiveWatching;
    [ObservableProperty] private bool hasLiveStream;
    [ObservableProperty] private WebViewSource? streamSource;
    [ObservableProperty] private double streamHeight = 210;
    [ObservableProperty] private bool showFullScorecard;
    [ObservableProperty] private string currentInningsLine = string.Empty;
    [ObservableProperty] private string currentBatter = "—";
    [ObservableProperty] private string currentBowler = "—";
    [ObservableProperty] private string currentOver = "0.0";
    [ObservableProperty] private string totalOvers = "—";
    [ObservableProperty] private bool isLiveStreamOpen;
    [ObservableProperty] private bool isDetailScorecardModalOpen;
    [ObservableProperty] private string strikerName = "—";
    [ObservableProperty] private string strikerScore = "";
    [ObservableProperty] private string nonStrikerName = "—";
    [ObservableProperty] private string nonStrikerScore = "";
    [ObservableProperty] private string bowlerName = "—";
    [ObservableProperty] private string bowlerBugLine = "—";
    [ObservableProperty] private string bugTeamScore = "—";
    [ObservableProperty] private string bugOvers = "0.0";
    [ObservableProperty] private string currentRunRateText = "-";
    [ObservableProperty] private bool isChase;
    [ObservableProperty] private string chaseNeedText = string.Empty;
    [ObservableProperty] private string requiredRunRateText = "-";
    [ObservableProperty] private string captainsLine = string.Empty;

    public ObservableCollection<OverBallChip> ThisOverBalls { get; } = new();

    public bool HasTossText => !string.IsNullOrWhiteSpace(TossText);
    public bool HasPlayerOfTheMatch => !string.IsNullOrWhiteSpace(PlayerOfTheMatchText);
    public bool HasCaptainsLine => !string.IsNullOrWhiteSpace(CaptainsLine);
    public bool HasChaseDetails => IsChase && !string.IsNullOrWhiteSpace(ChaseNeedText);
    public bool ShowPlaceholder => !HasScorecard;
    public bool ShowIdlePlaceholder => !HasScorecard && !IsBusy;
    public bool HasCurrentInningsLine => !string.IsNullOrWhiteSpace(CurrentInningsLine);
    public bool ShowMainScorecard => HasScorecard && !ShowFullScorecard && !IsLiveStreamOpen;
    public bool ShowLiveButton => HasLiveStream && ShowMainScorecard;
    public bool ShowAnalyticsButton => HasScorecard && ShowMainScorecard;
    public bool ShowLiveOverlay => IsLiveStreamOpen && HasLiveStream;
    public bool ShowDetailPane => ShowFullScorecard || ShowPlaceholder;
    public bool ShowShellNavBar => !ShowLiveOverlay;
    public bool ShowDetailScorecardModal => IsDetailScorecardModalOpen && IsLiveStreamOpen;
    public string ScorecardTapHint => "Tap for full scorecard";
    public string LiveBannerText => HasLiveStream ? "LIVE · STREAM + SCORE" : "LIVE · ball by ball";

    public MatchScorecardViewModel(IMatchService matches, ILiveHubClient hub, ILeagueContext league, IOptions<ApiOptions> api)
    {
        _matches = matches;
        _hub = hub;
        _league = league;
        _api = api;
    }

    partial void OnTossTextChanged(string value)
        => OnPropertyChanged(nameof(HasTossText));

    partial void OnCaptainsLineChanged(string value)
        => OnPropertyChanged(nameof(HasCaptainsLine));

    partial void OnIsChaseChanged(bool value)
        => OnPropertyChanged(nameof(HasChaseDetails));

    partial void OnChaseNeedTextChanged(string value)
        => OnPropertyChanged(nameof(HasChaseDetails));

    partial void OnHasLiveStreamChanged(bool value)
    {
        OnPropertyChanged(nameof(LiveBannerText));
        OnPropertyChanged(nameof(ShowLiveButton));
        OnPropertyChanged(nameof(ShowAnalyticsButton));
        OnPropertyChanged(nameof(ShowLiveOverlay));
    }

    partial void OnShowFullScorecardChanged(bool value)
    {
        if (value && !IsLiveStreamOpen)
            StopStreamPlayback();
        OnPropertyChanged(nameof(ShowMainScorecard));
        OnPropertyChanged(nameof(ShowLiveButton));
        OnPropertyChanged(nameof(ShowAnalyticsButton));
        OnPropertyChanged(nameof(ShowLiveOverlay));
        OnPropertyChanged(nameof(ShowDetailPane));
        OnPropertyChanged(nameof(ShowShellNavBar));
        OnPropertyChanged(nameof(ShowDetailScorecardModal));
    }

    partial void OnIsLiveStreamOpenChanged(bool value)
    {
        if (!value)
            IsDetailScorecardModalOpen = false;
        OnPropertyChanged(nameof(ShowMainScorecard));
        OnPropertyChanged(nameof(ShowLiveButton));
        OnPropertyChanged(nameof(ShowAnalyticsButton));
        OnPropertyChanged(nameof(ShowLiveOverlay));
        OnPropertyChanged(nameof(ShowShellNavBar));
        OnPropertyChanged(nameof(ShowDetailScorecardModal));
    }

    partial void OnIsDetailScorecardModalOpenChanged(bool value)
        => OnPropertyChanged(nameof(ShowDetailScorecardModal));

    partial void OnHasScorecardChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPlaceholder));
        OnPropertyChanged(nameof(ShowIdlePlaceholder));
        OnPropertyChanged(nameof(ShowMainScorecard));
        OnPropertyChanged(nameof(ShowLiveButton));
        OnPropertyChanged(nameof(ShowAnalyticsButton));
        OnPropertyChanged(nameof(ShowDetailPane));
    }

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(ShowIdlePlaceholder));

    partial void OnCurrentInningsLineChanged(string value)
        => OnPropertyChanged(nameof(HasCurrentInningsLine));

    public Task EnsureLoadedAsync()
    {
        if (HasScorecard || IsBusy)
            return Task.CompletedTask;
        return LoadAsync();
    }

    public async Task StartLiveWatchAsync()
    {
        if (!Guid.TryParse(MatchId, out var id))
            return;

        await StopLiveWatchAsync().ConfigureAwait(true);
        await LoadCoreAsync(force: true, silent: false).ConfigureAwait(true);

        if (TeamAWon || TeamBWon || HasPlayerOfTheMatch)
            return;

        _watchCts = new CancellationTokenSource();
        var ct = _watchCts.Token;
        IsLiveWatching = true;

        try
        {
            await _hub.WatchMatchAsync(id, () => LoadCoreAsync(force: true, silent: true), ct)
                .ConfigureAwait(true);
        }
        catch
        {
            /* REST poll below */
        }

        _ = PollWhileOpenAsync(ct);
    }

    public async Task StopLiveWatchAsync()
    {
        IsLiveWatching = false;
        var cts = _watchCts;
        _watchCts = null;
        cts?.Cancel();
        cts?.Dispose();
        try { await _hub.StopAsync().ConfigureAwait(true); }
        catch { /* ignore */ }
    }

    private async Task PollWhileOpenAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(true);
                await LoadCoreAsync(force: true, silent: true).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            /* page closed */
        }
    }

    [RelayCommand]
    public Task RefreshAsync() => LoadCoreAsync(force: true, silent: false);

    [RelayCommand]
    public Task LoadAsync() => LoadCoreAsync(force: false, silent: false);

    [RelayCommand]
    private void OpenLiveStream()
    {
        if (!HasLiveStream)
            return;
        ShowFullScorecard = false;
        EnsureStreamSource();
        IsLiveStreamOpen = true;
    }

    [RelayCommand]
    private async Task OpenAnalyticsAsync()
    {
        if (!Guid.TryParse(MatchId, out var id))
            return;
        await Shell.Current.GoToAsync($"{nameof(Pages.MatchAnalyticsPage)}?id={id:D}").ConfigureAwait(true);
    }

    [RelayCommand]
    private void CloseLiveStream()
    {
        StopStreamPlayback();
        IsLiveStreamOpen = false;
    }

    [RelayCommand]
    private void OpenDetailScorecard()
    {
        if (IsLiveStreamOpen)
        {
            IsDetailScorecardModalOpen = true;
            return;
        }

        ShowFullScorecard = true;
    }

    [RelayCommand]
    private void CloseDetailScorecardModal()
        => IsDetailScorecardModalOpen = false;

    [RelayCommand]
    private void ToggleDetailScorecard()
    {
        if (ShowFullScorecard)
        {
            ShowFullScorecard = false;
            return;
        }

        OpenDetailScorecard();
    }

    private async Task LoadCoreAsync(bool force, bool silent)
    {
        if (!Guid.TryParse(MatchId, out var id))
            return;

        if (!silent && IsBusy)
            return;
        if (silent && Interlocked.Exchange(ref _tickBusy, 1) == 1)
            return;

        if (!force && !silent && _loadedMatchId == id && HasScorecard)
            return;

        try
        {
            if (!silent)
            {
                IsBusy = true;
                ErrorMessage = null;
            }

            var card = await _matches.GetMatchScorecardAsync(id).ConfigureAwait(true);
            if (card is null)
            {
                if (!silent)
                {
                    Innings.Clear();
                    CurrentInningsLine = string.Empty;
                    CurrentBatter = "—";
                    CurrentBowler = "—";
                    CurrentOver = "0.0";
                    TotalOvers = "—";
                    HasScorecard = false;
                    ErrorMessage = "Scorecard not found on this device.";
                }
                return;
            }

            Title = card.Title;
            TeamAName = card.TeamAName;
            TeamBName = card.TeamBName;
            TeamAScore = card.TeamAScore;
            TeamBScore = card.TeamBScore;
            ResultText = string.IsNullOrWhiteSpace(card.ResultText) && IsLiveWatching
                ? "LIVE"
                : card.ResultText;
            MetaLine = card.MetaLine;
            TossText = card.TossText;
            TeamAWon = card.TeamAWon;
            TeamBWon = card.TeamBWon;
            PlayerOfTheMatchText = card.PlayerOfTheMatchLine;
            CaptainsLine = card.CaptainsLine;
            OnPropertyChanged(nameof(HasPlayerOfTheMatch));
            BindStream(card);

            ReplaceInnings(card.Innings);
            CurrentInningsLine = card.Innings.Count == 0
                ? string.Empty
                : $"{card.Innings[^1].HeaderText}  {card.Innings[^1].TotalText}";
            await ApplyLivePanelAsync(id, card).ConfigureAwait(true);
            HasScorecard = true;
            _loadedMatchId = id;

            if (card.Innings.Count == 0 && !silent)
                ErrorMessage = "Waiting for the first ball…";

            if (card.IsFinished)
                IsLiveWatching = false;
        }
        catch (Exception ex)
        {
            if (!silent)
                ErrorMessage = ex.Message;
        }
        finally
        {
            if (!silent)
                IsBusy = false;
            if (silent)
                Interlocked.Exchange(ref _tickBusy, 0);
        }
    }

    private void BindStream(MatchFullScorecard card)
    {
        _rawStreamUrl = card.LiveStreamUrl;
        HasLiveStream = LiveStreamUrls.HasValue(card.LiveStreamUrl) && !card.IsFinished;
        if (!HasLiveStream)
        {
            StopStreamPlayback();
            if (IsLiveStreamOpen)
                IsLiveStreamOpen = false;
        }
        else if (IsLiveStreamOpen)
            EnsureStreamSource();
    }

    private void EnsureStreamSource()
    {
        if (!HasLiveStream || string.IsNullOrWhiteSpace(_rawStreamUrl))
        {
            StreamSource = null;
            return;
        }

        if (string.Equals(_rawStreamUrl, _boundStreamUrl ?? string.Empty, StringComparison.Ordinal)
            && StreamSource is not null)
            return;

        _boundStreamUrl = _rawStreamUrl;
        var watchPage = InAppWatchPageUrl();
        StreamSource = !string.IsNullOrWhiteSpace(watchPage)
            ? new UrlWebViewSource { Url = watchPage }
            : LiveStreamUrls.ToWebViewSource(_rawStreamUrl, InAppWatchOrigin());
    }

    private void StopStreamPlayback()
    {
        StreamSource = null;
        _boundStreamUrl = null;
    }

    private string? InAppWatchOrigin()
    {
        var root = (_api.Value.BaseUrl ?? string.Empty).TrimEnd('/');
        return string.IsNullOrWhiteSpace(root) ? null : root;
    }

    private string? InAppWatchPageUrl()
    {
        if (!Guid.TryParse(MatchId, out var matchId)
            || _league.CurrentLeagueId is not Guid leagueId
            || leagueId == Guid.Empty)
            return null;

        var root = InAppWatchOrigin();
        return string.IsNullOrWhiteSpace(root)
            ? null
            : $"{root}/watch/{leagueId:D}/{matchId:D}?video=1";
    }

    private void ReplaceInnings(IReadOnlyList<InningsScorecardSection> sections)
    {
        Innings.Clear();
        foreach (var inn in sections)
            Innings.Add(inn);
    }

    private async Task ApplyLivePanelAsync(Guid matchId, MatchFullScorecard card)
    {
        LiveScoreSnapshot? live = null;
        try { live = await _matches.GetLiveSnapshotAsync(matchId).ConfigureAwait(true); }
        catch { /* watchers without a local scoring row */ }

        var inn = card.Innings.LastOrDefault();

        string NameOf(Guid? id)
        {
            if (id is not Guid g || g == Guid.Empty)
                return "—";
            var bat = inn?.Batting.FirstOrDefault(b => b.PlayerId == g);
            if (bat is not null)
                return bat.Name;
            var bowl = inn?.Bowling.FirstOrDefault(b => b.PlayerId == g);
            return bowl?.Name ?? "—";
        }

        if (live is not null)
        {
            var bat = inn?.Batting.FirstOrDefault(b => b.PlayerId == live.StrikerId);
            CurrentBatter = bat is not null
                ? $"{bat.Name}  {bat.Runs}({bat.Balls})"
                : NameOf(live.StrikerId);
            var ns = inn?.Batting.FirstOrDefault(b => b.PlayerId == live.NonStrikerId && b.PlayerId != live.StrikerId);
            var bowl = inn?.Bowling.FirstOrDefault(b => b.PlayerId == live.BowlerId);
            CurrentBowler = bowl is not null
                ? $"{bowl.Name}  {bowl.FiguresText}"
                : NameOf(live.BowlerId);
            CurrentOver = $"{live.Overs}.{live.BallsInCurrentOver}";
            TotalOvers = live.OversMode == OversMode.Unlimited
                ? "Unlimited"
                : live.OversLimit.ToString();
            BugTeamScore = inn is null
                ? $"{live.Runs}/{live.Wickets}"
                : $"{inn.BattingTeamName} {live.Runs}/{live.Wickets}";
            BugOvers = CurrentOver;
            CurrentRunRateText = live.CurrentRunRate.ToString("0.00");
            ApplyBugNames(
                CurrentBatter,
                ns is not null ? $"{ns.Name}  {ns.Runs}({ns.Balls})" : NameOf(live.NonStrikerId),
                CurrentBowler,
                bowl);
            ReplaceThisOver(card.ThisOver);
            ApplyChase(live, card);
            return;
        }

        CurrentBatter = string.IsNullOrWhiteSpace(card.CurrentBatter) ? "—" : card.CurrentBatter;
        CurrentBowler = string.IsNullOrWhiteSpace(card.CurrentBowler) ? "—" : card.CurrentBowler;
        CurrentOver = string.IsNullOrWhiteSpace(card.CurrentOver) ? "0.0" : card.CurrentOver;
        TotalOvers = string.IsNullOrWhiteSpace(card.TotalOvers)
            ? InferTotalOvers(card.MetaLine)
            : card.TotalOvers;
        BugTeamScore = string.IsNullOrWhiteSpace(card.BugTeamScore) ? CurrentInningsLine : card.BugTeamScore;
        BugOvers = CurrentOver;
        CurrentRunRateText = string.IsNullOrWhiteSpace(card.CurrentRunRate) ? "-" : card.CurrentRunRate;
        ApplyBugNames(CurrentBatter, card.CurrentNonStriker, CurrentBowler, inn?.Bowling.LastOrDefault());
        ReplaceThisOver(card.ThisOver);
        ApplyChase(null, card);
    }

    private void ApplyChase(LiveScoreSnapshot? live, MatchFullScorecard card)
    {
        if (card.IsFinished)
        {
            IsChase = false;
            ChaseNeedText = string.Empty;
            RequiredRunRateText = "-";
            return;
        }

        if (live is { IsChase: true, RunsRequired: int need })
        {
            IsChase = true;
            if (live.OversMode == OversMode.Unlimited)
            {
                ChaseNeedText = need == 0
                    ? $"Target {live.Target} achieved"
                    : $"Need {need} runs  ·  Target {live.Target}";
                RequiredRunRateText = "-";
            }
            else
            {
                var oversLeft = $"{live.BallsRemaining / 6}.{live.BallsRemaining % 6}";
                ChaseNeedText = need == 0
                    ? $"Target {live.Target} achieved"
                    : $"Need {need} from {live.BallsRemaining} balls ({oversLeft} ov)  ·  Target {live.Target}";
                RequiredRunRateText = live.RequiredRunRate is double rrr
                    ? rrr.ToString("0.00")
                    : "-";
            }
            return;
        }

        IsChase = card.IsChase;
        ChaseNeedText = card.ChaseNeedText ?? string.Empty;
        RequiredRunRateText = string.IsNullOrWhiteSpace(card.RequiredRunRate) ? "-" : card.RequiredRunRate;
    }

    private void ApplyBugNames(string strikerLine, string? nonStrikerLine, string bowlerLine, BowlingCardRow? bowl)
    {
        var (sn, ss) = SplitNameScore(strikerLine);
        StrikerName = sn;
        StrikerScore = ss;
        var (nn, nsc) = SplitNameScore(string.IsNullOrWhiteSpace(nonStrikerLine) ? "—" : nonStrikerLine);
        NonStrikerName = nn;
        NonStrikerScore = nsc;
        var (bn, _) = SplitNameScore(bowlerLine);
        BowlerName = bn;
        BowlerBugLine = bowl is null
            ? bowlerLine
            : $"{bowl.RunsConceded}-{bowl.Wickets}  {bowl.OversText}";
    }

    private void ReplaceThisOver(IEnumerable<OverBallMark>? marks)
    {
        ThisOverBalls.Clear();
        if (marks is null)
            return;
        foreach (var mark in marks)
            ThisOverBalls.Add(OverBallChip.From(mark));
    }

    private static (string Name, string Score) SplitNameScore(string line)
    {
        var text = (line ?? "—").Trim();
        var i = text.LastIndexOf(' ');
        if (i <= 0)
            return (text, "");
        var right = text[(i + 1)..];
        if (right.Contains('(', StringComparison.Ordinal) || right.Contains('-', StringComparison.Ordinal))
            return (text[..i].Trim(), right);
        return (text, "");
    }

    private static string InferTotalOvers(string metaLine)
    {
        if (string.IsNullOrWhiteSpace(metaLine))
            return "—";
        if (metaLine.Contains("unlimited", StringComparison.OrdinalIgnoreCase))
            return "Unlimited";
        var parts = metaLine.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1] : "—";
    }
}

public sealed class OverBallChip
{
    public string Text { get; init; } = "•";
    public Color Background { get; init; } = Colors.White;
    public Color Foreground { get; init; } = Color.FromArgb("#0D1B2A");

    public static OverBallChip From(OverBallMark mark)
    {
        var kind = mark.Kind ?? "dot";
        return kind switch
        {
            "four" => new OverBallChip { Text = mark.Text, Background = Color.FromArgb("#E6C84A"), Foreground = Color.FromArgb("#0D1B2A") },
            "six" => new OverBallChip { Text = mark.Text, Background = Color.FromArgb("#2BBBAD"), Foreground = Colors.White },
            "wicket" => new OverBallChip { Text = mark.Text, Background = Color.FromArgb("#C62828"), Foreground = Colors.White },
            "extra" => new OverBallChip { Text = mark.Text, Background = Color.FromArgb("#1565C0"), Foreground = Colors.White },
            "run" => new OverBallChip { Text = mark.Text, Background = Colors.White, Foreground = Color.FromArgb("#0D1B2A") },
            _ => new OverBallChip { Text = string.IsNullOrWhiteSpace(mark.Text) ? "•" : mark.Text, Background = Colors.White, Foreground = Color.FromArgb("#0D1B2A") }
        };
    }
}
