namespace CodeCrack.App.Core.ViewModels;

public sealed class ConsoleViewModel : ObservableObject
{
    private string _output = "";
    public string Output
    {
        get => _output;
        private set { if (Set(ref _output, value)) Raise(nameof(Display)); }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set { if (Set(ref _isRunning, value)) Raise(nameof(CanSubmitInput)); }
    }

    /// "No output yet." placeholder while empty; raw output otherwise.
    public string Display => _output.Length == 0 ? "No output yet." : _output;

    /// stdin box is visible/usable only while a program is running.
    public bool CanSubmitInput => _isRunning;

    public void Append(string text) => Output = _output + text;
    public void Clear() => Output = "";
}
