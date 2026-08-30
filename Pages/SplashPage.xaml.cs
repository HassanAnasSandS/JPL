using JPL.Services;

namespace JPL.Pages;

public partial class SplashPage : ContentPage
{
    private readonly IAppStartupStatus? _startup;

    public SplashPage()
    {
        InitializeComponent();
    }

    public SplashPage(IAppStartupStatus startup) : this()
    {
        _startup = startup;
        _startup.Changed += OnStartupChanged;
        ApplyStatus();
    }

    public void SetStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        var text = message.Trim();
        if (MainThread.IsMainThread)
            StatusLabel.Text = text;
        else
            MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = text);
    }

    protected override bool OnBackButtonPressed() => true;

    protected override void OnDisappearing()
    {
        if (_startup is not null)
            _startup.Changed -= OnStartupChanged;
        base.OnDisappearing();
    }

    private void OnStartupChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(ApplyStatus);

    private void ApplyStatus()
    {
        if (_startup is null)
            return;
        if (!string.IsNullOrWhiteSpace(_startup.Message))
            StatusLabel.Text = _startup.Message;
    }
}
