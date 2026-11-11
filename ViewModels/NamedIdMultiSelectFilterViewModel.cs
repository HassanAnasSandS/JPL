using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JPL.ViewModels;

public sealed partial class NamedIdFilterOption : ObservableObject
{
    private readonly Action? _onChanged;

    public NamedIdFilterOption(Guid id, string name, bool isSelected, Action? onChanged)
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

/// <summary>Multi-select id/name dropdown (Tournament / Series / Match filters).</summary>
public sealed partial class NamedIdMultiSelectFilterViewModel : ObservableObject
{
    private readonly string _allSummary;
    private readonly string _pluralLabel;
    private string _committedKey = string.Empty;

    public NamedIdMultiSelectFilterViewModel(
        string title,
        string allSummary,
        string singularLabel,
        string pluralLabel,
        string hint)
    {
        Title = title;
        Hint = hint;
        _allSummary = allSummary;
        _ = singularLabel;
        _pluralLabel = pluralLabel;
        SummaryText = allSummary;
    }

    public string Title { get; }
    public string Hint { get; }
    public ObservableCollection<NamedIdFilterOption> Options { get; } = new();

    [ObservableProperty] private bool isDropdownOpen;
    [ObservableProperty] private string summaryText;

    public bool HasSelection => Options.Any(o => o.IsSelected);
    public bool HasOptions => Options.Count > 0;

    /// <summary>Null means no filter (nothing checked).</summary>
    public IReadOnlyList<Guid>? SelectedIds
    {
        get
        {
            var ids = Options.Where(o => o.IsSelected).Select(o => o.Id).ToList();
            return ids.Count == 0 ? null : ids;
        }
    }

    public event EventHandler? Committed;

    public void SetOptions(IEnumerable<(Guid Id, string Name)> items)
    {
        var keep = Options.Where(o => o.IsSelected).Select(o => o.Id).ToHashSet();
        Options.Clear();
        foreach (var item in items)
        {
            if (item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Name))
                continue;
            Options.Add(new NamedIdFilterOption(
                item.Id,
                item.Name.Trim(),
                keep.Contains(item.Id),
                RefreshSummary));
        }

        RefreshSummary();
        // Keep committed key in sync so first open after load doesn't false-fire.
        _committedKey = SelectedIds is null ? string.Empty : string.Join(",", SelectedIds.OrderBy(id => id));
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
            0 => _allSummary,
            1 => selected[0].Name,
            2 => $"{selected[0].Name}, {selected[1].Name}",
            _ => $"{selected.Count} {_pluralLabel}"
        };
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasOptions));
    }
}
