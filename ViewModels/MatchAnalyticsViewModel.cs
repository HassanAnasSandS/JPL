using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

[QueryProperty(nameof(MatchId), "id")]
public partial class MatchAnalyticsViewModel : ObservableObject
{
    private readonly IMatchService _matches;
    private string? _matchId;

    public string? MatchId
    {
        get => _matchId;
        set
        {
            if (_matchId == value)
                return;
            _matchId = value;
            _ = LoadAsync();
        }
    }

    [ObservableProperty] private string title = "Match analytics";
    [ObservableProperty] private string resultText = "";
    [ObservableProperty] private string chaseText = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private MatchAnalyticsReport? report;
    [ObservableProperty] private InningsAnalytics? selectedInnings;
    [ObservableProperty] private string inningsChip = "All";

    public IReadOnlyList<ChartSeriesData> WormSeries => Report?.WormSeries ?? [];
    public IReadOnlyList<ChartSeriesData> RunRateSeries => Report?.RunRateSeries ?? [];
    public IReadOnlyList<BarPoint> Manhattan => SelectedInnings?.Manhattan ?? Report?.Innings.FirstOrDefault()?.Manhattan ?? [];
    public IReadOnlyList<BarPoint> Momentum => SelectedInnings?.Momentum ?? Report?.Innings.FirstOrDefault()?.Momentum ?? [];
    public IReadOnlyList<PhaseRow> Phases => SelectedInnings?.Phases ?? [];
    public IReadOnlyList<PartnershipRow> Partnerships => SelectedInnings?.Partnerships ?? [];
    public IReadOnlyList<string> Insights => Report?.Insights ?? [];
    public IReadOnlyList<InningsAnalytics> InningsList => Report?.Innings ?? [];
    public bool HasReport => Report is not null;
    public bool HasChase => Report?.IsChase == true && !string.IsNullOrWhiteSpace(Report.ChaseText);
    public bool HasResultText => !string.IsNullOrWhiteSpace(ResultText);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string InningsSummary => SelectedInnings is null
        ? ""
        : $"{SelectedInnings.TeamName} {SelectedInnings.Runs}/{SelectedInnings.Wickets}  RR {SelectedInnings.RunRate}  4s {SelectedInnings.Fours}  6s {SelectedInnings.Sixes}  dots {SelectedInnings.Dots}";

    public MatchAnalyticsViewModel(IMatchService matches) => _matches = matches;

    partial void OnResultTextChanged(string value) => OnPropertyChanged(nameof(HasResultText));
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnReportChanged(MatchAnalyticsReport? value)
    {
        SelectedInnings = value?.Innings.FirstOrDefault();
        InningsChip = SelectedInnings is null ? "All" : $"Inns {SelectedInnings.Number}";
        NotifyCharts();
    }

    partial void OnSelectedInningsChanged(InningsAnalytics? value) => NotifyCharts();

    private void NotifyCharts()
    {
        OnPropertyChanged(nameof(WormSeries));
        OnPropertyChanged(nameof(RunRateSeries));
        OnPropertyChanged(nameof(Manhattan));
        OnPropertyChanged(nameof(Momentum));
        OnPropertyChanged(nameof(Phases));
        OnPropertyChanged(nameof(Partnerships));
        OnPropertyChanged(nameof(Insights));
        OnPropertyChanged(nameof(InningsList));
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(HasChase));
        OnPropertyChanged(nameof(HasResultText));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(InningsSummary));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (!Guid.TryParse(MatchId, out var id))
            return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var data = await _matches.GetMatchAnalyticsAsync(id).ConfigureAwait(true);
            if (data is null)
            {
                ErrorMessage = "No balls scored yet.";
                Report = null;
                return;
            }
            Report = data;
            Title = string.IsNullOrWhiteSpace(data.Title) ? "Match analytics" : data.Title;
            ResultText = data.ResultText;
            ChaseText = data.ChaseText;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenScorecardAsync()
    {
        if (!Guid.TryParse(MatchId, out var id))
            return;
        await Shell.Current
            .GoToAsync($"{nameof(Pages.MatchScorecardPage)}?id={id:D}")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private void SelectInnings(InningsAnalytics? inn)
    {
        if (inn is null)
            return;
        SelectedInnings = inn;
        InningsChip = $"Inns {inn.Number}";
    }
}
