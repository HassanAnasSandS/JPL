namespace JPL.Services;

/// <summary>Logs fatal crashes + keeps the process alive on network failures.</summary>
public static class CrashGuard
{
    private static int _installed;
    private static readonly object LogLock = new();

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1)
            return;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is not Exception ex)
                return;

            if (ex is OperationCanceledException || NetworkUi.IsNetworkFailure(ex))
            {
                if (NetworkUi.IsNetworkFailure(ex) && ex is not OperationCanceledException)
                    NetworkUi.ShowOfflinePopupFireAndForget(ex);
                return;
            }

            TryWriteCrashLog(ex, "UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            if (args.Exception.GetBaseException() is SessionExpiredException
                || args.Exception.Flatten().InnerExceptions.Any(e => e is SessionExpiredException))
            {
                args.SetObserved();
                return;
            }

            if (args.Exception.GetBaseException() is OperationCanceledException
                || args.Exception.Flatten().InnerExceptions.Any(e => e is OperationCanceledException)
                || NetworkUi.IsNetworkFailure(args.Exception))
            {
                args.SetObserved();
                if (NetworkUi.IsNetworkFailure(args.Exception)
                    && args.Exception.GetBaseException() is not OperationCanceledException)
                    NetworkUi.ShowOfflinePopupFireAndForget(args.Exception);
                return;
            }

            TryWriteCrashLog(args.Exception, "UnobservedTaskException");
        };

#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
        {
            if (args.Exception is SessionExpiredException or OperationCanceledException)
            {
                // Logout redirect already started, or HTTP was cancelled — do not kill the process.
                args.Handled = true;
                return;
            }

            TryWriteCrashLog(args.Exception, "AndroidUnhandled");
            if (!NetworkUi.IsNetworkFailure(args.Exception))
                return;

            args.Handled = true;
            NetworkUi.ShowOfflinePopupFireAndForget(args.Exception);
        };
#endif
    }

    private static void TryWriteCrashLog(Exception ex, string source)
    {
        try
        {
            var dir = FileSystem.AppDataDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "jpl-crash.log");
            var line =
                $"[{DateTime.Now:O}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";
            lock (LogLock)
                File.AppendAllText(path, line);
        }
        catch
        {
            /* never throw from crash handler */
        }
    }
}
