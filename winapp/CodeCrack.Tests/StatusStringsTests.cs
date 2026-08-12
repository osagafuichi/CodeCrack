using System.Collections.Generic;
using System.Threading.Tasks;
using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.Services;
using CodeCrack.App.Core.Settings;
using CodeCrack.App.Core.ViewModels;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public sealed class StatusStringsTests
{
    private sealed class StubSettings : IAppSettings
    {
        public string EnginePathOverride { get; set; } = "";
        public int FontSize { get; set; } = 13;
        public string EditorTheme { get; set; } = "system";
        public bool IndentUsesSpaces { get; set; } = true;
        public int IndentWidth { get; set; } = 4;
        public string ClaudeApiKey { get; set; } = "";
        public void Save() { }
    }

    private static (MainViewModel vm, StatusBus status) Build(FakeAnalyzer? analyzer = null)
    {
        var status = new StatusBus();
        var vm = new MainViewModel(analyzer ?? new FakeAnalyzer(), new StubSettings(), status, new FakeFileIO());
        vm.EditorHost = new FakeEditorHost();
        return (vm, status);
    }

    private static OpenDocument Doc(string path) =>
        new() { Path = path, Text = "print(1)\n", IsDirty = true };

    [Fact]
    public void Saved_status_uses_file_name()
    {
        var (vm, status) = Build();
        vm.Active = Doc(@"C:\proj\bug.py");
        vm.Save();
        Assert.Equal("Saved bug.py", status.Text);
    }

    [Fact]
    public void Unknown_extension_run_message_is_verbatim()
    {
        var (vm, status) = Build();
        vm.Active = Doc(@"C:\proj\readme.md");
        vm.Run();
        Assert.Equal("No run configuration for .md", status.Text);
        Assert.Contains("Don't know how to run .md files yet.", vm.Console.Display);
    }

    [Fact]
    public async Task Analyzing_status_uses_ellipsis_character()
    {
        var result = new EngineOutcome(
            new AnalysisResult(new List<Finding>(), new List<GeneratedTest>(),
                new Summary(0, 0, 0, 0, new OutcomeCounts(0, 0, 0, 0))), null);
        // Gate keeps the engine call pending so the transient "Analyzing…" status is observable.
        var analyzer = new FakeAnalyzer { Gate = new TaskCompletionSource<EngineOutcome>() };
        var (vm, status) = Build(analyzer);
        vm.Active = Doc(@"C:\proj\bug.py");

        var pending = vm.AnalyzeAsync();
        // Analyzing status is set synchronously before the awaited engine call resolves.
        Assert.Equal("Analyzing bug.py\u2026", status.Text);
        analyzer.Gate.SetResult(result);
        await pending;
        Assert.Equal("Analysis found 0 issues in bug.py", status.Text);
    }

    [Fact]
    public void Initial_status_is_the_mac_seed_string()
    {
        // Mac parity: the status bar opens with this exact prompt.
        var status = new StatusBus();
        Assert.Equal("Open a file or folder to begin", status.Text);
    }
}
