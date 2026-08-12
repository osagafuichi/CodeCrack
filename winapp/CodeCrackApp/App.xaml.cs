using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CodeCrack.App.Core;

namespace CodeCrackApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Never let an unexpected error take the whole app down silently: log it, tell the
        // user, and keep running where the failure is recoverable.
        DispatcherUnhandledException += (_, args) =>
        {
            Report(args.Exception, "UI");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Report(args.Exception, "Task");
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Report(args.ExceptionObject as Exception, "Domain");

        var services = new AppServices(AppServices.DefaultDataDir);
        var window = new MainWindow();
        window.Bind(services);
        window.Show();
    }

    private static void Report(Exception? ex, string source)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodeCrack");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "crash.log"), $"[{DateTime.Now:o}] ({source}) {ex}\n\n");
        }
        catch { /* logging must never throw */ }

        try
        {
            MessageBox.Show(
                "CodeCrack hit an unexpected error but is still running.\n" +
                "Details were written to %APPDATA%\\CodeCrack\\crash.log.",
                "CodeCrack", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { /* no UI available (e.g. during shutdown) */ }
    }
}
