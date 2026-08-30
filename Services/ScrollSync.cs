namespace JPL.Services;

/// <summary>
/// Keeps table column headers pinned while a vertical ScrollView moves.
/// TranslationY = ScrollY holds the header at the top of the viewport.
/// </summary>
internal static class ScrollSync
{
    public static void FreezeHeader(ScrollView scroll, params VisualElement[] headers)
    {
        if (headers.Length == 0)
            return;

        scroll.Scrolled += (_, e) =>
        {
            foreach (var header in headers)
                header.TranslationY = e.ScrollY;
        };
    }

    public static void Horizontal(ScrollView a, ScrollView b)
    {
        var gate = 0;
        a.Scrolled += async (_, e) =>
        {
            if (Interlocked.Exchange(ref gate, 1) == 1)
                return;
            try
            {
                if (Math.Abs(b.ScrollX - e.ScrollX) > 0.5)
                    await b.ScrollToAsync(e.ScrollX, 0, false).ConfigureAwait(true);
            }
            finally
            {
                Interlocked.Exchange(ref gate, 0);
            }
        };
        b.Scrolled += async (_, e) =>
        {
            if (Interlocked.Exchange(ref gate, 1) == 1)
                return;
            try
            {
                if (Math.Abs(a.ScrollX - e.ScrollX) > 0.5)
                    await a.ScrollToAsync(e.ScrollX, 0, false).ConfigureAwait(true);
            }
            finally
            {
                Interlocked.Exchange(ref gate, 0);
            }
        };
    }
}
