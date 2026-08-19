namespace JPL.Services;

public interface IAppNavigator
{
    void Attach(Window window);
    Task ShowLoginAsync();
    Task ShowMainAsync();
    Task ShowLeaguePickerAsync();
    Task ShowLeagueEditorAsync(Guid? id);
}

public sealed class AppNavigator : IAppNavigator
{
    private readonly IServiceProvider _services;
    private Window? _window;

    public AppNavigator(IServiceProvider services)
    {
        _services = services;
    }

    public void Attach(Window window) => _window = window;

    public Task ShowLoginAsync()
    {
        if (_window is null)
            return Task.CompletedTask;

        // Fresh login page + view-model after logout (clear any stuck busy/error state).
        _window.Page = _services.GetRequiredService<Pages.LoginPage>();
        return Task.CompletedTask;
    }

    public Task ShowLeaguePickerAsync()
    {
        if (_window is null)
            return Task.CompletedTask;

        var picker = _services.GetRequiredService<Pages.LeaguePickerPage>();
        NavigationPage.SetHasNavigationBar(picker, false);
        _window.Page = new NavigationPage(picker)
        {
            BarBackgroundColor = Color.FromArgb("#0D1B2A"),
            BarTextColor = Colors.White
        };
        return Task.CompletedTask;
    }

    public async Task ShowLeagueEditorAsync(Guid? id)
    {
        var page = _services.GetRequiredService<Pages.LeagueEditPage>();
        if (page.BindingContext is ViewModels.LeagueEditViewModel vm)
            vm.Prepare(id);

        if (_window?.Page is NavigationPage navigationPage)
        {
            await navigationPage.PushAsync(page).ConfigureAwait(true);
            return;
        }

        if (_window is not null)
        {
            NavigationPage.SetHasNavigationBar(page, true);
            _window.Page = new NavigationPage(page)
            {
                BarBackgroundColor = Color.FromArgb("#0D1B2A"),
                BarTextColor = Colors.White
            };
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }

    public Task ShowMainAsync()
    {
        if (_window is null)
            return Task.CompletedTask;

        // Paint shell immediately — don't block on active-match lookup / scoring navigation.
        _window.Page = _services.GetRequiredService<AppShell>();
        _ = ResumeActiveMatchAsync();
        return Task.CompletedTask;
    }

    private async Task ResumeActiveMatchAsync()
    {
        try
        {
            // Let the flyout / first tab paint before navigating.
            await Task.Delay(150).ConfigureAwait(true);

            var matches = _services.GetRequiredService<IMatchService>();
            var active = await matches.GetActiveMatchAsync().ConfigureAwait(true);
            if (active is null)
                return;

            if (Shell.Current is null)
                return;

            await Shell.Current.GoToAsync($"{nameof(Pages.ScoringPage)}?id={active.Id}").ConfigureAwait(true);
        }
        catch
        {
            /* shell may not be ready — Matches page Continue still works */
        }
    }
}
