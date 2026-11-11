using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public partial class TournamentTeamPick : ObservableObject
{
    public Guid TeamId { get; init; }
    public string TeamName { get; init; } = string.Empty;

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private int groupNumber = 1;
    [ObservableProperty] private string matchLoadLabel = string.Empty;

    public string GroupLabel => $"Group {GroupNumber}";

    partial void OnGroupNumberChanged(int value) => OnPropertyChanged(nameof(GroupLabel));
}

public partial class TournamentCustomPairDraft : ObservableObject
{
    public Guid TeamAId { get; init; }
    public Guid TeamBId { get; init; }

    [ObservableProperty] private string label = string.Empty;
}

public partial class TournamentEditViewModel : ObservableObject, IQueryAttributable
{
    private readonly ITournamentService _tournaments;
    private readonly ITeamService _teams;
    private readonly IVenueService _venues;
    private readonly IFormatService _formats;
    private readonly IAuthService _auth;

    private Guid? _id;

    public ObservableCollection<TournamentTeamPick> TeamPicks { get; } = new();
    public ObservableCollection<int> GroupOptions { get; } = new() { 1, 2, 4 };
    public ObservableCollection<int> QualifyOptions { get; } = new();
    public ObservableCollection<string> TypeOptions { get; } = new()
    {
        "Single Round Robin",
        "Double Round Robin",
        "Custom"
    };
    public ObservableCollection<string> KnockoutOptions { get; } = new()
    {
        "No",
        "Quarter Finals → Semi Finals → Final",
        "Playoff → Semi Final → Final",
        "Semi Finals → Final",
        "Final"
    };
    public ObservableCollection<int> MatchesPerTeamOptions { get; } = new();
    public ObservableCollection<string> CustomMethodOptions { get; } = new() { "Manual", "Auto" };
    public ObservableCollection<TournamentTeamPick> CustomTeamChoices { get; } = new();
    public ObservableCollection<TournamentCustomPairDraft> CustomPairs { get; } = new();
    public ObservableCollection<VenueListItem> VenueOptions { get; } = new();
    public ObservableCollection<FormatListItem> FormatOptions { get; } = new();

    private bool _suppressCustomRefresh;

    [ObservableProperty] private string pageTitle = "New tournament";
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private int groupCount = 1;
    [ObservableProperty] private int qualifyPerGroup = 2;
    [ObservableProperty] private string selectedType = "Single Round Robin";
    [ObservableProperty] private string selectedKnockout = "Semi Finals → Final";
    [ObservableProperty] private int matchesPerTeam = 2;
    [ObservableProperty] private string selectedCustomMethod = "Manual";
    [ObservableProperty] private string customPairingHint = string.Empty;
    [ObservableProperty] private TournamentTeamPick? pairTeamA;
    [ObservableProperty] private TournamentTeamPick? pairTeamB;
    [ObservableProperty] private VenueListItem? selectedVenue;
    [ObservableProperty] private FormatListItem? selectedFormat;
    [ObservableProperty] private int oversLimit = 8;
    [ObservableProperty] private bool noOversLimit;
    [ObservableProperty] private bool oversAreMatchTotal = true;
    [ObservableProperty] private bool lastBatsmanCanBatAsSingleWicket;
    [ObservableProperty] private int bowlerMaxOvers;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string formatHint = string.Empty;
    [ObservableProperty] private bool showGroup2;
    [ObservableProperty] private bool showGroup4;

    public bool CanWrite => _auth.CanWrite;
    public bool IsCustom =>
        string.Equals(SelectedType, "Custom", StringComparison.Ordinal);
    public bool IsCustomManual =>
        IsCustom && string.Equals(SelectedCustomMethod, "Manual", StringComparison.Ordinal);
    public bool IsCustomAuto =>
        IsCustom && string.Equals(SelectedCustomMethod, "Auto", StringComparison.Ordinal);
    public bool ShowGroupPicker => !IsCustom;
    public bool ShowCustomBuilder => IsCustom;
    public bool ShowQualifyPicker =>
        !string.Equals(SelectedKnockout, "No", StringComparison.Ordinal);
    public string AutoGenerateButtonText =>
        CustomPairs.Count == 0 ? "Generate fixtures" : "Generate again";
    public bool ShowOversFields => !NoOversLimit;
    public string OversFieldLabel => OversAreMatchTotal ? "Match overs *" : "Overs per innings *";
    public OversMode CurrentOversMode => NoOversLimit
        ? OversMode.Unlimited
        : OversAreMatchTotal
            ? OversMode.MatchTotal
            : OversMode.PerInnings;

    public TournamentEditViewModel(
        ITournamentService tournaments,
        ITeamService teams,
        IVenueService venues,
        IFormatService formats,
        IAuthService auth)
    {
        _tournaments = tournaments;
        _teams = teams;
        _venues = venues;
        _formats = formats;
        _auth = auth;
        SelectedType = TypeOptions[0];
        SelectedKnockout = KnockoutOptions[3]; // Semi Finals → Final
        RefreshQualifyOptions();
        UpdateHint();
        UpdateOversHint();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _id = null;
        PageTitle = "New tournament";
        if (query.TryGetValue("id", out var raw) && Guid.TryParse($"{raw}", out var id))
            _id = id;
    }

    partial void OnGroupCountChanged(int value)
    {
        if (IsCustom)
        {
            if (value != 1)
            {
                GroupCount = 1;
                return;
            }

            ShowGroup2 = false;
            ShowGroup4 = false;
            foreach (var p in TeamPicks.Where(t => t.GroupNumber != 1))
                p.GroupNumber = 1;
            RefreshQualifyOptions();
            UpdateHint();
            return;
        }

        if (value is not (1 or 2 or 4))
        {
            GroupCount = 2;
            return;
        }

        ShowGroup2 = value >= 2;
        ShowGroup4 = value >= 4;
        foreach (var p in TeamPicks.Where(t => t.GroupNumber > value))
            p.GroupNumber = 1;
        RefreshQualifyOptions();
        UpdateHint();
    }

    partial void OnQualifyPerGroupChanged(int value) => UpdateHint();

    partial void OnSelectedKnockoutChanged(string value)
    {
        RefreshQualifyOptions();
        OnPropertyChanged(nameof(ShowQualifyPicker));
        UpdateHint();
    }

    partial void OnSelectedTypeChanged(string value)
    {
        if (IsCustom)
        {
            GroupCount = 1;
            foreach (var p in TeamPicks)
                p.GroupNumber = 1;
            ShowGroup2 = false;
            ShowGroup4 = false;
        }
        else
        {
            CustomPairs.Clear();
            PairTeamA = null;
            PairTeamB = null;
            ShowGroup2 = GroupCount >= 2;
            ShowGroup4 = GroupCount >= 4;
        }

        if (IsCustom && string.IsNullOrWhiteSpace(SelectedCustomMethod))
            SelectedCustomMethod = "Manual";

        OnPropertyChanged(nameof(IsCustom));
        OnPropertyChanged(nameof(IsCustomManual));
        OnPropertyChanged(nameof(IsCustomAuto));
        OnPropertyChanged(nameof(ShowGroupPicker));
        OnPropertyChanged(nameof(ShowCustomBuilder));
        OnPropertyChanged(nameof(AutoGenerateButtonText));
        RefreshQualifyOptions();
        RefreshCustomUi(syncMatchesPerTeamOptions: true);
        UpdateHint();
    }

    partial void OnMatchesPerTeamChanged(int value)
    {
        if (_suppressCustomRefresh)
            return;

        // Changing degree invalidates pairings — clear without rebuilding the Picker source
        // (Clearing MatchesPerTeamOptions here hangs Android while the Picker is open).
        if (IsCustom && CustomPairs.Count > 0)
            CustomPairs.Clear();

        RefreshCustomUi(syncMatchesPerTeamOptions: false);
        UpdateHint();
        OnPropertyChanged(nameof(AutoGenerateButtonText));
    }

    partial void OnSelectedCustomMethodChanged(string value)
    {
        OnPropertyChanged(nameof(IsCustomManual));
        OnPropertyChanged(nameof(IsCustomAuto));
        OnPropertyChanged(nameof(AutoGenerateButtonText));
        ErrorMessage = null;
    }

    private void RefreshQualifyOptions()
    {
        var ko = ParseKnockout(SelectedKnockout);
        var groups = IsCustom ? 1 : GroupCount;
        var options = SchedulerEngine.QualifyOptions(groups, ko);
        QualifyOptions.Clear();
        foreach (var q in options)
            QualifyOptions.Add(q);
        if (!QualifyOptions.Contains(QualifyPerGroup))
            QualifyPerGroup = QualifyOptions.Count > 0
                ? QualifyOptions[0]
                : (ko == SchedulerKnockoutFormat.None ? 0 : 1);
    }

    private void UpdateHint()
    {
        var rr = ParseRoundRobin(SelectedType);
        var ko = ParseKnockout(SelectedKnockout);
        var groups = IsCustom ? 1 : GroupCount;
        if (QualifyOptions.Count == 0)
        {
            FormatHint =
                $"{SchedulerEngine.KnockoutLabel(ko)} does not fit {groups} group{(groups == 1 ? "" : "s")}. Pick another knockout or group count.";
            UpdateOversHint();
            return;
        }

        FormatHint = SchedulerEngine.SetupHint(
            groups,
            QualifyPerGroup,
            ko,
            rr == TournamentRoundRobinType.Custom ? SchedulerRoundRobinType.Custom
                : rr == TournamentRoundRobinType.Double ? SchedulerRoundRobinType.Double
                : SchedulerRoundRobinType.Single,
            TeamPicks.Count(t => t.IsSelected),
            rr == TournamentRoundRobinType.Custom ? MatchesPerTeam : null);
        UpdateOversHint();
    }

    partial void OnOversLimitChanged(int value) => UpdateOversHint();
    partial void OnSelectedFormatChanged(FormatListItem? value)
    {
        if (value is not null)
        {
            OversLimit = value.OversLimit > 0 ? value.OversLimit : 8;
            ApplyOversMode(value.OversMode);
            OversHint = value.Hint;
        }
        else
            UpdateOversHint();
    }
    partial void OnNoOversLimitChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowOversFields));
        OnPropertyChanged(nameof(OversFieldLabel));
        UpdateOversHint();
    }
    partial void OnOversAreMatchTotalChanged(bool value)
    {
        OnPropertyChanged(nameof(OversFieldLabel));
        UpdateOversHint();
    }

    [ObservableProperty] private string oversHint = string.Empty;

    private void UpdateOversHint()
    {
        OversHint = SelectedFormat?.Hint
            ?? "No formats in this league yet. Open Formats and add one first.";
    }

    private void ApplyOversMode(OversMode mode)
    {
        NoOversLimit = mode == OversMode.Unlimited;
        OversAreMatchTotal = mode != OversMode.PerInnings;
    }

    public async Task InitializeAsync()
    {
        if (!CanWrite)
        {
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            VenueOptions.Clear();
            try
            {
                var venues = await _venues.GetAllAsync().ConfigureAwait(true);
                foreach (var v in venues)
                    VenueOptions.Add(v);
            }
            catch
            {
                /* offline */
            }

            FormatOptions.Clear();
            try
            {
                foreach (var f in await _formats.GetAllAsync().ConfigureAwait(true))
                    FormatOptions.Add(f);
            }
            catch
            {
                /* offline */
            }

            var teams = await _teams.GetActiveAsync().ConfigureAwait(true);
            TeamPicks.Clear();
            foreach (var t in teams.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var pick = new TournamentTeamPick
                {
                    TeamId = t.Id,
                    TeamName = t.Name,
                    IsSelected = false,
                    GroupNumber = 1
                };
                pick.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(TournamentTeamPick.IsSelected))
                        RefreshCustomUi(syncMatchesPerTeamOptions: true);
                };
                TeamPicks.Add(pick);
            }

            if (_id is Guid id)
            {
                PageTitle = "Edit tournament";
                var detail = await _tournaments.GetAsync(id).ConfigureAwait(true)
                             ?? throw new InvalidOperationException("Tournament not found.");
                if (detail.Status != 0)
                    throw new InvalidOperationException("Only draft tournaments can be edited.");

                Name = detail.Name;
                SelectedType = TypeLabel(detail.RoundRobinType);
                GroupCount = detail.RoundRobinType == TournamentRoundRobinType.Custom ? 1 : detail.GroupCount;
                SelectedKnockout = KnockoutLabel(detail.KnockoutFormat);
                RefreshQualifyOptions();
                QualifyPerGroup = detail.QualifyPerGroup;
                MatchesPerTeam = detail.MatchesPerTeam > 0 ? detail.MatchesPerTeam : 2;
                SelectedVenue = VenueOptions.FirstOrDefault(v => v.Id == detail.VenueId)
                                ?? VenueOptions.FirstOrDefault();
                SelectedFormat = (detail.FormatId is Guid formatId && formatId != Guid.Empty
                                     ? FormatOptions.FirstOrDefault(f => f.Id == formatId)
                                     : null)
                                 ?? FormatOptions.FirstOrDefault(f =>
                                     (byte)f.Format == detail.Format
                                     && f.OversMode == detail.OversMode
                                     && f.OversLimit == detail.OversLimit)
                                 ?? FormatOptions.FirstOrDefault(f => (byte)f.Format == detail.Format)
                                 ?? FormatOptions.FirstOrDefault();
                OversLimit = detail.OversLimit > 0 ? detail.OversLimit : 8;
                ApplyOversMode(detail.OversMode);
                LastBatsmanCanBatAsSingleWicket = detail.LastBatsmanCanBatAsSingleWicket;
                BowlerMaxOvers = detail.BowlerMaxOvers;

                var map = detail.Teams.ToDictionary(t => t.TeamId);
                foreach (var pick in TeamPicks)
                {
                    if (!map.TryGetValue(pick.TeamId, out var row)) continue;
                    pick.IsSelected = true;
                    pick.GroupNumber = row.GroupNumber;
                }

                CustomPairs.Clear();
                if (detail.RoundRobinType == TournamentRoundRobinType.Custom)
                {
                    var names = detail.Teams.ToDictionary(t => t.TeamId, t => t.TeamName);
                    foreach (var (a, b) in ReadPairs(detail.CustomPairsJson))
                    {
                        CustomPairs.Add(new TournamentCustomPairDraft
                        {
                            TeamAId = a,
                            TeamBId = b,
                            Label = $"{names.GetValueOrDefault(a, "?")}  vs  {names.GetValueOrDefault(b, "?")}"
                        });
                    }
                }
            }
            else
            {
                SelectedVenue ??= VenueOptions.FirstOrDefault();
                SelectedFormat ??= FormatOptions.FirstOrDefault();
                if (SelectedFormat is null)
                    OversHint = "No formats in this league yet. Open Formats and add one first.";
            }

            RefreshCustomUi();
            UpdateHint();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ToggleGroup(TournamentTeamPick? pick)
    {
        if (pick is null || GroupCount < 2 || IsCustom) return;
        pick.GroupNumber = pick.GroupNumber >= GroupCount ? 1 : pick.GroupNumber + 1;
    }

    [RelayCommand]
    private void AddCustomPair()
    {
        if (!IsCustom) return;
        ErrorMessage = null;
        SelectedCustomMethod = "Manual";
        if (PairTeamA is null || PairTeamB is null)
        {
            ErrorMessage = "Pick both teams for the fixture.";
            return;
        }

        if (PairTeamA.TeamId == PairTeamB.TeamId)
        {
            ErrorMessage = "A team cannot play itself.";
            return;
        }

        var a = PairTeamA.TeamId;
        var b = PairTeamB.TeamId;
        if (CustomPairs.Any(p =>
                (p.TeamAId == a && p.TeamBId == b) || (p.TeamAId == b && p.TeamBId == a)))
        {
            ErrorMessage = "That fixture is already added.";
            return;
        }

        var degreeA = CustomPairs.Count(p => p.TeamAId == a || p.TeamBId == a);
        var degreeB = CustomPairs.Count(p => p.TeamAId == b || p.TeamBId == b);
        if (degreeA >= MatchesPerTeam || degreeB >= MatchesPerTeam)
        {
            ErrorMessage = $"Each team can have at most {MatchesPerTeam} match{(MatchesPerTeam == 1 ? "" : "es")}.";
            return;
        }

        CustomPairs.Add(new TournamentCustomPairDraft
        {
            TeamAId = a,
            TeamBId = b,
            Label = $"{PairTeamA.TeamName}  vs  {PairTeamB.TeamName}"
        });
        RefreshCustomUi(syncMatchesPerTeamOptions: false);
        OnPropertyChanged(nameof(AutoGenerateButtonText));
    }

    [RelayCommand]
    private void RemoveCustomPair(TournamentCustomPairDraft? pair)
    {
        if (pair is null) return;
        CustomPairs.Remove(pair);
        RefreshCustomUi(syncMatchesPerTeamOptions: false);
        OnPropertyChanged(nameof(AutoGenerateButtonText));
    }

    [RelayCommand]
    private void GenerateCustomFixtures()
    {
        if (!IsCustom)
            return;

        ErrorMessage = null;
        var selected = TeamPicks.Where(t => t.IsSelected).ToList();
        if (selected.Count < 2)
        {
            ErrorMessage = "Select at least two teams first.";
            return;
        }

        if (MatchesPerTeam < 1 || !MatchesPerTeamOptions.Contains(MatchesPerTeam))
        {
            ErrorMessage = MatchesPerTeamOptions.Count == 0
                ? "Not enough teams for a custom schedule."
                : $"Pick matches per team ({string.Join("/", MatchesPerTeamOptions)}).";
            return;
        }

        try
        {
            var avoid = CustomPairs.Select(p => (p.TeamAId, p.TeamBId)).ToList();
            var generated = SchedulerEngine.GenerateCustomPairings(
                selected.Select(t => t.TeamId).ToList(),
                MatchesPerTeam,
                avoid.Count > 0 ? avoid : null);

            var names = selected.ToDictionary(t => t.TeamId, t => t.TeamName);
            CustomPairs.Clear();
            foreach (var (a, b) in generated)
            {
                CustomPairs.Add(new TournamentCustomPairDraft
                {
                    TeamAId = a,
                    TeamBId = b,
                    Label = $"{names.GetValueOrDefault(a, "?")}  vs  {names.GetValueOrDefault(b, "?")}"
                });
            }

            SelectedCustomMethod = "Auto";
            RefreshCustomUi(syncMatchesPerTeamOptions: false);
            OnPropertyChanged(nameof(AutoGenerateButtonText));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            if (SelectedVenue is null)
                throw new ArgumentException("Venue is required.");
            if (SelectedFormat is null)
                throw new ArgumentException("Select a format. Add one in Formats if the list is empty.");
            OversRules.Validate(SelectedFormat.OversMode, SelectedFormat.OversLimit, BowlerMaxOvers);

            var rr = ParseRoundRobin(SelectedType);
            var selected = TeamPicks.Where(t => t.IsSelected)
                .Select(t => (t.TeamId, rr == TournamentRoundRobinType.Custom ? 1 : t.GroupNumber))
                .ToList();

            if (rr == TournamentRoundRobinType.Custom && CustomPairs.Count > 0)
            {
                var ids = selected.Select(t => t.TeamId).ToList();
                var pairs = CustomPairs.Select(p => (p.TeamAId, p.TeamBId)).ToList();
                // Draft may save with no fixtures yet; partial graphs must be finished or cleared.
                if (!SchedulerEngine.IsCustomPairingComplete(ids, MatchesPerTeam, pairs))
                    throw new ArgumentException(
                        SchedulerEngine.CustomPairingHint(ids, MatchesPerTeam, pairs)
                        + " Clear fixtures to save draft, or finish them.");
            }

            var ko = ParseKnockout(SelectedKnockout);
            var draft = new TournamentDraft(
                Name,
                rr == TournamentRoundRobinType.Custom ? 1 : GroupCount,
                QualifyPerGroup,
                SelectedVenue.Id,
                (byte)SelectedFormat.Format,
                SelectedFormat.OversLimit,
                LastBatsmanCanBatAsSingleWicket,
                BowlerMaxOvers,
                SelectedFormat.OversMode,
                selected,
                SelectedFormat.Id,
                rr,
                rr == TournamentRoundRobinType.Custom ? MatchesPerTeam : 0,
                rr == TournamentRoundRobinType.Custom ? WritePairs(CustomPairs) : null,
                (TournamentKnockoutFormat)(byte)ko);
            if (_id is Guid id)
                await _tournaments.UpdateAsync(id, draft).ConfigureAwait(true);
            else
            {
                var created = await _tournaments.CreateAsync(draft).ConfigureAwait(true);
                _id = created.Id;
            }

            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            if (_id is Guid tid)
                await Shell.Current.GoToAsync($"{nameof(Pages.TournamentDetailPage)}?id={tid}")
                    .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    private void RefreshCustomUi(bool syncMatchesPerTeamOptions = true)
    {
        if (_suppressCustomRefresh)
            return;

        var selected = TeamPicks.Where(t => t.IsSelected).ToList();
        CustomTeamChoices.Clear();
        foreach (var team in selected.OrderBy(t => t.TeamName, StringComparer.OrdinalIgnoreCase))
            CustomTeamChoices.Add(team);

        if (PairTeamA is not null && selected.All(t => t.TeamId != PairTeamA.TeamId))
            PairTeamA = null;
        if (PairTeamB is not null && selected.All(t => t.TeamId != PairTeamB.TeamId))
            PairTeamB = null;

        if (syncMatchesPerTeamOptions)
            SyncMatchesPerTeamOptions(selected.Count);

        var ids = selected.Select(t => t.TeamId).ToList();
        foreach (var pair in CustomPairs.Where(p => !ids.Contains(p.TeamAId) || !ids.Contains(p.TeamBId)).ToList())
            CustomPairs.Remove(pair);

        var pairs = CustomPairs.Select(p => (p.TeamAId, p.TeamBId)).ToList();
        foreach (var team in selected)
        {
            var used = pairs.Count(p => p.TeamAId == team.TeamId || p.TeamBId == team.TeamId);
            team.MatchLoadLabel = MatchesPerTeam > 0
                ? $"{used}/{MatchesPerTeam} matches"
                : $"{used} matches";
        }

        CustomPairingHint = IsCustom
            ? SchedulerEngine.CustomPairingHint(ids, MatchesPerTeam, pairs)
            : string.Empty;
    }

    /// <summary>
    /// Rebuilds matches-per-team choices only when the set actually changes.
    /// Never call this from <see cref="OnMatchesPerTeamChanged"/> — clearing a bound
    /// Picker ItemsSource mid-selection hangs Android.
    /// </summary>
    private void SyncMatchesPerTeamOptions(int selectedTeamCount)
    {
        var options = SchedulerEngine.CustomMatchesPerTeamOptions(selectedTeamCount);
        var same = MatchesPerTeamOptions.Count == options.Count
                   && MatchesPerTeamOptions.Zip(options, (a, b) => a == b).All(eq => eq);
        if (!same)
        {
            MatchesPerTeamOptions.Clear();
            foreach (var n in options)
                MatchesPerTeamOptions.Add(n);
        }

        if (MatchesPerTeamOptions.Count == 0)
        {
            if (MatchesPerTeam != 0)
            {
                _suppressCustomRefresh = true;
                try { MatchesPerTeam = 0; }
                finally { _suppressCustomRefresh = false; }
            }
            return;
        }

        if (!MatchesPerTeamOptions.Contains(MatchesPerTeam))
        {
            var next = MatchesPerTeamOptions.Contains(2) ? 2 : MatchesPerTeamOptions[0];
            if (MatchesPerTeam != next)
            {
                _suppressCustomRefresh = true;
                try { MatchesPerTeam = next; }
                finally { _suppressCustomRefresh = false; }
            }
        }
    }

    private static TournamentRoundRobinType ParseRoundRobin(string? text) => text switch
    {
        "Double Round Robin" => TournamentRoundRobinType.Double,
        "Custom" => TournamentRoundRobinType.Custom,
        _ => TournamentRoundRobinType.Single
    };

    private static SchedulerKnockoutFormat ParseKnockout(string? text) => text switch
    {
        "No" => SchedulerKnockoutFormat.None,
        "Quarter Finals → Semi Finals → Final" => SchedulerKnockoutFormat.QuarterFinals,
        "Playoff → Semi Final → Final" => SchedulerKnockoutFormat.Playoff,
        "Final" => SchedulerKnockoutFormat.FinalOnly,
        _ => SchedulerKnockoutFormat.SemiFinals
    };

    private static string KnockoutLabel(TournamentKnockoutFormat format) =>
        SchedulerEngine.KnockoutLabel((SchedulerKnockoutFormat)(byte)format);

    private static string TypeLabel(TournamentRoundRobinType type) => type switch
    {
        TournamentRoundRobinType.Double => "Double Round Robin",
        TournamentRoundRobinType.Custom => "Custom",
        _ => "Single Round Robin"
    };

    private static List<(Guid A, Guid B)> ReadPairs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            var list = new List<(Guid A, Guid B)>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("a", out var aEl) || !el.TryGetProperty("b", out var bEl))
                    continue;
                if (!Guid.TryParse(aEl.GetString(), out var a) || !Guid.TryParse(bEl.GetString(), out var b))
                    continue;
                list.Add((a, b));
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    private static string? WritePairs(IEnumerable<TournamentCustomPairDraft> pairs)
    {
        var list = pairs.Select(p => new { a = p.TeamAId, b = p.TeamBId }).ToList();
        return list.Count == 0 ? null : JsonSerializer.Serialize(list);
    }
}
