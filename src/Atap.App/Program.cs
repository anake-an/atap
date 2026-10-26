using System.Windows;
using System.Windows.Threading;
using Atap.Composition;
using Atap.Core.Storage;
using Atap.Diagnostics;
#if !STORE
using Velopack;
#endif

namespace Atap;

internal static class Program
{
    // "Local\" scopes the guard to the Windows session: one notch per signed-in user.
    private const string SingleInstanceMutexName = @"Local\Atap.SingleInstance";

    // More UI exceptions than this within the window means one fires on every layout or
    // render pass. Atap then closes instead of spinning the CPU.
    private const int UiExceptionLimit = 20;
    private static readonly TimeSpan UiExceptionWindow = TimeSpan.FromSeconds(10);

    // Times (Environment.TickCount64) of recent UI exceptions. UI thread only.
    private static readonly Queue<long> RecentUiExceptions = new();
    private static bool _closingAfterUiExceptions;

    [STAThread]
    private static int Main()
    {
#if !STORE
        // Must run first: handles Velopack's install, update and uninstall hooks, which may exit the process.
        try
        {
            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(_ => UninstallCleanup.Run())
                .Run();
        }
        catch (Exception ex)
        {
            Log.Error("Velopack startup failed.", ex);
        }
#endif

        using var instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Already running: ask it to open Settings, then exit quietly. This launch came from the user, so it
            // may bring a window to the front. It hands that on, or Windows keeps Settings behind other windows.
            Interop.NativeMethods.AllowSetForegroundWindow(Interop.NativeMethods.ASFW_ANY);
            SecondInstance.Signal();
            return 0;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Log.Info($"Atap {AppInfo.Version} starting on Windows {Environment.OSVersion.Version}.");

        try
        {
            var app = new App();
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;
            app.InitializeComponent();
            return app.Run();
        }
        finally
        {
            instanceMutex.ReleaseMutex();
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error(
            e.IsTerminating ? "Unhandled exception; Atap is closing." : "Unhandled exception.",
            e.ExceptionObject as Exception);
        // The next start is in safe mode.
        if (e.IsTerminating)
            SafeMode.WriteMarker(AtapPaths.Default);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the notch alive: one failed handler should not close it.
        e.Handled = true;
        if (_closingAfterUiExceptions)
            return;

        var now = Environment.TickCount64;
        RecentUiExceptions.Enqueue(now);
        while (now - RecentUiExceptions.Peek() > UiExceptionWindow.TotalMilliseconds)
            RecentUiExceptions.Dequeue();

        if (RecentUiExceptions.Count <= UiExceptionLimit)
        {
            Log.Error("Unhandled UI exception.", e.Exception);
            return;
        }

        _closingAfterUiExceptions = true;
        Log.Error("UI exceptions keep repeating; Atap is closing.", e.Exception);
        // The next start is in safe mode.
        SafeMode.WriteMarker(AtapPaths.Default);
        Application.Current?.Shutdown(1);
    }
}
