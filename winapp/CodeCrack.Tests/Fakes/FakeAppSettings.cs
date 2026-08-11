using CodeCrack.App.Core.Settings;

namespace CodeCrack.Tests.Fakes;

public sealed class FakeAppSettings : IAppSettings
{
    public string EnginePathOverride { get; set; } = "";
    public int FontSize { get; set; } = 13;
    public string EditorTheme { get; set; } = "system";
    public bool IndentUsesSpaces { get; set; } = true;
    public int IndentWidth { get; set; } = 4;
    public string ClaudeApiKey { get; set; } = "";
    public int SaveCount { get; private set; }
    public void Save() => SaveCount++;
}
