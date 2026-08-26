using System.Collections.ObjectModel;
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

    public string GroupLabel => $"Group {GroupNumber}";

    partial void OnGroupNumberChanged(int value) => OnPropertyChanged(nameof(GroupLabel));
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
    public ObservableCollection<int> GroupOptions { get; } = new() { 1, 2 };
    public ObservableCollection<int> QualifyOptions { get; } = new() { 1, 2 };
    public ObservableCollection<VenueListItem> VenueOptions { get; } = new();
    public ObservableCollection<FormatListItem> FormatOptions { get; } = new();

    [ObservableProperty] private string pageTitle = "New tournament";
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private int groupCount = 1;
    [ObservableProperty] private int qualifyPerGroup = 1;
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

    public bool CanWrite => _auth.CanWrite;
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
        ShowGroup2 = value >= 2;
        if (value < 2)
        {
            foreach (var p in TeamPicks.Where(t => t.GroupNumber == 2))
                p.GroupNumber = 1;
        }
        UpdateHint();
    }

    partial void OnQualifyPerGroupChanged(int value) => UpdateHint();

    private void UpdateHint()
    {
        FormatHint = (GroupCount, QualifyPerGroup) switch
        {
            (1, 1) => "1 group · top team is the champion after round robin.",
            (1, 2) => "1 group · top 2 play the Final.",
            (2, 1) => "2 groups · group winners play the Final.",
            (2, 2) => "2 groups · top 2 from each play Semi-finals → Final.",
            _ => "Round robin within each group, then knockouts."
        };
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
                TeamPicks.Add(new TournamentTeamPick
                {
                    TeamId = t.Id,
                    TeamName = t.Name,
                    IsSelected = false,
                    GroupNumber = 1
                });
            }

            if (_id is Guid id)
            {
                PageTitle = "Edit tournament";
                var detail = await _tournaments.GetAsync(id).ConfigureAwait(true)
                             ?? throw new InvalidOperationException("Tournament not found.");
                if (detail.Status != 0)
                    throw new InvalidOperationException("Only draft tournaments can be edited.");

                Name = detail.Name;
                GroupCount = detail.GroupCount;
                QualifyPerGroup = detail.QualifyPerGroup;
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
            }
            else
            {
                SelectedVenue ??= VenueOptions.FirstOrDefault();
                SelectedFormat ??= FormatOptions.FirstOrDefault();
                if (SelectedFormat is null)
                    OversHint = "No formats in this league yet. Open Formats and add one first.";
            }
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
        if (pick is null || GroupCount < 2) return;
        pick.GroupNumber = pick.GroupNumber == 1 ? 2 : 1;
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

            var selected = TeamPicks.Where(t => t.IsSelected)
                .Select(t => (t.TeamId, t.GroupNumber))
                .ToList();
            var draft = new TournamentDraft(
                Name,
                GroupCount,
                QualifyPerGroup,
                SelectedVenue.Id,
                (byte)SelectedFormat.Format,
                SelectedFormat.OversLimit,
                LastBatsmanCanBatAsSingleWicket,
                BowlerMaxOvers,
                SelectedFormat.OversMode,
                selected,
                SelectedFormat.Id);
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
}
