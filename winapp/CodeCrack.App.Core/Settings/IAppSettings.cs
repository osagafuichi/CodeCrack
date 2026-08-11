namespace CodeCrack.App.Core.Settings;

public interface IAppSettings
{
    string EnginePathOverride { get; set; }
    int FontSize { get; set; }
    string EditorTheme { get; set; }
    bool IndentUsesSpaces { get; set; }
    int IndentWidth { get; set; }
    string ClaudeApiKey { get; set; }
    void Save();
}
