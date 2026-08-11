using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using CodeCrack.App.Core.ViewModels;

namespace CodeCrackApp.Panels;

public partial class ConsolePanel : UserControl
{
    public ConsolePanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ConsoleViewModel vm)
                vm.PropertyChanged += OnVmChanged;
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConsoleViewModel.Display))
            Scroller.ScrollToBottom(); // auto-scroll
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        // The owning MainViewModel handles SendInput; the view forwards the text via its parent.
        if (Tag is MainViewModel main)
        {
            main.SendInput(Input.Text);
            Input.Clear();
        }
    }
}
