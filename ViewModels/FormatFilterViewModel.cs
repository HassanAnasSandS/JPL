using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Services;

namespace JPL.ViewModels;

public sealed partial class FormatFilterOption : ObservableObject
{
    private readonly Action? _onChanged;

    public FormatFilterOption(Guid id, string name, bool isSelected, Action? onChanged)
    {
        Id = id;
        Name = name;
        _onChanged = onChanged;
        IsSelected = isSelected;
    }

    public Guid Id { get; }
    public string Name { get; }

    [ObservableProperty] private bool isSelected;

    partial void OnIsSelectedChanged(bool value) => _onChanged?.Invoke();
}

/// <summary>Multi-select format dropdown shared by Statistics, Rankings, Hall of Fame, and Strength Meter.</summary>
public sealed partial class FormatFilterViewModel : ObservableObject
{
    private readonly IFormatService _formats;
    private string _committedKey = string.Empty;

    public ObservableCollection<FormatFilterOption> Options { get; } = new();

    [ObservableProperty] private bool isDropdownOpen;
    [ObservableProperty] private string summaryText = "All formats";

    public bool HasSelection => Options.Any(o => o.IsSelected);
    public bool HasOptions => Options.Count > 0;
    public bool HasNoOptions => Options.Count == 0;

    /// <summary>Null means all formats (nothing checked).</summary>
    public IReadOnlyList<Guid>? SelectedIds
    {
        get
        {
            var ids = Options.Where(o => o.IsSelected).Select(o => o.Id).ToList();
            return ids.Count == 0 ? null : ids;
        }
    }

    /// <summary>Raised when the dropdown closes with a different selection.</summary>
    public event EventHandler? Committed;

    public FormatFilterViewModel(IFormatService formats) => _formats = formats;

    public async Task EnsureLoadedAsync()
    {
        try
        {
            var keep = Options.Where(o => o.IsSelected).Select(o => o.Id).ToHashSet();
            var list = await _formats.GetAllAsync().ConfigureAwait(true);
            Options.Clear();
            foreach (var item in list)
            {
                Options.Add(new FormatFilterOption(
                    item.Id,
                    item.Name,
                    keep.Contains(item.Id),
                    RefreshSummary));
            }

            RefreshSummary();
        }
        catch
        {
            /* format list is optional — pages still load unfiltered */
        }
    }

    public void ClearSelection()
    {
        foreach (var option in Options)
            option.IsSelected = false;
        _committedKey = string.Empty;
        RefreshSummary();
    }

    [RelayCommand]
    private void ToggleDropdown()
    {
        if (IsDropdownOpen)
            CloseAndCommit();
        else
            IsDropdownOpen = true;
    }

    [RelayCommand]
    private void CloseDropdown() => CloseAndCommit();

    private void CloseAndCommit()
    {
        IsDropdownOpen = false;
        var key = SelectedIds is null ? string.Empty : string.Join(",", SelectedIds.OrderBy(id => id));
        if (string.Equals(key, _committedKey, StringComparison.Ordinal))
            return;
        _committedKey = key;
        Committed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshSummary()
    {
        var selected = Options.Where(o => o.IsSelected).ToList();
        SummaryText = selected.Count switch
        {
            0 => "All formats",
            1 => selected[0].Name,
            2 => $"{selected[0].Name}, {selected[1].Name}",
            _ => $"{selected.Count} formats"
        };
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasOptions));
        OnPropertyChanged(nameof(HasNoOptions));
    }
}
