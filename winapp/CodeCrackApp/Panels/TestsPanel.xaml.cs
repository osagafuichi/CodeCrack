using System.Windows.Controls;
using System.Windows.Input;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.ViewModels;

namespace CodeCrackApp.Panels;

public partial class TestsPanel : UserControl
{
    public TestsPanel() => InitializeComponent();

    private void OnJump(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is TestsViewModel vm && ((ListBox)sender).SelectedItem is GeneratedTest t)
            vm.JumpToFinding(t);
    }

    private void OnJumpKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is TestsViewModel vm &&
            ((ListBox)sender).SelectedItem is GeneratedTest t)
            vm.JumpToFinding(t);
    }
}
