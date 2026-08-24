using CommunityToolkit.Mvvm.ComponentModel;

namespace JPL.Services;

/// <summary>
/// Startup / session-restore busy flag so Login can show an overlay
/// while DB init and remote session check run (the form otherwise looks hung).
/// </summary>
public interface IAppStartupStatus
{
    bool IsBusy { get; }
    string Message { get; }
    event EventHandler? Changed;
    void SetBusy(string message);
    void Clear();
}

public sealed partial class AppStartupStatus : ObservableObject, IAppStartupStatus
{
    public event EventHandler? Changed;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string message = string.Empty;

    public void SetBusy(string message)
    {
        Message = string.IsNullOrWhiteSpace(message) ? "Please wait…" : message.Trim();
        IsBusy = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        IsBusy = false;
        Message = string.Empty;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
