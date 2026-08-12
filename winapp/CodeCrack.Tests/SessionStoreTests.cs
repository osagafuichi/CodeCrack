using System.IO;
using CodeCrack.App.Core.Session;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public class SessionStoreTests : System.IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "cc_session_" + Path.GetRandomFileName());

    public SessionStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Touch(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "");
        return p;
    }

    [Fact] public void Save_is_ignored_until_Restore_runs_the_didRestore_guard()
    {
        var store = new InMemoryKeyValueStore();
        var s = new SessionStore(store);
        s.Save(new[] { Touch("a.py") }, null); // before Restore -> no-op
        Assert.Equal(0, store.Writes);
        Assert.Null(store.GetStringArray("sessionOpenDocumentPaths"));
    }

    [Fact] public void Restore_skips_missing_and_returns_existing_only()
    {
        var store = new InMemoryKeyValueStore();
        var live = Touch("live.py");
        var gone = Path.Combine(_dir, "gone.py");
        store.SetStringArray("sessionOpenDocumentPaths", new[] { gone, live });
        store.SetString("sessionActiveDocumentPath", gone);

        var state = new SessionStore(store).Restore();
        Assert.NotNull(state);
        Assert.Equal(new[] { live }, state!.OpenPaths);
        Assert.Null(state.ActivePath); // active was missing -> dropped
    }

    [Fact] public void Fingerprint_suppresses_redundant_saves()
    {
        var store = new InMemoryKeyValueStore();
        var a = Touch("a.py");
        store.SetStringArray("sessionOpenDocumentPaths", new[] { a });
        store.SetString("sessionActiveDocumentPath", a);

        var s = new SessionStore(store);
        s.Restore();                 // seeds signature + enables saving
        var before = store.Writes;
        s.Save(new[] { a }, a);      // identical set -> no write
        Assert.Equal(before, store.Writes);
    }

    [Fact] public void Empty_tab_set_clears_the_keys()
    {
        var store = new InMemoryKeyValueStore();
        var a = Touch("a.py");
        store.SetStringArray("sessionOpenDocumentPaths", new[] { a });
        var s = new SessionStore(store);
        s.Restore();
        s.Save(System.Array.Empty<string>(), null);
        Assert.Null(store.GetStringArray("sessionOpenDocumentPaths"));
        Assert.Null(store.GetString("sessionActiveDocumentPath"));
    }

    [Fact] public void WindowFrame_round_trips_and_rejects_non_positive_sizes()
    {
        var store = new InMemoryKeyValueStore();
        new WindowFrame(100, 50, 800, 600).Save(store);
        var f = WindowFrame.Load(store);
        Assert.Equal(new WindowFrame(100, 50, 800, 600), f);

        var s2 = new InMemoryKeyValueStore();
        new WindowFrame(0, 0, 0, 600).Save(s2);        // width <= 0 -> not saved
        Assert.Null(WindowFrame.Load(s2));
    }
}
