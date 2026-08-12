using System.IO;
using CodeCrack.App.Core.Editor;

namespace CodeCrack.App.Core.ViewModels;

/// CanExecute predicates for the shell commands, kept pure so they are unit-testable
/// off the UI thread. The WPF CommandBindings in MainWindow.xaml call straight into these.
public static class CommandGates
{
    public static bool CanSave(OpenDocument? active) => active is not null;

    public static bool CanRun(OpenDocument? active) => active is not null;

    public static bool CanAnalyze(OpenDocument? active, bool isAnalyzing) =>
        active is not null && !isAnalyzing && IsPython(active.Path);

    /// The engine only supports Python; Analyze is gated on .py/.pyw.
    public static bool IsPython(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".py" || ext == ".pyw";
    }
}
