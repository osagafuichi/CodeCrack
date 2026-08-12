using System.IO;
using System.Linq;
using CodeCrack.App.Core.Session;
using CodeCrack.App.Core.ViewModels;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public class RecentFilesTests : System.IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "cc_recent_" + Path.GetRandomFileName());

    public RecentFilesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Touch(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "");
        return p;
    }

    [Fact] public void Record_dedups_by_full_path_and_moves_to_front()
    {
        var a = Touch("a.py"); var b = Touch("b.py");
        var r = new RecentFiles(new InMemoryKeyValueStore());
        r.Record(a); r.Record(b); r.Record(a);
        Assert.Equal(new[] { Path.GetFullPath(a), Path.GetFullPath(b) }, r.Paths);
    }

    [Fact] public void Record_caps_at_10_newest_first()
    {
        var r = new RecentFiles(new InMemoryKeyValueStore());
        for (var i = 0; i < 12; i++) r.Record(Touch($"f{i}.py"));
        Assert.Equal(RecentFiles.Cap, r.Paths.Count);
        Assert.EndsWith("f11.py", r.Paths[0]);
    }

    [Fact] public void Load_prunes_missing_entries()
    {
        var store = new InMemoryKeyValueStore();
        var gone = Path.Combine(_dir, "ghost.py");
        var live = Touch("live.py");
        store.SetStringArray("recentDocumentPaths", new[] { gone, live });
        var r = new RecentFiles(store);
        Assert.Equal(new[] { Path.GetFullPath(live) }, r.Paths);
    }

    [Fact] public void Clear_and_IsEmpty()
    {
        var r = new RecentFiles(new InMemoryKeyValueStore());
        r.Record(Touch("a.py"));
        Assert.False(r.IsEmpty);
        r.Clear();
        Assert.True(r.IsEmpty);
    }

    [Fact] public void MissingStatus_is_verbatim()
        => Assert.Equal("\"ghost.py\" is no longer available",
                        RecentOpen.MissingStatus(@"C:\proj\ghost.py"));
}
