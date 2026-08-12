using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.ViewModels;
using Xunit;

namespace CodeCrack.Tests;

public class CommandGatesTests
{
    private static OpenDocument Doc(string path, bool dirty = false) =>
        new() { Path = path, Text = "", IsDirty = dirty, LastWriteUtc = System.DateTime.UtcNow };

    [Fact] public void Save_Run_Analyze_disabled_with_no_active_document()
    {
        Assert.False(CommandGates.CanSave(null));
        Assert.False(CommandGates.CanRun(null));
        Assert.False(CommandGates.CanAnalyze(null, isAnalyzing: false));
    }

    [Fact] public void Save_and_Run_enabled_with_active_document()
    {
        var d = Doc(@"C:\proj\a.txt");
        Assert.True(CommandGates.CanSave(d));
        Assert.True(CommandGates.CanRun(d));
    }

    [Fact] public void Analyze_requires_python_and_not_already_analyzing()
    {
        Assert.True(CommandGates.CanAnalyze(Doc(@"C:\proj\a.py"), isAnalyzing: false));
        Assert.True(CommandGates.CanAnalyze(Doc(@"C:\proj\a.pyw"), isAnalyzing: false));
        Assert.False(CommandGates.CanAnalyze(Doc(@"C:\proj\a.js"), isAnalyzing: false));
        Assert.False(CommandGates.CanAnalyze(Doc(@"C:\proj\a.py"), isAnalyzing: true));
    }
}
