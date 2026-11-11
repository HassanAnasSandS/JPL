using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Controls;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public sealed partial class StrengthTeamPick : ObservableObject
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;

    [ObservableProperty] private bool isSelected;
}

public sealed partial class StrengthPlayerPick : ObservableObject
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;

    [ObservableProperty] private bool inListA;
    [ObservableProperty] private bool inListB;
    [ObservableProperty] private bool inCompare;
    [ObservableProperty] private bool isListCaptain;

    public string DisplayName => IsListCaptain ? $"{FullName} (C)" : FullName;
    public bool CanAddToA => !InListA && !InListB && !InCompare;
    public bool CanAddToB => !InListA && !InListB && !InCompare;
    public bool CanAddToCompare => !InListA && !InListB && !InCompare;

    partial void OnInListAChanged(bool value) => NotifyAvailability();
    partial void OnInListBChanged(bool value) => NotifyAvailability();
    partial void OnInCompareChanged(bool value) => NotifyAvailability();
    partial void OnIsListCaptainChanged(bool value) => OnPropertyChanged(nameof(DisplayName));

    private void NotifyAvailability()
    {
        OnPropertyChanged(nameof(CanAddToA));
        OnPropertyChanged(nameof(CanAddToB));
        OnPropertyChanged(nameof(CanAddToCompare));
    }
}

public sealed class StrengthCompareSide
{
    public string Title { get; init; } = string.Empty;
    public Color Accent { get; init; } = Color.FromArgb("#1565C0");
    public StrengthChartCard Summary { get; init; } = new();
    public ObservableCollection<StrengthChartCard> Charts { get; } = new();
}

public partial class StrengthMeterViewModel : ObservableObject
{
    private static readonly Color[] SideAccents =
    [
        Color.FromArgb("#1565C0"),
        Color.FromArgb("#C62828"),
        Color.FromArgb("#2E7D32"),
        Color.FromArgb("#6A1B9A"),
        Color.FromArgb("#EF6C00"),
        Color.FromArgb("#00838F"),
        Color.FromArgb("#AD1457"),
        Color.FromArgb("#455A64")
    ];

    private readonly ITeamService _teams;
    private readonly IPlayerService _players;
    private readonly IStatsService _stats;
    public FormatFilterViewModel FormatFilter { get; }
    public EventScopeFilterViewModel EventScope { get; }

    private List<StrengthPlayerPick> _allPlayers = new();
    private IReadOnlyDictionary<Guid, SkillRating> _ratings = new Dictionary<Guid, SkillRating>();

    public ObservableCollection<StrengthTeamPick> TeamPicks { get; } = new();
    public ObservableCollection<StrengthPlayerPick> PlayerCandidates { get; } = new();
    public ObservableCollection<StrengthPlayerPick> ListA { get; } = new();
    public ObservableCollection<StrengthPlayerPick> ListB { get; } = new();
    public ObservableCollection<StrengthPlayerPick> ComparePlayers { get; } = new();
    public ObservableCollection<StrengthCompareSide> CompareSides { get; } = new();

    [ObservableProperty] private bool isTeamMode = true;
    /// <summary>Players mode: false = build List A/B sides; true = compare individuals.</summary>
    [ObservableProperty] private bool isPlayerIndividuals;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string playerFilter = string.Empty;
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private string resultHeadline = string.Empty;
    [ObservableProperty] private string resultDetail = string.Empty;
    [ObservableProperty] private StrengthPlayerPick? listACaptain;
    [ObservableProperty] private StrengthPlayerPick? listBCaptain;

    public bool IsPlayersMode => !IsTeamMode;
    public bool IsPlayerSides => IsPlayersMode && !IsPlayerIndividuals;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasActiveFilters => FormatFilter.HasSelection || EventScope.HasSelection;
    public int SelectedTeamCount => TeamPicks.Count(t => t.IsSelected);
    public string SelectedTeamsSummary => SelectedTeamCount == 0
        ? "Select 2 or more teams"
        : $"{SelectedTeamCount} team{(SelectedTeamCount == 1 ? "" : "s")} selected";

    public bool CanCompare => !IsBusy && (IsTeamMode
        ? SelectedTeamCount >= 2
        : IsPlayerIndividuals
            ? ComparePlayers.Count >= 2
            : ListA.Count > 0 && ListB.Count > 0 && ListACaptain is not null && ListBCaptain is not null);

    public StrengthMeterViewModel(
        ITeamService teams, IPlayerService players, IStatsService stats, IFormatService formats, ILeagueContext league)
    {
        _teams = teams;
        _players = players;
        _stats = stats;
        FormatFilter = new FormatFilterViewModel(formats);
        EventScope = new EventScopeFilterViewModel(stats, league);
        FormatFilter.Committed += (_, _) =>
        {
            OnPropertyChanged(nameof(HasActiveFilters));
            ClearResult();
            _ = LoadAsync();
        };
        FormatFilter.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FormatFilterViewModel.HasSelection) or null)
                OnPropertyChanged(nameof(HasActiveFilters));
        };
        EventScope.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(HasActiveFilters));
            ClearResult();
            _ = LoadAsync();
        };
        EventScope.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EventScopeFilterViewModel.HasSelection) or null)
                OnPropertyChanged(nameof(HasActiveFilters));
        };
        ListA.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanCompare));
        ListB.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanCompare));
        ComparePlayers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanCompare));
        TeamPicks.CollectionChanged += (_, _) => RefreshTeamSelectionUi();
    }

    partial void OnErrorMessageChanged(string? value)
        => OnPropertyChanged(nameof(HasError));

    partial void OnIsBusyChanged(bool value)
        => OnPropertyChanged(nameof(CanCompare));

    partial void OnIsTeamModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPlayersMode));
        OnPropertyChanged(nameof(IsPlayerSides));
        OnPropertyChanged(nameof(CanCompare));
        ClearResult();
        ApplyPlayerFilter();
    }

    partial void OnIsPlayerIndividualsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPlayerSides));
        OnPropertyChanged(nameof(CanCompare));
        ClearResult();
        ApplyPlayerFilter();
    }

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
    private void UsePlayerSides() => IsPlayerIndividuals = false;

    [RelayCommand]
    private void UsePlayerIndividuals() => IsPlayerIndividuals = true;

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await FormatFilter.EnsureLoadedAsync().ConfigureAwait(true);
            await EventScope.EnsureLoadedAsync().ConfigureAwait(true);

            var keepA = ListA.Select(p => p.Id).ToHashSet();
            var keepB = ListB.Select(p => p.Id).ToHashSet();
            var keepCompare = ComparePlayers.Select(p => p.Id).ToHashSet();
            var selectedTeams = TeamPicks.Where(t => t.IsSelected).Select(t => t.Id).ToHashSet();
            var capA = ListACaptain?.Id;
            var capB = ListBCaptain?.Id;

            var teamsTask = _teams.GetActiveAsync();
            var playersTask = _players.GetActiveAsync();
            var boardTask = _stats.GetLeaderboardAsync(
                formatIds: FormatFilter.SelectedIds,
                tournamentIds: EventScope.TournamentIds,
                seriesIds: EventScope.SeriesIds,
                matchIds: EventScope.MatchIds);

            await Task.WhenAll(teamsTask, playersTask, boardTask).ConfigureAwait(true);

            TeamPicks.Clear();
            foreach (var t in teamsTask.Result.OrderBy(t => t.Name))
            {
                var pick = new StrengthTeamPick
                {
                    Id = t.Id,
                    Name = t.Name,
                    IsSelected = selectedTeams.Contains(t.Id)
                };
                pick.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(StrengthTeamPick.IsSelected) or null)
                        RefreshTeamSelectionUi();
                };
                TeamPicks.Add(pick);
            }
            RefreshTeamSelectionUi();

            _ratings = StrengthScores.RateLeague(boardTask.Result);
            _allPlayers = playersTask.Result
                .OrderBy(p => p.FullName)
                .Select(p => new StrengthPlayerPick
                {
                    Id = p.Id,
                    FullName = p.FullName,
                    InListA = keepA.Contains(p.Id),
                    InListB = keepB.Contains(p.Id),
                    InCompare = keepCompare.Contains(p.Id)
                })
                .ToList();

            ListA.Clear();
            ListB.Clear();
            ComparePlayers.Clear();
            foreach (var p in _allPlayers.Where(x => x.InListA))
                ListA.Add(p);
            foreach (var p in _allPlayers.Where(x => x.InListB))
                ListB.Add(p);
            foreach (var p in _allPlayers.Where(x => x.InCompare))
                ComparePlayers.Add(p);

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

    private void RefreshTeamSelectionUi()
    {
        OnPropertyChanged(nameof(SelectedTeamCount));
        OnPropertyChanged(nameof(SelectedTeamsSummary));
        OnPropertyChanged(nameof(CanCompare));
        ClearResult();
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
    private void AddToCompare(StrengthPlayerPick? player)
    {
        if (player is null || !player.CanAddToCompare)
            return;
        player.InCompare = true;
        ComparePlayers.Add(player);
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
    private void RemoveFromCompare(StrengthPlayerPick? player)
    {
        if (player is null)
            return;
        player.InCompare = false;
        ComparePlayers.Remove(player);
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
            else if (IsPlayerIndividuals)
                CompareIndividuals();
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
        var selected = TeamPicks.Where(t => t.IsSelected).ToList();
        if (selected.Count < 2)
            throw new InvalidOperationException("Select at least two teams.");

        var sides = new List<(string Name, IReadOnlyList<Player> Players, Guid? CaptainId, Color Accent)>();
        for (var i = 0; i < selected.Count; i++)
        {
            var pick = selected[i];
            var detail = await _teams.GetAsync(pick.Id).ConfigureAwait(true)
                         ?? throw new InvalidOperationException($"Team “{pick.Name}” not found.");
            if (detail.Players.Count == 0)
                throw new InvalidOperationException($"“{pick.Name}” needs at least one player in the squad.");
            sides.Add((detail.Name, detail.Players, detail.CaptainPlayerId, AccentAt(i)));
        }

        PublishSides(sides.Select(s => BuildTeamSide(s.Name, s.Players, s.CaptainId, s.Accent)).ToList());
    }

    private void ComparePlayerLists()
    {
        if (ListA.Count == 0 || ListB.Count == 0)
            throw new InvalidOperationException("Add at least one player to each list.");
        if (ListACaptain is null || ListBCaptain is null)
            throw new InvalidOperationException("Select one captain for List A and one for List B.");

        var sideA = BuildTeamSide(
            "List A",
            ListA.Select(p => new Player { Id = p.Id, FullName = p.FullName }).ToList(),
            ListACaptain.Id,
            AccentAt(0));
        var sideB = BuildTeamSide(
            "List B",
            ListB.Select(p => new Player { Id = p.Id, FullName = p.FullName }).ToList(),
            ListBCaptain.Id,
            AccentAt(1));
        PublishSides([sideA, sideB]);
    }

    private void CompareIndividuals()
    {
        if (ComparePlayers.Count < 2)
            throw new InvalidOperationException("Add at least two players to compare.");

        var sides = new List<StrengthCompareSide>();
        for (var i = 0; i < ComparePlayers.Count; i++)
        {
            var p = ComparePlayers[i];
            var accent = AccentAt(i);
            var rating = RateId(p.Id);
            var card = BuildCard(
                p.FullName,
                "Player strength",
                rating,
                includeCaptaincy: true,
                kind: StrengthCardKind.Player,
                accentOverride: accent);
            sides.Add(new StrengthCompareSide
            {
                Title = p.FullName,
                Accent = accent,
                Summary = card
            });
        }

        PublishSides(sides);
    }

    private StrengthCompareSide BuildTeamSide(
        string name,
        IReadOnlyList<Player> players,
        Guid? captainId,
        Color accent)
    {
        var ratings = players.Select(Rate).ToList();
        var cap = captainId is Guid cid ? RateId(cid).Captaincy : (int?)null;
        var summary = BuildCard(
            name,
            $"Team strength · {players.Count} players",
            Average(ratings, cap),
            includeCaptaincy: captainId is not null,
            kind: StrengthCardKind.Team,
            accentOverride: accent);

        var side = new StrengthCompareSide
        {
            Title = name,
            Accent = accent,
            Summary = summary
        };
        foreach (var card in BuildPlayerCards(players, captainId, name))
            side.Charts.Add(card);
        return side;
    }

    private IEnumerable<StrengthChartCard> BuildPlayerCards(
        IEnumerable<Player> players,
        Guid? captainId,
        string sideName)
        => players.Select(p =>
            {
                var isCap = captainId is Guid cid && p.Id == cid;
                return BuildCard(
                    p.FullName,
                    isCap ? $"{sideName} · Captain" : sideName,
                    Rate(p),
                    includeCaptaincy: isCap,
                    kind: isCap ? StrengthCardKind.Captain : StrengthCardKind.Player);
            })
            .OrderByDescending(c => c.IsCaptain)
            .ThenByDescending(c => c.Overall)
            .ThenBy(c => c.Title);

    private void PublishSides(IReadOnlyList<StrengthCompareSide> sides)
    {
        CompareSides.Clear();
        foreach (var side in sides.OrderByDescending(s => s.Summary.Overall).ThenBy(s => s.Title))
            CompareSides.Add(side);

        SetHeadline(sides);
        HasResult = true;
    }

    private SkillRating Rate(Player p) => RateId(p.Id);

    private SkillRating RateId(Guid playerId)
        => StrengthScores.For(playerId, _ratings);

    private void SetHeadline(IReadOnlyList<StrengthCompareSide> sides)
    {
        if (sides.Count == 0)
        {
            ResultHeadline = string.Empty;
            ResultDetail = string.Empty;
            return;
        }

        var ranked = sides.OrderByDescending(s => s.Summary.Overall).ThenBy(s => s.Title).ToList();
        var top = ranked[0];
        var tied = ranked.Where(s => s.Summary.Overall == top.Summary.Overall).ToList();
        ResultHeadline = tied.Count > 1
            ? $"Evenly matched at {top.Summary.Overall}"
            : $"{top.Title} is strongest";
        ResultDetail = string.Join("  ·  ", ranked.Select(s => $"{s.Title} {s.Summary.Overall}"));
    }

    private static Color AccentAt(int index)
        => SideAccents[index % SideAccents.Length];

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        FormatFilter.ClearSelection();
        EventScope.Clear();
        OnPropertyChanged(nameof(HasActiveFilters));
        ClearResult();
        await LoadAsync().ConfigureAwait(true);
    }

    private void ClearResult()
    {
        HasResult = false;
        ResultHeadline = string.Empty;
        ResultDetail = string.Empty;
        CompareSides.Clear();
    }

    private void ApplyPlayerFilter()
    {
        var q = PlayerFilter.Trim();
        PlayerCandidates.Clear();
        foreach (var p in _allPlayers)
        {
            if (p.InListA || p.InListB || p.InCompare)
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
        StrengthCardKind kind,
        Color? accentOverride = null)
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
        var (bg, stroke, overallColor, displayTitle) = ColorsFor(kind, title, dark, accentOverride);
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
        bool dark,
        Color? accentOverride)
    {
        if (accentOverride is Color accent && kind == StrengthCardKind.Team)
        {
            return dark
                ? (Color.FromArgb("#2A2F36"), accent, accent, title)
                : (Color.FromArgb("#FFF8E1"), accent, accent, title);
        }

        if (accentOverride is Color playerAccent && kind == StrengthCardKind.Player)
        {
            return dark
                ? (Color.FromArgb("#152033"), playerAccent, playerAccent, title)
                : (Colors.White, playerAccent, playerAccent, title);
        }

        return kind switch
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
    }

    private static StrengthBarItem Bar(string label, int value, string color, string strong) => new()
    {
        Label = label,
        Value = Math.Clamp(value, 0, 100),
        Color = color,
        ColorStrong = strong
    };
}
