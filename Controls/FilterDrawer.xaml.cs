using System.Windows.Input;

namespace JPL.Controls;

[ContentProperty(nameof(DrawerContent))]
public partial class FilterDrawer : ContentView
{
    public static readonly BindableProperty IsOpenProperty =
        BindableProperty.Create(nameof(IsOpen), typeof(bool), typeof(FilterDrawer), false,
            propertyChanged: OnChromeChanged);

    public static readonly BindableProperty HasActiveFiltersProperty =
        BindableProperty.Create(nameof(HasActiveFilters), typeof(bool), typeof(FilterDrawer), false,
            propertyChanged: OnChromeChanged);

    public static readonly BindableProperty ClearCommandProperty =
        BindableProperty.Create(nameof(ClearCommand), typeof(ICommand), typeof(FilterDrawer),
            propertyChanged: OnChromeChanged);

    public static readonly BindableProperty ApplyCommandProperty =
        BindableProperty.Create(nameof(ApplyCommand), typeof(ICommand), typeof(FilterDrawer),
            propertyChanged: OnChromeChanged);

    public static readonly BindableProperty ApplyButtonTextProperty =
        BindableProperty.Create(nameof(ApplyButtonText), typeof(string), typeof(FilterDrawer), "Search",
            propertyChanged: OnChromeChanged);

    public static readonly BindableProperty DrawerContentProperty =
        BindableProperty.Create(nameof(DrawerContent), typeof(View), typeof(FilterDrawer),
            propertyChanged: (b, _, n) => ((FilterDrawer)b).AttachDrawerContent(n as View));

    private Grid? _closedBar;
    private Border? _openPanel;
    private Button? _openButton;
    private Button? _closedClearButton;
    private Button? _openClearButton;
    private Button? _applyButton;
    private ContentView? _drawerHost;
    private bool _chromeReady;

    public FilterDrawer()
    {
        InitializeComponent();
        BuildChrome();
        AttachDrawerContent(DrawerContent);
        RefreshChrome();
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public bool HasActiveFilters
    {
        get => (bool)GetValue(HasActiveFiltersProperty);
        set => SetValue(HasActiveFiltersProperty, value);
    }

    public ICommand? ClearCommand
    {
        get => (ICommand?)GetValue(ClearCommandProperty);
        set => SetValue(ClearCommandProperty, value);
    }

    public ICommand? ApplyCommand
    {
        get => (ICommand?)GetValue(ApplyCommandProperty);
        set => SetValue(ApplyCommandProperty, value);
    }

    public string ApplyButtonText
    {
        get => (string)GetValue(ApplyButtonTextProperty);
        set => SetValue(ApplyButtonTextProperty, value);
    }

    public View? DrawerContent
    {
        get => (View?)GetValue(DrawerContentProperty);
        set => SetValue(DrawerContentProperty, value);
    }

    private static void OnChromeChanged(BindableObject b, object? o, object? n)
        => ((FilterDrawer)b).RefreshChrome();

    private void OnOpenClicked(object? sender, EventArgs e) => IsOpen = true;

    private void OnCloseClicked(object? sender, EventArgs e) => IsOpen = false;

    private void OnClearClicked(object? sender, EventArgs e)
    {
        if (ClearCommand?.CanExecute(null) == true)
            ClearCommand.Execute(null);
    }

    private void OnApplyClicked(object? sender, EventArgs e)
    {
        if (ApplyCommand?.CanExecute(null) == true)
            ApplyCommand.Execute(null);
        IsOpen = false;
    }

    private void AttachDrawerContent(View? view)
    {
        if (_drawerHost is null)
            return;
        _drawerHost.Content = view;
    }

    private void BuildChrome()
    {
        _openButton = new Button
        {
            Text = "Filters",
            Style = TryStyle("GhostButton"),
            Padding = new Thickness(16, 10),
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Fill
        };
        _openButton.Clicked += OnOpenClicked;

        _closedClearButton = new Button
        {
            Text = "Clear",
            Style = TryStyle("GhostButton"),
            Padding = new Thickness(12, 8),
            IsVisible = false
        };
        _closedClearButton.Clicked += OnClearClicked;

        _closedBar = new Grid { ColumnSpacing = 8 };
        _closedBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        _closedBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _closedBar.Add(_openButton);
        _closedBar.Add(_closedClearButton, 1);

        var title = new Label
        {
            Text = "Filters",
            FontAttributes = FontAttributes.Bold,
            FontSize = 16,
            VerticalOptions = LayoutOptions.Center,
            TextColor = TryColor("Primary", Color.FromArgb("#0D1B2A"))
        };
        var closeTop = new Button
        {
            Text = "Close",
            Style = TryStyle("GhostButton"),
            Padding = new Thickness(12, 8),
            FontAttributes = FontAttributes.Bold
        };
        closeTop.Clicked += OnCloseClicked;

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        header.Add(title);
        header.Add(closeTop, 1);

        _drawerHost = new ContentView();
        var body = new ScrollView { MaximumHeightRequest = 420, Content = _drawerHost };

        _applyButton = GoldButton("Search", OnApplyClicked);
        _applyButton.IsVisible = false;
        _openClearButton = new Button
        {
            Text = "Clear",
            Style = TryStyle("GhostButton"),
            Padding = new Thickness(12, 8)
        };
        _openClearButton.Clicked += OnClearClicked;
        var doneBottom = new Button
        {
            Text = "Done",
            Style = TryStyle("OutlineButton"),
            Padding = new Thickness(12, 8)
        };
        doneBottom.Clicked += OnCloseClicked;

        var footer = new Grid { ColumnSpacing = 8 };
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        footer.Add(_applyButton);
        footer.Add(_openClearButton, 1);
        footer.Add(doneBottom, 2);

        var openStack = new VerticalStackLayout { Spacing = 10 };
        openStack.Add(header);
        openStack.Add(body);
        openStack.Add(footer);

        _openPanel = new Border
        {
            Style = TryStyle("CardBorder"),
            Padding = 12,
            IsVisible = false,
            Content = openStack
        };

        var root = new VerticalStackLayout { Spacing = 8 };
        root.Add(_closedBar);
        root.Add(_openPanel);
        Content = root;
        _chromeReady = true;
    }

    private Button GoldButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            Style = TryStyle("GoldButton"),
            Padding = new Thickness(14, 8)
        };
        button.Clicked += handler;
        return button;
    }

    private void RefreshChrome()
    {
        if (!_chromeReady || _closedBar is null || _openPanel is null)
            return;

        _closedBar.IsVisible = !IsOpen;
        _openPanel.IsVisible = IsOpen;
        if (_closedClearButton is not null)
            _closedClearButton.IsVisible = !IsOpen && HasActiveFilters && ClearCommand is not null;
        if (_openClearButton is not null)
            _openClearButton.IsVisible = ClearCommand is not null;
        if (_applyButton is not null)
        {
            _applyButton.IsVisible = ApplyCommand is not null;
            _applyButton.Text = string.IsNullOrWhiteSpace(ApplyButtonText) ? "Search" : ApplyButtonText;
        }

        if (_openButton is null)
            return;
        _openButton.Text = HasActiveFilters ? "Filters · On" : "Filters";
        _openButton.FontAttributes = HasActiveFilters ? FontAttributes.Bold : FontAttributes.None;
        _openButton.TextColor = HasActiveFilters
            ? TryColor("Secondary", Color.FromArgb("#D4AF37"))
            : TryColor("Primary", Color.FromArgb("#0D1B2A"));
    }

    private static Style? TryStyle(string key)
        => Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Style style
            ? style
            : null;

    private static Color TryColor(string key, Color fallback)
        => Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : fallback;
}
