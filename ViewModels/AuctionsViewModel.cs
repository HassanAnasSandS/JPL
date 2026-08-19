using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public partial class AuctionsViewModel : ObservableObject
{
    private readonly IAuctionService _auctions;
    private readonly IAuthService _auth;
    private readonly IAuctionsModuleGate _gate;

    public ObservableCollection<AuctionListItem> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool canWrite;
    [ObservableProperty] private bool isUnlocked;

    public AuctionsViewModel(IAuctionService auctions, IAuthService auth, IAuctionsModuleGate gate)
    {
        _auctions = auctions;
        _auth = auth;
        _gate = gate;
        CanWrite = _auth.CanWrite;
        IsUnlocked = _gate.IsUnlocked;
    }

    public void RefreshAuthState()
    {
        CanWrite = _auth.CanWrite;
        IsUnlocked = _gate.IsUnlocked;
    }

    /// <summary>Prompt for module password only when write action needs it.</summary>
    private async Task<bool> EnsureWriteUnlockedAsync()
    {
        if (!CanWrite)
        {
            ErrorMessage = "Read-only user cannot change auctions.";
            return false;
        }

        if (_gate.IsUnlocked)
        {
            IsUnlocked = true;
            return true;
        }

        var password = await Shell.Current.DisplayPromptAsync(
            "Auction password",
            "Enter password for Add / Edit / Delete.",
            accept: "Unlock",
            cancel: "Cancel",
            placeholder: "Password",
            maxLength: 32,
            keyboard: Keyboard.Numeric).ConfigureAwait(true);

        if (password is null)
            return false;

        if (_gate.TryUnlock(password))
        {
            IsUnlocked = true;
            ErrorMessage = null;
            return true;
        }

        IsUnlocked = false;
        await Shell.Current.DisplayAlert("Access denied", "Wrong password.", "OK").ConfigureAwait(true);
        return false;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            CanWrite = _auth.CanWrite;
            IsUnlocked = _gate.IsUnlocked;
            await MauiProgram.WhenReadyAsync().ConfigureAwait(true);

            var list = await _auctions.GetAllAsync().ConfigureAwait(true);
            Items.Clear();
            foreach (var item in list)
                Items.Add(item);
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
        if (!await EnsureWriteUnlockedAsync().ConfigureAwait(true))
            return;
        await Shell.Current.GoToAsync(nameof(Pages.AuctionEditPage)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditAsync(AuctionListItem? item)
    {
        if (item is null) return;
        if (!await EnsureWriteUnlockedAsync().ConfigureAwait(true))
            return;
        await Shell.Current.GoToAsync($"{nameof(Pages.AuctionEditPage)}?id={item.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ViewAsync(AuctionListItem? item)
    {
        if (item is null) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.AuctionEditPage)}?id={item.Id}&mode=view")
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(AuctionListItem? item)
    {
        if (item is null) return;
        if (!await EnsureWriteUnlockedAsync().ConfigureAwait(true))
            return;

        var ok = await Shell.Current.DisplayAlert(
            "Delete auction",
            $"Delete \"{item.Title}\"? This cannot be undone.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok) return;

        try
        {
            IsBusy = true;
            await _auctions.SoftDeleteAsync(item.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
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
    private async Task OpenReportAsync()
        => await Shell.Current.GoToAsync(nameof(Pages.AuctionReportPage)).ConfigureAwait(true);
}
