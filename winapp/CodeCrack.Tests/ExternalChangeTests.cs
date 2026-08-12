using System;
using System.Collections.Generic;
using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Services;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public class ExternalChangeTests
{
    private static OpenDocument Doc(string path, string text, bool dirty, DateTime known) =>
        new() { Path = path, Text = text, IsDirty = dirty, LastWriteUtc = known };

    [Fact] public void All_four_branches()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddMinutes(1);

        var unchanged = Doc(@"C:\p\unchanged.txt", "same", false, t0); // branch 1: no newer mtime
        var caughtUp  = Doc(@"C:\p\caughtup.txt",  "same", true,  t0); // branch 2: newer mtime, same text
        var reloaded  = Doc(@"C:\p\clean.txt",     "old",  false, t0); // branch 4: clean + differs
        var prompted  = Doc(@"C:\p\dirty.txt",     "mine", true,  t0); // branch 3: dirty + differs

        var probe = new FakeExternalFileProbe();
        probe.Files[unchanged.Path] = (t0, "same", true);
        probe.Files[caughtUp.Path]  = (t1, "same", true);
        probe.Files[reloaded.Path]  = (t1, "disk", true);
        probe.Files[prompted.Path]  = (t1, "disk", true);

        var status = new CodeCrack.App.Core.Services.StatusBus();
        var w = new ExternalChangeWatcher(probe, status);
        var docs = new List<OpenDocument> { unchanged, caughtUp, reloaded, prompted };

        var prompt = w.Scan(docs);

        Assert.Equal(t0, unchanged.LastWriteUtc);                 // 1: untouched
        Assert.Equal(t1, caughtUp.LastWriteUtc);                  // 2: mtime caught up
        Assert.True(caughtUp.IsDirty);                            //    but edits preserved
        Assert.Equal("disk", reloaded.Text);                     // 4: silently reloaded
        Assert.False(reloaded.IsDirty);
        Assert.Equal("Reloaded clean.txt — changed on disk", status.Text);
        Assert.NotNull(prompt);                                   // 3: dirty -> prompt
        Assert.Equal(prompted.Path, prompt!.Doc.Path);
        Assert.Equal("disk", prompt.DiskText);
    }

    [Fact] public void Scan_is_a_no_op_while_a_prompt_is_active()
    {
        var probe = new FakeExternalFileProbe();
        var doc = Doc(@"C:\p\a.txt", "mine", true, DateTime.UtcNow.AddMinutes(-1));
        probe.Files[doc.Path] = (DateTime.UtcNow, "disk", true);
        var w = new ExternalChangeWatcher(probe, new CodeCrack.App.Core.Services.StatusBus())
        {
            PromptActive = true
        };
        Assert.Null(w.Scan(new List<OpenDocument> { doc }));
    }

    [Fact] public void Resolve_reload_adopts_disk_text_keep_records_new_mtime()
    {
        var t1 = DateTime.UtcNow;
        var doc = Doc(@"C:\p\a.txt", "mine", true, t1.AddMinutes(-1));
        var status = new CodeCrack.App.Core.Services.StatusBus();
        var w = new ExternalChangeWatcher(new FakeExternalFileProbe(), status);

        w.Resolve(new ExternalChangePrompt(doc, "disk", t1), reload: true);
        Assert.Equal("disk", doc.Text);
        Assert.False(doc.IsDirty);
        Assert.Equal(t1, doc.LastWriteUtc);
        Assert.Equal("Reloaded a.txt", status.Text);

        var doc2 = Doc(@"C:\p\b.txt", "mine", true, t1.AddMinutes(-1));
        w.Resolve(new ExternalChangePrompt(doc2, "disk", t1), reload: false);
        Assert.Equal("mine", doc2.Text);          // kept
        Assert.Equal(t1, doc2.LastWriteUtc);      // but mtime recorded so it stops asking
        Assert.Equal("Kept your version of b.txt", status.Text);
    }
}
