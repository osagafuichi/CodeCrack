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

public sealed class UxSafetyTests
{
    private static MainViewModel Build(out FakeAnalyzer analyzer)
    {
        analyzer = new FakeAnalyzer();
        var vm = new MainViewModel(analyzer, new FakeAppSettings(), new StatusBus(), new FakeFileIO());
        vm.EditorHost = new FakeEditorHost();
        return vm;
    }

    // ---- 1) Untitled tab title ------------------------------------------

    [Theory]
    [InlineData(@"C:\dir\bug.py", 1, "bug.py")]
    [InlineData("", 1, "Untitled")]
    [InlineData("", 0, "Untitled")]
    [InlineData("", 2, "Untitled 2")]
    [InlineData("", 7, "Untitled 7")]
    public void DisplayNameFor_never_blank(string path, int number, string expected)
        => Assert.Equal(expected, OpenDocument.DisplayNameFor(path, number));

    [Fact]
    public void DisplayName_switches_to_file_name_after_a_path_is_assigned()
    {
        var doc = new OpenDocument { UntitledNumber = 3 };
        Assert.Equal("Untitled 3", doc.DisplayName);
        doc.Path = @"C:\proj\saved.py";
        Assert.Equal("saved.py", doc.DisplayName);
    }

    [Fact]
    public void NextUntitledNumber_fills_the_lowest_free_slot()
    {
        Assert.Equal(1, OpenDocument.NextUntitledNumber(new int[0]));
        Assert.Equal(2, OpenDocument.NextUntitledNumber(new[] { 1 }));
        Assert.Equal(2, OpenDocument.NextUntitledNumber(new[] { 1, 3 }));
        Assert.Equal(4, OpenDocument.NextUntitledNumber(new[] { 1, 2, 3 }));
    }

    [Fact]
    public void NewDocument_assigns_sequential_untitled_titles()
    {
        var vm = Build(out _);
        var a = vm.NewDocument();
        var b = vm.NewDocument();
        Assert.Equal("Untitled", a.DisplayName);
        Assert.Equal("Untitled 2", b.DisplayName);
    }

    // ---- 2) Dirty-close / quit decision ---------------------------------

    [Fact]
    public void ForClosingTab_prompts_only_when_dirty()
    {
        Assert.Equal(CloseAction.CloseImmediately, ClosePolicy.ForClosingTab(null));
        Assert.Equal(CloseAction.CloseImmediately,
            ClosePolicy.ForClosingTab(new OpenDocument { IsDirty = false }));
        Assert.Equal(CloseAction.Prompt,
            ClosePolicy.ForClosingTab(new OpenDocument { IsDirty = true }));
    }

    [Fact]
    public void ForQuit_prompts_only_when_something_is_dirty()
    {
        var clean = new[] { new OpenDocument(), new OpenDocument { IsDirty = false } };
        Assert.Equal(CloseAction.CloseImmediately, ClosePolicy.ForQuit(clean));
        Assert.Equal(CloseAction.CloseImmediately, ClosePolicy.ForQuit(new OpenDocument[0]));

        var mixed = new[] { new OpenDocument(), new OpenDocument { IsDirty = true } };
        Assert.Equal(CloseAction.Prompt, ClosePolicy.ForQuit(mixed));
        Assert.Single(ClosePolicy.DirtyDocuments(mixed));
    }

    // ---- 3) Empty states ------------------------------------------------

    [Fact]
    public void Issues_empty_state_guides_before_analyze_and_reassures_after_clean()
    {
        var vm = new IssuesViewModel();
        Assert.True(vm.ShowEmptyState);
        Assert.Equal("Run Analyze to find issues.", vm.EmptyStateMessage);

        vm.SetFindings(System.Array.Empty<Finding>());
        Assert.True(vm.ShowEmptyState);
        Assert.Equal("No issues found.", vm.EmptyStateMessage);

        vm.SetFindings(new[] { new Finding("a", "k", "t", new[] { 1 }, "r", "high") });
        Assert.False(vm.ShowEmptyState);
    }

    [Fact]
    public void Issues_error_banner_suppresses_the_empty_state()
    {
        var vm = new IssuesViewModel { ErrorMessage = "boom" };
        Assert.False(vm.ShowEmptyState);
        vm.ErrorMessage = null;
        Assert.True(vm.ShowEmptyState);
    }

    [Fact]
    public void Tests_empty_state_guides_before_analyze_and_reports_after()
    {
        var vm = new TestsViewModel();
        Assert.True(vm.ShowEmptyState);
        Assert.Equal("Run Analyze to generate tests.", vm.EmptyStateMessage);

        vm.SetResult(System.Array.Empty<GeneratedTest>(), null);
        Assert.True(vm.ShowEmptyState);
        Assert.Equal("No tests generated.", vm.EmptyStateMessage);
    }

    // ---- 4) Results auto-focus signal -----------------------------------

    [Fact]
    public async Task Analyze_completion_requests_the_Issues_panel()
    {
        var vm = Build(out var analyzer);
        vm.Active = new OpenDocument { Path = @"C:\proj\bug.py", Text = "print(1)\n" };
        analyzer.Next = new EngineOutcome(
            new AnalysisResult(new List<Finding>(), new List<GeneratedTest>(),
                new Summary(0, 0, 0, 0, new OutcomeCounts(0, 0, 0, 0))), null);

        ResultsPanel? revealed = null;
        vm.RevealResultsRequested += p => revealed = p;

        await vm.AnalyzeAsync();

        Assert.Equal(ResultsPanel.Issues, revealed);
    }

    [Fact]
    public void Run_requests_the_Console_panel()
    {
        var vm = Build(out _);
        vm.Active = new OpenDocument { Path = @"C:\proj\bug.py", Text = "print(1)\n" };
        vm.RunnerFactory = (cmd, onOut, onFin) => new StubSession();

        ResultsPanel? revealed = null;
        vm.RevealResultsRequested += p => revealed = p;

        vm.Run();

        Assert.Equal(ResultsPanel.Console, revealed);
    }

    private sealed class StubSession : IRunSession
    {
        public void Send(string text) { }
        public void Stop() { }
    }
}
