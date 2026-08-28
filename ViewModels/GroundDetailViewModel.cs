using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using JPL.Services;

namespace JPL.ViewModels;

public sealed record GroundGalleryDot(int Index, bool IsActive);

[QueryProperty(nameof(GroundId), "id")]
public partial class GroundDetailViewModel : ObservableObject
{
    private readonly IGroundsApiClient _api;
    private readonly IAuthService _auth;
    private readonly IAppNavigator _navigator;
    private readonly IOptions<GroundsApiOptions> _groundsApi;

    public string? GroundId { get; set; }

    [ObservableProperty] private GroundDetailDto? ground;
    [ObservableProperty] private IReadOnlyList<GroundImageDto> images = [];
    [ObservableProperty] private GroundImageDto? currentImage;
    [ObservableProperty] private string? currentImageUrl;
    [ObservableProperty] private int galleryIndex;
    [ObservableProperty] private bool isViewerOpen;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private IReadOnlyList<GroundReviewDto> reviews = [];
    [ObservableProperty] private bool hasReviews;
    [ObservableProperty] private bool reviewsHasMore;
    [ObservableProperty] private bool reviewsBusy;
    [ObservableProperty] private bool reviewsEmpty;

    [ObservableProperty] private bool hasImages;
    [ObservableProperty] private bool hasMultipleImages;
    [ObservableProperty] private string galleryCounter = string.Empty;
    public IReadOnlyList<GroundGalleryDot> GalleryDots
        => Images.Select((_, i) => new GroundGalleryDot(i, i == GalleryIndex)).ToList();

    public string LocationLine => Ground is null
        ? string.Empty
        : string.Join(" · ", new[] { Ground.PostalCode, Ground.AreaName, Ground.CityName, Ground.ProvinceName, Ground.CountryName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string RateLabel => Ground is null
        ? string.Empty
        : GroundBookingOfferDto.Summarize(Ground.Offers, Ground.HourlyRate);

    public IReadOnlyList<GroundBookingOfferDto> OfferedTypes
        => Ground?.Offers?.Where(o => o.IsEnabled).ToList() ?? [];

    public bool HasOffers => OfferedTypes.Count > 0;

    public string RateWindowsLine => Ground is null
        ? string.Empty
        : GroundBookingKinds.NightFromLabel(Ground.NightStartsHour, Ground.NightEndsHour);

    public string AmenitiesLine => Ground?.Amenities is { Count: > 0 } list
        ? string.Join(" · ", list)
        : "Amenities not listed";

    public string ServicesLine => Ground?.Services is { Count: > 0 } list
        ? GroundServiceDto.Summarize(list)
        : "Services not listed";

    public string ShopLine => Ground?.Shop is { Count: > 0 } list
        ? GroundShopItemDto.Summarize(list)
        : "Shop not listed";

    public bool HasWhatsApp => PakistanWhatsApp.TryNormalize(Ground?.WhatsAppNumber ?? Ground?.ContactPhone, out _);

    public IReadOnlyList<GroundLinkDto> Links
        => Ground?.Links?.Where(l => !string.IsNullOrWhiteSpace(l.Url)).ToList() ?? [];

    public bool HasLinks => Links.Count > 0;

    public IReadOnlyList<GroundVideoDto> Videos
        => Ground?.Videos?.Where(v => !string.IsNullOrWhiteSpace(v.Url)).ToList() ?? [];

    public bool HasVideos => Videos.Count > 0;

    [ObservableProperty] private bool isVideoPlayerOpen;
    [ObservableProperty] private GroundVideoDto? playingVideo;
    [ObservableProperty] private WebViewSource? videoPlayerSource;

    public string PlayingVideoTitle => PlayingVideo?.DisplayTitle ?? "Video";

    public bool CanRequestBooking => _auth.Current?.CanBookGrounds == true;

    public string RatingLine => Ground?.RatingLine ?? string.Empty;

    private string? _reviewsCursor;
    private int _reviewsLoadBusy;

    public GroundDetailViewModel(
        IGroundsApiClient api, IAuthService auth, IAppNavigator navigator, IOptions<GroundsApiOptions> groundsApi)
    {
        _api = api;
        _auth = auth;
        _navigator = navigator;
        _groundsApi = groundsApi;
    }

    public async Task LoadAsync()
    {
        if (!Guid.TryParse(GroundId, out var id))
            return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            Ground = await _api.GetGroundAsync(id).ConfigureAwait(true);
            var list = Ground?.Images?.OrderBy(i => i.SortOrder).ToList() ?? [];
            Images = list;
            var cover = list.FindIndex(i => i.IsCover);
            GalleryIndex = cover >= 0 ? cover : 0;
            NotifyGroundFields();
            if (Ground is null)
                ErrorMessage = "Ground not found.";
            else
                await LoadReviewsAsync(reset: true).ConfigureAwait(true);
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

    partial void OnImagesChanged(IReadOnlyList<GroundImageDto> value) => SyncGallery();

    partial void OnGalleryIndexChanged(int value) => SyncGallery();

    void SyncGallery()
    {
        CurrentImage = Images.Count == 0
            ? null
            : Images[Math.Clamp(GalleryIndex, 0, Images.Count - 1)];
        CurrentImageUrl = CurrentImage?.Url;
        HasImages = Images.Count > 0;
        HasMultipleImages = Images.Count > 1;
        GalleryCounter = HasImages ? $"{GalleryIndex + 1} / {Images.Count}" : string.Empty;
        OnPropertyChanged(nameof(GalleryDots));
    }

    void NotifyGroundFields()
    {
        OnPropertyChanged(nameof(LocationLine));
        OnPropertyChanged(nameof(RateLabel));
        OnPropertyChanged(nameof(AmenitiesLine));
        OnPropertyChanged(nameof(ServicesLine));
        OnPropertyChanged(nameof(ShopLine));
        OnPropertyChanged(nameof(OfferedTypes));
        OnPropertyChanged(nameof(HasOffers));
        OnPropertyChanged(nameof(RateWindowsLine));
        OnPropertyChanged(nameof(HasWhatsApp));
        OnPropertyChanged(nameof(Links));
        OnPropertyChanged(nameof(HasLinks));
        OnPropertyChanged(nameof(Videos));
        OnPropertyChanged(nameof(HasVideos));
        OnPropertyChanged(nameof(RatingLine));
    }

    [RelayCommand]
    private Task LoadMoreReviewsAsync() => LoadReviewsAsync(reset: false);

    private async Task LoadReviewsAsync(bool reset)
    {
        if (Ground is null)
            return;
        if (!reset && (!ReviewsHasMore || Interlocked.Exchange(ref _reviewsLoadBusy, 1) == 1))
            return;
        if (reset)
            Interlocked.Exchange(ref _reviewsLoadBusy, 1);
        try
        {
            if (reset)
            {
                _reviewsCursor = null;
                ReviewsHasMore = true;
                Reviews = [];
            }

            if (!ReviewsHasMore)
                return;

            ReviewsBusy = Reviews.Count == 0;
            var page = await _api.GetGroundReviewsAsync(Ground.Id, _reviewsCursor).ConfigureAwait(true);
            var next = reset ? page.Items.ToList() : Reviews.Concat(page.Items).ToList();
            Reviews = next;
            HasReviews = Reviews.Count > 0;
            ReviewsEmpty = !HasReviews;
            ReviewsHasMore = page.HasMore;
            _reviewsCursor = page.NextCursor;
        }
        catch
        {
            if (reset)
            {
                Reviews = [];
                HasReviews = false;
                ReviewsEmpty = true;
            }
        }
        finally
        {
            ReviewsBusy = false;
            Interlocked.Exchange(ref _reviewsLoadBusy, 0);
        }
    }

    [RelayCommand]
    private void PreviousImage()
    {
        if (Images.Count == 0)
            return;
        GalleryIndex = (GalleryIndex - 1 + Images.Count) % Images.Count;
    }

    [RelayCommand]
    private void NextImage()
    {
        if (Images.Count == 0)
            return;
        GalleryIndex = (GalleryIndex + 1) % Images.Count;
    }

    [RelayCommand]
    private void SelectImage(int index)
    {
        if (index < 0 || index >= Images.Count)
            return;
        GalleryIndex = index;
    }

    [RelayCommand]
    private void OpenViewer()
    {
        if (!HasImages)
            return;
        CloseVideo();
        IsViewerOpen = true;
    }

    [RelayCommand]
    private void CloseViewer() => IsViewerOpen = false;

    [RelayCommand]
    private void OpenVideo(GroundVideoDto? video)
    {
        if (video is null || string.IsNullOrWhiteSpace(video.Url))
            return;
        IsViewerOpen = false;
        PlayingVideo = video;
        VideoPlayerSource = GroundVideoUrls.ToPlayerSource(video.Url, _groundsApi.Value.BaseUrl);
        OnPropertyChanged(nameof(PlayingVideoTitle));
        IsVideoPlayerOpen = VideoPlayerSource is not null;
    }

    [RelayCommand]
    private void CloseVideo()
    {
        IsVideoPlayerOpen = false;
        VideoPlayerSource = null;
        PlayingVideo = null;
        OnPropertyChanged(nameof(PlayingVideoTitle));
    }

    [RelayCommand]
    private async Task OpenMapsAsync()
    {
        var url = Ground?.GoogleMapsUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            await Shell.Current.DisplayAlert("Maps", "No Google Maps link for this ground.", "OK")
                .ConfigureAwait(true);
            return;
        }

        try
        {
            await Launcher.OpenAsync(url).ConfigureAwait(true);
        }
        catch
        {
            await Shell.Current.DisplayAlert("Maps", url, "OK").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task OpenLinkAsync(GroundLinkDto? link)
    {
        var url = link?.Url;
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            await Launcher.OpenAsync(url).ConfigureAwait(true);
        }
        catch
        {
            await Shell.Current.DisplayAlert("Link", url, "OK").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task BookAsync()
    {
        if (Ground is null)
            return;
        if (!await GroundBookingGate.EnsureCanBookAsync(_auth, _navigator).ConfigureAwait(true))
            return;
        await Shell.Current.GoToAsync($"{nameof(Pages.GroundBookRequestPage)}?id={Ground.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private Task ContactAsync()
        => PakistanWhatsApp.OpenChatAsync(
            Ground?.WhatsAppNumber ?? Ground?.ContactPhone,
            Ground is null ? null : $"Assalam o Alaikum, I want to chat about booking {Ground.Name}.");
}
