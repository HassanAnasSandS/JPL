using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public partial class SchedulerTeamDraft : ObservableObject
{
    public Guid? Id { get; set; }

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private int groupNumber = 1;
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

public partial class TournamentSchedulerEditViewModel : ObservableObject, IQueryAttributable
{
    private readonly ITournamentSchedulerService _scheduler;

    private Guid? _id;

    public ObservableCollection<int> GroupOptions { get; } = new() { 1, 2, 4 };
    public ObservableCollection<string> TypeOptions { get; } = new()
    {
        "Single Round Robin",
        "Double Round Robin"
    };
    public ObservableCollection<string> KnockoutOptions { get; } = new()
    {
        "Quarter Finals → Semi Finals → Final",
        "Playoff → Semi Final → Final",
        "Semi Finals → Final",
        "Final"
    };
    public ObservableCollection<int> QualifyOptions { get; } = new();
    public ObservableCollection<SchedulerGroupDraft> Groups { get; } = new();

    [ObservableProperty] private string pageTitle = "New scheduler";
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private int groupCount = 1;
    [ObservableProperty] private string selectedType = "Single Round Robin";
    [ObservableProperty] private string selectedKnockout = "Semi Finals → Final";
    [ObservableProperty] private int qualifyPerGroup = 2;
    [ObservableProperty] private string setupHint = string.Empty;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool isBusy;

    public TournamentSchedulerEditViewModel(ITournamentSchedulerService scheduler)
    {
        _scheduler = scheduler;
        SelectedType = TypeOptions[0];
        SelectedKnockout = KnockoutOptions[2];
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
            GroupCount = detail.GroupCount;
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
    partial void OnSelectedTypeChanged(string value) => RefreshHint();
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
            Name = n,
            GroupNumber = group.GroupNumber
        });
        group.NewTeamName = string.Empty;
        RefreshHint();
    }

    [RelayCommand]
    private void RemoveTeam(SchedulerTeamDraft? team)
    {
        if (team is null)
            return;
        var group = GroupByNumber(team.GroupNumber);
        group?.Teams.Remove(team);
        RefreshHint();
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
            var id = await _scheduler.SaveAsync(
                _id,
                Name,
                GroupCount,
                rr,
                QualifyPerGroup,
                ko,
                AllTeams().Select(t => new SchedulerTeamInput
                {
                    Id = t.Id,
                    Name = t.Name,
                    GroupNumber = t.GroupNumber
                }).ToList()).ConfigureAwait(true);

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
        SelectedKnockout = KnockoutOptions[2];
        QualifyPerGroup = 2;
        Groups.Clear();
        SyncGroups();
    }

    private IEnumerable<SchedulerTeamDraft> AllTeams()
        => Groups.SelectMany(g => g.Teams);

    private SchedulerGroupDraft? GroupByNumber(int groupNumber)
        => Groups.FirstOrDefault(g => g.GroupNumber == groupNumber);

    private void SyncGroups()
    {
        while (Groups.Count < GroupCount)
        {
            var n = Groups.Count + 1;
            Groups.Add(new SchedulerGroupDraft { GroupNumber = n });
        }

        while (Groups.Count > GroupCount)
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

        foreach (var group in Groups)
            group.NotifyCount();
    }

    private void RefreshDerived()
    {
        if (GroupCount is not (1 or 2 or 4))
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
            QualifyPerGroup = QualifyOptions.Count > 0 ? QualifyOptions[0] : 1;

        RefreshHint();
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

        SetupHint = SchedulerEngine.SetupHint(GroupCount, QualifyPerGroup, ko, rr, Groups.Sum(g => g.Teams.Count));
    }

    private static SchedulerRoundRobinType ParseRoundRobin(string? text) =>
        string.Equals(text, "Double Round Robin", StringComparison.Ordinal)
            ? SchedulerRoundRobinType.Double
            : SchedulerRoundRobinType.Single;

    private static SchedulerKnockoutFormat ParseKnockout(string? text) => text switch
    {
        "Quarter Finals → Semi Finals → Final" => SchedulerKnockoutFormat.QuarterFinals,
        "Playoff → Semi Final → Final" => SchedulerKnockoutFormat.Playoff,
        "Final" => SchedulerKnockoutFormat.FinalOnly,
        _ => SchedulerKnockoutFormat.SemiFinals
    };

    private static string TypeLabel(SchedulerRoundRobinType type) =>
        SchedulerEngine.RoundRobinLabel(type);
}
