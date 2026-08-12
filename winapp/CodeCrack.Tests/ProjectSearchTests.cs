using System.IO;
using System.Linq;
using CodeCrack.App.Core.Search;
using Xunit;

namespace CodeCrack.Tests;

public class ProjectSearchTests : System.IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cc_search_" + Path.GetRandomFileName());

    public ProjectSearchTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string name, string text)
    {
        var p = Path.Combine(_root, name);
        File.WriteAllText(p, text);
        return p;
    }

    [Fact] public void Search_is_case_insensitive_and_reports_1_based_lines()
    {
        Write("a.txt", "alpha\nBETA beta\ngamma");
        var hits = ProjectSearch.Search("beta", _root);
        Assert.Single(hits);
        Assert.Equal(2, hits[0].Line);
        Assert.Equal("BETA beta", hits[0].Preview);
    }

    [Fact] public void ReplaceAll_is_case_SENSITIVE()
    {
        var p = Write("a.txt", "Foo foo FOO");
        var changed = ProjectSearch.ReplaceAll("foo", "bar", _root);
        Assert.Single(changed);
        Assert.Equal("Foo bar FOO", File.ReadAllText(p)); // only exact-case "foo" replaced
    }

    [Fact] public void ReplaceAll_makes_no_change_when_only_other_cases_match()
    {
        Write("a.txt", "FOO");
        var changed = ProjectSearch.ReplaceAll("foo", "bar", _root);
        Assert.Empty(changed); // case-insensitive Search would have matched; ReplaceAll must not
    }

    [Fact] public void Search_skips_hidden_and_non_utf8_files()
    {
        Write(".hidden.txt", "needle");
        File.WriteAllBytes(Path.Combine(_root, "bin.dat"), new byte[] { 0xFF, 0xFE, 0x00, 0x6E });
        Write("ok.txt", "needle here");
        var hits = ProjectSearch.Search("needle", _root);
        Assert.Single(hits);
        Assert.EndsWith("ok.txt", hits[0].Path);
    }

    [Fact] public void Search_caps_at_500_hits()
    {
        Write("many.txt", string.Concat(Enumerable.Repeat("x\n", 600)));
        Assert.Equal(ProjectSearch.MaxHits, ProjectSearch.Search("x", _root).Count);
    }
}
