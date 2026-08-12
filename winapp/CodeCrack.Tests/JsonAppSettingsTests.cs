using System.IO;
using CodeCrack.App.Core.Settings;
using Xunit;

namespace CodeCrack.Tests;

public class JsonAppSettingsTests : System.IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), "cc_settings_" + Path.GetRandomFileName(), "settings.json");

    public void Dispose()
    {
        var d = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    [Fact] public void Defaults_match_the_contract()
    {
        var s = new JsonAppSettings(_path);
        Assert.Equal(13, s.FontSize);
        Assert.Equal("system", s.EditorTheme);
        Assert.True(s.IndentUsesSpaces);
        Assert.Equal(4, s.IndentWidth);
        Assert.Equal("", s.EnginePathOverride);
        Assert.Equal("", s.ClaudeApiKey);
    }

    [Fact] public void Save_then_reload_persists_all_values()
    {
        var s = new JsonAppSettings(_path)
        {
            FontSize = 18, EditorTheme = "dark", IndentUsesSpaces = false,
            IndentWidth = 2, EnginePathOverride = @"C:\eng", ClaudeApiKey = "BLOB=="
        };
        s.Save();

        var reloaded = new JsonAppSettings(_path);
        Assert.Equal(18, reloaded.FontSize);
        Assert.Equal("dark", reloaded.EditorTheme);
        Assert.False(reloaded.IndentUsesSpaces);
        Assert.Equal(2, reloaded.IndentWidth);
        Assert.Equal(@"C:\eng", reloaded.EnginePathOverride);
        Assert.Equal("BLOB==", reloaded.ClaudeApiKey); // stored verbatim; no decryption in Core
    }

    [Fact] public void On_disk_json_uses_the_contract_keys()
    {
        new JsonAppSettings(_path) { FontSize = 20 }.Save();
        var json = File.ReadAllText(_path);
        Assert.Contains("\"fontSize\": 20", json);
        Assert.Contains("\"claudeAPIKey\"", json);
        Assert.Contains("\"enginePathOverride\"", json);
    }
}
