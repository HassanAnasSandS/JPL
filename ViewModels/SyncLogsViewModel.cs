using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JPL.Domain;
using JPL.Services;

namespace JPL.ViewModels;

public partial class SyncLogRow : ObservableObject
{
    public Guid Id { get; init; }
    public string TimeLabel { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public string LevelLabel { get; init; } = string.Empty;
    public Color Accent { get; init; } = Colors.Gray;
    public bool HasDetail { get; init; }
    public string DetailAction => IsExpanded ? "Hide details" : "Show details";

    [ObservableProperty] private bool isExpanded;

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(DetailAction));
}

public sealed class SyncConflictRow
{
    public Guid OutboxId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string RecordId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string LocalChange { get; init; } = string.Empty;
    public string ServerCopy { get; init; } = string.Empty;
    public string Difference { get; init; } = string.Empty;
    public string DeviceEdit { get; init; } = string.Empty;
    public string LocalChangeTitle { get; init; } = "On this device";
    public string ServerCopyTitle { get; init; } = "On server";
    public bool HasDifference { get; init; }
    public bool HasDeviceEdit { get; init; }
    public bool HasServerCopy { get; init; }
    public bool IsDelete { get; init; }
    public Color Accent { get; init; } = Color.FromArgb("#C62828");
}

public sealed class SyncOutboxRow
{
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
    public bool HasFailureReason => !string.IsNullOrWhiteSpace(FailureReason);
    public string RecordId { get; init; } = string.Empty;
    public Color Accent { get; init; } = Color.FromArgb("#455A64");
}

internal static class SyncPayloadDescription
{
    private static readonly (string Key, string Label)[] Fields =
    [
        ("name", "Name"), ("username", "Username"), ("label", "Label"),
        ("isActive", "Active"), ("isDeleted", "Deleted"), ("status", "Status"),
        ("matchCount", "Matches"), ("sequence", "Sequence"),
        ("isTied", "Tied"), ("version", "Version")
    ];

    public static string Describe(string? json)
    {
        var values = Read(json);
        var parts = new List<string>();
        foreach (var (key, label) in Fields)
        {
            if (!values.TryGetValue(key, out var value))
                continue;
            if (key == "isActive") value = value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Active" : "Inactive";
            if (key == "isDeleted") value = value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No";
            if (key == "isTied") value = value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No";
            parts.Add($"{label}: {value}");
            if (parts.Count == 4)
                break;
        }
        return parts.Count == 0 ? "No readable fields in this change." : string.Join(" · ", parts);
    }

    public static string Compare(string? beforeJson, string? afterJson)
    {
        var before = Read(beforeJson);
        var after = Read(afterJson);
        var changes = new List<string>();
        foreach (var (key, label) in Fields)
        {
            if (!before.TryGetValue(key, out var oldValue) || !after.TryGetValue(key, out var newValue)
                || string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase))
                continue;
            if (key == "isActive")
            {
                oldValue = oldValue.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Active" : "Inactive";
                newValue = newValue.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Active" : "Inactive";
            }
            changes.Add($"{label}: {oldValue} → {newValue}");
            if (changes.Count == 4)
                break;
        }
        return string.Join("\n", changes);
    }

    private static Dictionary<string, string> Read(string? json)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
            return values;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return values;
            foreach (var (key, _) in Fields)
            {
                if (!doc.RootElement.TryGetProperty(key, out var element) || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    continue;
                values[key] = element.ValueKind == JsonValueKind.String
                    ? element.GetString() ?? string.Empty
                    : element.ToString();
            }
        }
        catch (JsonException)
        {
            /* Keep malformed payload details out of the UI. */
        }
        return values;
    }
}

public partial class SyncLogsViewModel : ObservableObject
{
    private readonly ISyncLogService _logs;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly ILivePublisher _live;
    private int _loadBusy;
    private int _logPollGate;
    private CancellationTokenSource? _logPollCts;

    public ObservableCollection<SyncLogRow> Items { get; } = new();
    public ObservableCollection<SyncConflictRow> Conflicts { get; } = new();
    public ObservableCollection<SyncOutboxRow> WaitingItems { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string statusLine = "—";
    [ObservableProperty] private string cursorLine = "—";
    [ObservableProperty] private string pendingLine = "—";
    [ObservableProperty] private string filter = "All";
    [ObservableProperty] private bool canSync;
    [ObservableProperty] private bool isSyncRunning;
    [ObservableProperty] private string syncButtonText = "Sync now";
    [ObservableProperty] private string latestReportTitle = "No sync report yet";
    [ObservableProperty] private string latestReportSummary = "Run Sync now to see what was uploaded and downloaded.";
    [ObservableProperty] private string latestReportSteps = string.Empty;
    [ObservableProperty] private bool hasConflicts;
    [ObservableProperty] private bool hasWaitingItems;
    [ObservableProperty] private bool hasNoWaitingItems = true;
    [ObservableProperty] private bool hasReportSteps;
    [ObservableProperty] private string waitingHeading = "On this device";
    [ObservableProperty] private string conflictHeading = "Needs review";

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool CanStartSync => CanSync && !IsSyncRunning;
    public IReadOnlyList<string> Filters { get; } = ["All", "Errors", "Warn", "Success"];

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
    partial void OnCanSyncChanged(bool value) => OnPropertyChanged(nameof(CanStartSync));
    partial void OnLatestReportStepsChanged(string value) => HasReportSteps = !string.IsNullOrWhiteSpace(value);
    partial void OnHasWaitingItemsChanged(bool value) => HasNoWaitingItems = !value;
    partial void OnIsSyncRunningChanged(bool value)
    {
        SyncButtonText = value ? "Syncing…" : "Sync now";
        OnPropertyChanged(nameof(CanStartSync));
        if (value)
            StartLogPolling();
        else
            StopLogPolling();
    }

    public SyncLogsViewModel(
        ISyncLogService logs, ISyncService sync, IAuthService auth, ILeagueContext league,
        ILivePublisher live)
    {
        _logs = logs;
        _sync = sync;
        _auth = auth;
        _league = league;
        _live = live;
        RefreshSyncRunningFromService();
    }

    public void OnAppearing()
    {
        _sync.SyncStateChanged -= OnSyncStateChanged;
        _sync.SyncStateChanged += OnSyncStateChanged;
        RefreshSyncRunningFromService();
        if (IsSyncRunning)
            StartLogPolling();
    }

    public void OnDisappearing()
    {
        _sync.SyncStateChanged -= OnSyncStateChanged;
        // Do NOT cancel the sync — only stop UI log polling while off this page.
        StopLogPolling();
    }

    private void OnSyncStateChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshSyncRunningFromService();
            if (!IsSyncRunning)
                _ = LoadAsync();
        });
    }

    private void RefreshSyncRunningFromService()
    {
        IsSyncRunning = _sync.IsSyncing;
    }

    private void StartLogPolling()
    {
        StopLogPolling();
        var cts = new CancellationTokenSource();
        _logPollCts = cts;
        _ = PollLogsWhileSyncingAsync(cts.Token);
    }

    private void StopLogPolling()
    {
        try { _logPollCts?.Cancel(); }
        catch { /* ignore */ }
        _logPollCts?.Dispose();
        _logPollCts = null;
    }

    private async Task PollLogsWhileSyncingAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _sync.IsSyncing)
        {
            try
            {
                await Task.Delay(1500, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (ct.IsCancellationRequested || !_sync.IsSyncing)
                break;

            if (Interlocked.CompareExchange(ref _logPollGate, 1, 0) != 0)
                continue;
            try
            {
                await LoadAsync().ConfigureAwait(true);
            }
            finally
            {
                Interlocked.Exchange(ref _logPollGate, 0);
            }
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (Interlocked.Exchange(ref _loadBusy, 1) == 1)
            return;

        try
        {
            IsBusy = true;
            CanSync = LeagueSyncPolicy.AllowsLocalOfflineSync(_auth.Current, _league.CurrentLeagueId);
            RefreshSyncRunningFromService();

            var summary = await _logs.GetSummaryAsync().ConfigureAwait(true);
            StatusLine = IsSyncRunning
                ? "Syncing in background…"
                : summary.IsBootstrapped ? "Ready · local offline sync" : "Needs first sync";
            CursorLine = summary.LastSuccessfulSyncUtc is DateTime at
                ? $"Last OK · {at.ToLocalTime():g} · seq {summary.LastSequence}"
                : $"Cursor · seq {summary.LastSequence}";
            PendingLine = summary.PendingOutbox == 0
                ? $"On this device: nothing waiting · {summary.RecentErrors} sync error(s) in 24h"
                : $"On this device: {summary.PendingOutbox} change(s) not yet synced · {summary.FailedOutbox} need review · {summary.SendingOutbox} sending · {summary.RecentErrors} error(s) in 24h";

            var rows = await _logs.GetRecentAsync(250).ConfigureAwait(true);
            var conflicts = await _sync.GetConflictsAsync().ConfigureAwait(true);
            Conflicts.Clear();
            foreach (var conflict in conflicts)
                Conflicts.Add(ToConflictRow(conflict));
            HasConflicts = Conflicts.Count > 0;
            ConflictHeading = HasConflicts
                ? $"Needs review · {Conflicts.Count} conflict(s)"
                : "Needs review · none";
            UpdateLatestReport(rows, HasConflicts);

            var outbox = await _sync.GetOutboxItemsAsync(100).ConfigureAwait(true);
            var conflictIds = conflicts.Select(x => x.OutboxId).ToHashSet();
            WaitingItems.Clear();
            foreach (var item in outbox.Where(x => !conflictIds.Contains(x.OutboxId)))
                WaitingItems.Add(ToOutboxRow(item));
            HasWaitingItems = WaitingItems.Count > 0;
            WaitingHeading = summary.PendingOutbox == 0
                ? "On this device · nothing waiting"
                : $"On this device · {summary.PendingOutbox} change(s) waiting";

            var filtered = Filter switch
            {
                "Errors" => rows.Where(r => r.Level == SyncLogLevel.Error),
                "Warn" => rows.Where(r => r.Level is SyncLogLevel.Warn or SyncLogLevel.Error),
                "Success" => rows.Where(r => r.Level == SyncLogLevel.Success),
                _ => rows
            };

            Items.Clear();
            foreach (var row in filtered)
                Items.Add(ToRow(row));
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.UserMessage(ex);
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _loadBusy, 0);
        }
    }

    [RelayCommand]
    private async Task SetFilterAsync(string? value)
    {
        Filter = string.IsNullOrWhiteSpace(value) ? "All" : value;
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (IsSyncRunning)
        {
            ErrorMessage = "Wait for sync to finish before clearing logs.";
            return;
        }

        await _logs.ClearAsync().ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private Task SyncNowAsync()
    {
        if (!CanSync)
            return Task.CompletedTask;

        if (_sync.IsSyncing || IsSyncRunning)
        {
            ErrorMessage = "Sync already running — wait until it finishes.";
            RefreshSyncRunningFromService();
            return Task.CompletedTask;
        }

        ErrorMessage = null;
        // Fire-and-forget on the singleton SyncService: leaving this page must NOT stop sync.
        _ = RunManualSyncBackgroundAsync();
        RefreshSyncRunningFromService();
        return LoadAsync();
    }

    private async Task RunManualSyncBackgroundAsync()
    {
        try
        {
            _live.RequestFlush();
            var started = await _sync.TrySyncAllNowAsync().ConfigureAwait(false);
            if (!started)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ErrorMessage = "Sync already running — wait until it finishes.";
                    RefreshSyncRunningFromService();
                }).ConfigureAwait(false);
                return;
            }
        }
        catch (OperationCanceledException ex) when (!NetworkUi.IsHttpTimeout(ex))
        {
            // Signed out (or switched admin) while Sync now was running — sync stopped on purpose.
        }
        catch (SyncConflictException)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                ErrorMessage = "Some changes need review. See the conflict cards below.";
                await LoadAsync().ConfigureAwait(true);
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OfflineException || NetworkUi.IsNetworkFailure(ex))
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                ErrorMessage = NetworkUi.OfflineMessage).ConfigureAwait(false);
        }
        catch (Exception ex) when (NetworkUi.IsHttpTimeout(ex) || ex is TimeoutException)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                ErrorMessage = NetworkUi.SlowServerMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                ErrorMessage = NetworkUi.UserMessage(ex)).ConfigureAwait(false);
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                RefreshSyncRunningFromService();
                await LoadAsync().ConfigureAwait(true);
            }).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private Task KeepDeviceCopyAsync(SyncConflictRow? conflict)
    {
        return conflict is null ? Task.CompletedTask : ResolveConflictAsync(conflict, keepDeviceCopy: true);
    }

    [RelayCommand]
    private async Task UseServerCopyAsync(SyncConflictRow? conflict)
    {
        if (conflict is null)
            return;

        if (!conflict.HasServerCopy)
        {
            ErrorMessage = "Server copy is not available yet. Sync while online, then try again.";
            return;
        }

        var confirmed = await Shell.Current.DisplayAlert(
            "Use server copy?",
            "This replaces the local version and removes its queued edits. This cannot be undone.",
            "Use server copy",
            "Keep mine").ConfigureAwait(true);
        if (confirmed)
            await ResolveConflictAsync(conflict, keepDeviceCopy: false).ConfigureAwait(true);
    }

    private async Task ResolveConflictAsync(SyncConflictRow conflict, bool keepDeviceCopy)
    {
        if (IsSyncRunning || _sync.IsSyncing)
        {
            ErrorMessage = "Wait for the current sync to finish before resolving this change.";
            return;
        }

        try
        {
            ErrorMessage = null;
            if (keepDeviceCopy)
                await _sync.ResolveConflictKeepLocalAsync(conflict.OutboxId).ConfigureAwait(true);
            else
                await _sync.ResolveConflictAcceptServerAsync(conflict.OutboxId).ConfigureAwait(true);

            ErrorMessage = "Choice saved. Sending the resolved change now…";
            await _sync.SyncAllAsync().ConfigureAwait(true);
            ErrorMessage = null;
        }
        catch (SyncConflictException)
        {
            ErrorMessage = "Some changes still need review. Check the remaining conflict cards below.";
        }
        catch (Exception ex) when (NetworkUi.IsNetworkFailure(ex) || ex is OfflineException)
        {
            ErrorMessage = "Choice saved on this device. It will sync when you are online.";
        }
        catch (Exception ex)
        {
            ErrorMessage = NetworkUi.UserMessage(ex);
        }
        finally
        {
            await LoadAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private static void ToggleLogDetails(SyncLogRow? row)
    {
        if (row is not null)
            row.IsExpanded = !row.IsExpanded;
    }

    private static SyncLogRow ToRow(SyncLogEntry entry)
    {
        var (label, accent) = entry.Level switch
        {
            SyncLogLevel.Success => ("OK", Color.FromArgb("#2E7D32")),
            SyncLogLevel.Warn => ("WARN", Color.FromArgb("#C77800")),
            SyncLogLevel.Error => ("ERR", Color.FromArgb("#C62828")),
            _ => ("INFO", Color.FromArgb("#455A64"))
        };

        return new SyncLogRow
        {
            Id = entry.Id,
            TimeLabel = entry.AtUtc.ToLocalTime().ToString("HH:mm:ss"),
            Phase = entry.Phase,
            Message = entry.Message,
            Detail = entry.Detail,
            HasDetail = !string.IsNullOrWhiteSpace(entry.Detail),
            LevelLabel = label,
            Accent = accent
        };
    }

    private static SyncConflictRow ToConflictRow(SyncConflictInfo conflict)
    {
        var message = conflict.Message.StartsWith("SYNC_CONFLICT:", StringComparison.Ordinal)
            ? conflict.Message["SYNC_CONFLICT:".Length..].Trim()
            : conflict.Message;
        var difference = SyncPayloadDescription.Compare(conflict.ServerPayloadJson, conflict.LocalPayloadJson);
        var deviceEdit = SyncPayloadDescription.Compare(conflict.BasePayloadJson, conflict.LocalPayloadJson);
        return new SyncConflictRow
        {
            OutboxId = conflict.OutboxId,
            Title = $"{FriendlyEntity(conflict.EntityType)} · {FriendlyOperation(conflict.Operation)}",
            RecordId = $"Record {conflict.EntityId.ToString("N")[..8]}",
            Message = message,
            LocalChange = SyncPayloadDescription.Describe(conflict.LocalPayloadJson),
            ServerCopy = string.IsNullOrWhiteSpace(conflict.ServerPayloadJson)
                ? "Server copy not received yet. Sync while online to load it."
                : SyncPayloadDescription.Describe(conflict.ServerPayloadJson),
            Difference = string.IsNullOrWhiteSpace(difference)
                ? "No common summary fields differ. Review the conflict message before choosing a copy."
                : difference,
            DeviceEdit = string.IsNullOrWhiteSpace(deviceEdit)
                ? "This is a new record or its original values are unavailable."
                : deviceEdit,
            HasDeviceEdit = !string.IsNullOrWhiteSpace(deviceEdit),
            HasDifference = true,
            HasServerCopy = !string.IsNullOrWhiteSpace(conflict.ServerPayloadJson),
            IsDelete = string.Equals(conflict.Operation, "Delete", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static SyncOutboxRow ToOutboxRow(SyncOutboxInfo item)
    {
        var (status, accent) = item.Status switch
        {
            SyncOutboxStatus.InFlight => ("Sending now", Color.FromArgb("#C77800")),
            SyncOutboxStatus.Failed => ("Needs attention", Color.FromArgb("#C62828")),
            _ => ("Waiting to sync", Color.FromArgb("#455A64"))
        };
        return new SyncOutboxRow
        {
            Title = $"{FriendlyEntity(item.EntityType)} · {FriendlyOperation(item.Operation)}",
            Summary = SyncPayloadDescription.Describe(item.PayloadJson),
            StatusText = status,
            FailureReason = string.IsNullOrWhiteSpace(item.LastError)
                ? null
                : item.LastError.StartsWith("SYNC_CONFLICT:", StringComparison.Ordinal)
                    ? item.LastError["SYNC_CONFLICT:".Length..].Trim()
                    : item.LastError,
            RecordId = $"Record {item.EntityId.ToString("N")[..8]}",
            Accent = accent
        };
    }

    private void UpdateLatestReport(IReadOnlyList<SyncLogEntry> rows, bool hasPendingConflicts)
    {
        var ordered = rows.OrderByDescending(x => x.AtUtc).ToList();
        var startIndex = ordered.FindIndex(x => string.Equals(x.Phase, "Start", StringComparison.OrdinalIgnoreCase));
        if (startIndex < 0)
        {
            LatestReportTitle = "No sync report yet";
            LatestReportSummary = "Run Sync now to see what was uploaded and downloaded.";
            LatestReportSteps = string.Empty;
            return;
        }

        var run = ordered.Take(startIndex + 1).ToList();
        var started = ordered[startIndex];
        var completed = run.Any(x => x.Level == SyncLogLevel.Success
            && string.Equals(x.Phase, "Done", StringComparison.OrdinalIgnoreCase));
        var failed = run.Any(x => x.Level == SyncLogLevel.Error
            && string.Equals(x.Phase, "Fail", StringComparison.OrdinalIgnoreCase));
        var conflicts = hasPendingConflicts
            || run.Any(x => x.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase));
        LatestReportTitle = $"Latest run · {started.AtUtc.ToLocalTime():g}";
        LatestReportSummary = IsSyncRunning ? "Sync is still running."
            : completed ? "Completed successfully."
            : conflicts ? "Finished with changes that need review."
            : failed ? "Stopped with an error."
            : "Run did not finish; local queued changes remain safe to retry.";

        var steps = run.OrderBy(x => x.AtUtc)
            .Where(x => x.Phase is "Start" or "Bootstrap" or "Push" or "Pull" or "Done" or "Fail")
            .Select(x => $"{x.Phase}: {x.Message}")
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();
        LatestReportSteps = string.Join(Environment.NewLine, steps);
    }

    private static string FriendlyEntity(string entityType)
        => entityType switch
        {
            "SeriesFixture" => "Series match",
            "TournamentFixture" => "Tournament match",
            "TeamPlayer" => "Team squad",
            "NetSession" => "Practice session",
            "NetDelivery" => "Practice delivery",
            _ => entityType
        };

    private static string FriendlyOperation(string operation)
        => string.Equals(operation, "Delete", StringComparison.OrdinalIgnoreCase) ? "Delete" : "Update";
}
