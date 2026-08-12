using System;
using System.Threading.Tasks;
using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.Services;
using CodeCrack.App.Core.Settings;
using CodeCrack.App.Core.ViewModels;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public sealed class AnalyzeTimeoutTests
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

    // Gated analyzer: the engine call stays pending until cancelled/timed out.
    private static (MainViewModel vm, StatusBus status) Build()
    {
        var analyzer = new FakeAnalyzer { Gate = new TaskCompletionSource<EngineOutcome>() };
        var status = new StatusBus();
        var vm = new MainViewModel(analyzer, new StubSettings(), status, new FakeFileIO());
        vm.EditorHost = new FakeEditorHost();
        vm.Active = new OpenDocument { Path = @"C:\proj\bug.py", Text = "print(1)\n", IsDirty = true };
        return (vm, status);
    }

    [Fact]
    public async Task Cancel_stops_analysis_and_sets_cancelled_status()
    {
        var (vm, status) = Build();

        var pending = vm.AnalyzeAsync();
        Assert.True(vm.IsAnalyzing);
        Assert.True(vm.CanCancelAnalyze);

        vm.CancelAnalyze();
        await pending;

        Assert.False(vm.IsAnalyzing);
        Assert.False(vm.CanCancelAnalyze);
        Assert.Equal("Analysis cancelled", status.Text);
        Assert.Empty(vm.Issues.Findings);
    }

    [Fact]
    public async Task Timeout_elapses_and_sets_timed_out_status()
    {
        var (vm, status) = Build();
        vm.AnalyzeTimeout = TimeSpan.FromMilliseconds(50); // gate is never released

        await vm.AnalyzeAsync();

        Assert.False(vm.IsAnalyzing);
        Assert.Contains("timed out", status.Text);
    }
}
