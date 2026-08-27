using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class CatalogRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public partial class GroundSettingsViewModel : ObservableObject
{
    public const string TabAmenities = "amenities";
    public const string TabServices = "services";

    private readonly IGroundsApiClient _api;
    private readonly IAuthService _auth;

    public ObservableCollection<CatalogRow> Amenities { get; } = new();
    public ObservableCollection<CatalogRow> Services { get; } = new();

    [ObservableProperty] private string selectedTab = TabAmenities;
    [ObservableProperty] private string newName = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;

    public bool CanManage => _auth.Current?.CanManagePlatform == true;
    public bool IsAmenitiesTab => SelectedTab == TabAmenities;
    public bool IsServicesTab => SelectedTab == TabServices;
    public string AddPlaceholder => IsAmenitiesTab ? "Amenity name (e.g. Floodlights)" : "Service name (e.g. Umpire)";

    public GroundSettingsViewModel(IGroundsApiClient api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    partial void OnSelectedTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsAmenitiesTab));
        OnPropertyChanged(nameof(IsServicesTab));
        OnPropertyChanged(nameof(AddPlaceholder));
        ErrorMessage = null;
    }

    [RelayCommand]
    private void SelectTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab))
            return;
        SelectedTab = tab.Trim().ToLowerInvariant();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (!CanManage)
        {
            ErrorMessage = "Only HassanAnas can manage ground settings.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var amenities = await _api.GetAmenityCatalogAsync().ConfigureAwait(true);
            var services = await _api.GetServiceCatalogAsync().ConfigureAwait(true);
            Replace(Amenities, amenities);
            Replace(Services, services);
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
    private async Task AddAsync()
    {
        if (!CanManage || IsBusy)
            return;
        var name = NewName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            ErrorMessage = "Enter a name.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            if (IsAmenitiesTab)
            {
                var saved = await _api.CreateAmenityCatalogAsync(name).ConfigureAwait(true);
                Amenities.Add(new CatalogRow { Id = saved.Id, Name = saved.Name });
            }
            else
            {
                var saved = await _api.CreateServiceCatalogAsync(name).ConfigureAwait(true);
                Services.Add(new CatalogRow { Id = saved.Id, Name = saved.Name });
            }
            NewName = string.Empty;
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
    private async Task EditAsync(CatalogRow? row)
    {
        if (!CanManage || IsBusy || row is null || Shell.Current is null)
            return;
        var next = await Shell.Current.DisplayPromptAsync(
            IsAmenitiesTab ? "Rename amenity" : "Rename service",
            "Name",
            "Save",
            "Cancel",
            initialValue: row.Name,
            maxLength: 40).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(next) || string.Equals(next.Trim(), row.Name, StringComparison.Ordinal))
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var name = next.Trim();
            CatalogItemDto saved;
            if (IsAmenitiesTab)
            {
                saved = await _api.UpdateAmenityCatalogAsync(row.Id, name).ConfigureAwait(true);
                ReplaceRow(Amenities, saved);
            }
            else
            {
                saved = await _api.UpdateServiceCatalogAsync(row.Id, name).ConfigureAwait(true);
                ReplaceRow(Services, saved);
            }
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
    private async Task DeleteAsync(CatalogRow? row)
    {
        if (!CanManage || IsBusy || row is null || Shell.Current is null)
            return;
        var kind = IsAmenitiesTab ? "amenity" : "service";
        var ok = await Shell.Current.DisplayAlert(
            "Delete",
            $"Remove \"{row.Name}\" from the {kind} list? Grounds that already use it keep their own copy.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            if (IsAmenitiesTab)
            {
                await _api.DeleteAmenityCatalogAsync(row.Id).ConfigureAwait(true);
                Remove(Amenities, row.Id);
            }
            else
            {
                await _api.DeleteServiceCatalogAsync(row.Id).ConfigureAwait(true);
                Remove(Services, row.Id);
            }
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

    private static void Replace(ObservableCollection<CatalogRow> target, IReadOnlyList<CatalogItemDto> source)
    {
        target.Clear();
        foreach (var row in source)
            target.Add(new CatalogRow { Id = row.Id, Name = row.Name });
    }

    private static void ReplaceRow(ObservableCollection<CatalogRow> target, CatalogItemDto saved)
    {
        for (var i = 0; i < target.Count; i++)
        {
            if (target[i].Id != saved.Id)
                continue;
            target[i] = new CatalogRow { Id = saved.Id, Name = saved.Name };
            return;
        }
    }

    private static void Remove(ObservableCollection<CatalogRow> target, Guid id)
    {
        var match = target.FirstOrDefault(x => x.Id == id);
        if (match is not null)
            target.Remove(match);
    }
}
