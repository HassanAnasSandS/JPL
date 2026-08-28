using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class AmenityEditViewModel : GroundCatalogEditViewModel
{
    public AmenityEditViewModel(IGroundsApiClient api, IAuthService auth)
        : base(api, auth, GroundCatalogKind.Amenities) { }
}

public sealed class ServiceEditViewModel : GroundCatalogEditViewModel
{
    public ServiceEditViewModel(IGroundsApiClient api, IAuthService auth)
        : base(api, auth, GroundCatalogKind.Services) { }
}

public sealed class ShopCatalogEditViewModel : GroundCatalogEditViewModel
{
    public ShopCatalogEditViewModel(IGroundsApiClient api, IAuthService auth)
        : base(api, auth, GroundCatalogKind.Shop) { }
}

public partial class GroundCatalogEditViewModel : ObservableObject, IQueryAttributable
{
    private readonly IGroundsApiClient _api;
    private readonly IAuthService _auth;
    private readonly GroundCatalogKind _kind;
    private Guid? _id;

    [ObservableProperty] private string pageTitle = "New item";
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;

    public string NameLabel => _kind switch
    {
        GroundCatalogKind.Services => "Service name *",
        GroundCatalogKind.Shop => "Shop item *",
        _ => "Amenity name *"
    };

    public string NamePlaceholder => _kind switch
    {
        GroundCatalogKind.Services => "e.g. Umpire",
        GroundCatalogKind.Shop => "e.g. Helmet",
        _ => "e.g. Floodlights"
    };

    public string Hint => _kind switch
    {
        GroundCatalogKind.Services => "Owners can offer this on a ground with their own PKR amount.",
        GroundCatalogKind.Shop => "Owners pick this on a ground and set Rent or Sell plus the PKR price.",
        _ => "Owners can tick this when they list a ground."
    };

    public GroundCatalogEditViewModel(IGroundsApiClient api, IAuthService auth, GroundCatalogKind kind)
    {
        _api = api;
        _auth = auth;
        _kind = kind;
        PageTitle = NewTitle();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _id = null;
        PageTitle = NewTitle();
        Name = string.Empty;
        ErrorMessage = null;
        if (query.TryGetValue("id", out var raw) && Guid.TryParse($"{raw}", out var id))
            _id = id;
    }

    public async Task InitializeAsync()
    {
        if (_auth.Current?.CanManagePlatform != true)
        {
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        if (_id is not Guid id)
            return;

        try
        {
            IsBusy = true;
            PageTitle = EditTitle();
            if (_kind == GroundCatalogKind.Shop)
            {
                var row = (await _api.GetShopCatalogAsync().ConfigureAwait(true))
                    .FirstOrDefault(x => x.Id == id)
                    ?? throw new InvalidOperationException("Shop item not found.");
                Name = row.Name;
                return;
            }

            var list = _kind == GroundCatalogKind.Services
                ? await _api.GetServiceCatalogAsync().ConfigureAwait(true)
                : await _api.GetAmenityCatalogAsync().ConfigureAwait(true);
            var item = list.FirstOrDefault(x => x.Id == id)
                       ?? throw new InvalidOperationException("Item not found.");
            Name = item.Name;
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
    private async Task SaveAsync()
    {
        if (IsBusy)
            return;

        var trimmed = (Name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            ErrorMessage = "Enter a name.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            if (_kind == GroundCatalogKind.Shop)
            {
                if (_id is Guid id)
                    await _api.UpdateShopCatalogAsync(id, trimmed).ConfigureAwait(true);
                else
                    await _api.CreateShopCatalogAsync(trimmed).ConfigureAwait(true);
            }
            else if (_kind == GroundCatalogKind.Services)
            {
                if (_id is Guid id)
                    await _api.UpdateServiceCatalogAsync(id, trimmed).ConfigureAwait(true);
                else
                    await _api.CreateServiceCatalogAsync(trimmed).ConfigureAwait(true);
            }
            else if (_id is Guid amenityId)
                await _api.UpdateAmenityCatalogAsync(amenityId, trimmed).ConfigureAwait(true);
            else
                await _api.CreateAmenityCatalogAsync(trimmed).ConfigureAwait(true);

            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
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

    private string NewTitle() => _kind switch
    {
        GroundCatalogKind.Services => "New service",
        GroundCatalogKind.Shop => "New shop item",
        _ => "New amenity"
    };

    private string EditTitle() => _kind switch
    {
        GroundCatalogKind.Services => "Edit service",
        GroundCatalogKind.Shop => "Edit shop item",
        _ => "Edit amenity"
    };
}
