namespace CodeCrack.App.Core.Engine;

public static class PythonInvocation
{
    public static (string Exe, string[] LeadingArgs) Resolve()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "python", "python.exe");
        if (File.Exists(bundled))
            return (bundled, Array.Empty<string>());
        return ("py", new[] { "-3" });
    }
}
