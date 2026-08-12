using System;
using System.Windows;
using System.Windows.Input;
using CodeCrack.App.Core.ViewModels;
using Microsoft.Win32;

namespace CodeCrackApp;

/// Custom routed commands with no built-in WPF key (Run, Analyze).
public static class Commands
{
    public static readonly RoutedUICommand Run =
        new("Run", nameof(Run), typeof(Commands));
    public static readonly RoutedUICommand Analyze =
        new("Analyze", nameof(Analyze), typeof(Commands));
}

public partial class MainWindow : Window
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public MainWindow() => InitializeComponent();

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
        if (dlg.ShowDialog(this) == true) Vm.OpenPath(dlg.FileName);
    }

    private void OnNew(object sender, ExecutedRoutedEventArgs e) => Vm?.NewDocument();

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
        if (dlg.ShowDialog(this) == true) Vm.SaveToPath(dlg.FileName);
    }

    private void OnCloseTab(object sender, ExecutedRoutedEventArgs e) => Vm?.CloseActive();
    private void OnRun(object sender, ExecutedRoutedEventArgs e) => Vm?.Run();
    private void OnAnalyze(object sender, ExecutedRoutedEventArgs e) => Vm?.Analyze();
    // Ctrl+F: AvalonEdit's SearchInputHandler (installed by CodeEditorControl) opens the
    // SearchPanel itself once the editor has focus.
    private void OnFind(object sender, ExecutedRoutedEventArgs e) => Vm?.EditorHost?.FocusEditor();

    // Frame persistence (4.5) + external-change (4.6) hooks; bodies land in those tasks.
    private void OnWindowSourceInitialized(object? sender, EventArgs e) { }
    private void OnWindowActivated(object? sender, EventArgs e) { }
    private void OnLocationChanged(object? sender, EventArgs e) { }
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) { }
}
