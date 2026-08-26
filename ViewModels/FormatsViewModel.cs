using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed class FormatRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string DetailsLine { get; init; } = string.Empty;
}

public partial class FormatsViewModel : ObservableObject
{
    private readonly IFormatService _formats;
    private readonly IAuthService _auth;

    public ObservableCollection<FormatRow> Items { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? nameFilter;
    [ObservableProperty] private string? errorMessage;

    private List<FormatListItem> _all = new();

    public bool CanWrite => _auth.CanWrite;
    public bool HasFilter => !string.IsNullOrWhiteSpace(NameFilter);

    public FormatsViewModel(IFormatService formats, IAuthService auth)
    {
        _formats = formats;
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
            _all = (await _formats.GetAllAsync().ConfigureAwait(true)).ToList();
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
        IEnumerable<FormatListItem> q = _all;
        if (!string.IsNullOrWhiteSpace(NameFilter))
        {
            var f = NameFilter.Trim();
            q = q.Where(x =>
                x.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                || x.DetailsLine.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var x in q.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
            Items.Add(new FormatRow { Id = x.Id, Name = x.Name, DetailsLine = x.DetailsLine });
    }

    [RelayCommand]
    private void ClearFilter() => NameFilter = null;

    [RelayCommand]
    private async Task EditAsync(FormatRow? row)
    {
        if (row is null || !CanWrite) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.FormatEditPage)}?id={row.Id}").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteAsync(FormatRow? row)
    {
        if (row is null || !CanWrite) return;
        var ok = await Shell.Current.DisplayAlert(
            "Delete format",
            $"Delete \"{row.Name}\"? Only allowed if no match, series, or tournament uses it.",
            "Delete",
            "Cancel").ConfigureAwait(true);
        if (!ok) return;
        try
        {
            await _formats.DeleteAsync(row.Id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Delete failed", ex.Message, "OK").ConfigureAwait(true);
        }
    }
}
