using System.IO;

namespace CodeCrack.App.Core.ViewModels;

/// Verbatim status string for opening a recent file that has vanished from disk.
public static class RecentOpen
{
    public static string MissingStatus(string path) =>
        $"\"{Path.GetFileName(path)}\" is no longer available";
}
