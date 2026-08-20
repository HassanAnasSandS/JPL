using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class TournamentStandingGroup
{
    public string Title { get; init; } = string.Empty;
    public bool ShowTitle { get; init; }
    public ObservableCollection<TournamentStandingItem> Rows { get; } = new();
}

public sealed class TournamentFixtureGroup
{
    public string Title { get; init; } = string.Empty;
    public ObservableCollection<TournamentFixtureItem> Fixtures { get; } = new();
}

public partial class TournamentDetailViewModel : ObservableObject, IQueryAttributable
{
    private readonly ITournamentService _tournaments;
    private readonly IMatchService _matches;
    private readonly IAuthService _auth;

    private Guid _id;

    public ObservableCollection<TournamentStandingGroup> StandingGroups { get; } = new();
    public ObservableCollection<TournamentFixtureGroup> FixtureGroups { get; } = new();
    public ObservableCollection<TournamentTeamItem> Teams { get; } = new();

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string metaLine = string.Empty;
    [ObservableProperty] private string matchDefaultsLine = string.Empty;
    [ObservableProperty] private string winnerLine = string.Empty;
    [ObservableProperty] private bool hasWinner;
    [ObservableProperty] private bool isDraft;
    [ObservableProperty] private bool canSave;
    [ObservableProperty] private bool hasStandings;
    [ObservableProperty] private bool hasFixtures;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private byte status;

    private Guid _venueId;
    private byte _format;
    private int _oversLimit = 8;
    private byte _oversMode;
    private bool _lastBatsmanCanBat;
    private int _bowlerMaxOvers;

    public bool CanWrite => _auth.CanWrite;
    public bool ShowNoFixtures => !HasFixtures;

    public TournamentDetailViewModel(ITournamentService tournaments, IMatchService matches, IAuthService auth)
    {
        _tournaments = tournaments;
        _matches = matches;
        _auth = auth;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var raw) && Guid.TryParse($"{raw}", out var id))
            _id = id;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_id == Guid.Empty || IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var detail = await _tournaments.GetAsync(_id).ConfigureAwait(true)
                         ?? throw new InvalidOperationException("Tournament not found.");
            Bind(detail);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    private void Bind(TournamentDetail detail)
    {
        Name = detail.Name;
        Status = detail.Status;
        StatusText = detail.StatusText;
        MetaLine =
            $"Round robin · {detail.GroupCount} group{(detail.GroupCount == 1 ? "" : "s")} · qualify top {detail.QualifyPerGroup}";
        _venueId = detail.VenueId;
        _format = detail.Format;
        _oversLimit = detail.OversLimit;
        _oversMode = (byte)detail.OversMode;
        _lastBatsmanCanBat = detail.LastBatsmanCanBatAsSingleWicket;
        _bowlerMaxOvers = detail.BowlerMaxOvers;
        var lastBat = detail.LastBatsmanCanBatAsSingleWicket ? "Last bat ON" : "Last bat OFF";
        var bowlerMax = detail.BowlerMaxOvers <= 0 ? "no bowler max" : $"bowler max {detail.BowlerMaxOvers}";
        MatchDefaultsLine =
            $"{detail.VenueName} · {detail.FormatText} · {OversRules.ShortLabel(detail.OversMode, detail.OversLimit)} · {lastBat} · {bowlerMax}";
        HasWinner = !string.IsNullOrWhiteSpace(detail.WinnerTeamName);
        WinnerLine = HasWinner ? $"Champion — {detail.WinnerTeamName}" : string.Empty;
        IsDraft = detail.Status == 0;
        CanSave = CanWrite && IsDraft;

        Teams.Clear();
        foreach (var t in detail.Teams) Teams.Add(t);

        StandingGroups.Clear();
        foreach (var g in detail.Standings
                     .GroupBy(s => s.GroupNumber)
                     .OrderBy(g => g.Key))
        {
            var section = new TournamentStandingGroup
            {
                Title = detail.GroupCount <= 1 ? string.Empty : $"Group {g.Key}",
                ShowTitle = detail.GroupCount > 1
            };
            foreach (var row in g.OrderBy(s => s.Rank).ThenBy(s => s.TeamName))
                section.Rows.Add(row);
            StandingGroups.Add(section);
        }
        HasStandings = StandingGroups.Count > 0;

        FixtureGroups.Clear();
        var fixtures = detail.Fixtures.ToList();
        foreach (var g in fixtures
                     .Where(f => string.Equals(f.StageText, "Group", StringComparison.OrdinalIgnoreCase)
                                 || f.GroupNumber is not null)
                     .GroupBy(f => f.GroupNumber ?? 0)
                     .Where(g => g.Key > 0)
                     .OrderBy(g => g.Key))
        {
            var section = new TournamentFixtureGroup
            {
                Title = detail.GroupCount <= 1 ? "Group matches" : $"Group {g.Key}"
            };
            foreach (var f in g.OrderBy(x => x.Label))
                section.Fixtures.Add(f);
            FixtureGroups.Add(section);
        }

        var knockouts = fixtures
            .Where(f => !string.Equals(f.StageText, "Group", StringComparison.OrdinalIgnoreCase)
                        && f.GroupNumber is null)
            .OrderBy(f => StageOrder(f.StageText))
            .ThenBy(f => f.Label)
            .ToList();
        if (knockouts.Count > 0)
        {
            var section = new TournamentFixtureGroup { Title = "Knockouts" };
            foreach (var f in knockouts)
                section.Fixtures.Add(f);
            FixtureGroups.Add(section);
        }

        var shown = FixtureGroups.SelectMany(g => g.Fixtures).Select(f => f.Id).ToHashSet();
        var leftover = fixtures.Where(f => !shown.Contains(f.Id)).OrderBy(f => f.Label).ToList();
        if (leftover.Count > 0)
        {
            var section = new TournamentFixtureGroup { Title = "Schedule" };
            foreach (var f in leftover)
                section.Fixtures.Add(f);
            FixtureGroups.Add(section);
        }

        HasFixtures = FixtureGroups.Count > 0;

        if (!CanWrite)
        {
            foreach (var f in FixtureGroups.SelectMany(g => g.Fixtures))
            {
                f.CanStart = false;
                f.CanResume = false;
            }
        }
    }

    partial void OnHasFixturesChanged(bool value)
        => OnPropertyChanged(nameof(ShowNoFixtures));

    private static int StageOrder(string stage) => stage switch
    {
        "Semi-final" or "SemiFinal" => 1,
        "Final" => 2,
        _ => 0
    };

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanSave || IsBusy) return;
        var ok = await Shell.Current.DisplayAlert(
            "Save tournament",
            "Save locks this tournament: schedule is created, Edit/Delete stop, and fixtures can be started.\n\nContinue?",
            "Save",
            "Cancel").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            IsBusy = true;
            var detail = await _tournaments.GenerateAsync(_id).ConfigureAwait(true);
            Bind(detail);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await Shell.Current.DisplayAlert("Save failed", ex.Message, "OK").ConfigureAwait(true);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (!IsDraft || !CanWrite) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.TournamentEditPage)}?id={_id}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task StartFixtureAsync(TournamentFixtureItem? fixture)
    {
        if (fixture is null || !CanWrite) return;
        if (IsDraft)
        {
            await Shell.Current.DisplayAlert(
                "Draft tournament",
                "Save the tournament first. Matches cannot start while it is still a draft.",
                "OK").ConfigureAwait(true);
            return;
        }
        if (!fixture.CanStart || fixture.TeamAId is null || fixture.TeamBId is null)
        {
            await Shell.Current.DisplayAlert(
                "Not ready",
                "Both teams must be known before this fixture can start.",
                "OK").ConfigureAwait(true);
            return;
        }

        var active = await _matches.GetActiveMatchAsync().ConfigureAwait(true);
        if (active is not null)
        {
            await Shell.Current.DisplayAlert(
                "Match in progress",
                $"Only one match can run at a time in this league on this device. Finish or complete \"{active.Title}\" first.",
                "Continue").ConfigureAwait(true);
            await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={active.Id}")
                .ConfigureAwait(true);
            return;
        }

        var stage = string.IsNullOrWhiteSpace(fixture.Label) ? fixture.StageText : fixture.Label.Trim();
        var matchTitle = string.IsNullOrWhiteSpace(stage)
            ? $"{Name.Trim()} : {fixture.TeamAName} vs {fixture.TeamBName}"
            : $"{Name.Trim()} : {fixture.TeamAName} vs {fixture.TeamBName} ({stage})";
        var title = Uri.EscapeDataString(matchTitle);
        await Shell.Current.GoToAsync(
                $"{nameof(Pages.MatchEditPage)}" +
                $"?tournamentId={_id}&fixtureId={fixture.Id}" +
                $"&teamAId={fixture.TeamAId}&teamBId={fixture.TeamBId}&title={title}" +
                $"&venueId={_venueId}&format={_format}&overs={_oversLimit}&oversMode={_oversMode}" +
                $"&lastBat={(_lastBatsmanCanBat ? 1 : 0)}&bowlerMax={_bowlerMaxOvers}" +
                $"&lockSettings=1")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResumeFixtureAsync(TournamentFixtureItem? fixture)
    {
        if (fixture?.MatchId is not Guid matchId) return;

        var active = await _matches.GetActiveMatchAsync().ConfigureAwait(true);
        if (active is not null && active.Id != matchId)
        {
            await Shell.Current.DisplayAlert(
                "Match in progress",
                $"Only one match can run at a time in this league on this device. Finish or complete \"{active.Title}\" first.",
                "Continue").ConfigureAwait(true);
            await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={active.Id}")
                .ConfigureAwait(true);
            return;
        }

        await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={matchId}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenScorecardAsync(TournamentFixtureItem? fixture)
    {
        if (fixture?.MatchId is not Guid matchId) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={matchId}")
            .ConfigureAwait(true);
    }
}
