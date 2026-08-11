using CodeCrack.App.Core.Settings;

namespace CodeCrack.App.Core.Engine;

public static class EngineLocator
{
    private static bool IsEngineDir(string dir) =>
        File.Exists(Path.Combine(dir, "codecrack", "__main__.py"));

    public static string? Resolve(string filePath, IAppSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.EnginePathOverride))
            return settings.EnginePathOverride;

        var env = Environment.GetEnvironmentVariable("CODECRACK_ENGINE_DIR");
        if (!string.IsNullOrEmpty(env))
            return env;

        var bundled = Path.Combine(AppContext.BaseDirectory, "Resources", "engine");
        if (IsEngineDir(bundled))
            return bundled;

        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "engine");
            if (IsEngineDir(candidate))
                return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break; // reached the filesystem root
            dir = parent;
        }
        return null;
    }
}
