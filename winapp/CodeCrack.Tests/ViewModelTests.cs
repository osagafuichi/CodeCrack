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

    private static GeneratedTest T(string findingId, string name, bool reproduced,
        string? outcome, string expects) =>
        new(findingId, name, "def test(): assert False", expects, outcome,
            "traceback", "captured stdout", 0.12, reproduced);

    [Fact]
    public void Headline_and_reproduced_count_come_from_summary()
    {
        var vm = new TestsViewModel();
        vm.SetResult(
            new[] { T("f1", "test_a", true, "failed", "regression") },
            new Summary(2, 3, 3, 1, new OutcomeCounts(0, 1, 0, 0)));

        Assert.Equal(1, vm.ReproducedCount);
        // Mac-verbatim string: the verb is not conjugated for the singular.
        Assert.Equal("1 test reproduce a real failure", vm.Headline);
    }

    [Fact]
    public void Headline_pluralizes_when_multiple_reproduced()
    {
        var vm = new TestsViewModel();
        vm.SetResult(System.Array.Empty<GeneratedTest>(),
            new Summary(0, 5, 5, 2, new OutcomeCounts(0, 2, 0, 0)));
        Assert.Equal("2 tests reproduce a real failure", vm.Headline);
    }

    [Fact]
    public void BugProven_binds_to_reproduced_and_NeedsInput_to_outcome_or_expects()
    {
        var proven = T("f1", "t1", reproduced: true, outcome: "failed", expects: "regression");
        var skipped = T("f2", "t2", reproduced: false, outcome: "skipped", expects: "raises");
        var normal = T("f3", "t3", reproduced: false, outcome: "passed", expects: "raises");

        Assert.True(proven.Reproduced);
        Assert.True(skipped.NeedsInput);   // outcome == "skipped"
        Assert.False(normal.NeedsInput);
    }

    [Fact]
    public void Jumping_from_a_test_reveals_the_matching_findings_line()
    {
        var (vm, _, _, _, host) = Build();
        vm.Issues.SetFindings(new[] { new Finding("f7", "k", "t", new[] { 99 }, "r", "high") });
        vm.Tests.SetResult(new[] { T("f7", "test_x", false, "passed", "raises") }, null);

        vm.Tests.JumpToFinding(vm.Tests.Tests[0]);

        Assert.Equal(99, host.RevealedLine);
    }

    [Fact]
    public void Run_unknown_extension_prints_help_and_sets_status()
    {
        var (vm, _, status, _, _) = Build();
        vm.Active = Doc(@"C:\proj\notes.md", "hi\n");

        var result = vm.Run();

        Assert.Equal("No run configuration for .md", status.Text);
        Assert.Equal("No run configuration for .md", result);
        Assert.Contains("Don't know how to run .md files yet.", vm.Console.Display);
        Assert.True(vm.ShowConsole);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public void Run_runnable_file_echoes_command_and_streams_via_factory()
    {
        var (vm, _, _, _, _) = Build();
        vm.Active = Doc(@"C:\proj\bug.py", "print(1)\n");

        Action<string>? captured = null;
        Action<int>? finish = null;
        vm.RunnerFactory = (cmd, onOut, onFin) =>
        {
            captured = onOut; finish = onFin;
            return new StubSession();
        };

        vm.Run();
        Assert.StartsWith("$ ", vm.Console.Display);
        Assert.True(vm.IsRunning);

        captured!("line one\n");
        finish!(0);

        Assert.Contains("line one", vm.Console.Display);
        Assert.Contains("[exited with code 0]", vm.Console.Display);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public void Console_display_falls_back_when_empty()
    {
        var c = new ConsoleViewModel();
        Assert.Equal("No output yet.", c.Display);
        Assert.False(c.CanSubmitInput);
        c.IsRunning = true;
        Assert.True(c.CanSubmitInput);
        c.Append("x");
        Assert.Equal("x", c.Display);
    }

    private sealed class StubSession : IRunSession
    {
        public void Send(string text) { }
        public void Stop() { }
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
