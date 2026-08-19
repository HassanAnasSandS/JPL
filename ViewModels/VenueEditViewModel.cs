using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class VenueEditViewModel : ObservableObject, IQueryAttributable
{
    private readonly IVenueService _venues;
    private readonly IAuthService _auth;
    private Guid? _id;

    [ObservableProperty] private string pageTitle = "New venue";
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;

    public VenueEditViewModel(IVenueService venues, IAuthService auth)
    {
        _venues = venues;
        _auth = auth;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _id = null;
        PageTitle = "New venue";
        Name = string.Empty;
        if (query.TryGetValue("id", out var raw) && Guid.TryParse($"{raw}", out var id))
            _id = id;
    }

    public async Task InitializeAsync()
    {
        if (!_auth.CanWrite)
        {
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
            return;
        }

        if (_id is not Guid id) return;
        try
        {
            IsBusy = true;
            PageTitle = "Edit venue";
            var item = await _venues.GetAsync(id).ConfigureAwait(true)
                       ?? throw new InvalidOperationException("Venue not found.");
            Name = item.Name;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            if (_id is Guid id)
                await _venues.UpdateAsync(id, Name).ConfigureAwait(true);
            else
                await _venues.CreateAsync(Name).ConfigureAwait(true);
            await Shell.Current.GoToAsync("..").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }
}
