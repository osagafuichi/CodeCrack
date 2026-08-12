using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Services;
using CodeCrack.App.Core.ViewModels;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public sealed class TabSwitchTests
{
    private static MainViewModel Build(out FakeEditorHost host)
    {
        var vm = new MainViewModel(new FakeAnalyzer(), new FakeAppSettings(), new StatusBus(), new FakeFileIO());
        host = new FakeEditorHost();
        vm.EditorHost = host;
        return vm;
    }

    [Fact]
    public void Switching_tabs_preserves_unsaved_buffer_edits_and_marks_dirty()
    {
        var vm = Build(out var host);
        var a = new OpenDocument { Path = @"C:\proj\a.py", Text = "print('a')\n" };
        var b = new OpenDocument { Path = @"C:\proj\b.py", Text = "print('b')\n" };
        vm.Documents.Add(a);
        vm.Documents.Add(b);

        vm.Active = a;                                   // A loads into the host
        Assert.Equal("print('a')\n", host.Text);

        host.Text = "print('a edited')\n";               // user edits the buffer (no save)
        vm.Active = b;                                   // switch away -> must sync A first

        Assert.Equal("print('a edited')\n", a.Text);     // A's buffer preserved
        Assert.True(a.IsDirty);                          // and marked dirty
        Assert.Equal("print('b')\n", host.Text);         // B now in the host

        vm.Active = a;                                   // switch back
        Assert.Equal("print('a edited')\n", host.Text);  // A restored with its edits
    }
}
