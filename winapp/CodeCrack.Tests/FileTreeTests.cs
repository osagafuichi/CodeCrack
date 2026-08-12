using System.IO;
using System.Linq;
using CodeCrack.App.Core.ViewModels;
using Xunit;

namespace CodeCrack.Tests;

public class FileTreeTests : System.IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cc_tree_" + Path.GetRandomFileName());

    public FileTreeTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "z.py"), "z");
        File.WriteAllText(Path.Combine(_root, "src", "a.py"), "a");
        File.WriteAllText(Path.Combine(_root, "readme.md"), "r");
        File.WriteAllText(Path.Combine(_root, ".secret"), "hidden"); // dotfile -> skipped
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact] public void Build_skips_hidden_dirs_first_then_case_insensitive_names()
    {
        var vm = new FileTreeViewModel();
        vm.BuildFrom(_root);

        var names = vm.Root!.Children.Select(c => c.Name).ToArray();
        Assert.Equal(new[] { "src", "readme.md" }, names); // dir before file; .secret gone
        Assert.DoesNotContain(vm.Root.Children, c => c.Name == ".secret");

        var srcKids = vm.Root.Children.First(c => c.Name == "src").Children.Select(c => c.Name);
        Assert.Equal(new[] { "a.py", "z.py" }, srcKids); // case-insensitive sort
    }

    [Fact] public void Build_from_single_file_roots_at_its_parent()
    {
        var vm = new FileTreeViewModel();
        vm.BuildFrom(Path.Combine(_root, "readme.md"));
        Assert.Equal(_root, vm.Root!.Path);
        Assert.True(vm.Root.IsDirectory);
    }
}
