using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class NightStartOption
{
    public int Hour { get; init; }
    public string Label { get; init; } = string.Empty;
}

public sealed class BookingOfferEdit : ObservableObject
{
    public string Kind { get; set; } = string.Empty;

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (!SetProperty(ref _title, value))
                return;
            if (IsHourly)
                DurationHoursText = "1";
            OnPropertyChanged(nameof(IsHourly));
            OnPropertyChanged(nameof(IsDurationReadOnly));
        }
    }

    public bool IsHourly =>
        string.Equals(_title.Trim(), "Hourly", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Kind, "Hourly", StringComparison.OrdinalIgnoreCase);

    public bool IsDurationReadOnly => IsHourly;

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    private string _priceText = "0";
    public string PriceText
    {
        get => _priceText;
        set => SetProperty(ref _priceText, value);
    }

    private string _nightPriceText = string.Empty;
    public string NightPriceText
    {
        get => _nightPriceText;
        set => SetProperty(ref _nightPriceText, value);
    }

    private string _weekendPriceText = string.Empty;
    public string WeekendPriceText
    {
        get => _weekendPriceText;
        set => SetProperty(ref _weekendPriceText, value);
    }

    private string _weekendNightPriceText = string.Empty;
    public string WeekendNightPriceText
    {
        get => _weekendNightPriceText;
        set => SetProperty(ref _weekendNightPriceText, value);
    }

    private string _durationHoursText = "1";
    public string DurationHoursText
    {
        get => _durationHoursText;
        set
        {
            if (IsHourly)
                value = "1";
            SetProperty(ref _durationHoursText, value);
        }
    }
}

public sealed class AmenityChoice : ObservableObject
{
    public string Code { get; init; } = string.Empty;
    public bool IsCustom { get; init; }
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class ServiceChoice : ObservableObject
{
    public string Code { get; init; } = string.Empty;
    public bool IsCustom { get; init; }
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private string _amountText = "0";
    public string AmountText
    {
        get => _amountText;
        set => SetProperty(ref _amountText, value);
    }
}

public sealed class GroundLinkEdit : ObservableObject
{
    public const int MaxPerGround = 8;

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private string _url = string.Empty;
    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }

    public static bool TryNormalizeUrl(string? raw, out string url)
    {
        url = string.Empty;
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0)
            return false;
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
            return false;
        url = uri.AbsoluteUri;
        return url.Length is > 0 and <= 500;
    }
}

public sealed class GroundPhotoItem
{
    public const int MaxPerGround = 10;

    public Guid Key { get; init; } = Guid.NewGuid();
    public ImageSource? Preview { get; init; }
    public GroundImageDto? Remote { get; init; }
    public byte[]? Jpeg { get; init; }
    public string FileName { get; init; } = "photo.jpg";
    public bool IsLocal => Jpeg is { Length: > 0 };

    public static GroundPhotoItem FromRemote(GroundImageDto dto)
        => new()
        {
            Key = dto.Id,
            Remote = dto,
            Preview = Uri.TryCreate(dto.Url, UriKind.Absolute, out var uri) ? ImageSource.FromUri(uri) : null,
            FileName = dto.FileName
        };

    public static GroundPhotoItem FromJpeg(byte[] jpeg, string fileName)
        => new()
        {
            Jpeg = jpeg,
            Preview = PlayerPhotos.ToImageSource(jpeg),
            FileName = string.IsNullOrWhiteSpace(fileName) ? "photo.jpg" : fileName
        };
}

[QueryProperty(nameof(GroundId), "id")]
public partial class OwnerGroundEditViewModel : ObservableObject
{
    private readonly IGroundsApiClient _api;
    private Guid? _existingId;
    private int _hydrate;
    private int _resolveGen;
    private bool _catalogLoaded;

    public string? GroundId { get; set; }

    public ObservableCollection<GroundPhotoItem> Photos { get; } = new();
    public IReadOnlyList<string> ListingOptions { get; } = new[] { "Draft", "Published", "Paused" };
    public IReadOnlyList<NightStartOption> NightStartOptions { get; } =
    [
        new NightStartOption { Hour = 16, Label = "4:00 PM" },
        new NightStartOption { Hour = 17, Label = "5:00 PM" },
        new NightStartOption { Hour = 18, Label = "6:00 PM" },
        new NightStartOption { Hour = 19, Label = "7:00 PM" },
        new NightStartOption { Hour = 20, Label = "8:00 PM" },
        new NightStartOption { Hour = 21, Label = "9:00 PM" }
    ];
    public IReadOnlyList<NightStartOption> NightEndOptions { get; } =
    [
        new NightStartOption { Hour = 21, Label = "9:00 PM" },
        new NightStartOption { Hour = 22, Label = "10:00 PM" },
        new NightStartOption { Hour = 23, Label = "11:00 PM" },
        new NightStartOption { Hour = 0, Label = "12:00 AM" },
        new NightStartOption { Hour = 1, Label = "1:00 AM" },
        new NightStartOption { Hour = 2, Label = "2:00 AM" },
        new NightStartOption { Hour = 3, Label = "3:00 AM" },
        new NightStartOption { Hour = 4, Label = "4:00 AM" },
        new NightStartOption { Hour = 5, Label = "5:00 AM" },
        new NightStartOption { Hour = 6, Label = "6:00 AM" },
        new NightStartOption { Hour = 7, Label = "7:00 AM" },
        new NightStartOption { Hour = 8, Label = "8:00 AM" }
    ];
    public ObservableCollection<AmenityChoice> AmenityOptions { get; } = new();
    public ObservableCollection<ServiceChoice> ServiceOptions { get; } = new();
    public ObservableCollection<BookingOfferEdit> BookingOffers { get; } = new();
    public ObservableCollection<GroundLinkEdit> Links { get; } = new();
    public bool CanAddLink => Links.Count < GroundLinkEdit.MaxPerGround;
    public bool HasPhotos => Photos.Count > 0;
    public bool CanAddMore => Photos.Count < GroundPhotoItem.MaxPerGround;
    public string PhotoSummary => Photos.Count == 0
        ? "Add up to 10 photos. Save this tab to continue."
        : $"{Photos.Count} / {GroundPhotoItem.MaxPerGround} photos";
    public string FetchLabel => IsFetching ? " " : "Fetch";
    public bool HasMapsFetchError => !string.IsNullOrWhiteSpace(MapsFetchError);
    public bool HasNameError => !string.IsNullOrWhiteSpace(NameError);
    public bool HasPhoneError => !string.IsNullOrWhiteSpace(PhoneError);
    public bool HasWhatsAppError => !string.IsNullOrWhiteSpace(WhatsAppError);
    public bool HasPostalError => !string.IsNullOrWhiteSpace(PostalError);
    public bool HasCountryError => !string.IsNullOrWhiteSpace(CountryError);
    public bool HasStateError => !string.IsNullOrWhiteSpace(StateError);
    public bool HasCityError => !string.IsNullOrWhiteSpace(CityError);
    public bool HasAreaError => !string.IsNullOrWhiteSpace(AreaError);
    public bool HasAddressError => !string.IsNullOrWhiteSpace(AddressError);

    [ObservableProperty] private string title = "Add ground";
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private string addressLine = string.Empty;
    [ObservableProperty] private string contactPhone = string.Empty;
    [ObservableProperty] private string whatsAppNumber = string.Empty;
    [ObservableProperty] private string googleMapsUrl = string.Empty;
    [ObservableProperty] private string postalCode = string.Empty;
    [ObservableProperty] private string countryName = string.Empty;
    [ObservableProperty] private string stateName = string.Empty;
    [ObservableProperty] private string cityName = string.Empty;
    [ObservableProperty] private string areaName = string.Empty;
    [ObservableProperty] private string hourlyRateText = "0";
    [ObservableProperty] private string selectedListing = "Draft";
    [ObservableProperty] private NightStartOption? selectedNightStart;
    [ObservableProperty] private NightStartOption? selectedNightEnd;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isFetching;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? mapsFetchError;
    [ObservableProperty] private string? nameError;
    [ObservableProperty] private string? phoneError;
    [ObservableProperty] private string? whatsAppError;
    [ObservableProperty] private string? postalError;
    [ObservableProperty] private string? countryError;
    [ObservableProperty] private string? stateError;
    [ObservableProperty] private string? cityError;
    [ObservableProperty] private string? areaError;
    [ObservableProperty] private string? addressError;
    public const string TabGeneral = "general";
    public const string TabLocation = "location";
    public const string TabBooking = "booking";
    public const string TabAmenities = "amenities";
    public const string TabServices = "services";
    public const string TabGallery = "gallery";
    public const string TabListing = "listing";

    public static readonly string[] TabOrder =
    [
        TabGeneral, TabLocation, TabBooking, TabAmenities, TabServices, TabGallery, TabListing
    ];

    [ObservableProperty] private string customAmenity = string.Empty;
    [ObservableProperty] private string customService = string.Empty;
    [ObservableProperty] private string customServiceAmount = string.Empty;
    [ObservableProperty] private string selectedTab = TabGeneral;
    [ObservableProperty] private int unlockedCount = 1;

    public bool IsGeneralTab => SelectedTab == TabGeneral;
    public bool IsLocationTab => SelectedTab == TabLocation;
    public bool IsBookingTab => SelectedTab == TabBooking;
    public bool IsAmenitiesTab => SelectedTab == TabAmenities;
    public bool IsServicesTab => SelectedTab == TabServices;
    public bool IsGalleryTab => SelectedTab == TabGallery;
    public bool IsListingTab => SelectedTab == TabListing;
    public bool CanOpenGeneralTab => UnlockedCount >= 1;
    public bool CanOpenLocationTab => UnlockedCount >= 2;
    public bool CanOpenBookingTab => UnlockedCount >= 3;
    public bool CanOpenAmenitiesTab => UnlockedCount >= 4;
    public bool CanOpenServicesTab => UnlockedCount >= 5;
    public bool CanOpenGalleryTab => UnlockedCount >= 6;
    public bool CanOpenListingTab => UnlockedCount >= 7;
    public string SaveButtonText => SelectedTab switch
    {
        TabLocation => "Save Location",
        TabBooking => "Save Booking",
        TabAmenities => "Save Amenities",
        TabServices => "Save Services",
        TabGallery => "Save Gallery",
        TabListing => "Save listing",
        _ => "Save General"
    };
    public string TabLockHint => UnlockedCount >= TabOrder.Length
        ? string.Empty
        : $"Save {TabTitle(TabOrder[UnlockedCount - 1])} to open the next tab.";
    public bool HasTabLockHint => UnlockedCount < TabOrder.Length;

    public OwnerGroundEditViewModel(IGroundsApiClient api)
    {
        _api = api;
        Photos.CollectionChanged += (_, _) => NotifyPhotoState();
        ApplyNightStart(18);
        ApplyNightEnd(6);
        AddBookingType();
    }

    partial void OnIsFetchingChanged(bool value) => OnPropertyChanged(nameof(FetchLabel));
    partial void OnMapsFetchErrorChanged(string? value) => OnPropertyChanged(nameof(HasMapsFetchError));
    partial void OnNameErrorChanged(string? value) => OnPropertyChanged(nameof(HasNameError));
    partial void OnPhoneErrorChanged(string? value) => OnPropertyChanged(nameof(HasPhoneError));
    partial void OnWhatsAppErrorChanged(string? value) => OnPropertyChanged(nameof(HasWhatsAppError));
    partial void OnPostalErrorChanged(string? value) => OnPropertyChanged(nameof(HasPostalError));
    partial void OnCountryErrorChanged(string? value) => OnPropertyChanged(nameof(HasCountryError));
    partial void OnStateErrorChanged(string? value) => OnPropertyChanged(nameof(HasStateError));
    partial void OnCityErrorChanged(string? value) => OnPropertyChanged(nameof(HasCityError));
    partial void OnAreaErrorChanged(string? value) => OnPropertyChanged(nameof(HasAreaError));
    partial void OnAddressErrorChanged(string? value) => OnPropertyChanged(nameof(HasAddressError));
    partial void OnNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(NameError))
            NameError = null;
    }
    partial void OnContactPhoneChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(PhoneError))
            PhoneError = null;
    }
    partial void OnWhatsAppNumberChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(WhatsAppError))
            WhatsAppError = null;
    }
    partial void OnGoogleMapsUrlChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(MapsFetchError))
            MapsFetchError = null;
    }
    partial void OnPostalCodeChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(PostalError))
            PostalError = null;
    }
    partial void OnCountryNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(CountryError))
            CountryError = null;
    }
    partial void OnStateNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(StateError))
            StateError = null;
    }
    partial void OnCityNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(CityError))
            CityError = null;
    }
    partial void OnAreaNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(AreaError))
            AreaError = null;
    }
    partial void OnAddressLineChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(AddressError))
            AddressError = null;
    }
    partial void OnSelectedTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsGeneralTab));
        OnPropertyChanged(nameof(IsLocationTab));
        OnPropertyChanged(nameof(IsBookingTab));
        OnPropertyChanged(nameof(IsAmenitiesTab));
        OnPropertyChanged(nameof(IsServicesTab));
        OnPropertyChanged(nameof(IsGalleryTab));
        OnPropertyChanged(nameof(IsListingTab));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    partial void OnUnlockedCountChanged(int value)
    {
        OnPropertyChanged(nameof(CanOpenLocationTab));
        OnPropertyChanged(nameof(CanOpenBookingTab));
        OnPropertyChanged(nameof(CanOpenAmenitiesTab));
        OnPropertyChanged(nameof(CanOpenServicesTab));
        OnPropertyChanged(nameof(CanOpenGalleryTab));
        OnPropertyChanged(nameof(CanOpenListingTab));
        OnPropertyChanged(nameof(TabLockHint));
        OnPropertyChanged(nameof(HasTabLockHint));
        SelectGeneralTabCommand.NotifyCanExecuteChanged();
        SelectLocationTabCommand.NotifyCanExecuteChanged();
        SelectBookingTabCommand.NotifyCanExecuteChanged();
        SelectAmenitiesTabCommand.NotifyCanExecuteChanged();
        SelectServicesTabCommand.NotifyCanExecuteChanged();
        SelectGalleryTabCommand.NotifyCanExecuteChanged();
        SelectListingTabCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenGeneralTab))]
    private void SelectGeneralTab() => SelectedTab = TabGeneral;

    [RelayCommand(CanExecute = nameof(CanOpenLocationTab))]
    private void SelectLocationTab() => SelectedTab = TabLocation;

    [RelayCommand(CanExecute = nameof(CanOpenBookingTab))]
    private void SelectBookingTab() => SelectedTab = TabBooking;

    [RelayCommand(CanExecute = nameof(CanOpenAmenitiesTab))]
    private void SelectAmenitiesTab() => SelectedTab = TabAmenities;

    [RelayCommand(CanExecute = nameof(CanOpenServicesTab))]
    private void SelectServicesTab() => SelectedTab = TabServices;

    [RelayCommand(CanExecute = nameof(CanOpenGalleryTab))]
    private void SelectGalleryTab() => SelectedTab = TabGallery;

    [RelayCommand(CanExecute = nameof(CanOpenListingTab))]
    private void SelectListingTab() => SelectedTab = TabListing;

    public async Task InitializeAsync()
    {
        if (!_catalogLoaded)
        {
            await LoadCatalogAsync().ConfigureAwait(true);
            _catalogLoaded = true;
        }

        if (!Guid.TryParse(GroundId, out var id))
        {
            Title = "Add ground";
            UnlockedCount = 1;
            _existingId = null;
            SelectedTab = TabGeneral;
            if (string.IsNullOrWhiteSpace(SelectedListing))
                SelectedListing = "Draft";
            return;
        }

        if (_existingId == id)
            return;

        _existingId = id;
        Title = "Edit ground";
        var g = await _api.GetGroundAsync(id).ConfigureAwait(true);
        if (g is null)
        {
            _existingId = null;
            ErrorMessage = "Ground not found.";
            return;
        }

        _hydrate++;
        Name = g.Name;
        Description = g.Description ?? string.Empty;
        AddressLine = g.AddressLine ?? string.Empty;
        ContactPhone = g.ContactPhone ?? string.Empty;
        WhatsAppNumber = g.WhatsAppNumber ?? string.Empty;
        GoogleMapsUrl = g.GoogleMapsUrl ?? string.Empty;
        ApplyLocation(g.PostalCode, g.CountryName, g.ProvinceName, g.CityName, g.AreaName);
        HourlyRateText = g.HourlyRate.ToString("0");
        SelectedListing = string.IsNullOrWhiteSpace(g.ListingStatus) ? "Draft" : g.ListingStatus;
        _hydrate--;
        ApplySavedAmenities(g.Amenities);
        ApplySavedServices(g.Services);
        ApplySavedOffers(g.Offers, g.HourlyRate);
        ApplyNightStart(g.NightStartsHour);
        ApplyNightEnd(g.NightEndsHour);
        ApplySavedLinks(g.Links);
        Photos.Clear();
        foreach (var img in g.Images ?? [])
            Photos.Add(GroundPhotoItem.FromRemote(img));
        UnlockedCount = InferUnlockedCount(g);
    }

    private static int InferUnlockedCount(GroundDetailDto g)
    {
        var n = 2;
        if (string.IsNullOrWhiteSpace(g.GoogleMapsUrl)
            || string.IsNullOrWhiteSpace(g.CityName)
            || string.IsNullOrWhiteSpace(g.PostalCode)
            || string.IsNullOrWhiteSpace(g.AreaName)
            || string.IsNullOrWhiteSpace(g.AddressLine)
            || string.IsNullOrWhiteSpace(g.ProvinceName))
            return n;
        n = 3;
        if (g.Offers is null || !g.Offers.Any(o => o.IsEnabled && o.Price > 0))
            return n;
        n = 4;
        if (g.Amenities is { Count: > 0 })
            n = 5;
        if (g.Services is { Count: > 0 })
            n = Math.Max(n, 6);
        if (g.Images is { Count: > 0 })
            n = Math.Max(n, 7);
        if (!string.IsNullOrWhiteSpace(g.ListingStatus)
            && !string.Equals(g.ListingStatus, "Draft", StringComparison.OrdinalIgnoreCase))
            n = TabOrder.Length;
        return Math.Min(n, TabOrder.Length);
    }

    private async Task LoadCatalogAsync()
    {
        IReadOnlyList<CatalogItemDto> amenities = [];
        IReadOnlyList<CatalogItemDto> services = [];
        try
        {
            amenities = await _api.GetAmenityCatalogAsync().ConfigureAwait(true);
            services = await _api.GetServiceCatalogAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        AmenityOptions.Clear();
        foreach (var row in amenities)
            AmenityOptions.Add(new AmenityChoice { Code = row.Name });
        ServiceOptions.Clear();
        foreach (var row in services)
            ServiceOptions.Add(new ServiceChoice { Code = row.Name });
    }

    [RelayCommand]
    private async Task FetchAsync()
    {
        if (IsFetching)
            return;
        var url = GoogleMapsUrl.Trim();
        if (url.Length < 12)
        {
            MapsFetchError = "Paste a Google Maps link first.";
            return;
        }

        try
        {
            IsFetching = true;
            MapsFetchError = null;
            ErrorMessage = null;
            Interlocked.Increment(ref _resolveGen);
            var pin = await _api.ResolveMapsAsync(url).ConfigureAwait(true);
            ApplyLocation(pin.PostalCode, pin.CountryName, pin.ProvinceName, pin.CityName, pin.AreaName);
        }
        catch (Exception ex)
        {
            MapsFetchError = ex.Message;
        }
        finally
        {
            IsFetching = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy || IsFetching)
            return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            NameError = null;
            PhoneError = null;
            WhatsAppError = null;
            ClearLocationErrors();
            Interlocked.Increment(ref _resolveGen);

            var nameOk = !string.IsNullOrWhiteSpace(Name);
            var phoneOk = PakistanWhatsApp.TryNormalize(ContactPhone, out var contactPhone);
            var whatsAppOk = PakistanWhatsApp.TryNormalize(WhatsAppNumber, out var whatsApp);
            if (!nameOk)
                NameError = "Ground name is required.";
            if (!phoneOk)
                PhoneError = PakistanWhatsApp.PhoneInvalidError;
            if (!whatsAppOk)
                WhatsAppError = string.IsNullOrWhiteSpace(WhatsAppNumber)
                    ? PakistanWhatsApp.RequiredError
                    : PakistanWhatsApp.InvalidError;
            if (!nameOk || !phoneOk || !whatsAppOk)
            {
                SelectedTab = TabGeneral;
                return;
            }

            switch (SelectedTab)
            {
                case TabLocation:
                    if (!TryValidateLocation())
                        return;
                    break;
                case TabBooking:
                    if (!BookingOffers.Any(o => o.IsEnabled && ParseMoney(o.PriceText) > 0 && !string.IsNullOrWhiteSpace(o.Title)))
                        throw new ArgumentException("Add a booking type with a name and weekday / day rate, then enable it.");
                    break;
                case TabAmenities:
                    if (AmenityOptions.Count(a => a.IsSelected) > 16)
                        throw new ArgumentException("Pick or add at most 16 amenities.");
                    break;
                case TabServices:
                    var picked = ServiceOptions.Where(s => s.IsSelected).ToList();
                    if (picked.Count > 16)
                        throw new ArgumentException("Pick or add at most 16 paid services.");
                    foreach (var row in picked)
                    {
                        if (string.IsNullOrWhiteSpace(row.AmountText))
                            continue;
                        if (!decimal.TryParse(row.AmountText, out var amount) || amount < 0)
                            throw new ArgumentException($"Set 0 PKR or more for {row.Code}.");
                    }
                    break;
                case TabListing:
                    if (string.Equals(SelectedListing, "Published", StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrWhiteSpace(GoogleMapsUrl)
                            || !BookingOffers.Any(o => o.IsEnabled && ParseMoney(o.PriceText) > 0)))
                        throw new ArgumentException("Save Location and Booking before publishing.");
                    break;
                case TabGeneral:
                    foreach (var row in Links)
                    {
                        var hasTitle = !string.IsNullOrWhiteSpace(row.Title);
                        var hasUrl = !string.IsNullOrWhiteSpace(row.Url);
                        if (!hasTitle && !hasUrl)
                            continue;
                        if (!hasUrl)
                            throw new ArgumentException("Each link needs a URL, or tap Remove.");
                        if (!GroundLinkEdit.TryNormalizeUrl(row.Url, out _))
                            throw new ArgumentException($"Not a valid web link: {row.Url.Trim()}");
                    }
                    if (Links.Count(l => !string.IsNullOrWhiteSpace(l.Url)) > GroundLinkEdit.MaxPerGround)
                        throw new ArgumentException($"At most {GroundLinkEdit.MaxPerGround} links.");
                    break;
            }

            if (SelectedTab == TabGallery)
            {
                if (_existingId is not Guid galleryId)
                    throw new ArgumentException("Save General first.");
                await UploadPendingPhotosAsync(galleryId).ConfigureAwait(true);
                await FinishTabSaveAsync().ConfigureAwait(true);
                return;
            }

            var offers = SelectedTab == TabBooking
                ? BookingOffers.Where(o => !string.IsNullOrWhiteSpace(o.Title)).Select(o =>
                {
                    var duration = decimal.TryParse(o.DurationHoursText, out var h) ? h : 1m;
                    return new
                    {
                        kind = o.Kind,
                        title = o.Title.Trim(),
                        price = ParseMoney(o.PriceText),
                        nightPrice = ParseMoney(o.NightPriceText),
                        weekendPrice = ParseMoney(o.WeekendPriceText),
                        weekendNightPrice = ParseMoney(o.WeekendNightPriceText),
                        durationHours = duration,
                        pricesPerHour = duration <= 1,
                        isEnabled = o.IsEnabled
                    };
                }).ToList()
                : null;
            var rate = offers is null
                ? 0
                : offers.Where(o => o.isEnabled)
                    .SelectMany(o => new[] { o.price, o.nightPrice, o.weekendPrice, o.weekendNightPrice })
                    .Where(p => p > 0)
                    .DefaultIfEmpty(0)
                    .Min();

            var body = new
            {
                name = Name.Trim(),
                description = Description,
                addressLine = AddressLine,
                contactPhone = contactPhone,
                whatsAppNumber = whatsApp,
                googleMapsUrl = SelectedTab == TabLocation ? GoogleMapsUrl.Trim() : null,
                postalCode = PostalCode.Trim(),
                countryName = CountryName.Trim(),
                provinceName = StateName.Trim(),
                cityName = CityName.Trim(),
                areaName = AreaName.Trim(),
                hourlyRate = rate,
                nightStartsHour = SelectedNightStart?.Hour ?? 18,
                nightEndsHour = SelectedNightEnd?.Hour ?? 6,
                listingStatus = SelectedTab == TabListing ? (SelectedListing ?? "Draft") : null,
                amenities = SelectedTab == TabAmenities
                    ? AmenityOptions.Where(a => a.IsSelected).Select(a => a.Code).ToList()
                    : null,
                services = SelectedTab == TabServices
                    ? ServiceOptions.Where(s => s.IsSelected).Select(s => new
                    {
                        name = s.Code,
                        amount = decimal.TryParse(s.AmountText, out var n) && n >= 0 ? n : 0
                    }).ToList()
                    : null,
                links = SelectedTab == TabGeneral
                    ? Links.Where(l => GroundLinkEdit.TryNormalizeUrl(l.Url, out _)).Select(l =>
                    {
                        GroundLinkEdit.TryNormalizeUrl(l.Url, out var url);
                        return new { title = (l.Title ?? string.Empty).Trim(), url };
                    }).ToList()
                    : null,
                offers
            };

            GroundDetailDto saved;
            if (_existingId is Guid id)
                saved = await _api.UpdateGroundAsync(id, body).ConfigureAwait(true);
            else
                saved = await _api.CreateGroundAsync(body).ConfigureAwait(true);

            _existingId = saved.Id;
            GroundId = saved.Id.ToString("D");
            Title = "Edit ground";
            _hydrate++;
            if (!string.IsNullOrWhiteSpace(saved.GoogleMapsUrl))
                GoogleMapsUrl = saved.GoogleMapsUrl;
            ApplyLocation(saved.PostalCode, saved.CountryName, saved.ProvinceName, saved.CityName, saved.AreaName);
            if (SelectedTab == TabBooking)
                ApplySavedOffers(saved.Offers, saved.HourlyRate);
            if (SelectedTab == TabServices)
                ApplySavedServices(saved.Services);
            if (SelectedTab == TabAmenities)
                ApplySavedAmenities(saved.Amenities);
            if (SelectedTab == TabGeneral)
                ApplySavedLinks(saved.Links);
            if (!string.IsNullOrWhiteSpace(saved.ListingStatus))
                SelectedListing = saved.ListingStatus;
            ApplyNightStart(saved.NightStartsHour);
            ApplyNightEnd(saved.NightEndsHour);
            _hydrate--;
            await FinishTabSaveAsync().ConfigureAwait(true);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            if (Shell.Current is not null)
                await Shell.Current.DisplayAlert("Could not save", ex.Message, "OK").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task FinishTabSaveAsync()
    {
        var index = Array.IndexOf(TabOrder, SelectedTab);
        if (index >= 0)
            UnlockedCount = Math.Max(UnlockedCount, Math.Min(TabOrder.Length, index + 2));
        if (index >= 0 && index < TabOrder.Length - 1)
        {
            SelectedTab = TabOrder[index + 1];
            return;
        }

        if (index == TabOrder.Length - 1 && Shell.Current is not null)
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
    }

    private static string TabTitle(string tab)
        => tab switch
        {
            TabLocation => "Location",
            TabBooking => "Booking",
            TabAmenities => "Amenities",
            TabServices => "Services",
            TabGallery => "Gallery",
            TabListing => "Listing",
            _ => "General"
        };

    [RelayCommand]
    private async Task AddPhotosAsync()
    {
        if (!CanAddMore)
        {
            ErrorMessage = $"Maximum {GroundPhotoItem.MaxPerGround} photos.";
            return;
        }

        IReadOnlyList<FileResult> files;
        try
        {
            var picked = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Ground photos",
                FileTypes = FilePickerFileType.Images
            }).ConfigureAwait(true);
            files = picked?.ToList() ?? [];
        }
        catch
        {
            var one = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Ground photo",
                FileTypes = FilePickerFileType.Images
            }).ConfigureAwait(true);
            files = one is null ? [] : [one];
        }

        if (files.Count == 0)
            return;

        try
        {
            ErrorMessage = null;
            var existingId = _existingId;
            foreach (var file in files)
            {
                if (!CanAddMore)
                    break;
                await using var stream = await file.OpenReadAsync().ConfigureAwait(true);
                var jpeg = await PlayerPhotos.ToJpegAsync(stream).ConfigureAwait(true);
                var name = Path.GetFileNameWithoutExtension(file.FileName);
                if (string.IsNullOrWhiteSpace(name))
                    name = "photo";
                var fileName = name + ".jpg";

                if (existingId is Guid id)
                {
                    await using var upload = new MemoryStream(jpeg);
                    var img = await _api.UploadGroundImageAsync(id, upload, fileName, "image/jpeg")
                        .ConfigureAwait(true);
                    Photos.Add(GroundPhotoItem.FromRemote(img));
                }
                else
                {
                    Photos.Add(GroundPhotoItem.FromJpeg(jpeg, fileName));
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeletePhotoAsync(GroundPhotoItem? photo)
    {
        if (photo is null)
            return;
        try
        {
            if (photo.Remote is not null && _existingId is Guid id)
                await _api.DeleteGroundImageAsync(id, photo.Remote.Id).ConfigureAwait(true);
            Photos.Remove(photo);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task UploadPendingPhotosAsync(Guid groundId)
    {
        var pending = Photos.Where(p => p.IsLocal).ToList();
        foreach (var photo in pending)
        {
            await using var stream = new MemoryStream(photo.Jpeg!);
            var img = await _api.UploadGroundImageAsync(groundId, stream, photo.FileName, "image/jpeg")
                .ConfigureAwait(true);
            var i = Photos.IndexOf(photo);
            if (i >= 0)
                Photos[i] = GroundPhotoItem.FromRemote(img);
            else
                Photos.Add(GroundPhotoItem.FromRemote(img));
        }
    }

    [RelayCommand]
    private void AddAmenity()
    {
        var raw = CustomAmenity?.Trim() ?? string.Empty;
        if (raw.Length == 0)
            return;

        ErrorMessage = null;
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            TryAddAmenity(part);
        CustomAmenity = string.Empty;
    }

    [RelayCommand]
    private void RemoveAmenity(AmenityChoice? amenity)
    {
        if (amenity is { IsCustom: true })
            AmenityOptions.Remove(amenity);
    }

    private void ApplySavedAmenities(IReadOnlyList<string>? saved)
    {
        foreach (var extra in AmenityOptions.Where(a => a.IsCustom).ToList())
            AmenityOptions.Remove(extra);
        foreach (var choice in AmenityOptions)
            choice.IsSelected = false;
        foreach (var code in saved ?? [])
            TryAddAmenity(code);
    }

    private void TryAddAmenity(string? raw)
    {
        var name = (raw ?? string.Empty).Trim();
        if (name.Length == 0)
            return;
        if (name.Length > 40)
            name = name[..40];

        var existing = AmenityOptions.FirstOrDefault(a =>
            string.Equals(a.Code, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsSelected = true;
            return;
        }

        AmenityOptions.Add(new AmenityChoice { Code = name, IsCustom = true, IsSelected = true });
    }

    [RelayCommand]
    private void AddService()
    {
        var name = CustomService?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return;
        TryAddService(name, CustomServiceAmount);
        CustomService = string.Empty;
        CustomServiceAmount = string.Empty;
    }

    [RelayCommand]
    private void RemoveService(ServiceChoice? service)
    {
        if (service is { IsCustom: true })
            ServiceOptions.Remove(service);
    }

    private void ApplySavedServices(IReadOnlyList<GroundServiceDto>? saved)
    {
        foreach (var extra in ServiceOptions.Where(s => s.IsCustom).ToList())
            ServiceOptions.Remove(extra);
        foreach (var choice in ServiceOptions)
        {
            choice.IsSelected = false;
            choice.AmountText = "0";
        }

        foreach (var row in saved ?? [])
            TryAddService(row.Name, row.Amount.ToString("0"));
    }

    private void TryAddService(string? rawName, string? rawAmount)
    {
        var name = (rawName ?? string.Empty).Trim();
        if (name.Length == 0)
            return;
        if (name.Length > 40)
            name = name[..40];

        ErrorMessage = null;
        var amountText = decimal.TryParse(rawAmount, out var amount) && amount >= 0
            ? amount.ToString("0")
            : "0";

        var existing = ServiceOptions.FirstOrDefault(s =>
            string.Equals(s.Code, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsSelected = true;
            existing.AmountText = amountText;
            return;
        }

        ServiceOptions.Add(new ServiceChoice
        {
            Code = name,
            IsCustom = true,
            IsSelected = true,
            AmountText = amountText
        });
    }

    [RelayCommand]
    private void AddLink()
    {
        if (Links.Count >= GroundLinkEdit.MaxPerGround)
        {
            ErrorMessage = $"At most {GroundLinkEdit.MaxPerGround} links.";
            return;
        }

        Links.Add(new GroundLinkEdit());
        OnPropertyChanged(nameof(CanAddLink));
    }

    [RelayCommand]
    private void RemoveLink(GroundLinkEdit? row)
    {
        if (row is null)
            return;
        Links.Remove(row);
        OnPropertyChanged(nameof(CanAddLink));
    }

    private void ApplySavedLinks(IReadOnlyList<GroundLinkDto>? rows)
    {
        Links.Clear();
        foreach (var saved in rows ?? [])
        {
            if (string.IsNullOrWhiteSpace(saved.Url))
                continue;
            Links.Add(new GroundLinkEdit
            {
                Title = saved.Title ?? string.Empty,
                Url = saved.Url
            });
        }

        OnPropertyChanged(nameof(CanAddLink));
    }

    [RelayCommand]
    private void AddBookingType()
    {
        if (BookingOffers.Count >= GroundBookingKinds.MaxPerGround)
        {
            ErrorMessage = $"At most {GroundBookingKinds.MaxPerGround} booking types.";
            return;
        }

        BookingOffers.Add(new BookingOfferEdit
        {
            Title = BookingOffers.Count == 0 ? "Hourly" : string.Empty,
            DurationHoursText = "1",
            IsEnabled = true,
            PriceText = "0"
        });
    }

    [RelayCommand]
    private void RemoveBookingType(BookingOfferEdit? row)
    {
        if (row is null)
            return;
        BookingOffers.Remove(row);
        if (BookingOffers.Count == 0)
            AddBookingType();
    }

    private void ApplySavedOffers(IReadOnlyList<GroundBookingOfferDto>? offers, decimal hourlyRate)
    {
        BookingOffers.Clear();
        foreach (var saved in offers ?? [])
        {
            BookingOffers.Add(new BookingOfferEdit
            {
                Kind = saved.Kind,
                Title = string.IsNullOrWhiteSpace(saved.Title)
                    ? GroundBookingKinds.TitleOf(saved.Kind)
                    : saved.Title,
                IsEnabled = saved.IsEnabled,
                PriceText = saved.Price.ToString("0"),
                NightPriceText = OptionalMoney(saved.NightPrice),
                WeekendPriceText = OptionalMoney(saved.WeekendPrice),
                WeekendNightPriceText = OptionalMoney(saved.WeekendNightPrice),
                DurationHoursText = saved.DurationHours > 0 ? saved.DurationHours.ToString("0.#") : "1"
            });
        }

        if (BookingOffers.Count == 0)
        {
            AddBookingType();
            if (hourlyRate > 0)
            {
                BookingOffers[0].IsEnabled = true;
                BookingOffers[0].PriceText = hourlyRate.ToString("0");
            }
        }
    }

    private void ApplyNightStart(int hour)
    {
        var h = GroundBookingKinds.ClampNightHour(hour);
        SelectedNightStart = NightStartOptions.FirstOrDefault(x => x.Hour == h)
                             ?? NightStartOptions.FirstOrDefault(x => x.Hour == 18);
    }

    private void ApplyNightEnd(int hour)
    {
        var h = GroundBookingKinds.ClampNightEndHour(hour);
        SelectedNightEnd = NightEndOptions.FirstOrDefault(x => x.Hour == h)
                           ?? NightEndOptions.FirstOrDefault(x => x.Hour == 6);
    }

    private static decimal ParseMoney(string? text)
        => decimal.TryParse(text, out var n) && n > 0 ? n : 0;

    private static string OptionalMoney(decimal amount)
        => amount > 0 ? amount.ToString("0") : string.Empty;

    private void NotifyPhotoState()
    {
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(CanAddMore));
        OnPropertyChanged(nameof(PhotoSummary));
    }

    private void ClearLocationErrors()
    {
        MapsFetchError = null;
        PostalError = null;
        CountryError = null;
        StateError = null;
        CityError = null;
        AreaError = null;
        AddressError = null;
    }

    private bool TryValidateLocation()
    {
        ClearLocationErrors();
        var ok = true;
        if (string.IsNullOrWhiteSpace(GoogleMapsUrl))
        {
            MapsFetchError = "Google Maps location is required.";
            ok = false;
        }
        if (string.IsNullOrWhiteSpace(PostalCode))
        {
            PostalError = "Postal code is required.";
            ok = false;
        }
        if (string.IsNullOrWhiteSpace(CountryName))
        {
            CountryError = "Country is required.";
            ok = false;
        }
        if (string.IsNullOrWhiteSpace(StateName))
        {
            StateError = "State / province is required.";
            ok = false;
        }
        if (string.IsNullOrWhiteSpace(CityName))
        {
            CityError = "City is required.";
            ok = false;
        }
        if (string.IsNullOrWhiteSpace(AreaName))
        {
            AreaError = "Area is required.";
            ok = false;
        }
        if (string.IsNullOrWhiteSpace(AddressLine))
        {
            AddressError = "Street / ground address is required.";
            ok = false;
        }
        return ok;
    }

    private void ApplyLocation(string? postal, string? country, string? state, string? city, string? area)
    {
        PostalCode = postal ?? string.Empty;
        CountryName = country ?? string.Empty;
        StateName = state ?? string.Empty;
        CityName = city ?? string.Empty;
        AreaName = area ?? string.Empty;
    }
}
