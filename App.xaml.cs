using System.Windows;
using System.Windows.Threading;
using JokerDBDTracker.Services;

namespace JokerDBDTracker;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        InitializeDiagnosticsFromSettings();
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private static void InitializeDiagnosticsFromSettings()
    {
        try
        {
            var settings = new AppSettingsService().LoadAsync().GetAwaiter().GetResult();
            DiagnosticsService.SetEnabled(settings.LoggingEnabled);
        }
        catch
        {
            DiagnosticsService.SetEnabled(true);
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DiagnosticsService.LogException("DispatcherUnhandledException", e.Exception);
        ShowFatalErrorMessage(e.Exception.Message);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            DiagnosticsService.LogException("CurrentDomain_UnhandledException", exception);
            return;
        }

        DiagnosticsService.LogInfo("CurrentDomain_UnhandledException", e.ExceptionObject?.ToString() ?? "Unknown exception.");
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        DiagnosticsService.LogException("TaskScheduler_UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void ShowFatalErrorMessage(string reason)
    {
        try
        {
            var logInfo = DiagnosticsService.IsEnabled()
                ? $"Log: {DiagnosticsService.GetLogFilePath()}"
                : "Logging is currently disabled in Settings.";
            MessageBox.Show(
                $"An unexpected error was handled and the app continued running.{Environment.NewLine}" +
                $"Details: {reason}{Environment.NewLine}" +
                logInfo,
                "JokerDBD Tracker",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // Ignore UI notification errors.
        }
    }
}
