using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class OwnerGroundsViewModel : ObservableObject
{
    private readonly IGroundsApiClient _api;
    private int _loadBusy;
    private string? _cursor;

    public ObservableCollection<GroundCardDto> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private string? errorMessage;

    public OwnerGroundsViewModel(IGroundsApiClient api) => _api = api;

    [RelayCommand]
    private Task ReloadAsync()
    {
        _cursor = null;
        HasMore = true;
        Items.Clear();
        return LoadMoreAsync();
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
            ErrorMessage = null;
            var page = await _api.GetOwnerGroundsAsync(_cursor).ConfigureAwait(true);
            foreach (var row in page.Items)
                Items.Add(row);
            _cursor = page.NextCursor;
            HasMore = page.HasMore;
            if (Items.Count == 0)
                ErrorMessage = "No grounds yet. Tap + Add to register one.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _loadBusy, 0);
        }
    }

    [RelayCommand]
    private async Task AddAsync()
        => await Shell.Current.GoToAsync(nameof(Pages.OwnerGroundEditPage)).ConfigureAwait(true);

    [RelayCommand]
    private async Task ViewAsync(GroundCardDto? row)
    {
        if (row is null)
            return;
        await Shell.Current.GoToAsync($"{nameof(Pages.GroundDetailPage)}?id={row.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync(GroundCardDto? row)
    {
        if (row is null)
            return;
        await Shell.Current.GoToAsync($"{nameof(Pages.OwnerGroundEditPage)}?id={row.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(GroundCardDto? row)
    {
        if (row is null)
            return;
        if (!row.CanDelete)
        {
            await Shell.Current.DisplayAlert(
                    "Cannot delete",
                    "This ground has bookings, so it cannot be deleted.",
                    "OK")
                .ConfigureAwait(true);
            return;
        }

        var ok = await Shell.Current.DisplayAlert(
                "Delete ground",
                $"Delete \"{row.Name}\"? This cannot be undone.",
                "Delete",
                "Cancel")
            .ConfigureAwait(true);
        if (!ok)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await _api.DeleteGroundAsync(row.Id).ConfigureAwait(true);
            Items.Remove(row);
            if (Items.Count == 0)
                ErrorMessage = "No grounds yet. Tap + Add to register one.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await Shell.Current.DisplayAlert("Delete failed", ex.Message, "OK").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
