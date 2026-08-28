using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class GroundDiscoverViewModel : ObservableObject
{
    public const string AllLocations = "All";

    private readonly IGroundsApiClient _api;
    private readonly IAuthService _auth;
    private int _loadBusy;
    private int _initialized;
    private int _optionsGen;
    private int _suppressFilter;
    private string? _cursor;

    public ObservableCollection<GroundCardDto> Items { get; } = new();
    public ObservableCollection<string> Countries { get; } = new() { AllLocations };
    public ObservableCollection<string> States { get; } = new() { AllLocations };
    public ObservableCollection<string> Cities { get; } = new() { AllLocations };
    public ObservableCollection<string> Areas { get; } = new() { AllLocations };

    [ObservableProperty] private string postalCode = string.Empty;
    [ObservableProperty] private string country = AllLocations;
    [ObservableProperty] private string state = AllLocations;
    [ObservableProperty] private string city = AllLocations;
    [ObservableProperty] private string area = AllLocations;
    [ObservableProperty] private string nameQuery = string.Empty;
    [ObservableProperty] private string minRateText = string.Empty;
    [ObservableProperty] private string maxRateText = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool showRegisterPopup;

    public bool ShowRegisterOwner =>
        _auth.Current?.CanManagePlatform != true && _auth.Current?.IsGroundOwner != true;

    public bool UsePostalFilter => !HasLocationFilter;

    public bool UseLocationFilter => string.IsNullOrWhiteSpace(PostalCode);

    public bool HasLocationFilter =>
        !IsAll(Country) || !IsAll(State) || !IsAll(City) || !IsAll(Area);

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(NameQuery)
        || !string.IsNullOrWhiteSpace(PostalCode)
        || HasLocationFilter
        || !string.IsNullOrWhiteSpace(MinRateText)
        || !string.IsNullOrWhiteSpace(MaxRateText);

    public GroundDiscoverViewModel(IGroundsApiClient api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    private static bool IsAll(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           string.Equals(value, AllLocations, StringComparison.OrdinalIgnoreCase);

    private static string? ApiValue(string? value)
        => IsAll(value) ? null : value?.Trim();

    private void NotifyFilterMode()
    {
        OnPropertyChanged(nameof(UsePostalFilter));
        OnPropertyChanged(nameof(UseLocationFilter));
        OnPropertyChanged(nameof(HasLocationFilter));
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    partial void OnPostalCodeChanged(string value)
    {
        if (_suppressFilter > 0)
            return;
        if (!string.IsNullOrWhiteSpace(value))
            ResetLocationSelections();
        NotifyFilterMode();
    }

    partial void OnCountryChanged(string value)
    {
        if (_suppressFilter > 0)
            return;
        if (!IsAll(value))
            ClearPostalQuiet();
        ResetChild(resetState: true, resetCity: true, resetArea: true);
        NotifyFilterMode();
        _ = RefreshLocationOptionsAsync();
    }

    partial void OnStateChanged(string value)
    {
        if (_suppressFilter > 0)
            return;
        if (!IsAll(value))
            ClearPostalQuiet();
        ResetChild(resetState: false, resetCity: true, resetArea: true);
        NotifyFilterMode();
        _ = RefreshLocationOptionsAsync();
    }

    partial void OnCityChanged(string value)
    {
        if (_suppressFilter > 0)
            return;
        if (!IsAll(value))
            ClearPostalQuiet();
        ResetChild(resetState: false, resetCity: false, resetArea: true);
        NotifyFilterMode();
        _ = RefreshLocationOptionsAsync();
    }

    partial void OnAreaChanged(string value)
    {
        if (_suppressFilter > 0)
            return;
        if (!IsAll(value))
            ClearPostalQuiet();
        NotifyFilterMode();
    }

    partial void OnNameQueryChanged(string value) => OnPropertyChanged(nameof(HasActiveFilters));
    partial void OnMinRateTextChanged(string value) => OnPropertyChanged(nameof(HasActiveFilters));
    partial void OnMaxRateTextChanged(string value) => OnPropertyChanged(nameof(HasActiveFilters));

    private void ClearPostalQuiet()
    {
        if (string.IsNullOrWhiteSpace(PostalCode))
            return;
        _suppressFilter++;
        PostalCode = string.Empty;
        _suppressFilter--;
    }

    private void ResetLocationSelections()
    {
        _suppressFilter++;
        Country = AllLocations;
        State = AllLocations;
        City = AllLocations;
        Area = AllLocations;
        Fill(States, []);
        Fill(Cities, []);
        Fill(Areas, []);
        _suppressFilter--;
    }

    private void ResetChild(bool resetState, bool resetCity, bool resetArea)
    {
        _suppressFilter++;
        if (resetState)
            State = AllLocations;
        if (resetCity)
            City = AllLocations;
        if (resetArea)
            Area = AllLocations;
        _suppressFilter--;
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            return;
        await RefreshLocationOptionsAsync().ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private Task SearchAsync() => ReloadAsync();

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        _suppressFilter++;
        PostalCode = string.Empty;
        NameQuery = string.Empty;
        MinRateText = string.Empty;
        MaxRateText = string.Empty;
        Country = AllLocations;
        State = AllLocations;
        City = AllLocations;
        Area = AllLocations;
        _suppressFilter--;
        NotifyFilterMode();
        await RefreshLocationOptionsAsync().ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        _cursor = null;
        HasMore = true;
        Items.Clear();
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

            var postalRaw = PostalCode.Trim();
            string? postal = null;
            if (postalRaw.Length > 0)
            {
                var digits = new string(postalRaw.Where(char.IsDigit).ToArray());
                if (digits.Length != 5)
                {
                    ErrorMessage = "Enter a 5-digit postal code, or pick a location.";
                    return;
                }
                postal = digits;
            }

            decimal? min = decimal.TryParse(MinRateText, out var mn) ? mn : null;
            decimal? max = decimal.TryParse(MaxRateText, out var mx) ? mx : null;
            var page = await _api.SearchAsync(
                    postal,
                    NameQuery,
                    postal is null ? ApiValue(Country) : null,
                    postal is null ? ApiValue(State) : null,
                    postal is null ? ApiValue(City) : null,
                    postal is null ? ApiValue(Area) : null,
                    min, max, _cursor)
                .ConfigureAwait(true);

            foreach (var item in page.Items)
                Items.Add(item);
            _cursor = page.NextCursor;
            HasMore = page.HasMore;
            if (Items.Count == 0)
                ErrorMessage = "No grounds match these filters yet.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsLoadingMore = false;
            Interlocked.Exchange(ref _loadBusy, 0);
        }
    }

    private async Task RefreshLocationOptionsAsync()
    {
        var gen = Interlocked.Increment(ref _optionsGen);
        try
        {
            var keepCountry = Country;
            var keepState = State;
            var keepCity = City;
            var keepArea = Area;
            var options = await _api.GetLocationOptionsAsync(
                    ApiValue(keepCountry),
                    ApiValue(keepState),
                    ApiValue(keepCity))
                .ConfigureAwait(true);
            if (gen != Volatile.Read(ref _optionsGen))
                return;
            _suppressFilter++;
            Fill(Countries, options.Countries);
            Fill(States, IsAll(keepCountry) ? [] : options.States);
            Fill(Cities, IsAll(keepState) ? [] : options.Cities);
            Fill(Areas, IsAll(keepCity) ? [] : options.Areas);
            Country = KeepOrAll(Countries, keepCountry);
            State = KeepOrAll(States, keepState);
            City = KeepOrAll(Cities, keepCity);
            Area = KeepOrAll(Areas, keepArea);
            _suppressFilter--;
        }
        catch
        {
            /* keep last options */
        }
    }

    private static void Fill(ObservableCollection<string> target, IEnumerable<string> values)
    {
        var next = new List<string> { AllLocations };
        foreach (var value in values.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            if (!string.Equals(value, AllLocations, StringComparison.OrdinalIgnoreCase)
                && next.TrueForAll(s => !string.Equals(s, value, StringComparison.OrdinalIgnoreCase)))
                next.Add(value);
        }

        if (target.Count == next.Count
            && target.Zip(next).All(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase)))
            return;

        target.Clear();
        foreach (var value in next)
            target.Add(value);
    }

    private static string KeepOrAll(ObservableCollection<string> items, string current)
    {
        if (items.Any(s => string.Equals(s, current, StringComparison.OrdinalIgnoreCase)))
            return items.First(s => string.Equals(s, current, StringComparison.OrdinalIgnoreCase));
        return AllLocations;
    }

    [RelayCommand]
    private async Task OpenAsync(GroundCardDto? card)
    {
        if (card is null)
            return;
        await Shell.Current.GoToAsync($"{nameof(Pages.GroundDetailPage)}?id={card.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private Task ChatOwnerAsync(GroundCardDto? card)
        => PakistanWhatsApp.OpenChatAsync(
            card?.ChatPhone,
            card is null ? null : $"Assalam o Alaikum, I want to chat about {card.Name}.");

    [RelayCommand]
    private void OpenRegisterPopup() => ShowRegisterPopup = true;

    [RelayCommand]
    private void CloseRegisterPopup() => ShowRegisterPopup = false;

    [RelayCommand]
    private async Task WhatsAppRegisterAsync()
    {
        try
        {
            await LeagueRegistrationContact.OpenGroundOwnerWhatsAppAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not open WhatsApp. Message Cric360 at "
                           + LeagueRegistrationContact.WhatsAppDisplay
                           + ". " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task EmailRegisterAsync()
    {
        try
        {
            await LeagueRegistrationContact.OpenGroundOwnerEmailAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not open Email. Write to "
                           + LeagueRegistrationContact.EmailDisplay
                           + ". " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task FacebookRegisterAsync()
    {
        try
        {
            await LeagueRegistrationContact.OpenFacebookAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not open Facebook. Visit "
                           + LeagueRegistrationContact.FacebookUrl
                           + ". " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task InstagramRegisterAsync()
    {
        try
        {
            await LeagueRegistrationContact.OpenInstagramAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not open Instagram. Visit "
                           + LeagueRegistrationContact.InstagramUrl
                           + ". " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task TikTokRegisterAsync()
    {
        try
        {
            await LeagueRegistrationContact.OpenTikTokAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not open TikTok. Visit "
                           + LeagueRegistrationContact.TikTokUrl
                           + ". " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task YouTubeRegisterAsync()
    {
        try
        {
            await LeagueRegistrationContact.OpenYouTubeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not open YouTube. Visit "
                           + LeagueRegistrationContact.YouTubeUrl
                           + ". " + ex.Message;
        }
    }
}
