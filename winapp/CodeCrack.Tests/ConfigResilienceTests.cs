using System.IO;
using CodeCrack.App.Core.Session;
using CodeCrack.App.Core.Settings;
using Xunit;

namespace CodeCrack.Tests;

/// A corrupt or unwritable %APPDATA% must never crash the app: config reads fall back to
/// defaults and writes are swallowed.
public sealed class ConfigResilienceTests
{
    private static string TempFile(string contents)
    {
        var p = Path.Combine(Path.GetTempPath(), "cc_cfg_" + Path.GetRandomFileName());
        File.WriteAllText(p, contents);
        return p;
    }

    [Fact]
    public void Corrupt_settings_json_loads_defaults()
    {
        var path = TempFile("{ not json");
        try
        {
            var s = new JsonAppSettings(path); // must not throw
            Assert.Equal(13, s.FontSize);      // default
            Assert.Equal("system", s.EditorTheme);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Corrupt_session_json_yields_empty_store()
    {
        var path = TempFile("{ not json");
        try
        {
            var store = new JsonKeyValueStore(path); // must not throw
            Assert.Null(store.GetString("anything"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Save_to_unwritable_path_is_swallowed()
    {
        // A regular file used as if it were a directory -> CreateDirectory throws IOException.
        var file = TempFile("x");
        try
        {
            var settings = new JsonAppSettings(Path.Combine(file, "settings.json"));
            settings.Save(); // must not throw

            var store = new JsonKeyValueStore(Path.Combine(file, "session.json"));
            store.SetString("k", "v"); // Flush must not throw
        }
        finally { File.Delete(file); }
    }
}
