using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Maui.Controls.Shapes;

namespace JPL.Controls;

/// <summary>
/// Combo box: type to filter, tap to select. Keeps the selected value when the source list is rebuilt.
/// </summary>
public sealed class SearchablePicker : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(SearchablePicker),
        propertyChanged: (b, oldValue, _) => ((SearchablePicker)b).OnItemsSourceChanged(oldValue));

    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(
        nameof(SelectedItem),
        typeof(string),
        typeof(SearchablePicker),
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, value) => ((SearchablePicker)b).OnSelectedChanged(value as string));

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title),
        typeof(string),
        typeof(SearchablePicker),
        propertyChanged: (b, _, value) => ((SearchablePicker)b)._title.Text = value as string ?? string.Empty);

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder),
        typeof(string),
        typeof(SearchablePicker),
        "Type to search",
        propertyChanged: (b, _, value) => ((SearchablePicker)b)._entry.Placeholder = value as string ?? string.Empty);

    static readonly Color Gold = Color.FromArgb("#D4AF37");
    static readonly Color Navy = Color.FromArgb("#0D1B2A");
    static readonly Color ListBg = Color.FromArgb("#F4F7FB");

    readonly Label _title;
    readonly Entry _entry;
    readonly CollectionView _list;
    readonly ObservableCollection<string> _filtered = new();
    INotifyCollectionChanged? _notify;
    bool _syncing;
    bool _open;

    public SearchablePicker()
    {
        _title = new Label
        {
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#5A6A7A"),
            Margin = new Thickness(2, 0, 0, 2)
        };

        _entry = new Entry
        {
            Placeholder = "Type to search",
            FontSize = 14,
            BackgroundColor = Colors.Transparent
        };
        _entry.Focused += (_, _) => ShowList();
        _entry.TextChanged += OnTextChanged;
        _entry.Completed += (_, _) => CommitTyped();

        _list = new CollectionView
        {
            ItemsSource = _filtered,
            SelectionMode = SelectionMode.Single,
            IsVisible = false,
            HeightRequest = 168,
            BackgroundColor = ListBg,
            ItemTemplate = new DataTemplate(() =>
            {
                var label = new Label
                {
                    FontSize = 14,
                    TextColor = Navy,
                    Padding = new Thickness(12, 10),
                    LineBreakMode = LineBreakMode.TailTruncation
                };
                label.SetBinding(Label.TextProperty, ".");
                return label;
            })
        };
        _list.SelectionChanged += OnListSelectionChanged;

        var listBorder = new Border
        {
            Stroke = Color.FromArgb("#D0D7E2"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = 0,
            Margin = new Thickness(0, 4, 0, 0),
            Content = _list
        };
        listBorder.SetBinding(IsVisibleProperty, new Binding(nameof(CollectionView.IsVisible), source: _list));

        Content = new VerticalStackLayout
        {
            Spacing = 0,
            Children = { _title, _entry, listBorder }
        };
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string? SelectedItem
    {
        get => (string?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsEnabled) || propertyName is null)
        {
            _entry.IsEnabled = IsEnabled;
            if (!IsEnabled)
                HideList();
        }
    }

    void OnItemsSourceChanged(object? oldValue)
    {
        if (oldValue is INotifyCollectionChanged oldNotify)
            oldNotify.CollectionChanged -= OnSourceCollectionChanged;
        _notify = ItemsSource as INotifyCollectionChanged;
        if (_notify is not null)
            _notify.CollectionChanged += OnSourceCollectionChanged;
        RebuildFilter();
    }

    void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => MainThread.BeginInvokeOnMainThread(RebuildFilter);

    void OnSelectedChanged(string? value)
    {
        if (_syncing)
            return;
        _syncing = true;
        _entry.Text = value ?? string.Empty;
        _syncing = false;
    }

    void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncing)
            return;
        ShowList();
        RebuildFilter();
    }

    void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not string value)
            return;
        Pick(value);
        _list.SelectedItem = null;
    }

    void Pick(string value)
    {
        _syncing = true;
        SelectedItem = value;
        _entry.Text = value;
        _syncing = false;
        HideList();
        _entry.Unfocus();
    }

    void CommitTyped()
    {
        var typed = _entry.Text?.Trim() ?? string.Empty;
        var match = AllItems().FirstOrDefault(s => string.Equals(s, typed, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            Pick(match);
        else
        {
            _syncing = true;
            _entry.Text = SelectedItem ?? string.Empty;
            _syncing = false;
            HideList();
        }
    }

    void ShowList()
    {
        if (!IsEnabled)
            return;
        _open = true;
        _list.IsVisible = true;
        RebuildFilter();
    }

    void HideList()
    {
        _open = false;
        _list.IsVisible = false;
    }

    void RebuildFilter()
    {
        var items = AllItems();
        IEnumerable<string> shown = items;
        var query = _entry.Text?.Trim() ?? string.Empty;
        if (_open && query.Length > 0 && !string.Equals(query, SelectedItem, StringComparison.OrdinalIgnoreCase))
            shown = items.Where(s => s.Contains(query, StringComparison.OrdinalIgnoreCase));

        var next = shown.ToList();
        if (_filtered.Count == next.Count && _filtered.SequenceEqual(next, StringComparer.OrdinalIgnoreCase))
            return;
        _filtered.Clear();
        foreach (var row in next)
            _filtered.Add(row);
    }

    IReadOnlyList<string> AllItems()
    {
        if (ItemsSource is null)
            return [];
        return ItemsSource.Cast<object?>()
            .Select(o => o?.ToString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();
    }
}
