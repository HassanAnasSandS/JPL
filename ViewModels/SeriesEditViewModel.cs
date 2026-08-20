using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public partial class SeriesEditViewModel : ObservableObject, IQueryAttributable
{
    private readonly ISeriesService _series;
    private readonly ITeamService _teams;
    private readonly IVenueService _venues;
    private readonly IAuthService _auth;

    private Guid? _id;

    public ObservableCollection<TeamListItem> TeamOptions { get; } = new();
    public ObservableCollection<int> MatchCountOptions { get; } = new();
    public ObservableCollection<VenueListItem> VenueOptions { get; } = new();
    public ObservableCollection<SelectOption<MatchFormat>> FormatOptions { get; } = new();

    [ObservableProperty] private string pageTitle = "New series";
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private int matchCount = 3;
    [ObservableProperty] private TeamListItem? selectedTeamA;
    [ObservableProperty] private TeamListItem? selectedTeamB;
    [ObservableProperty] private VenueListItem? selectedVenue;
    [ObservableProperty] private SelectOption<MatchFormat>? selectedFormat;
    [ObservableProperty] private int oversLimit = 8;
    [ObservableProperty] private bool noOversLimit;
    [ObservableProperty] private bool oversAreMatchTotal = true;
    [ObservableProperty] private bool lastBatsmanCanBatAsSingleWicket;
    [ObservableProperty] private int bowlerMaxOvers;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string oversHint = string.Empty;

    public bool CanWrite => _auth.CanWrite;
    public bool ShowOversFields => !NoOversLimit;
    public string OversFieldLabel => OversAreMatchTotal ? "Match overs *" : "Overs per innings *";
    public OversMode CurrentOversMode => NoOversLimit
        ? OversMode.Unlimited
        : OversAreMatchTotal
            ? OversMode.MatchTotal
            : OversMode.PerInnings;

    public SeriesEditViewModel(
        ISeriesService series,
        ITeamService teams,
        IVenueService venues,
        IAuthService auth)
    {
        _series = series;
        _teams = teams;
        _venues = venues;
        _auth = auth;

        for (var i = 1; i <= 7; i++)
            MatchCountOptions.Add(i);

        FormatOptions.Add(new SelectOption<MatchFormat>
        {
            Label = "Single inning (1 turn each)",
            Value = MatchFormat.SingleInning
        });
        FormatOptions.Add(new SelectOption<MatchFormat>
        {
            Label = "Double innings (2 turns each)",
            Value = MatchFormat.DoubleInnings
        });
        SelectedFormat = FormatOptions[0];
        UpdateOversHint();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _id = null;
        PageTitle = "New series";
        if (query.TryGetValue("id", out var raw) && Guid.TryParse($"{raw}", out var id))
            _id = id;
    }

    partial void OnOversLimitChanged(int value) => UpdateOversHint();
    partial void OnSelectedFormatChanged(SelectOption<MatchFormat>? value) => UpdateOversHint();
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

    private void UpdateOversHint()
    {
        var format = SelectedFormat?.Value ?? MatchFormat.SingleInning;
        if (NoOversLimit)
        {
            OversHint = "Innings end only on all out or declare. Overs never stop a turn.";
            return;
        }

        OversHint = OversAreMatchTotal
            ? $"All innings share {OversLimit} overs for each match. All out or declare still ends a turn."
            : format == MatchFormat.DoubleInnings
                ? $"Each turn gets {OversLimit} overs."
                : $"Each side gets {OversLimit} overs.";
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
                foreach (var v in await _venues.GetAllAsync().ConfigureAwait(true))
                    VenueOptions.Add(v);
            }
            catch { /* offline */ }

            TeamOptions.Clear();
            foreach (var t in (await _teams.GetActiveAsync().ConfigureAwait(true))
                     .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                TeamOptions.Add(t);

            if (_id is Guid id)
            {
                PageTitle = "Edit series";
                var detail = await _series.GetAsync(id).ConfigureAwait(true)
                             ?? throw new InvalidOperationException("Series not found.");
                if (detail.Status != 0)
                    throw new InvalidOperationException("Only draft series can be edited.");

                Name = detail.Name;
                MatchCount = detail.MatchCount;
                SelectedTeamA = TeamOptions.FirstOrDefault(t => t.Id == detail.TeamAId);
                SelectedTeamB = TeamOptions.FirstOrDefault(t => t.Id == detail.TeamBId);
                SelectedVenue = VenueOptions.FirstOrDefault(v => v.Id == detail.VenueId)
                                ?? VenueOptions.FirstOrDefault();
                SelectedFormat = FormatOptions.FirstOrDefault(f => (byte)f.Value == detail.Format)
                                 ?? FormatOptions[0];
                OversLimit = detail.OversLimit > 0 ? detail.OversLimit : 8;
                ApplyOversMode(detail.OversMode);
                LastBatsmanCanBatAsSingleWicket = detail.LastBatsmanCanBatAsSingleWicket;
                BowlerMaxOvers = detail.BowlerMaxOvers;
            }
            else
            {
                SelectedVenue ??= VenueOptions.FirstOrDefault();
                SelectedFormat ??= FormatOptions[0];
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            if (SelectedTeamA is null || SelectedTeamB is null)
                throw new ArgumentException("Select both teams.");
            if (SelectedTeamA.Id == SelectedTeamB.Id)
                throw new ArgumentException("A series must be between two different teams.");
            if (SelectedVenue is null)
                throw new ArgumentException("Venue is required.");
            if (SelectedFormat is null)
                throw new ArgumentException("Match format is required.");
            OversRules.Validate(CurrentOversMode, OversLimit, BowlerMaxOvers);

            var draft = new SeriesDraft(
                Name,
                SelectedTeamA.Id,
                SelectedTeamB.Id,
                MatchCount,
                SelectedVenue.Id,
                (byte)SelectedFormat.Value,
                OversLimit,
                LastBatsmanCanBatAsSingleWicket,
                BowlerMaxOvers,
                CurrentOversMode);
            if (_id is Guid id)
                await _series.UpdateAsync(id, draft).ConfigureAwait(true);
            else
            {
                var created = await _series.CreateAsync(draft).ConfigureAwait(true);
                _id = created.Id;
            }

            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            if (_id is Guid sid)
                await Shell.Current.GoToAsync($"{nameof(Pages.SeriesDetailPage)}?id={sid}")
                    .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }
}
