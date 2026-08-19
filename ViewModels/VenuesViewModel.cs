using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class VenueRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public partial class VenuesViewModel : ObservableObject
{
    private readonly IVenueService _venues;
    private readonly IAuthService _auth;

    public ObservableCollection<VenueRow> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? nameFilter;
    [ObservableProperty] private string? errorMessage;

    private List<VenueListItem> _all = new();

    public bool CanWrite => _auth.CanWrite;
    public bool HasFilter => !string.IsNullOrWhiteSpace(NameFilter);

    public VenuesViewModel(IVenueService venues, IAuthService auth)
    {
        _venues = venues;
        _auth = auth;
    }

    partial void OnNameFilterChanged(string? value)
    {
        OnPropertyChanged(nameof(HasFilter));
        ApplyFilter();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            _all = (await _venues.GetAllAsync().ConfigureAwait(true)).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _all = new();
            Items.Clear();
        }
        finally { IsBusy = false; }
    }

    private void ApplyFilter()
    {
        Items.Clear();
        IEnumerable<VenueListItem> q = _all;
        if (!string.IsNullOrWhiteSpace(NameFilter))
        {
            var f = NameFilter.Trim();
            q = q.Where(v => v.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var v in q.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            Items.Add(new VenueRow { Id = v.Id, Name = v.Name });
    }

    [RelayCommand]
    private void ClearFilter() => NameFilter = null;

    [RelayCommand]
    private async Task EditAsync(VenueRow? row)
    {
        if (row is null || !CanWrite) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.VenueEditPage)}?id={row.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(VenueRow? row)
    {
        if (row is null || !CanWrite) return;
        var ok = await Shell.Current.DisplayAlert(
            "Delete venue",
            $"Delete \"{row.Name}\"? Only allowed if no bookings use it.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            await _venues.DeleteAsync(row.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Delete failed", ex.Message, "OK").ConfigureAwait(true);
        }
    }
}
