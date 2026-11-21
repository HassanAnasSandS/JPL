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
    private ContentPage? _modalPage;
    private Page? _toolbarOwner;
    private ToolbarItem? _toolbarFilter;
    private View? _modalContent;
    private object? _modalBindingContext;
    private bool _chromeReady;

    public FilterDrawer()
    {
        InitializeComponent();
        BuildChrome();
        AttachDrawerContent(DrawerContent);
        RefreshChrome();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        UpdateToolbarFilter();
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

    private async void OnOpenClicked(object? sender, EventArgs e)
    {
        if (IsOpen || DrawerContent is null)
            return;

        var owner = GetParentPage();
        var navigation = owner?.Navigation ?? Shell.Current?.Navigation;
        if (navigation is null)
            return;

        _modalContent = DrawerContent;
        _modalBindingContext = BindingContext;
        _modalContent.BindingContext = _modalBindingContext;

        var title = new Label
        {
            Text = "Filters",
            FontAttributes = FontAttributes.Bold,
            FontSize = 18,
            VerticalOptions = LayoutOptions.Center,
            TextColor = TryColor("Primary", Color.FromArgb("#0D1B2A"))
        };
        var close = new Button
        {
            Text = "Close",
            Style = TryStyle("GhostButton"),
            Padding = new Thickness(12, 8)
        };
        close.Clicked += OnCloseClicked;

        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(title);
        header.Add(close, 1);

        var scroll = new ScrollView { Content = _modalContent };
        var clear = new Button
        {
            Text = "Clear",
            Style = TryStyle("GhostButton"),
            IsVisible = ClearCommand is not null
        };
        clear.Clicked += OnClearClicked;
        var apply = GoldButton(string.IsNullOrWhiteSpace(ApplyButtonText) ? "Search" : ApplyButtonText, OnApplyClicked);
        apply.IsVisible = ApplyCommand is not null;
        var done = new Button { Text = "Done", Style = TryStyle("OutlineButton"), Padding = new Thickness(12, 8) };
        done.Clicked += OnCloseClicked;

        var footer = new Grid { ColumnSpacing = 8 };
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        footer.Add(apply);
        footer.Add(clear, 1);
        footer.Add(done, 2);

        var card = new Border
        {
            Style = TryStyle("CardBorder"),
            Padding = 14,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            Margin = new Thickness(18),
            MaximumHeightRequest = DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density * 0.85,
            Content = new VerticalStackLayout { Spacing = 12, Children = { header, scroll, footer } }
        };
        _modalPage = new ContentPage
        {
            BackgroundColor = Color.FromArgb("#99000000"),
            BindingContext = _modalBindingContext,
            Content = new Grid { Children = { card } }
        };
        _modalPage.Disappearing += OnModalDisappearing;
        IsOpen = true;
        _drawerHost!.Content = null;
        try
        {
            await navigation.PushModalAsync(_modalPage);
        }
        catch
        {
            RestoreModalContent();
            throw;
        }
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (_modalPage is null)
        {
            IsOpen = false;
            return;
        }

        var page = _modalPage;
        var navigation = page.Navigation;
        if (navigation is not null)
            await navigation.PopModalAsync();
        RestoreModalContent();
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        if (ClearCommand?.CanExecute(null) == true)
            ClearCommand.Execute(null);
    }

    private void OnApplyClicked(object? sender, EventArgs e)
    {
        if (ApplyCommand?.CanExecute(null) == true)
            ApplyCommand.Execute(null);
        OnCloseClicked(sender, e);
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
            HorizontalOptions = LayoutOptions.End
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
        _closedBar.Add(_closedClearButton);
        _closedBar.Add(_openButton, 1);

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

        // Filter content is hosted in a modal page so it does not consume list space.
        _openPanel = new Border { IsVisible = false, Content = openStack };

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

        _closedBar.IsVisible = _toolbarOwner is null && !IsOpen;
        _openPanel.IsVisible = false;
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
        if (_toolbarFilter is not null)
            _toolbarFilter.Text = HasActiveFilters ? "Filters · On" : "Filters";
    }

    private static Style? TryStyle(string key)
        => Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Style style
            ? style
            : null;

    private static Color TryColor(string key, Color fallback)
        => Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : fallback;

    private Page? GetParentPage()
    {
        Element? current = this;
        while (current is not null)
        {
            if (current is Page page)
                return page;
            current = current.Parent;
        }
        return null;
    }

    private void UpdateToolbarFilter()
    {
        var page = GetParentPage();
        if (ReferenceEquals(page, _toolbarOwner))
            return;

        if (_toolbarOwner is not null && _toolbarFilter is not null)
        {
            _toolbarFilter.Clicked -= OnOpenClicked;
            _toolbarOwner.ToolbarItems.Remove(_toolbarFilter);
        }

        _toolbarOwner = page;
        _toolbarFilter = null;
        if (_toolbarOwner is not null)
        {
            _toolbarFilter = new ToolbarItem
            {
                Text = HasActiveFilters ? "Filters · On" : "Filters",
                Order = ToolbarItemOrder.Primary,
                Priority = 0
            };
            _toolbarFilter.Clicked += OnOpenClicked;
            _toolbarOwner.ToolbarItems.Add(_toolbarFilter);
        }
        RefreshChrome();
    }

    private void RestoreModalContent()
    {
        if (_modalContent is not null)
        {
            _modalContent.BindingContext = _modalBindingContext;
            _drawerHost!.Content = _modalContent;
        }
        _modalPage = null;
        _modalContent = null;
        _modalBindingContext = null;
        IsOpen = false;
    }

    private void OnModalDisappearing(object? sender, EventArgs e)
    {
        if (_modalPage is not null)
            RestoreModalContent();
    }
}
