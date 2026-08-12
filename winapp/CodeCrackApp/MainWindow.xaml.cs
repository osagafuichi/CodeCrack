using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CodeCrack.App.Core;
using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Session;
using CodeCrack.App.Core.ViewModels;
using CodeCrackApp.Editor;
using CodeCrackApp.FileTree;
using CodeCrackApp.Panels;
using CodeCrackApp.Services;
using Microsoft.Win32;

namespace CodeCrackApp;

/// Custom routed commands with no built-in WPF key (Run, Analyze).
public static class Commands
{
    public static readonly RoutedUICommand Run =
        new("Run", nameof(Run), typeof(Commands));
    public static readonly RoutedUICommand Analyze =
        new("Analyze", nameof(Analyze), typeof(Commands));
    public static readonly RoutedUICommand Preferences =
        new("Preferences", nameof(Preferences), typeof(Commands));
    public static readonly RoutedUICommand CancelAnalyze =
        new("Cancel Analyze", nameof(CancelAnalyze), typeof(Commands));
}

public partial class MainWindow : Window
{
    private AppServices? _services;
    private CodeEditorControl? _editor;

    private MainViewModel? Vm => DataContext as MainViewModel;

    public MainWindow() => InitializeComponent();

    /// Composition root (view side): mount the editor + tree + panels, inject the editor host,
    /// and wire the status bar, Open-Recent menu, and window lifecycle handlers.
    public void Bind(AppServices services)
    {
        _services = services;
        DataContext = services.Main;

        _editor = new CodeEditorControl();
        EditorHostSurface.Content = _editor;
        services.Main.EditorHost = _editor;
        ApplyEditorTheme();
        _editor.ApplyFontSize(services.Settings.FontSize);
        WindowsTheme.SystemThemeChanged += (_, _) => Dispatcher.Invoke(ApplyEditorTheme);

        var tree = new FileTreeView { DataContext = services.Main.FileTree };
        tree.FileActivated += OpenFromUser;
        FileTreeHost.Content = tree;

        IssuesHost.Content = new IssuesPanel { DataContext = services.Main.Issues };
        TestsHost.Content = new TestsPanel { DataContext = services.Main.Tests };
        ConsoleHost.Content = new ConsolePanel { DataContext = services.Main.Console, Tag = services.Main };

        services.Status.Changed += (_, _) =>
            Dispatcher.Invoke(() => StatusText.Text = services.Status.Text);
        StatusText.Text = services.Status.Text;

        RebuildRecentMenu();
    }

    private void ApplyEditorTheme()
    {
        if (_services is null || _editor is null) return;
        var spec = EditorThemeRegistry.Resolve(_services.Settings.EditorTheme, WindowsTheme.AppsUseLightTheme);
        _editor.ApplyTheme(spec);
    }

    // ---- Open / Recent files --------------------------------------------

    /// User-initiated open (dialog, tree, or recents): opens the file AND records it in RecentFiles.
    private void OpenFromUser(string path)
    {
        if (_services is null) return;
        Vm?.OpenPath(path);
        _services.Recent.Record(path);
        RebuildRecentMenu();
        PersistSession();
    }

    private void RebuildRecentMenu()
    {
        if (_services is null) return;
        OpenRecentMenu.Items.Clear();
        OpenRecentMenu.IsEnabled = !_services.Recent.IsEmpty;
        foreach (var path in _services.Recent.Paths)
        {
            var captured = path;
            var item = new MenuItem { Header = path };
            item.Click += (_, _) => OpenRecent(captured);
            OpenRecentMenu.Items.Add(item);
        }
        if (!_services.Recent.IsEmpty)
        {
            OpenRecentMenu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "Clear Menu" };
            clear.Click += (_, _) => { _services.Recent.Clear(); RebuildRecentMenu(); };
            OpenRecentMenu.Items.Add(clear);
        }
    }

    private void OpenRecent(string path)
    {
        if (_services is null) return;
        if (!File.Exists(path))
        {
            _services.Recent.Remove(path);
            _services.Status.Set($"{Path.GetFileName(path)} is no longer available");
            RebuildRecentMenu();
            return;
        }
        OpenFromUser(path);
    }

    // ---- Command CanExecute + Executed ----------------------------------

    private void OnCanSave(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = Vm is not null && CommandGates.CanSave(Vm.Active);
    private void OnCanRun(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = Vm is not null && CommandGates.CanRun(Vm.Active);
    private void OnCanAnalyze(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = Vm is not null && CommandGates.CanAnalyze(Vm.Active, Vm.IsAnalyzing);

    private void OnOpen(object sender, ExecutedRoutedEventArgs e)
    {
        if (Vm is null) return;
        var dlg = new OpenFileDialog { Title = "Open" };
        if (dlg.ShowDialog(this) == true) OpenFromUser(dlg.FileName);
    }

    private void OnNew(object sender, ExecutedRoutedEventArgs e)
    {
        Vm?.NewDocument();
        PersistSession();
    }

    private void OnSave(object sender, ExecutedRoutedEventArgs e)
    {
        if (Vm?.Active is null) return;
        if (string.IsNullOrEmpty(Vm.Active.Path)) { OnSaveAs(sender, e); return; }
        Vm.Save();
    }

    private void OnSaveAs(object sender, ExecutedRoutedEventArgs e)
    {
        if (Vm?.Active is null) return;
        var dlg = new SaveFileDialog { Title = "Save As" };
        if (dlg.ShowDialog(this) == true)
        {
            Vm.SaveToPath(dlg.FileName);
            PersistSession();
        }
    }

    private void OnCloseTab(object sender, ExecutedRoutedEventArgs e)
    {
        Vm?.CloseActive();
        PersistSession();
    }

    /// Close button on a tab-strip header: select that doc, then close it (close-neighbor rule).
    private void OnTabClose(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if ((sender as FrameworkElement)?.Tag is OpenDocument doc)
        {
            Vm.Active = doc;
            Vm.CloseActive();
            PersistSession();
        }
    }

    private void OnRun(object sender, ExecutedRoutedEventArgs e) => Vm?.Run();
    private void OnAnalyze(object sender, ExecutedRoutedEventArgs e) => Vm?.Analyze();

    private void OnCanCancelAnalyze(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = Vm is not null && Vm.CanCancelAnalyze;
    private void OnCancelAnalyze(object sender, ExecutedRoutedEventArgs e) => Vm?.CancelAnalyze();

    /// Edit ▸ Preferences… (Ctrl+,): open the modal Settings window, then re-apply theme + font.
    private void OnPreferences(object sender, ExecutedRoutedEventArgs e)
    {
        if (_services is null) return;
        var dlg = new Settings.SettingsWindow(_services.Settings) { Owner = this };
        dlg.ShowDialog();
        ApplyEditorTheme();
        _editor?.ApplyFontSize(_services.Settings.FontSize);
    }
    // Ctrl+F: AvalonEdit's SearchInputHandler (installed by CodeEditorControl) opens the
    // SearchPanel itself once the editor has focus.
    private void OnFind(object sender, ExecutedRoutedEventArgs e) => Vm?.EditorHost?.FocusEditor();

    // ---- Window lifecycle: frame + session restore/save, external-change ----

    private void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (_services is null) return;
        WindowsTheme.Attach(this);

        var frame = WindowFrame.Load(_services.Store);
        if (frame is not null)
        {
            Left = frame.Left; Top = frame.Top; Width = frame.Width; Height = frame.Height;
        }

        var state = _services.Session.Restore();
        if (state is null || Vm is null) return;
        foreach (var p in state.OpenPaths) Vm.OpenPath(p);   // inline open, bypasses RecentFiles
        if (state.ActivePath is not null)
        {
            var full = Path.GetFullPath(state.ActivePath);
            var doc = Vm.Documents.FirstOrDefault(
                d => string.Equals(d.Path, full, StringComparison.OrdinalIgnoreCase));
            if (doc is not null) Vm.Active = doc;
        }
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_services is null || Vm is null) return;
        Vm.Editor.SyncFromHost();   // the active buffer's text must be current before disk compare
        ScanExternalChanges();
    }

    private void ScanExternalChanges()
    {
        if (_services is null || Vm is null) return;
        var prompt = _services.ExternalChange.Scan(Vm.Documents.ToList());

        // Branch 4 (silent reload) may have replaced the active doc's text on disk change.
        if (Vm.Active is not null && _editor is not null && _editor.Text != Vm.Active.Text)
            _editor.Text = Vm.Active.Text;

        if (prompt is null) return;

        _services.ExternalChange.PromptActive = true;
        var answer = MessageBox.Show(this,
            $"{prompt.Doc.Name} changed on disk. Reload and discard your changes?",
            "File changed on disk", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        _services.ExternalChange.Resolve(prompt, reload: answer == MessageBoxResult.Yes);
        if (ReferenceEquals(prompt.Doc, Vm.Active) && _editor is not null)
            _editor.Text = prompt.Doc.Text;
        _services.ExternalChange.PromptActive = false;

        ScanExternalChanges();   // resolve the remaining docs after this one
    }

    private void OnLocationChanged(object? sender, EventArgs e) => PersistFrameAndSession();
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => PersistFrameAndSession();

    private void PersistFrameAndSession()
    {
        if (_services is null) return;
        new WindowFrame(Left, Top, Width, Height).Save(_services.Store);
        PersistSession();
    }

    private void PersistSession()
    {
        if (_services is null || Vm is null) return;
        var open = Vm.Documents
            .Where(d => !string.IsNullOrEmpty(d.Path))
            .Select(d => d.Path)
            .ToList();
        _services.Session.Save(open, Vm.Active?.Path);
    }
}
