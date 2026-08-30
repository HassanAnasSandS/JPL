namespace JPL.Services;

public interface IAppNavigator
{
    void Attach(Window window);
    Task ShowLoginAsync(bool openSignup = false);
    Task ShowMainAsync();
    Task ShowLeaguePickerAsync();
    Task ShowLeagueEditorAsync(Guid? id);
    Task ShowModeChooserAsync();
    Task ContinueAfterAuthAsync(bool freshLogin);
}

public sealed class AppNavigator : IAppNavigator
{
    private readonly IServiceProvider _services;
    private readonly IAppStartupStatus _startup;
    private Window? _window;

    public AppNavigator(IServiceProvider services, IAppStartupStatus startup)
    {
        _services = services;
        _startup = startup;
    }

    public void Attach(Window window) => _window = window;

    public Task ShowLoginAsync(bool openSignup = false)
    {
        if (_window is null)
            return Task.CompletedTask;

        _startup.Clear();
        _services.GetRequiredService<IAppWorkspace>().Clear();
        var page = _services.GetRequiredService<Pages.LoginPage>();
        if (openSignup && page.BindingContext is ViewModels.LoginViewModel vm)
            vm.PrepareSignup();
        _window.Page = page;
        return Task.CompletedTask;
    }

    public Task ShowLeaguePickerAsync()
    {
        if (_window is null)
            return Task.CompletedTask;

        try
        {
            var picker = _services.GetRequiredService<Pages.LeaguePickerPage>();
            NavigationPage.SetHasNavigationBar(picker, false);
            _window.Page = new NavigationPage(picker)
            {
                BarBackgroundColor = Color.FromArgb("#0D1B2A"),
                BarTextColor = Colors.White
            };
        }
        catch (Exception ex)
        {
            CrashGuard.Log(ex, "ShowLeaguePicker");
        }

        return Task.CompletedTask;
    }

    public Task ShowModeChooserAsync()
    {
        if (_window is null)
            return Task.CompletedTask;

        var page = _services.GetRequiredService<Pages.ModeChooserPage>();
        NavigationPage.SetHasNavigationBar(page, false);
        _window.Page = new NavigationPage(page)
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

        try
        {
            _window.Page = _services.GetRequiredService<AppShell>();
            _ = ResumeActiveMatchAsync();
        }
        catch (Exception ex)
        {
            CrashGuard.Log(ex, "ShowMain");
        }

        return Task.CompletedTask;
    }

    public async Task ContinueAfterAuthAsync(bool freshLogin)
    {
        var auth = _services.GetRequiredService<IAuthService>();
        var league = _services.GetRequiredService<ILeagueContext>();
        var workspace = _services.GetRequiredService<IAppWorkspace>();
        var session = auth.Current;
        if (session is null)
        {
            await ShowLoginAsync().ConfigureAwait(true);
            return;
        }

        if (freshLogin)
        {
            workspace.Clear();
            await ShowModeChooserAsync().ConfigureAwait(true);
            return;
        }

        workspace.Restore();
        if (workspace.Current == AppWorkspace.Users)
        {
            if (session.CanManagePlatform)
            {
                await ShowMainAsync().ConfigureAwait(true);
                return;
            }

            workspace.Clear();
            await ShowModeChooserAsync().ConfigureAwait(true);
            return;
        }

        if (workspace.Current == AppWorkspace.Settings)
        {
            if (session.CanManagePlatform)
            {
                await ShowMainAsync().ConfigureAwait(true);
                return;
            }

            workspace.Clear();
            await ShowModeChooserAsync().ConfigureAwait(true);
            return;
        }

        if (workspace.Current == AppWorkspace.GroundOwner)
        {
            if (session.IsGroundOwner || session.CanManagePlatform)
            {
                await ShowMainAsync().ConfigureAwait(true);
                return;
            }

            workspace.Set(AppWorkspace.Grounds);
        }

        if (workspace.Current == AppWorkspace.Grounds && session.IsGroundOwner)
        {
            workspace.Set(AppWorkspace.GroundOwner);
            await ShowMainAsync().ConfigureAwait(true);
            return;
        }

        if (workspace.Current == AppWorkspace.Grounds)
        {
            await ShowMainAsync().ConfigureAwait(true);
            return;
        }

        if (workspace.Current == AppWorkspace.Scheduler)
        {
            if (session.IsGuest)
            {
                workspace.Clear();
                await ShowModeChooserAsync().ConfigureAwait(true);
                return;
            }

            await ShowMainAsync().ConfigureAwait(true);
            return;
        }

        if (workspace.Current == AppWorkspace.Leagues)
        {
            if (!league.HasSelection)
                await ShowLeaguePickerAsync().ConfigureAwait(true);
            else
                await ShowMainAsync().ConfigureAwait(true);
            return;
        }

        await ShowModeChooserAsync().ConfigureAwait(true);
    }

    private async Task ResumeActiveMatchAsync()
    {
        try
        {
            var workspace = _services.GetRequiredService<IAppWorkspace>();
            if (workspace.Current is AppWorkspace.Grounds or AppWorkspace.GroundOwner or AppWorkspace.Users or AppWorkspace.Settings or AppWorkspace.Scheduler)
                return;

            await Task.Delay(150).ConfigureAwait(true);

            var matches = _services.GetRequiredService<IMatchService>();
            var active = await matches.GetActiveMatchAsync().ConfigureAwait(true);
            if (active is null || !active.IsScoringOwner)
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
