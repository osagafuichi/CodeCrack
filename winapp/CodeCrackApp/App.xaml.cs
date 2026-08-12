using System.Windows;
using CodeCrack.App.Core;

namespace CodeCrackApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var services = new AppServices(AppServices.DefaultDataDir);
        var window = new MainWindow();
        window.Bind(services);
        window.Show();
    }
}
