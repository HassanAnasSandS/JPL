using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class HallOfFameViewModel : ObservableObject
{
    private readonly IApiClient _api;
    private int _loadBusy;

    public ObservableCollection<TrophyRow> Items { get; } = new();

    public FormatFilterViewModel FormatFilter { get; }

    [ObservableProperty] private int selectedCategory;
    [ObservableProperty] private string categoryTitle = "Player of the Match";
    [ObservableProperty] private string categoryHint = "Trophies for Player of the Match awards";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private string? errorMessage;

    public bool IsPotm => SelectedCategory == 0;
    public bool IsCaptain => SelectedCategory == 1;
    public bool IsTourChamp => SelectedCategory == 2;
    public bool IsSeriesChamp => SelectedCategory == 3;
    public string CountLabel => TotalCount <= 0 ? "No trophies" : $"{Items.Count} of {TotalCount}";
    private string CategoryKey => SelectedCategory switch
    {
        1 => "captain",
        2 => "tourchamp",
        3 => "serieschamp",
        _ => "potm"
    };

    public HallOfFameViewModel(IApiClient api, IFormatService formats)
    {
        _api = api;
        FormatFilter = new FormatFilterViewModel(formats);
        FormatFilter.Committed += (_, _) => _ = ReloadAsync();
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));

    partial void OnSelectedCategoryChanged(int value)
    {
        OnPropertyChanged(nameof(IsPotm));
        OnPropertyChanged(nameof(IsCaptain));
        OnPropertyChanged(nameof(IsTourChamp));
        OnPropertyChanged(nameof(IsSeriesChamp));
        ApplyCategoryTitles();
        _ = ReloadAsync();
    }

    [RelayCommand]
    private void SelectCategory(string? key)
    {
        SelectedCategory = key?.ToLowerInvariant() switch
        {
            "captain" => 1,
            "tourchamp" => 2,
            "serieschamp" => 3,
            _ => 0
        };
    }

    [RelayCommand]
    public Task LoadAsync() => ReloadAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        HasMore = true;
        Items.Clear();
        OnPropertyChanged(nameof(CountLabel));
        await LoadMoreAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || Interlocked.Exchange(ref _loadBusy, 1) == 1)
            return;

        try
        {
            if (Items.Count == 0)
                IsBusy = true;
            else
                IsLoadingMore = true;
            ErrorMessage = null;
            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);
            await FormatFilter.EnsureLoadedAsync().ConfigureAwait(true);
            var page = await _api.GetHallOfFamePageAsync(
                    Items.Count,
                    CategoryKey,
                    q: null,
                    FormatFilter.SelectedIds)
                .ConfigureAwait(true);
            TotalCount = page.TotalCount;
            ListPaging.Append(Items, page.Items, r => r.PlayerId);
            HasMore = page.HasMore;
            OnPropertyChanged(nameof(CountLabel));
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.IsNetworkFailure(ex)
                ? "Offline — Hall of Fame needs internet."
                : ex.Message;
            if (NetworkUi.IsNetworkFailure(ex))
                await NetworkUi.ShowOfflinePopupAsync(ex).ConfigureAwait(true);
            if (Items.Count == 0)
            {
                Items.Clear();
                TotalCount = 0;
            }
        }
        finally
        {
            IsBusy = false;
            IsLoadingMore = false;
            Interlocked.Exchange(ref _loadBusy, 0);
        }
    }

    private void ApplyCategoryTitles()
    {
        switch (SelectedCategory)
        {
            case 1:
                CategoryTitle = "Captain Win Trophies";
                CategoryHint = "Trophy each time a captain’s side wins the match";
                break;
            case 2:
                CategoryTitle = "Tournament Champions";
                CategoryHint = "Trophy when a captain’s side wins the tournament";
                break;
            case 3:
                CategoryTitle = "Series Champions";
                CategoryHint = "Trophy when a captain’s side wins the series";
                break;
            default:
                CategoryTitle = "Player of the Match";
                CategoryHint = "Trophies awarded when a match is completed";
                break;
        }
    }
}
