using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Pages;
using JPL.Services;

namespace JPL.ViewModels;

public enum GroundCatalogKind
{
    Amenities,
    Services,
    Shop
}

public sealed class CatalogRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string DetailsLine { get; init; } = string.Empty;
    public bool HasDetails => !string.IsNullOrWhiteSpace(DetailsLine);
}

public sealed class AmenitiesViewModel : GroundCatalogListViewModel
{
    public AmenitiesViewModel(IGroundsApiClient api, IAuthService auth)
        : base(api, auth, GroundCatalogKind.Amenities) { }
}

public sealed class ServicesViewModel : GroundCatalogListViewModel
{
    public ServicesViewModel(IGroundsApiClient api, IAuthService auth)
        : base(api, auth, GroundCatalogKind.Services) { }
}

public sealed class ShopCatalogViewModel : GroundCatalogListViewModel
{
    public ShopCatalogViewModel(IGroundsApiClient api, IAuthService auth)
        : base(api, auth, GroundCatalogKind.Shop) { }
}

public partial class GroundCatalogListViewModel : ObservableObject
{
    private readonly IGroundsApiClient _api;
    private readonly IAuthService _auth;
    private readonly GroundCatalogKind _kind;
    private List<CatalogRow> _all = [];

    public ObservableCollection<CatalogRow> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? nameFilter;
    [ObservableProperty] private string? errorMessage;

    public bool CanWrite => _auth.Current?.CanManagePlatform == true;
    public bool HasFilter => !string.IsNullOrWhiteSpace(NameFilter);

    public string Title => _kind switch
    {
        GroundCatalogKind.Services => "Services",
        GroundCatalogKind.Shop => "Shop",
        _ => "Amenities"
    };

    public string FilterPlaceholder => _kind switch
    {
        GroundCatalogKind.Services => "Filter service name",
        GroundCatalogKind.Shop => "Filter shop item",
        _ => "Filter amenity name"
    };

    public string EmptyView => _kind switch
    {
        GroundCatalogKind.Services => "No services yet. Add names owners can offer with a PKR amount.",
        GroundCatalogKind.Shop => "No shop items yet. Add names; owners set Rent/Sell and PKR on the ground.",
        _ => "No amenities yet. Add names owners can tick on a ground."
    };

    public string NewRoute => _kind switch
    {
        GroundCatalogKind.Services => nameof(ServiceEditPage),
        GroundCatalogKind.Shop => nameof(ShopCatalogEditPage),
        _ => nameof(AmenityEditPage)
    };

    public GroundCatalogListViewModel(IGroundsApiClient api, IAuthService auth, GroundCatalogKind kind)
    {
        _api = api;
        _auth = auth;
        _kind = kind;
    }

    partial void OnNameFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasFilter));
        ApplyFilter();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (!CanWrite)
        {
            ErrorMessage = "Only HassanAnas can manage this catalog.";
            return;
        }

        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            _all = _kind switch
            {
                GroundCatalogKind.Shop => (await _api.GetShopCatalogAsync().ConfigureAwait(true))
                    .Select(r => new CatalogRow { Id = r.Id, Name = r.Name })
                    .ToList(),
                GroundCatalogKind.Services => (await _api.GetServiceCatalogAsync().ConfigureAwait(true))
                    .Select(r => new CatalogRow { Id = r.Id, Name = r.Name })
                    .ToList(),
                _ => (await _api.GetAmenityCatalogAsync().ConfigureAwait(true))
                    .Select(r => new CatalogRow { Id = r.Id, Name = r.Name })
                    .ToList()
            };
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _all = [];
            Items.Clear();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearFilter() => NameFilter = null;

    [RelayCommand]
    private async Task EditAsync(CatalogRow? row)
    {
        if (row is null || !CanWrite)
            return;
        await Shell.Current.GoToAsync($"{NewRoute}?id={row.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(CatalogRow? row)
    {
        if (row is null || !CanWrite || Shell.Current is null)
            return;
        var label = _kind switch
        {
            GroundCatalogKind.Services => "service",
            GroundCatalogKind.Shop => "shop item",
            _ => "amenity"
        };
        var ok = await Shell.Current.DisplayAlert(
            $"Delete {label}",
            $"Remove \"{row.Name}\" from the list? Grounds that already use it keep their own copy.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            if (_kind == GroundCatalogKind.Shop)
                await _api.DeleteShopCatalogAsync(row.Id).ConfigureAwait(true);
            else if (_kind == GroundCatalogKind.Services)
                await _api.DeleteServiceCatalogAsync(row.Id).ConfigureAwait(true);
            else
                await _api.DeleteAmenityCatalogAsync(row.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Delete failed", ex.Message, "OK").ConfigureAwait(true);
        }
    }

    private void ApplyFilter()
    {
        Items.Clear();
        IEnumerable<CatalogRow> q = _all;
        if (!string.IsNullOrWhiteSpace(NameFilter))
        {
            var f = NameFilter.Trim();
            q = q.Where(r =>
                r.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                || r.DetailsLine.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var row in q.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            Items.Add(row);
    }
}
