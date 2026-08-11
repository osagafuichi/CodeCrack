using System.Collections.Generic;
using System.Threading.Tasks;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Services;
using CodeCrack.App.Core.Settings;
using CodeCrack.App.Core.ViewModels;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public sealed class ViewModelTests
{
    private static (MainViewModel vm, FakeAnalyzer analyzer, StatusBus status, FakeFileIO io, FakeEditorHost host)
        Build()
    {
        var analyzer = new FakeAnalyzer();
        var status = new StatusBus();
        var io = new FakeFileIO();
        var vm = new MainViewModel(analyzer, new StubSettings(), status, io);
        var host = new FakeEditorHost();
        vm.EditorHost = host;
        return (vm, analyzer, status, io, host);
    }

    private static OpenDocument Doc(string path, string text = "print(1)\n") =>
        new() { Path = path, Text = text, IsDirty = true };

    private static Finding F(string id, int line, string sev) =>
        new(id, "kind", "target", new[] { line }, "why", sev);

    private static Summary Sum(int findings) => new(findings, findings, findings, 0,
        new OutcomeCounts(0, 0, 0, 0));

    [Fact]
    public async Task Analyze_success_sets_status_with_pluralized_count()
    {
        var (vm, analyzer, status, _, _) = Build();
        vm.Active = Doc(@"C:\proj\bug.py");
        analyzer.Next = new EngineOutcome(
            new AnalysisResult(new List<Finding> { F("f1", 3, "high") },
                               new List<GeneratedTest>(), Sum(1)), null);

        await vm.AnalyzeAsync();

        Assert.True(vm.ShowIssues);
        Assert.False(vm.IsAnalyzing);
        Assert.Single(vm.Issues.Findings);
        Assert.Equal("Analysis found 1 issue in bug.py", status.Text);
    }

    [Fact]
    public async Task Analyze_zero_findings_uses_plural_s()
    {
        var (vm, analyzer, status, _, _) = Build();
        vm.Active = Doc(@"C:\proj\bug.py");
        analyzer.Next = new EngineOutcome(
            new AnalysisResult(new List<Finding>(), new List<GeneratedTest>(), Sum(0)), null);

        await vm.AnalyzeAsync();

        Assert.Equal("Analysis found 0 issues in bug.py", status.Text);
    }

    [Fact]
    public async Task Analyze_error_renders_message_and_failed_status()
    {
        var (vm, analyzer, status, _, _) = Build();
        vm.Active = Doc(@"C:\proj\bug.py");
        analyzer.Next = new EngineOutcome(null, new EngineNotFound(@"C:\proj\engine"));

        await vm.AnalyzeAsync();

        Assert.Equal("Analysis failed", status.Text);
        Assert.NotNull(vm.Issues.ErrorMessage);
        Assert.Empty(vm.Issues.Findings);
    }

    [Fact]
    public void CanExecute_predicates_track_active_and_language()
    {
        var (vm, _, _, _, _) = Build();
        Assert.False(vm.CanSave);
        Assert.False(vm.CanRun);
        Assert.False(vm.CanAnalyze);

        vm.Active = Doc(@"C:\proj\notes.txt");
        Assert.True(vm.CanSave);
        Assert.True(vm.CanRun);
        Assert.False(vm.CanAnalyze); // non-Python

        vm.Active = Doc(@"C:\proj\bug.py");
        Assert.True(vm.CanAnalyze);
    }

    [Fact]
    public void Save_writes_via_io_and_status()
    {
        var (vm, _, status, io, host) = Build();
        // Setting Active loads the doc into the host; the user then edits the buffer.
        vm.Active = Doc(@"C:\proj\bug.py", "old\n");
        host.Text = "edited body\n";

        var msg = vm.Save();

        Assert.Equal("Saved bug.py", msg);
        Assert.Equal("Saved bug.py", status.Text);
        Assert.Equal("edited body\n", io.Files[@"C:\proj\bug.py"]);
        Assert.False(vm.Active!.IsDirty);
    }

    [Fact]
    public void Issues_are_sorted_high_medium_low_stable_within_severity()
    {
        var vm = new IssuesViewModel();
        vm.SetFindings(new[]
        {
            new Finding("a", "k", "t", new[] { 5 }, "r", "low"),
            new Finding("b", "k", "t", new[] { 6 }, "r", "high"),
            new Finding("c", "k", "t", new[] { 7 }, "r", "medium"),
            new Finding("d", "k", "t", new[] { 8 }, "r", "high"),
        });

        Assert.Equal(new[] { "b", "d", "c", "a" }, vm.Findings.Select(f => f.Id).ToArray());
    }

    [Fact]
    public void Activating_a_finding_reveals_its_line_through_the_editor_host()
    {
        var (vm, _, _, _, host) = Build();
        var finding = new Finding("f9", "k", "t", new[] { 42 }, "r", "high");
        vm.Issues.SetFindings(new[] { finding });

        vm.Issues.Activate(vm.Issues.Findings[0]);

        Assert.Equal(42, host.RevealedLine);
    }

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
}
