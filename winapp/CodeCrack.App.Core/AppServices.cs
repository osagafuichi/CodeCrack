using System;
using System.IO;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.Services;
using CodeCrack.App.Core.Session;
using CodeCrack.App.Core.Settings;
using CodeCrack.App.Core.ViewModels;

namespace CodeCrack.App.Core;

/// <summary>
/// The headless composition root: constructs the full service + ViewModel graph from a single
/// data directory (settings.json + session.json live there). UI-free, so it is unit-testable and
/// builds on Linux CI; the WPF layer builds one of these, then mounts views onto <see cref="Main"/>.
/// </summary>
public sealed class AppServices
{
    public IAppSettings Settings { get; }
    public StatusBus Status { get; }
    public IFileIO Io { get; }
    public IAnalyzer Analyzer { get; }

    /// Single JSON key-value store backing recents, session tabs, and the window frame
    /// (each uses a distinct key).
    public IKeyValueStore Store { get; }
    public SessionStore Session { get; }
    public RecentFiles Recent { get; }
    public ExternalChangeWatcher ExternalChange { get; }

    public MainViewModel Main { get; }

    public AppServices(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        Settings = new JsonAppSettings(Path.Combine(dataDir, "settings.json"));
        Status = new StatusBus();
        Io = new FileIO();
        Analyzer = new Analyzer(Settings);

        Store = new JsonKeyValueStore(Path.Combine(dataDir, "session.json"));
        Session = new SessionStore(Store);
        Recent = new RecentFiles(Store);
        ExternalChange = new ExternalChangeWatcher(new DiskFileProbe(), Status);

        Main = new MainViewModel(Analyzer, Settings, Status, Io);
    }

    /// The per-user data directory, %APPDATA%\CodeCrack.
    public static string DefaultDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodeCrack");
}
