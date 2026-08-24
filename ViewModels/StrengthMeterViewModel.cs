using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Controls;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public sealed partial class StrengthPlayerPick : ObservableObject
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;

    [ObservableProperty] private bool inListA;
    [ObservableProperty] private bool inListB;
    [ObservableProperty] private bool isListCaptain;

    public string DisplayName => IsListCaptain ? $"{FullName} (C)" : FullName;
    public bool CanAddToA => !InListA && !InListB;
    public bool CanAddToB => !InListA && !InListB;

    partial void OnInListAChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAddToA));
        OnPropertyChanged(nameof(CanAddToB));
    }

    partial void OnInListBChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAddToA));
        OnPropertyChanged(nameof(CanAddToB));
    }

    partial void OnIsListCaptainChanged(bool value)
        => OnPropertyChanged(nameof(DisplayName));
}

public partial class StrengthMeterViewModel : ObservableObject
{
    private readonly ITeamService _teams;
    private readonly IPlayerService _players;
    private readonly IStatsService _stats;

    private List<StrengthPlayerPick> _allPlayers = new();
    private IReadOnlyDictionary<Guid, SkillRating> _ratings = new Dictionary<Guid, SkillRating>();

    public ObservableCollection<TeamListItem> TeamOptions { get; } = new();
    public ObservableCollection<StrengthPlayerPick> PlayerCandidates { get; } = new();
    public ObservableCollection<StrengthPlayerPick> ListA { get; } = new();
    public ObservableCollection<StrengthPlayerPick> ListB { get; } = new();
    public ObservableCollection<StrengthChartCard> SideACharts { get; } = new();
    public ObservableCollection<StrengthChartCard> SideBCharts { get; } = new();

    [ObservableProperty] private bool isTeamMode = true;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private TeamListItem? teamA;
    [ObservableProperty] private TeamListItem? teamB;
    [ObservableProperty] private string playerFilter = string.Empty;
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private StrengthChartCard? resultA;
    [ObservableProperty] private StrengthChartCard? resultB;
    [ObservableProperty] private string resultHeadline = string.Empty;
    [ObservableProperty] private string resultDetail = string.Empty;
    [ObservableProperty] private string sideATitle = "Side A";
    [ObservableProperty] private string sideBTitle = "Side B";
    [ObservableProperty] private StrengthPlayerPick? listACaptain;
    [ObservableProperty] private StrengthPlayerPick? listBCaptain;

    public bool IsPlayersMode => !IsTeamMode;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool CanCompare => !IsBusy && (IsTeamMode
        ? TeamA is not null && TeamB is not null && TeamA.Id != TeamB.Id
        : ListA.Count > 0 && ListB.Count > 0 && ListACaptain is not null && ListBCaptain is not null);

    public StrengthMeterViewModel(ITeamService teams, IPlayerService players, IStatsService stats)
    {
        _teams = teams;
        _players = players;
        _stats = stats;
        ListA.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanCompare));
        ListB.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanCompare));
    }

    partial void OnErrorMessageChanged(string? value)
        => OnPropertyChanged(nameof(HasError));

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(CanCompare));

    partial void OnIsTeamModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPlayersMode));
        OnPropertyChanged(nameof(CanCompare));
        ClearResult();
    }

    partial void OnTeamAChanged(TeamListItem? value)
        => OnPropertyChanged(nameof(CanCompare));

    partial void OnTeamBChanged(TeamListItem? value)
        => OnPropertyChanged(nameof(CanCompare));

    partial void OnListACaptainChanged(StrengthPlayerPick? value)
        => OnPropertyChanged(nameof(CanCompare));

    partial void OnListBCaptainChanged(StrengthPlayerPick? value)
        => OnPropertyChanged(nameof(CanCompare));

    partial void OnPlayerFilterChanged(string value)
        => ApplyPlayerFilter();

    [RelayCommand]
    private void UseTeamMode() => IsTeamMode = true;

    [RelayCommand]
    private void UsePlayersMode() => IsTeamMode = false;

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var keepA = ListA.Select(p => p.Id).ToHashSet();
            var keepB = ListB.Select(p => p.Id).ToHashSet();
            var capA = ListACaptain?.Id;
            var capB = ListBCaptain?.Id;
            var teamAId = TeamA?.Id;
            var teamBId = TeamB?.Id;

            var teamsTask = _teams.GetActiveAsync();
            var playersTask = _players.GetActiveAsync();
            var boardTask = _stats.GetLeaderboardAsync();

            await Task.WhenAll(teamsTask, playersTask, boardTask).ConfigureAwait(true);

            TeamOptions.Clear();
            foreach (var t in teamsTask.Result.OrderBy(t => t.Name))
                TeamOptions.Add(t);
            TeamA = TeamOptions.FirstOrDefault(t => t.Id == teamAId);
            TeamB = TeamOptions.FirstOrDefault(t => t.Id == teamBId);

            _ratings = StrengthScores.RateLeague(boardTask.Result);
            _allPlayers = playersTask.Result
                .OrderBy(p => p.FullName)
                .Select(p => new StrengthPlayerPick
                {
                    Id = p.Id,
                    FullName = p.FullName,
                    InListA = keepA.Contains(p.Id),
                    InListB = keepB.Contains(p.Id)
                })
                .ToList();

            ListA.Clear();
            ListB.Clear();
            foreach (var p in _allPlayers.Where(x => x.InListA))
                ListA.Add(p);
            foreach (var p in _allPlayers.Where(x => x.InListB))
                ListB.Add(p);

            ListACaptain = ListA.FirstOrDefault(p => p.Id == capA) ?? ListA.FirstOrDefault();
            ListBCaptain = ListB.FirstOrDefault(p => p.Id == capB) ?? ListB.FirstOrDefault();
            ApplyListCaptainFlags(ListA, ListACaptain);
            ApplyListCaptainFlags(ListB, ListBCaptain);

            ApplyPlayerFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — Strength Meter needs internet."
                : $"Could not load Strength Meter. {ex.Message}";
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddToListA(StrengthPlayerPick? player)
    {
        if (player is null || !player.CanAddToA)
            return;
        player.InListA = true;
        ListA.Add(player);
        if (ListACaptain is null)
            SetListACaptain(player);
        ApplyPlayerFilter();
    }

    [RelayCommand]
    private void AddToListB(StrengthPlayerPick? player)
    {
        if (player is null || !player.CanAddToB)
            return;
        player.InListB = true;
        ListB.Add(player);
        if (ListBCaptain is null)
            SetListBCaptain(player);
        ApplyPlayerFilter();
    }

    [RelayCommand]
    private void RemoveFromListA(StrengthPlayerPick? player)
    {
        if (player is null)
            return;
        var wasCaptain = ListACaptain?.Id == player.Id;
        player.IsListCaptain = false;
        player.InListA = false;
        ListA.Remove(player);
        if (wasCaptain)
            SetListACaptain(ListA.FirstOrDefault());
        ApplyPlayerFilter();
    }

    [RelayCommand]
    private void RemoveFromListB(StrengthPlayerPick? player)
    {
        if (player is null)
            return;
        var wasCaptain = ListBCaptain?.Id == player.Id;
        player.IsListCaptain = false;
        player.InListB = false;
        ListB.Remove(player);
        if (wasCaptain)
            SetListBCaptain(ListB.FirstOrDefault());
        ApplyPlayerFilter();
    }

    [RelayCommand]
    private void SetListACaptain(StrengthPlayerPick? player)
    {
        ListACaptain = player is not null && ListA.Contains(player) ? player : null;
        ApplyListCaptainFlags(ListA, ListACaptain);
    }

    [RelayCommand]
    private void SetListBCaptain(StrengthPlayerPick? player)
    {
        ListBCaptain = player is not null && ListB.Contains(player) ? player : null;
        ApplyListCaptainFlags(ListB, ListBCaptain);
    }

    private static void ApplyListCaptainFlags(IEnumerable<StrengthPlayerPick> list, StrengthPlayerPick? captain)
    {
        foreach (var p in list)
            p.IsListCaptain = captain is not null && p.Id == captain.Id;
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            ClearResult();

            if (IsTeamMode)
                await CompareTeamsAsync().ConfigureAwait(true);
            else
                ComparePlayerLists();
        }
        catch (Exception ex)
        {
            ClearResult();
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — Strength Meter needs internet."
                : ex.Message;
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CompareTeamsAsync()
    {
        if (TeamA is null || TeamB is null)
            throw new InvalidOperationException("Select two different teams.");
        if (TeamA.Id == TeamB.Id)
            throw new InvalidOperationException("Pick two different teams.");

        var a = await _teams.GetAsync(TeamA.Id).ConfigureAwait(true)
                ?? throw new InvalidOperationException("Team A not found.");
        var b = await _teams.GetAsync(TeamB.Id).ConfigureAwait(true)
                ?? throw new InvalidOperationException("Team B not found.");
        if (a.Players.Count == 0 || b.Players.Count == 0)
            throw new InvalidOperationException("Both teams need at least one player in the squad.");

        SideATitle = a.Name;
        SideBTitle = b.Name;
        FillSide(SideACharts, a.Players, a.CaptainPlayerId, a.Name);
        FillSide(SideBCharts, b.Players, b.CaptainPlayerId, b.Name);

        var ratingsA = a.Players.Select(Rate).ToList();
        var ratingsB = b.Players.Select(Rate).ToList();
        var capA = a.CaptainPlayerId is Guid cidA ? RateId(cidA).Captaincy : (int?)null;
        var capB = b.CaptainPlayerId is Guid cidB ? RateId(cidB).Captaincy : (int?)null;

        ResultA = BuildCard(
            a.Name,
            $"Team strength · {a.Players.Count} players",
            Average(ratingsA, capA),
            includeCaptaincy: a.CaptainPlayerId is not null,
            kind: StrengthCardKind.Team);
        ResultB = BuildCard(
            b.Name,
            $"Team strength · {b.Players.Count} players",
            Average(ratingsB, capB),
            includeCaptaincy: b.CaptainPlayerId is not null,
            kind: StrengthCardKind.Team);
        SetHeadline(a.Name, b.Name, ResultA.Overall, ResultB.Overall);
        HasResult = true;
    }

    private void ComparePlayerLists()
    {
        if (ListA.Count == 0 || ListB.Count == 0)
            throw new InvalidOperationException("Add at least one player to each list.");

        if (ListACaptain is null || ListBCaptain is null)
            throw new InvalidOperationException("Select one captain for List A and one for List B.");

        SideATitle = "List A";
        SideBTitle = "List B";
        FillSide(SideACharts, ListA.Select(p => new Player { Id = p.Id, FullName = p.FullName }), ListACaptain.Id, "List A");
        FillSide(SideBCharts, ListB.Select(p => new Player { Id = p.Id, FullName = p.FullName }), ListBCaptain.Id, "List B");

        var ratingsA = ListA.Select(p => RateId(p.Id)).ToList();
        var ratingsB = ListB.Select(p => RateId(p.Id)).ToList();
        var capA = RateId(ListACaptain.Id).Captaincy;
        var capB = RateId(ListBCaptain.Id).Captaincy;

        ResultA = BuildCard("List A", $"Team strength · {ListA.Count} players", Average(ratingsA, capA), includeCaptaincy: true, kind: StrengthCardKind.Team);
        ResultB = BuildCard("List B", $"Team strength · {ListB.Count} players", Average(ratingsB, capB), includeCaptaincy: true, kind: StrengthCardKind.Team);
        SetHeadline("List A", "List B", ResultA.Overall, ResultB.Overall);
        HasResult = true;
    }

    private void FillSide(
        ObservableCollection<StrengthChartCard> target,
        IEnumerable<Player> players,
        Guid? captainId,
        string sideName)
    {
        target.Clear();
        var cards = players.Select(p =>
        {
            var isCap = captainId is Guid cid && p.Id == cid;
            var rating = Rate(p);
            var subtitle = isCap ? $"{sideName} · Captain" : sideName;
            return BuildCard(
                p.FullName,
                subtitle,
                rating,
                includeCaptaincy: isCap,
                kind: isCap ? StrengthCardKind.Captain : StrengthCardKind.Player);
        });
        foreach (var card in cards
            .OrderByDescending(c => c.IsCaptain)
            .ThenByDescending(c => c.Overall)
            .ThenBy(c => c.Title))
            target.Add(card);
    }

    private SkillRating Rate(Player p) => RateId(p.Id);

    private SkillRating RateId(Guid playerId)
        => StrengthScores.For(playerId, _ratings);

    private void SetHeadline(string nameA, string nameB, int overallA, int overallB)
    {
        if (overallA > overallB)
        {
            ResultHeadline = $"{nameA} is stronger";
            ResultDetail = $"{overallA} vs {overallB} overall";
        }
        else if (overallB > overallA)
        {
            ResultHeadline = $"{nameB} is stronger";
            ResultDetail = $"{overallB} vs {overallA} overall";
        }
        else
        {
            ResultHeadline = "Evenly matched";
            ResultDetail = $"Both sides {overallA} overall";
        }
    }

    private void ClearResult()
    {
        HasResult = false;
        ResultA = null;
        ResultB = null;
        ResultHeadline = string.Empty;
        ResultDetail = string.Empty;
        SideACharts.Clear();
        SideBCharts.Clear();
    }

    private void ApplyPlayerFilter()
    {
        var q = PlayerFilter.Trim();
        PlayerCandidates.Clear();
        foreach (var p in _allPlayers)
        {
            if (p.InListA || p.InListB)
                continue;
            if (q.Length > 0 && p.FullName.Contains(q, StringComparison.OrdinalIgnoreCase) == false)
                continue;
            PlayerCandidates.Add(p);
        }
    }

    private static SkillRating Average(IReadOnlyList<SkillRating> ratings, int? captaincy)
    {
        if (ratings.Count == 0)
            return default;
        return new SkillRating(
            Avg(ratings.Select(r => r.Batting)),
            Avg(ratings.Select(r => r.Bowling)),
            Avg(ratings.Select(r => r.Fielding)),
            Avg(ratings.Select(r => r.AllRounder)),
            captaincy ?? Avg(ratings.Select(r => r.Captaincy)),
            Avg(ratings.Select(r => r.Experience)));
    }

    private static int Avg(IEnumerable<int> values)
    {
        var list = values as IList<int> ?? values.ToList();
        return list.Count == 0 ? 0 : (int)Math.Round(list.Average());
    }

    private static StrengthChartCard BuildCard(
        string title,
        string subtitle,
        SkillRating r,
        bool includeCaptaincy,
        StrengthCardKind kind)
    {
        var bars = new List<StrengthBarItem>
        {
            Bar("Batting", r.Batting, "#4FC3F7", "#0288D1"),
            Bar("Bowling", r.Bowling, "#81C784", "#2E7D32"),
            Bar("Fielding", r.Fielding, "#FFB74D", "#EF6C00"),
            Bar("All-Rounder", r.AllRounder, "#BA68C8", "#7B1FA2")
        };
        if (includeCaptaincy)
            bars.Add(Bar("Captaincy", r.Captaincy, "#FFD54F", "#F9A825"));
        bars.Add(Bar("Experience", r.Experience, "#4DD0E1", "#00838F"));

        var overall = r.Overall(includeCaptaincy);
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var (bg, stroke, overallColor, displayTitle) = ColorsFor(kind, title, dark);
        return new StrengthChartCard
        {
            Title = displayTitle,
            Subtitle = subtitle,
            Overall = overall,
            OverallText = $"Overall {overall}",
            OverallColor = overallColor,
            IsCaptain = kind == StrengthCardKind.Captain,
            IsTeam = kind == StrengthCardKind.Team,
            CardBackground = bg,
            CardStroke = stroke,
            Bars = bars
        };
    }

    private static (Color Bg, Color Stroke, Color Overall, string Title) ColorsFor(
        StrengthCardKind kind,
        string title,
        bool dark)
        => kind switch
        {
            StrengthCardKind.Team => dark
                ? (Color.FromArgb("#3D3314"), Color.FromArgb("#F9A825"), Color.FromArgb("#FFD54F"), title)
                : (Color.FromArgb("#FFF8E1"), Color.FromArgb("#F9A825"), Color.FromArgb("#F9A825"), title),
            StrengthCardKind.Captain => dark
                ? (Color.FromArgb("#2C1A3A"), Color.FromArgb("#BA68C8"), Color.FromArgb("#CE93D8"), $"{title} (C)")
                : (Color.FromArgb("#F3E5F5"), Color.FromArgb("#8E24AA"), Color.FromArgb("#7B1FA2"), $"{title} (C)"),
            _ => dark
                ? (Color.FromArgb("#152033"), Color.FromArgb("#3A4A5C"), Color.FromArgb("#4FC3F7"), title)
                : (Colors.White, Color.FromArgb("#D5DEE8"), Color.FromArgb("#1565C0"), title)
        };

    private static StrengthBarItem Bar(string label, int value, string color, string strong) => new()
    {
        Label = label,
        Value = Math.Clamp(value, 0, 100),
        Color = color,
        ColorStrong = strong
    };
}
