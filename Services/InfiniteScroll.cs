using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace JPL.Services;

/// <summary>
/// MAUI RemainingItemsThreshold often never fires when the first page is shorter
/// than the viewport (5 of 27, empty space, no scroll). Keep fetching pages of 5
/// until the list can scroll, then load the next page at the bottom.
/// </summary>
internal static class InfiniteScroll
{
    private const double EstimatedRowHeight = 64;

    private static readonly BindableProperty HookedProperty =
        BindableProperty.CreateAttached("Hooked", typeof(bool), typeof(InfiniteScroll), false);

    private static readonly BindableProperty ContextHookProperty =
        BindableProperty.CreateAttached("ContextHook", typeof(ContextHook), typeof(InfiniteScroll), null);

    private static readonly BindableProperty ItemsHookProperty =
        BindableProperty.CreateAttached("ItemsHook", typeof(ItemsHook), typeof(InfiniteScroll), null);

    public static void AttachCollectionView(CollectionView cv)
    {
        if (cv.GetValue(HookedProperty) is true)
            return;
        cv.SetValue(HookedProperty, true);
        cv.Scrolled += OnCollectionScrolled;
        cv.SizeChanged += OnSizeChanged;
        cv.Loaded += OnLoaded;
        cv.BindingContextChanged += OnBindingContextChanged;
        cv.PropertyChanged += OnCollectionPropertyChanged;
        HookContext(cv);
        HookItems(cv);
        Request(cv);
    }

    public static void AttachScrollView(ScrollView sv)
    {
        if (sv.GetValue(HookedProperty) is true)
            return;
        sv.SetValue(HookedProperty, true);
        sv.Scrolled += OnScrollViewScrolled;
        sv.SizeChanged += OnSizeChanged;
        sv.Loaded += OnLoaded;
        sv.BindingContextChanged += OnBindingContextChanged;
        sv.PropertyChanged += OnScrollContentChanged;
        if (sv.Content is not null)
            sv.Content.SizeChanged += OnSizeChanged;
        HookContext(sv);
        Request(sv);
    }

    private static void OnLoaded(object? sender, EventArgs e) => Request(sender as VisualElement);

    private static void OnSizeChanged(object? sender, EventArgs e)
    {
        var view = sender as VisualElement
                   ?? (sender as View)?.Parent as VisualElement;
        Request(view);
    }

    private static void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (sender is BindableObject view)
        {
            HookContext(view);
            if (view is CollectionView cv)
                HookItems(cv);
            Request(view as VisualElement);
        }
    }

    private static void OnCollectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is CollectionView cv && e.PropertyName == nameof(CollectionView.ItemsSource))
        {
            HookItems(cv);
            Request(cv);
        }
    }

    private static void OnScrollContentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ScrollView sv || e.PropertyName != nameof(ScrollView.Content))
            return;
        if (sv.Content is not null)
            sv.Content.SizeChanged += OnSizeChanged;
        Request(sv);
    }

    private static void OnCollectionScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (sender is not CollectionView cv)
            return;
        var count = CountItems(cv.ItemsSource);
        if (count <= 0)
            return;
        if (e.LastVisibleItemIndex >= count - 2)
            TryLoad(cv, forceNearEnd: true);
    }

    private static void OnScrollViewScrolled(object? sender, ScrolledEventArgs e)
    {
        if (sender is not ScrollView sv || sv.Height <= 0)
            return;
        var contentH = Math.Max(sv.ContentSize.Height, sv.Content?.Height ?? 0);
        if (contentH <= 0)
            return;
        if (e.ScrollY + sv.Height < contentH - 80)
            return;
        TryLoad(sv, forceNearEnd: true);
    }

    private static void HookContext(BindableObject view)
    {
        if (view.GetValue(ContextHookProperty) is ContextHook old)
            old.Detach();

        if (view.BindingContext is INotifyPropertyChanged npc)
        {
            var hook = new ContextHook(npc, () => Request(view as VisualElement));
            view.SetValue(ContextHookProperty, hook);
        }
    }

    private static void HookItems(CollectionView cv)
    {
        if (cv.GetValue(ItemsHookProperty) is ItemsHook old)
            old.Detach();

        if (cv.ItemsSource is INotifyCollectionChanged ncc)
        {
            var hook = new ItemsHook(ncc, () => Request(cv));
            cv.SetValue(ItemsHookProperty, hook);
        }
    }

    private static void Request(VisualElement? view)
    {
        if (view?.Dispatcher is null)
            return;
        view.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(48), () => TryLoad(view, forceNearEnd: false));
    }

    private static void TryLoad(VisualElement view, bool forceNearEnd)
    {
        var cmd = CommandOf(view);
        if (cmd is null)
            return;
        if (!HasMoreOf(view.BindingContext))
            return;
        if (cmd is IAsyncRelayCommand running && running.IsRunning)
            return;
        if (!cmd.CanExecute(null))
            return;

        if (view is ScrollView sv)
        {
            if (sv.Height <= 1)
                return;
            var contentH = Math.Max(sv.ContentSize.Height, sv.Content?.Height ?? 0);
            if (forceNearEnd || contentH <= sv.Height + 72)
                cmd.Execute(null);
            return;
        }

        if (view is CollectionView cv)
        {
            var count = CountItems(cv.ItemsSource);
            if (count <= 0 || cv.Height <= 1)
                return;
            if (forceNearEnd || count * EstimatedRowHeight <= cv.Height + 72)
                cmd.Execute(null);
        }
    }

    private static ICommand? CommandOf(VisualElement view)
    {
        if (view is CollectionView cv && cv.RemainingItemsThresholdReachedCommand is ICommand list)
            return list;
        return view.BindingContext is { } bc
            ? bc.GetType().GetProperty("LoadMoreCommand", BindingFlags.Instance | BindingFlags.Public)?.GetValue(bc) as ICommand
            : null;
    }

    private static bool HasMoreOf(object? bc)
    {
        if (bc is null)
            return false;
        return bc.GetType().GetProperty("HasMore", BindingFlags.Instance | BindingFlags.Public)?.GetValue(bc) is true;
    }

    private static int CountItems(object? source) => source switch
    {
        ICollection c => c.Count,
        IEnumerable e => e.Cast<object?>().Count(),
        _ => 0
    };

    private sealed class ContextHook
    {
        private readonly INotifyPropertyChanged _npc;
        private readonly PropertyChangedEventHandler _handler;

        public ContextHook(INotifyPropertyChanged npc, Action onChange)
        {
            _npc = npc;
            _handler = (_, e) =>
            {
                if (e.PropertyName is null or "HasMore" or "IsBusy" or "IsLoadingMore"
                    or "Items" or "Leaderboard" or "TeamLeaderboard" or "VisibleRows" or "CountLabel")
                    onChange();
            };
            _npc.PropertyChanged += _handler;
        }

        public void Detach() => _npc.PropertyChanged -= _handler;
    }

    private sealed class ItemsHook
    {
        private readonly INotifyCollectionChanged _ncc;
        private readonly NotifyCollectionChangedEventHandler _handler;

        public ItemsHook(INotifyCollectionChanged ncc, Action onChange)
        {
            _ncc = ncc;
            _handler = (_, _) => onChange();
            _ncc.CollectionChanged += _handler;
        }

        public void Detach() => _ncc.CollectionChanged -= _handler;
    }
}
