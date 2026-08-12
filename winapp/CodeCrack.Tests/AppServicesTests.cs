using System.IO;
using CodeCrack.App.Core;
using CodeCrack.Tests.Fakes;
using Xunit;

namespace CodeCrack.Tests;

public sealed class AppServicesTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "codecrack_appservices_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Builds_the_full_graph_without_throwing()
    {
        var dir = TempDir();
        try
        {
            var services = new AppServices(dir);

            Assert.NotNull(services.Main);
            Assert.NotNull(services.Session);
            Assert.NotNull(services.Recent);
            Assert.NotNull(services.ExternalChange);
            Assert.True(services.Recent.IsEmpty);

            // The editor host is injected by the view; a fake stands in headlessly.
            services.Main.EditorHost = new FakeEditorHost();
            var doc = services.Main.NewDocument();
            Assert.Same(doc, services.Main.Active);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Opening_a_file_builds_the_sidebar_tree_and_reveals_via_host()
    {
        var dir = TempDir();
        try
        {
            var file = Path.Combine(dir, "sample.py");
            File.WriteAllText(file, "def f():\n    return 1\n");

            var services = new AppServices(dir);
            var host = new FakeEditorHost();
            services.Main.EditorHost = host;

            var doc = services.Main.OpenPath(file);

            Assert.Equal(file, doc.Path);
            Assert.Contains("return 1", doc.Text);
            Assert.NotNull(services.Main.FileTree.Root);           // sidebar built from the file's dir
            Assert.True(services.Main.FileTree.Root!.IsDirectory);

            services.Main.RevealLine(2);
            Assert.Equal(2, host.RevealedLine);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
