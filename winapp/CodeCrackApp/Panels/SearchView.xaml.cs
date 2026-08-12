using System.Windows.Controls;
using System.Windows.Input;
using CodeCrack.App.Core.ViewModels;

namespace CodeCrackApp.Panels;

/// <summary>Project-wide Find in Files panel. Binds to <see cref="SearchViewModel"/>; selecting a
/// result opens the file and reveals the line (wired in the VM). Buttons call the VM methods
/// directly to match the repo's code-behind panel style.</summary>
public partial class SearchView : UserControl
{
    public SearchView() => InitializeComponent();

    private SearchViewModel? Vm => DataContext as SearchViewModel;

    /// Focus the query box (used when the shell surfaces the Search tab).
    public void FocusQuery() => QueryBox.Focus();

    private void OnFind(object sender, System.Windows.RoutedEventArgs e) => Vm?.Search();
    private void OnReplaceAll(object sender, System.Windows.RoutedEventArgs e) => Vm?.ReplaceAll();

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Vm?.Search();
    }
}
