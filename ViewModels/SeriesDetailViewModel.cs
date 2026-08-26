using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public partial class SeriesDetailViewModel : ObservableObject, IQueryAttributable
{
    private readonly ISeriesService _series;
    private readonly IMatchService _matches;
    private readonly IAuthService _auth;

    private Guid _id;
    private Guid _venueId;
    private Guid? _formatId;
    private byte _format;
    private int _oversLimit = 8;
    private byte _oversMode;
    private bool _lastBatsmanCanBat;
    private int _bowlerMaxOvers;

    public ObservableCollection<SeriesStandingItem> Standings { get; } = new();
    public ObservableCollection<SeriesFixtureItem> Fixtures { get; } = new();

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string metaLine = string.Empty;
    [ObservableProperty] private string matchDefaultsLine = string.Empty;
    [ObservableProperty] private string scoreLine = string.Empty;
    [ObservableProperty] private string winnerLine = string.Empty;
    [ObservableProperty] private bool hasWinner;
    [ObservableProperty] private bool isDraft;
    [ObservableProperty] private bool canSave;
    [ObservableProperty] private bool hasStandings;
    [ObservableProperty] private bool hasFixtures;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;

    public bool CanWrite => _auth.CanWrite;
    public bool ShowNoFixtures => !HasFixtures;

    public SeriesDetailViewModel(ISeriesService series, IMatchService matches, IAuthService auth)
    {
        _series = series;
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
            var detail = await _series.GetAsync(_id).ConfigureAwait(true)
                         ?? throw new InvalidOperationException("Series not found.");
            Bind(detail);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    private void Bind(SeriesDetail detail)
    {
        Name = detail.Name;
        StatusText = detail.StatusText;
        MetaLine = $"{detail.TeamAName} vs {detail.TeamBName} · {detail.MatchCount} matches";
        _venueId = detail.VenueId;
        _formatId = detail.FormatId;
        _format = detail.Format;
        _oversLimit = detail.OversLimit;
        _oversMode = (byte)detail.OversMode;
        _lastBatsmanCanBat = detail.LastBatsmanCanBatAsSingleWicket;
        _bowlerMaxOvers = detail.BowlerMaxOvers;
        var lastBat = detail.LastBatsmanCanBatAsSingleWicket ? "Last bat ON" : "Last bat OFF";
        var bowlerMax = detail.BowlerMaxOvers <= 0 ? "no bowler max" : $"bowler max {detail.BowlerMaxOvers}";
        MatchDefaultsLine =
            $"{detail.VenueName} · {detail.FormatText} · {OversRules.ShortLabel(detail.OversMode, detail.OversLimit)} · {lastBat} · {bowlerMax}";
        ScoreLine = string.IsNullOrWhiteSpace(detail.ScoreLine)
            ? $"{detail.TeamAName} vs {detail.TeamBName}"
            : detail.ScoreLine;
        HasWinner = !string.IsNullOrWhiteSpace(detail.WinnerTeamName);
        WinnerLine = detail.IsTied
            ? "Series tied"
            : HasWinner ? $"Winner — {detail.WinnerTeamName}" : string.Empty;
        IsDraft = detail.Status == 0;
        CanSave = CanWrite && IsDraft;

        Standings.Clear();
        foreach (var row in detail.Standings)
            Standings.Add(row);
        HasStandings = Standings.Count > 0;

        Fixtures.Clear();
        foreach (var f in detail.Fixtures)
        {
            if (!CanWrite)
            {
                f.CanStart = false;
                f.CanResume = false;
            }
            Fixtures.Add(f);
        }
        HasFixtures = Fixtures.Count > 0;
        OnPropertyChanged(nameof(ShowNoFixtures));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanSave || IsBusy) return;
        var ok = await Shell.Current.DisplayAlert(
            "Save series",
            "Save locks this series: the schedule is created, Edit/Delete stop, and matches can be started one by one.\n\nContinue?",
            "Save",
            "Cancel").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            IsBusy = true;
            var detail = await _series.GenerateAsync(_id).ConfigureAwait(true);
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
        await Shell.Current.GoToAsync($"{nameof(Pages.SeriesEditPage)}?id={_id}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task StartFixtureAsync(SeriesFixtureItem? fixture)
    {
        if (fixture is null || !CanWrite) return;
        if (IsDraft)
        {
            await Shell.Current.DisplayAlert(
                "Draft series",
                "Save the series first. Matches cannot start while it is still a draft.",
                "OK").ConfigureAwait(true);
            return;
        }
        if (!fixture.CanStart)
        {
            await Shell.Current.DisplayAlert("Not ready", "This fixture cannot start yet.", "OK")
                .ConfigureAwait(true);
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
                await Shell.Current.GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={active.Id}")
                    .ConfigureAwait(true);
                return;
            }

            await Shell.Current.DisplayAlert(
                "Match in progress",
                $"Only one match can run at a time on this device. Finish \"{active.Title}\" first.",
                "Continue").ConfigureAwait(true);
            await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={active.Id}")
                .ConfigureAwait(true);
            return;
        }

        var matchTitle = $"{Name.Trim()} : {fixture.TeamAName} vs {fixture.TeamBName} ({fixture.Label})";
        var title = Uri.EscapeDataString(matchTitle);
        await Shell.Current.GoToAsync(
                $"{nameof(Pages.MatchEditPage)}" +
                $"?seriesId={_id}&fixtureId={fixture.Id}" +
                $"&teamAId={fixture.TeamAId}&teamBId={fixture.TeamBId}&title={title}" +
                $"&venueId={_venueId}&formatId={_formatId}&format={_format}&overs={_oversLimit}&oversMode={_oversMode}" +
                $"&lastBat={(_lastBatsmanCanBat ? 1 : 0)}&bowlerMax={_bowlerMaxOvers}" +
                $"&lockSettings=1")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResumeFixtureAsync(SeriesFixtureItem? fixture)
    {
        if (fixture?.MatchId is not Guid matchId) return;

        if (!await _matches.IsScoredOnThisDeviceAsync(matchId).ConfigureAwait(true))
        {
            await Shell.Current.GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={matchId}")
                .ConfigureAwait(true);
            return;
        }

        var active = await _matches.GetActiveMatchAsync().ConfigureAwait(true);
        if (active is not null && active.Id != matchId)
        {
            if (!active.IsScoringOwner)
            {
                await Shell.Current.DisplayAlert(
                    "View only",
                    ScoringOwnership.OtherAccountMessage,
                    "OK").ConfigureAwait(true);
                await Shell.Current.GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={active.Id}")
                    .ConfigureAwait(true);
                return;
            }

            await Shell.Current.DisplayAlert(
                "Match in progress",
                $"Only one match can run at a time on this device. Finish \"{active.Title}\" first.",
                "Continue").ConfigureAwait(true);
            await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={active.Id}")
                .ConfigureAwait(true);
            return;
        }

        await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={matchId}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenScorecardAsync(SeriesFixtureItem? fixture)
    {
        if (fixture?.MatchId is not Guid matchId) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={matchId}")
            .ConfigureAwait(true);
    }
}
