using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class LeagueEditViewModel : ObservableObject, IQueryAttributable
{
    private readonly ILeagueService _leagues;
    private readonly IAuthService _auth;
    private readonly IAppNavigator _navigator;
    private Guid? _id;
    private byte[]? _imageBytes;

    [ObservableProperty] private string pageTitle = "New league";
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private ImageSource imagePreview = "cric360_logo.png";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;

    public LeagueEditViewModel(ILeagueService leagues, IAuthService auth, IAppNavigator navigator)
    {
        _leagues = leagues;
        _auth = auth;
        _navigator = navigator;
    }

    public void Prepare(Guid? id)
    {
        _id = id is Guid g && g != Guid.Empty ? g : null;
        PageTitle = _id is null ? "New league" : "Edit league";
        Name = string.Empty;
        _imageBytes = null;
        ImagePreview = "cric360_logo.png";
        ErrorMessage = null;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _id = null;
        PageTitle = "New league";
        Name = string.Empty;
        _imageBytes = null;
        ImagePreview = "cric360_logo.png";
        if (query.TryGetValue("id", out var raw) && Guid.TryParse($"{raw}", out var id))
            _id = id;
    }

    public async Task InitializeAsync()
    {
        if (_auth.Current?.CanManageLeagues != true)
        {
            await GoBackAsync().ConfigureAwait(true);
            return;
        }

        if (_id is not Guid id)
            return;

        try
        {
            IsBusy = true;
            PageTitle = "Edit league";
            var item = await _leagues.GetAsync(id).ConfigureAwait(true)
                       ?? throw new InvalidOperationException("League not found.");
            Name = item.Name;
            _imageBytes = item.ImageBytes;
            ImagePreview = LeagueImages.ToImageSource(item.ImageBytes);
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
    private async Task PickImageAsync()
    {
        try
        {
            var result = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions
            {
                Title = "League image"
            }).ConfigureAwait(true);
            if (result is null)
                return;

            await using var stream = await result.OpenReadAsync().ConfigureAwait(true);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(true);
            if (ms.Length > 2_000_000)
            {
                ErrorMessage = "Image must be under 2 MB.";
                return;
            }

            _imageBytes = ms.ToArray();
            ImagePreview = LeagueImages.ToImageSource(_imageBytes);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
            return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            if (_id is Guid id)
                await _leagues.UpdateAsync(id, Name, _imageBytes).ConfigureAwait(true);
            else
                await _leagues.CreateAsync(Name, _imageBytes).ConfigureAwait(true);
            await GoBackAsync().ConfigureAwait(true);
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

    private async Task GoBackAsync()
    {
        var page = Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0].Page : null;
        if (page?.Navigation.NavigationStack.Count > 1)
        {
            await page.Navigation.PopAsync().ConfigureAwait(true);
            return;
        }

        await _navigator.ShowLeaguePickerAsync().ConfigureAwait(true);
    }
}
