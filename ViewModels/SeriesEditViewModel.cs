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
    private readonly IFormatService _formats;
    private readonly IAuthService _auth;

    private Guid? _id;

    public ObservableCollection<TeamListItem> TeamOptions { get; } = new();
    public ObservableCollection<int> MatchCountOptions { get; } = new();
    public ObservableCollection<VenueListItem> VenueOptions { get; } = new();
    public ObservableCollection<FormatListItem> FormatOptions { get; } = new();

    [ObservableProperty] private string pageTitle = "New series";
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private int matchCount = 3;
    [ObservableProperty] private TeamListItem? selectedTeamA;
    [ObservableProperty] private TeamListItem? selectedTeamB;
    [ObservableProperty] private VenueListItem? selectedVenue;
    [ObservableProperty] private FormatListItem? selectedFormat;
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
        IFormatService formats,
        IAuthService auth)
    {
        _series = series;
        _teams = teams;
        _venues = venues;
        _formats = formats;
        _auth = auth;

        for (var i = 1; i <= 7; i++)
            MatchCountOptions.Add(i);

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
                foreach (var v in await _venues.GetAllAsync().ConfigureAwait(true))
                    VenueOptions.Add(v);
            }
            catch { /* offline */ }

            FormatOptions.Clear();
            try
            {
                foreach (var f in await _formats.GetAllAsync().ConfigureAwait(true))
                    FormatOptions.Add(f);
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
                throw new ArgumentException("Select a format. Add one in Formats if the list is empty.");
            OversRules.Validate(SelectedFormat.OversMode, SelectedFormat.OversLimit, BowlerMaxOvers);

            var draft = new SeriesDraft(
                Name,
                SelectedTeamA.Id,
                SelectedTeamB.Id,
                MatchCount,
                SelectedVenue.Id,
                (byte)SelectedFormat.Format,
                SelectedFormat.OversLimit,
                LastBatsmanCanBatAsSingleWicket,
                BowlerMaxOvers,
                SelectedFormat.OversMode,
                SelectedFormat.Id);
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
