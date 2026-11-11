using System.Runtime.CompilerServices;
using JPL.ViewModels;

namespace JPL.Controls;

/// <summary>Marks a stats table cell with its column key and designed width.</summary>
public static class StatsColumn
{
    public static readonly BindableProperty KeyProperty =
        BindableProperty.CreateAttached("Key", typeof(string), typeof(StatsColumn), null);

    public static readonly BindableProperty WidthProperty =
        BindableProperty.CreateAttached("Width", typeof(double), typeof(StatsColumn), 40d);

    public static string? GetKey(BindableObject view) => (string?)view.GetValue(KeyProperty);
    public static void SetKey(BindableObject view, string? value) => view.SetValue(KeyProperty, value);

    public static double GetWidth(BindableObject view) => (double)view.GetValue(WidthProperty);
    public static void SetWidth(BindableObject view, double value) => view.SetValue(WidthProperty, value);
}

/// <summary>
/// Collapses Grid columns whose cells are hidden via <see cref="StatsColumnVisibilityViewModel"/>.
/// </summary>
public static class StatsColumnGrid
{
    private static readonly ConditionalWeakTable<Grid, Subscription> Subscriptions = new();

    public static readonly BindableProperty ProviderProperty =
        BindableProperty.CreateAttached(
            "Provider",
            typeof(StatsColumnVisibilityViewModel),
            typeof(StatsColumnGrid),
            null,
            propertyChanged: OnProviderChanged);

    public static StatsColumnVisibilityViewModel? GetProvider(BindableObject view)
        => (StatsColumnVisibilityViewModel?)view.GetValue(ProviderProperty);

    public static void SetProvider(BindableObject view, StatsColumnVisibilityViewModel? value)
        => view.SetValue(ProviderProperty, value);

    private static void OnProviderChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not Grid grid)
            return;

        if (Subscriptions.TryGetValue(grid, out var existing))
        {
            existing.Detach();
            Subscriptions.Remove(grid);
        }

        if (newValue is not StatsColumnVisibilityViewModel provider)
            return;

        var sub = new Subscription(grid, provider);
        Subscriptions.Add(grid, sub);
        sub.Attach();
        Apply(grid, provider);
    }

    private static void Apply(Grid grid, StatsColumnVisibilityViewModel provider)
    {
        var maxCol = -1;
        foreach (var child in grid.Children)
        {
            if (child is not BindableObject bo)
                continue;
            if (StatsColumn.GetKey(bo) is null)
                continue;
            maxCol = Math.Max(maxCol, Grid.GetColumn((View)child));
        }

        if (maxCol < 0)
            return;

        while (grid.ColumnDefinitions.Count <= maxCol)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        foreach (var child in grid.Children)
        {
            if (child is not View view)
                continue;
            var key = StatsColumn.GetKey(view);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var col = Grid.GetColumn(view);
            if (col < 0 || col >= grid.ColumnDefinitions.Count)
                continue;

            var visible = provider.IsVisible(key);
            view.IsVisible = visible;
            var width = StatsColumn.GetWidth(view);
            grid.ColumnDefinitions[col].Width = visible
                ? new GridLength(width, GridUnitType.Absolute)
                : new GridLength(0);
        }
    }

    private sealed class Subscription
    {
        private readonly Grid _grid;
        private readonly StatsColumnVisibilityViewModel _provider;
        private bool _attached;

        public Subscription(Grid grid, StatsColumnVisibilityViewModel provider)
        {
            _grid = grid;
            _provider = provider;
        }

        public void Attach()
        {
            if (_attached)
                return;
            _attached = true;
            _provider.Changed += OnChanged;
            _grid.Loaded += OnLoaded;
            _grid.HandlerChanged += OnHandlerChanged;
        }

        public void Detach()
        {
            if (!_attached)
                return;
            _attached = false;
            _provider.Changed -= OnChanged;
            _grid.Loaded -= OnLoaded;
            _grid.HandlerChanged -= OnHandlerChanged;
        }

        private void OnChanged(object? sender, EventArgs e) => Apply(_grid, _provider);
        private void OnLoaded(object? sender, EventArgs e) => Apply(_grid, _provider);
        private void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (_grid.Handler is not null)
                Apply(_grid, _provider);
        }
    }
}
