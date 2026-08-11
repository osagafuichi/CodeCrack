namespace CodeCrack.App.Core.Services;

public sealed class StatusBus
{
    public string Text { get; private set; } = "Open a file or folder to begin";

    public event EventHandler? Changed;

    public void Set(string message)
    {
        Text = message;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
