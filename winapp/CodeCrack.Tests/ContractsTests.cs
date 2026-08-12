using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.Services;
using CodeCrack.App.Core.Settings;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public class ContractsTests
{
    [Fact]
    public void StatusBus_starts_with_initial_message_and_raises_changed()
    {
        var bus = new StatusBus();
        Assert.Equal("Open a file or folder to begin", bus.Text);

        var fired = 0;
        bus.Changed += (_, _) => fired++;
        bus.Set("Replaced in 3 file(s)");

        Assert.Equal("Replaced in 3 file(s)", bus.Text);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void EngineOutcome_IsSuccess_tracks_error_presence()
    {
        var summary = new Summary(0, 0, 0, 0, new OutcomeCounts(0, 0, 0, 0));
        var ok = new EngineOutcome(new AnalysisResult(new(), new(), summary), null);
        Assert.True(ok.IsSuccess);

        var bad = new EngineOutcome(null, new EngineNotFound("x"));
        Assert.False(bad.IsSuccess);
    }

    [Fact]
    public void FakeEditorHost_satisfies_IEditorHost_shape()
    {
        IEditorHost host = new FakeEditorHost();
        host.Text = "print(1)";
        host.CaretOffset = 3;
        host.Selection = (1, 2);
        host.RevealLine(10);
        host.SetLanguageByPath(@"C:\a\b.py");
        host.ApplyTheme(new EditorThemeSpec("dark", "Dark", "DarkPlus", true, "#1e1e1e", "#d4d4d4", "#ffffff", "#264f78", "#858585"));
        host.FocusEditor();

        Assert.Equal("print(1)", host.Text);
        Assert.Equal((1, 2), host.Selection);
    }

    [Fact]
    public void FakeAppSettings_satisfies_IAppSettings_defaults()
    {
        IAppSettings s = new FakeAppSettings();
        Assert.Equal(13, s.FontSize);
        Assert.Equal("system", s.EditorTheme);
        Assert.True(s.IndentUsesSpaces);
        Assert.Equal(4, s.IndentWidth);
        Assert.Equal("", s.EnginePathOverride);
        s.Save();
    }
}
