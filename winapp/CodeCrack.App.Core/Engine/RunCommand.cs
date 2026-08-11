using System;
using System.IO;

namespace CodeCrack.App.Core.Engine;

public sealed record RunCommand(string Tool, string[] Args, string Cwd)
{
    public string Display => $"{Tool} {string.Join(' ', Args)}";
}

/// Windows run table (mirrors the mac Runner.command table, POSIX-isms removed).
/// null => the file type isn't runnable.
public static class RunCommandTable
{
    public static RunCommand? For(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        var dir = Path.GetDirectoryName(full) ?? ".";
        var name = Path.GetFileName(full);
        var baseName = Path.GetFileNameWithoutExtension(full);
        var ext = Path.GetExtension(full).TrimStart('.').ToLowerInvariant();

        switch (ext)
        {
            case "py":
            case "pyw":
            {
                var (exe, leading) = PythonInvocation.Resolve();
                var args = new string[leading.Length + 1];
                Array.Copy(leading, args, leading.Length);
                args[leading.Length] = name;
                return new RunCommand(exe, args, dir);
            }
            case "js":
            case "mjs":
            case "cjs": return new RunCommand("node", new[] { name }, dir);
            case "rb":  return new RunCommand("ruby", new[] { name }, dir);
            case "sh":
            case "bash": return new RunCommand("bash", new[] { name }, dir); // git-bash / WSL on PATH
            case "swift": return new RunCommand("swift", new[] { name }, dir);
            case "go":   return new RunCommand("go", new[] { "run", name }, dir);
            case "php":  return new RunCommand("php", new[] { name }, dir);
            case "pl":   return new RunCommand("perl", new[] { name }, dir);
            case "ps1":  return new RunCommand("powershell",
                             new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", name }, dir);
            case "java":
                // Prefer JAVA_HOME's toolchain, else javac/java from PATH. No POSIX java_home, no bash.
                return new RunCommand("cmd", new[]
                {
                    "/c",
                    $"if defined JAVA_HOME (\"%JAVA_HOME%\\bin\\javac\" \"{name}\" && " +
                    $"\"%JAVA_HOME%\\bin\\java\" \"{baseName}\") else " +
                    $"(javac \"{name}\" && java \"{baseName}\")"
                }, dir);
            default: return null;
        }
    }
}
