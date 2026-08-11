using System.Windows.Controls;
using System.Windows.Input;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.ViewModels;

namespace CodeCrackApp.Panels;

public partial class IssuesPanel : UserControl
{
    public IssuesPanel() => InitializeComponent();

    private void OnRowActivated(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is IssuesViewModel vm && ((ListBox)sender).SelectedItem is Finding f)
            vm.Activate(f);
    }

    private void OnRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is IssuesViewModel vm &&
            ((ListBox)sender).SelectedItem is Finding f)
            vm.Activate(f);
    }
}
