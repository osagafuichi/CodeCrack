using System.IO;
using System.Linq;
using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Search;
using CodeCrack.App.Core.ViewModels;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public sealed class SearchViewModelTests : System.IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cc_svm_" + Path.GetRandomFileName());

    public SearchViewModelTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string name, string text) =>
        File.WriteAllText(Path.Combine(_root, name), text);

    private SearchViewModel ForRoot() => new(() => _root);

    [Fact]
    public void Search_populates_results_with_file_line_and_preview()
    {
        Write("a.txt", "alpha\nBETA beta\ngamma");
        var vm = ForRoot();
        vm.Query = "beta";

        vm.Search();

        var hit = Assert.Single(vm.Results);
        Assert.EndsWith("a.txt", hit.Path);
        Assert.Equal(2, hit.Line);
        Assert.Equal("BETA beta", hit.Preview);
        Assert.Contains("1 result", vm.Status);
    }

    [Fact]
    public void Search_with_no_root_reports_status_and_clears_results()
    {
        var vm = new SearchViewModel(() => null);
        vm.Query = "x";

        vm.Search();

        Assert.Empty(vm.Results);
        Assert.Equal("Open a file or folder first.", vm.Status);
    }

    [Fact]
    public void Empty_query_does_not_search()
    {
        Write("a.txt", "needle");
        var vm = ForRoot();
        vm.Query = "";

        vm.Search();

        Assert.Empty(vm.Results);
        Assert.Contains("Type something", vm.Status);
    }

    [Fact]
    public void Selecting_a_hit_requests_a_reveal_of_that_hit()
    {
        Write("a.txt", "one\ntwo needle\nthree");
        var vm = ForRoot();
        vm.Query = "needle";
        vm.Search();

        SearchHit? revealed = null;
        vm.RevealRequested += h => revealed = h;

        vm.SelectedHit = vm.Results[0];

        Assert.NotNull(revealed);
        Assert.Equal(2, revealed!.Line);
        Assert.EndsWith("a.txt", revealed.Path);
    }

    [Fact]
    public void Reveal_flows_through_the_editor_host_to_reveal_the_line()
    {
        Write("a.txt", "l1\nl2\nl3 needle");
        var vm = ForRoot();
        vm.Query = "needle";
        vm.Search();

        // Wire the reveal to a fake editor host, as the shell does.
        var host = new FakeEditorHost();
        vm.RevealRequested += h => host.RevealLine(h.Line);

        vm.SelectedHit = vm.Results.Single();

        Assert.Equal(3, host.RevealedLine);
    }

    [Fact]
    public void ReplaceAll_is_case_sensitive_and_reports_changed_files()
    {
        Write("a.txt", "Foo foo FOO");
        var vm = ForRoot();
        vm.Query = "foo";
        vm.Replacement = "bar";

        vm.ReplaceAll();

        Assert.Equal("Foo bar FOO", File.ReadAllText(Path.Combine(_root, "a.txt")));
        Assert.Contains("Replaced in 1 file", vm.Status);
    }

    [Fact]
    public void ReplaceAll_reports_when_nothing_matches_case_sensitively()
    {
        Write("a.txt", "FOO");
        var vm = ForRoot();
        vm.Query = "foo";
        vm.Replacement = "bar";

        vm.ReplaceAll();

        Assert.Equal("FOO", File.ReadAllText(Path.Combine(_root, "a.txt")));
        Assert.Contains("No case-sensitive matches", vm.Status);
    }
}
