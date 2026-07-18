using System.Windows;
using System.Windows.Threading;
using IPTVPlayer.Services;

namespace IPTVPlayer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DiagnosticLog.Initialize();
        DiagnosticLog.Info("App", $"Starting with {e.Args.Length} command-line argument(s)");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DiagnosticLog.Info("App", $"Exiting with code {e.ApplicationExitCode}");
        base.OnExit(e);
        DiagnosticLog.Shutdown();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DiagnosticLog.Error("Unhandled/UI", e.Exception);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        DiagnosticLog.Error("Unhandled/AppDomain", e.ExceptionObject?.ToString() ?? "Unknown exception");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        DiagnosticLog.Error("Unhandled/Task", e.Exception);
    }
}
