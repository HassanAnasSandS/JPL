using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class HallOfFameViewModel : ObservableObject
{
    private readonly IApiClient _api;
    private HallOfFameBundle _bundle = new();

    public ObservableCollection<TrophyRow> Items { get; } = new();

    [ObservableProperty] private int selectedCategory;
    [ObservableProperty] private string categoryTitle = "Player of the Match";
    [ObservableProperty] private string categoryHint = "Trophies for Player of the Match awards";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;

    public bool IsPotm => SelectedCategory == 0;
    public bool IsCaptain => SelectedCategory == 1;
    public bool IsTourChamp => SelectedCategory == 2;

    public HallOfFameViewModel(IApiClient api) => _api = api;

    partial void OnSelectedCategoryChanged(int value)
    {
        OnPropertyChanged(nameof(IsPotm));
        OnPropertyChanged(nameof(IsCaptain));
        OnPropertyChanged(nameof(IsTourChamp));
        ApplyCategory();
    }

    [RelayCommand]
    private void SelectCategory(string? key)
    {
        SelectedCategory = key?.ToLowerInvariant() switch
        {
            "captain" => 1,
            "tourchamp" => 2,
            _ => 0
        };
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);
            _bundle = await _api.GetHallOfFameAsync().ConfigureAwait(true);
            ApplyCategory();
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — Hall of Fame needs internet."
                : ex.Message;
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
            Items.Clear();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyCategory()
    {
        IReadOnlyList<TrophyRow> rows;
        switch (SelectedCategory)
        {
            case 1:
                CategoryTitle = "Captain Win Trophies";
                CategoryHint = "Trophy each time a captain’s side wins the match";
                rows = _bundle.CaptainTrophies;
                break;
            case 2:
                CategoryTitle = "Tournament Champions";
                CategoryHint = "Trophy when a captain’s side wins the tournament";
                rows = _bundle.TournamentChampionTrophies;
                break;
            default:
                CategoryTitle = "Player of the Match";
                CategoryHint = "Trophies awarded when a match is completed";
                rows = _bundle.PlayerOfTheMatch;
                break;
        }

        Items.Clear();
        foreach (var row in rows)
            Items.Add(row);
    }
}
