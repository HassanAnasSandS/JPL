using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public partial class SchedulerTeamDraft : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private int groupNumber = 1;
    [ObservableProperty] private string matchLoadLabel = string.Empty;
}

public partial class SchedulerGroupDraft : ObservableObject
{
    public int GroupNumber { get; init; }
    public ObservableCollection<SchedulerTeamDraft> Teams { get; } = new();

    [ObservableProperty] private string newTeamName = string.Empty;

    public string Title => $"Group {GroupNumber}";
    public int TeamCount => Teams.Count;
    public string CountLabel => TeamCount == 0
        ? "No teams yet"
        : TeamCount == 1 ? "1 team" : $"{TeamCount} teams";
    public string Header => $"{Title} · {CountLabel}";

    public SchedulerGroupDraft()
    {
        Teams.CollectionChanged += (_, _) => NotifyCount();
    }

    public void NotifyCount()
    {
        OnPropertyChanged(nameof(TeamCount));
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(Header));
    }
}

public partial class SchedulerCustomPairDraft : ObservableObject
{
    public Guid TeamAId { get; init; }
    public Guid TeamBId { get; init; }

    [ObservableProperty] private string label = string.Empty;
}

public partial class TournamentSchedulerEditViewModel : ObservableObject, IQueryAttributable
{
    private readonly ITournamentSchedulerService _scheduler;

    private Guid? _id;

    public ObservableCollection<int> GroupOptions { get; } = new() { 1, 2, 4 };
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
    public ObservableCollection<int> QualifyOptions { get; } = new();
    public ObservableCollection<int> MatchesPerTeamOptions { get; } = new();
    public ObservableCollection<string> CustomMethodOptions { get; } = new() { "Manual", "Auto" };
    public ObservableCollection<SchedulerGroupDraft> Groups { get; } = new();
    public ObservableCollection<SchedulerCustomPairDraft> CustomPairs { get; } = new();
    public ObservableCollection<SchedulerTeamDraft> CustomTeamChoices { get; } = new();

    private bool _suppressCustomRefresh;

    [ObservableProperty] private string pageTitle = "New scheduler";
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private int groupCount = 1;
    [ObservableProperty] private string selectedType = "Single Round Robin";
    [ObservableProperty] private string selectedKnockout = "Semi Finals → Final";
    [ObservableProperty] private int qualifyPerGroup = 2;
    [ObservableProperty] private int matchesPerTeam = 2;
    [ObservableProperty] private string selectedCustomMethod = "Manual";
    [ObservableProperty] private string setupHint = string.Empty;
    [ObservableProperty] private string customPairingHint = string.Empty;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private SchedulerTeamDraft? pairTeamA;
    [ObservableProperty] private SchedulerTeamDraft? pairTeamB;

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

    public string SaveButtonText
    {
        get
        {
            if (!IsCustom || IsCustomComplete())
                return "Save schedule";
            return CustomPairs.Count == 0
                ? "Save draft (add fixtures later)"
                : "Save schedule";
        }
    }

    public TournamentSchedulerEditViewModel(ITournamentSchedulerService scheduler)
    {
        _scheduler = scheduler;
        SelectedType = TypeOptions[0];
        SelectedKnockout = KnockoutOptions[3]; // Semi Finals → Final
        RefreshDerived();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _id = null;
        PageTitle = "New scheduler";
        if (query.TryGetValue("id", out var raw) && Guid.TryParse($"{raw}", out var id))
            _id = id;
    }

    public async Task InitializeAsync()
    {
        ErrorMessage = null;
        if (_id is not Guid id)
        {
            if (string.IsNullOrWhiteSpace(Name))
                ResetNew();
            RefreshDerived();
            return;
        }

        try
        {
            IsBusy = true;
            var detail = await _scheduler.GetAsync(id).ConfigureAwait(true);
            PageTitle = "Edit scheduler";
            Name = detail.Name;
            GroupCount = detail.RoundRobinType == SchedulerRoundRobinType.Custom ? 1 : detail.GroupCount;
            SelectedType = TypeLabel(detail.RoundRobinType);
            SelectedKnockout = SchedulerEngine.KnockoutLabel(detail.KnockoutFormat);
            QualifyPerGroup = detail.QualifyPerGroup;
            SyncGroups();
            foreach (var group in Groups)
                group.Teams.Clear();
            foreach (var team in detail.Teams.OrderBy(t => t.SortOrder))
            {
                var groupNo = Math.Clamp(team.GroupNumber, 1, GroupCount);
                GroupByNumber(groupNo)?.Teams.Add(new SchedulerTeamDraft
                {
                    Id = team.Id,
                    Name = team.Name,
                    GroupNumber = groupNo
                });
            }

            CustomPairs.Clear();
            if (detail.RoundRobinType == SchedulerRoundRobinType.Custom)
            {
                MatchesPerTeam = detail.MatchesPerTeam
                    ?? SchedulerEngine.CustomMatchesPerTeamOptions(detail.Teams.Count).FirstOrDefault();
                if (MatchesPerTeam < 1)
                    MatchesPerTeam = 2;

                var names = detail.Teams.ToDictionary(t => t.Id, t => t.Name);
                foreach (var match in detail.Matches.Where(m => !m.IsKnockout && m.TeamAId is Guid && m.TeamBId is Guid)
                             .OrderBy(m => m.Sequence))
                {
                    CustomPairs.Add(new SchedulerCustomPairDraft
                    {
                        TeamAId = match.TeamAId!.Value,
                        TeamBId = match.TeamBId!.Value,
                        Label = $"{names.GetValueOrDefault(match.TeamAId.Value, "?")}  vs  {names.GetValueOrDefault(match.TeamBId.Value, "?")}"
                    });
                }
            }

            RefreshDerived();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    partial void OnGroupCountChanged(int value) => RefreshDerived();
    partial void OnQualifyPerGroupChanged(int value) => RefreshHint();
    partial void OnMatchesPerTeamChanged(int value)
    {
        if (_suppressCustomRefresh)
            return;

        // Changing degree invalidates existing pairings — clear so Auto/Manual start fresh.
        // Do NOT rebuild MatchesPerTeamOptions here (clears bound Picker → Android hang).
        if (IsCustom && CustomPairs.Count > 0)
            CustomPairs.Clear();
        RefreshCustomUi();
        RefreshHint();
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(AutoGenerateButtonText));
    }

    partial void OnSelectedCustomMethodChanged(string value)
    {
        OnPropertyChanged(nameof(IsCustomManual));
        OnPropertyChanged(nameof(IsCustomAuto));
        OnPropertyChanged(nameof(AutoGenerateButtonText));
        ErrorMessage = null;
    }

    partial void OnSelectedTypeChanged(string value)
    {
        if (IsCustom)
        {
            GroupCount = 1;
            if (CustomPairs.Count > 0 && AllTeams().Count() < 2)
                CustomPairs.Clear();
            if (string.IsNullOrWhiteSpace(SelectedCustomMethod))
                SelectedCustomMethod = "Manual";
        }
        else
        {
            CustomPairs.Clear();
            PairTeamA = null;
            PairTeamB = null;
        }

        RefreshDerived();
        OnPropertyChanged(nameof(IsCustom));
        OnPropertyChanged(nameof(IsCustomManual));
        OnPropertyChanged(nameof(IsCustomAuto));
        OnPropertyChanged(nameof(ShowGroupPicker));
        OnPropertyChanged(nameof(ShowCustomBuilder));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(AutoGenerateButtonText));
    }

    partial void OnSelectedKnockoutChanged(string value) => RefreshDerived();

    [RelayCommand]
    private void AddTeam(SchedulerGroupDraft? group)
    {
        if (group is null)
            return;
        var n = (group.NewTeamName ?? string.Empty).Trim();
        if (n.Length == 0)
        {
            ErrorMessage = "Enter a team name.";
            return;
        }

        if (AllTeams().Any(t => string.Equals(t.Name.Trim(), n, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = $"Team \"{n}\" is already added.";
            return;
        }

        ErrorMessage = null;
        group.Teams.Add(new SchedulerTeamDraft
        {
            Id = Guid.NewGuid(),
            Name = n,
            GroupNumber = group.GroupNumber
        });
        group.NewTeamName = string.Empty;
        RefreshDerived();
    }

    [RelayCommand]
    private void RemoveTeam(SchedulerTeamDraft? team)
    {
        if (team is null)
            return;
        var group = GroupByNumber(team.GroupNumber);
        group?.Teams.Remove(team);

        if (IsCustom)
        {
            foreach (var pair in CustomPairs.Where(p => p.TeamAId == team.Id || p.TeamBId == team.Id).ToList())
                CustomPairs.Remove(pair);
            if (PairTeamA?.Id == team.Id)
                PairTeamA = null;
            if (PairTeamB?.Id == team.Id)
                PairTeamB = null;
        }

        RefreshDerived();
    }

    [RelayCommand]
    private void AddCustomPair()
    {
        if (!IsCustom)
            return;

        ErrorMessage = null;
        SelectedCustomMethod = "Manual";
        if (PairTeamA is null || PairTeamB is null)
        {
            ErrorMessage = "Pick both teams for the fixture.";
            return;
        }

        if (PairTeamA.Id == PairTeamB.Id)
        {
            ErrorMessage = "A team cannot play itself.";
            return;
        }

        var a = PairTeamA.Id;
        var b = PairTeamB.Id;
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

        CustomPairs.Add(new SchedulerCustomPairDraft
        {
            TeamAId = a,
            TeamBId = b,
            Label = $"{PairTeamA.Name}  vs  {PairTeamB.Name}"
        });
        RefreshCustomUi();
        OnPropertyChanged(nameof(SaveButtonText));
    }

    [RelayCommand]
    private void RemoveCustomPair(SchedulerCustomPairDraft? pair)
    {
        if (pair is null)
            return;
        CustomPairs.Remove(pair);
        RefreshCustomUi();
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(AutoGenerateButtonText));
    }

    [RelayCommand]
    private void GenerateCustomFixtures()
    {
        if (!IsCustom)
            return;

        ErrorMessage = null;
        var teams = AllTeams().ToList();
        if (teams.Count < 2)
        {
            ErrorMessage = "Add at least two teams first.";
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
                teams.Select(t => t.Id).ToList(),
                MatchesPerTeam,
                avoid.Count > 0 ? avoid : null);

            var names = teams.ToDictionary(t => t.Id, t => t.Name);
            CustomPairs.Clear();
            foreach (var (a, b) in generated)
            {
                CustomPairs.Add(new SchedulerCustomPairDraft
                {
                    TeamAId = a,
                    TeamBId = b,
                    Label = $"{names.GetValueOrDefault(a, "?")}  vs  {names.GetValueOrDefault(b, "?")}"
                });
            }

            SelectedCustomMethod = "Auto";
            RefreshCustomUi();
            OnPropertyChanged(nameof(SaveButtonText));
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
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var rr = ParseRoundRobin(SelectedType);
            var ko = ParseKnockout(SelectedKnockout);
            if (rr == SchedulerRoundRobinType.Custom)
            {
                GroupCount = 1;
                SyncGroups();
            }

            var groupCountToSave = rr == SchedulerRoundRobinType.Custom ? 1 : GroupCount;

            IReadOnlyList<SchedulerCustomPairInput>? pairs = null;
            int? mpt = null;
            if (rr == SchedulerRoundRobinType.Custom)
            {
                mpt = MatchesPerTeam;
                pairs = CustomPairs.Select(p => new SchedulerCustomPairInput
                {
                    TeamAId = p.TeamAId,
                    TeamBId = p.TeamBId
                }).ToList();
            }

            var id = await _scheduler.SaveAsync(
                _id,
                Name,
                groupCountToSave,
                rr,
                QualifyPerGroup,
                ko,
                AllTeams().Select(t => new SchedulerTeamInput
                {
                    Id = t.Id,
                    Name = t.Name,
                    GroupNumber = rr == SchedulerRoundRobinType.Custom ? 1 : t.GroupNumber
                }).ToList(),
                mpt,
                pairs).ConfigureAwait(true);

            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            await Shell.Current.GoToAsync($"{nameof(Pages.TournamentSchedulerDetailPage)}?id={id}")
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            try
            {
                await Shell.Current.DisplayAlert("Could not save", ex.Message, "OK").ConfigureAwait(true);
            }
            catch { /* ignore */ }
        }
        finally { IsBusy = false; }
    }

    private void ResetNew()
    {
        Name = string.Empty;
        GroupCount = 1;
        SelectedType = TypeOptions[0];
        SelectedKnockout = KnockoutOptions[3]; // Semi Finals → Final
        QualifyPerGroup = 2;
        MatchesPerTeam = 2;
        SelectedCustomMethod = "Manual";
        CustomPairs.Clear();
        PairTeamA = null;
        PairTeamB = null;
        Groups.Clear();
        SyncGroups();
    }

    private IEnumerable<SchedulerTeamDraft> AllTeams()
        => Groups.SelectMany(g => g.Teams);

    private SchedulerGroupDraft? GroupByNumber(int groupNumber)
        => Groups.FirstOrDefault(g => g.GroupNumber == groupNumber);

    private void SyncGroups()
    {
        var target = IsCustom ? 1 : GroupCount;
        if (IsCustom && GroupCount != 1)
            GroupCount = 1;

        while (Groups.Count < target)
        {
            var n = Groups.Count + 1;
            Groups.Add(new SchedulerGroupDraft { GroupNumber = n });
        }

        while (Groups.Count > target)
        {
            var extra = Groups[^1];
            var keep = Groups[0];
            foreach (var team in extra.Teams.ToList())
            {
                extra.Teams.Remove(team);
                team.GroupNumber = 1;
                keep.Teams.Add(team);
            }
            Groups.RemoveAt(Groups.Count - 1);
        }

        // Custom = single pool: every team belongs to group 1.
        if (IsCustom && Groups.Count > 0)
        {
            var keep = Groups[0];
            foreach (var team in keep.Teams)
                team.GroupNumber = 1;
        }

        foreach (var group in Groups)
            group.NotifyCount();
    }

    private void RefreshDerived()
    {
        if (IsCustom)
        {
            if (GroupCount != 1)
            {
                GroupCount = 1;
                return;
            }
        }
        else if (GroupCount is not (1 or 2 or 4))
        {
            GroupCount = 2;
            return;
        }

        SyncGroups();

        var knockout = ParseKnockout(SelectedKnockout);
        var options = SchedulerEngine.QualifyOptions(GroupCount, knockout);
        QualifyOptions.Clear();
        foreach (var q in options)
            QualifyOptions.Add(q);
        if (!QualifyOptions.Contains(QualifyPerGroup))
            QualifyPerGroup = QualifyOptions.Count > 0 ? QualifyOptions[0] : (knockout == SchedulerKnockoutFormat.None ? 0 : 1);

        RefreshCustomOptions();
        RefreshCustomUi();

        OnPropertyChanged(nameof(ShowQualifyPicker));
        OnPropertyChanged(nameof(IsCustom));
        OnPropertyChanged(nameof(IsCustomManual));
        OnPropertyChanged(nameof(IsCustomAuto));
        OnPropertyChanged(nameof(ShowGroupPicker));
        OnPropertyChanged(nameof(ShowCustomBuilder));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(AutoGenerateButtonText));
        RefreshHint();
    }

    private void RefreshCustomOptions()
    {
        var teamCount = AllTeams().Count();
        var options = SchedulerEngine.CustomMatchesPerTeamOptions(teamCount);
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

    private void RefreshCustomUi()
    {
        var teams = AllTeams().ToList();
        CustomTeamChoices.Clear();
        foreach (var team in teams.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            CustomTeamChoices.Add(team);

        if (PairTeamA is not null && teams.All(t => t.Id != PairTeamA.Id))
            PairTeamA = null;
        if (PairTeamB is not null && teams.All(t => t.Id != PairTeamB.Id))
            PairTeamB = null;

        var pairs = CustomPairs.Select(p => (p.TeamAId, p.TeamBId)).ToList();
        var ids = teams.Select(t => t.Id).ToList();
        foreach (var team in teams)
        {
            var used = pairs.Count(p => p.TeamAId == team.Id || p.TeamBId == team.Id);
            team.MatchLoadLabel = MatchesPerTeam > 0
                ? $"{used}/{MatchesPerTeam} matches"
                : $"{used} matches";
        }

        CustomPairingHint = IsCustom
            ? SchedulerEngine.CustomPairingHint(ids, MatchesPerTeam, pairs)
            : string.Empty;

        // Drop pairs that reference removed teams (safety).
        foreach (var pair in CustomPairs.Where(p => !ids.Contains(p.TeamAId) || !ids.Contains(p.TeamBId)).ToList())
            CustomPairs.Remove(pair);
    }

    private bool IsCustomComplete()
    {
        if (!IsCustom)
            return true;
        var teams = AllTeams().Select(t => t.Id).ToList();
        var pairs = CustomPairs.Select(p => (p.TeamAId, p.TeamBId)).ToList();
        return SchedulerEngine.IsCustomPairingComplete(teams, MatchesPerTeam, pairs);
    }

    private void RefreshHint()
    {
        var rr = ParseRoundRobin(SelectedType);
        var ko = ParseKnockout(SelectedKnockout);
        if (QualifyOptions.Count == 0)
        {
            SetupHint = $"{SchedulerEngine.KnockoutLabel(ko)} does not fit {GroupCount} group{(GroupCount == 1 ? "" : "s")}. Pick a larger knockout or fewer groups.";
            return;
        }

        SetupHint = SchedulerEngine.SetupHint(
            GroupCount,
            QualifyPerGroup,
            ko,
            rr,
            Groups.Sum(g => g.Teams.Count),
            rr == SchedulerRoundRobinType.Custom ? MatchesPerTeam : null);
    }

    private static SchedulerRoundRobinType ParseRoundRobin(string? text) => text switch
    {
        "Double Round Robin" => SchedulerRoundRobinType.Double,
        "Custom" => SchedulerRoundRobinType.Custom,
        _ => SchedulerRoundRobinType.Single
    };

    private static SchedulerKnockoutFormat ParseKnockout(string? text) => text switch
    {
        "No" => SchedulerKnockoutFormat.None,
        "Quarter Finals → Semi Finals → Final" => SchedulerKnockoutFormat.QuarterFinals,
        "Playoff → Semi Final → Final" => SchedulerKnockoutFormat.Playoff,
        "Final" => SchedulerKnockoutFormat.FinalOnly,
        _ => SchedulerKnockoutFormat.SemiFinals
    };

    private static string TypeLabel(SchedulerRoundRobinType type) =>
        SchedulerEngine.RoundRobinLabel(type);
}
