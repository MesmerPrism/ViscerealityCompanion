using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = WriteUnhandledExceptionLog("dispatcher", e.Exception, isTerminating: false);
        MessageBox.Show(
            $"Viscereality Companion hit an unhandled error and needs to close.{Environment.NewLine}{Environment.NewLine}{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}Crash log: {logPath}",
            "Viscereality Companion",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        Shutdown(-1);
    }

    private static void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception.");
        WriteUnhandledExceptionLog("appdomain", exception, e.IsTerminating);
    }

    private static void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteUnhandledExceptionLog("task", e.Exception, isTerminating: false);
        e.SetObserved();
    }

    private static string WriteUnhandledExceptionLog(string source, Exception exception, bool isTerminating)
    {
        try
        {
            var logDirectory = CompanionOperatorDataLayout.LogsRootPath;
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(
                logDirectory,
                $"unhandled-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{source}.log");

            var builder = new StringBuilder();
            builder.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
            builder.AppendLine($"Source: {source}");
            builder.AppendLine($"IsTerminating: {isTerminating}");
            builder.AppendLine($"Message: {exception.Message}");
            builder.AppendLine();
            builder.AppendLine(exception.ToString());

            File.WriteAllText(logPath, builder.ToString());
            return logPath;
        }
        catch (Exception logException)
        {
            return $"Failed to write crash log: {logException.Message}";
        }
    }
}
