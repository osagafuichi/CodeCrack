using System;
using System.Windows.Controls;
using System.Windows.Input;
using CodeCrack.App.Core.ViewModels;

namespace CodeCrackApp.FileTree;

public partial class FileTreeView : UserControl
{
    /// Raised with the file path when a leaf (non-directory) node is double-clicked.
    public event Action<string>? FileActivated;

    public FileTreeView() => InitializeComponent();

    private void OnActivate(object sender, MouseButtonEventArgs e)
    {
        if (Tree.SelectedItem is FileNode { IsDirectory: false } node)
            FileActivated?.Invoke(node.Path);
    }
}
