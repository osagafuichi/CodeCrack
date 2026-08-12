using System;
using System.Diagnostics;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace CodeCrack.UiTests;

/// End-to-end UI smoke against the PACKAGED CodeCrack.exe: launch with a buggy file,
/// invoke Analyze, and assert the real WPF binding surfaces an Issue and a proven test.
/// This is the automated proxy for the human click-test the app never had.
public sealed class AnalyzeSmokeTests
{
    // zero-division: the engine reliably finds it AND a generated test reproduces it.
    private const string BuggySource = "def divide(a, b):\n    return a / b\n";

    [Fact]
    public void Analyze_a_buggy_file_shows_an_issue_and_bug_proven()
    {
        var exe = FindPackagedExe();
        var tempPy = Path.Combine(Path.GetTempPath(), $"cc_ui_{Guid.NewGuid():N}.py");
        File.WriteAllText(tempPy, BuggySource);

        using var automation = new UIA3Automation();
        var app = Application.Launch(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"\"{tempPy}\"",
        });
        Window? win = null;
        try
        {
            // The single-file self-extracting exe re-launches into another process, so the
            // window isn't on the launched handle — find it on the desktop by its title.
            // First-run self-extract + Defender scan can be slow, so retry generously.
            var desktop = automation.GetDesktop();
            win = Retry.WhileNull(
                () => desktop.FindFirstChild(cf =>
                    cf.ByControlType(ControlType.Window).And(cf.ByName("CodeCrack")))?.AsWindow(),
                TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(1), ignoreException: true).Result;
            Assert.True(win is not null, "CodeCrack main window never appeared on the desktop");
            var window = win!;

            // The file arg opened a tab. Trigger Analyze via the Run ▸ Analyze menu with
            // physical clicks (robust to which window has keyboard focus, unlike a
            // Ctrl+B accelerator). The submenu opens in a popup, so find it on the desktop.
            var runMenu = Retry.WhileNull(
                () => window.FindFirstDescendant(cf => cf.ByAutomationId("MenuRun")),
                TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(500), ignoreException: true).Result;
            Assert.True(runMenu is not null, "Run menu not found");
            runMenu!.Click();
            var analyze = Retry.WhileNull(
                () => automation.GetDesktop().FindFirstDescendant(cf => cf.ByAutomationId("MenuAnalyze")),
                TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(500), ignoreException: true).Result;
            Assert.True(analyze is not null, "Analyze menu item not found (is a .py file open + active?)");
            analyze!.Click();

            // Analyze shells out to the bundled Python + pytest — allow generous time.
            string StatusNow() =>
                window.FindFirstDescendant(cf => cf.ByAutomationId("StatusBar"))?.Name ?? "";
            var status = Retry.WhileFalse(
                () => StatusNow().Contains("Analysis found", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(120), TimeSpan.FromMilliseconds(500), ignoreException: true);
            Assert.True(status.Success,
                $"status bar never reported 'Analysis found …' — last status was: '{StatusNow()}'");

            var statusText = window.FindFirstDescendant(cf => cf.ByAutomationId("StatusBar"))?.Name ?? "";
            Assert.DoesNotContain("found 0 issue", statusText, StringComparison.OrdinalIgnoreCase);

            // Issues header binds to Findings.Count → "Issues (1)" for this file.
            var issuesHeader = Retry.WhileNull(
                () => window.FindFirstDescendant(cf => cf.ByAutomationId("IssuesHeader")),
                TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(500), ignoreException: true).Result;
            Assert.True(issuesHeader is not null, "Issues header not found");
            Assert.DoesNotContain("(0)", issuesHeader!.Name);

            // Switch to the Tests tab and assert the "BUG PROVEN" badge rendered.
            var testsTab = window.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.TabItem).And(cf.ByName("Tests")));
            testsTab?.AsTabItem().Select();
            var proven = Retry.WhileNull(
                () => window.FindFirstDescendant(cf => cf.ByName("BUG PROVEN")),
                TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(500), ignoreException: true).Result;
            Assert.True(proven is not null, "BUG PROVEN badge not found in the Tests panel");
        }
        finally
        {
            try { win?.Close(); } catch { /* ignore */ }
            try { app.Close(); } catch { /* ignore */ }
            try { if (!app.HasExited) app.Kill(); } catch { /* ignore */ }
            try { File.Delete(tempPy); } catch { /* ignore */ }
        }
    }

    /// Locate the packaged exe: CODECRACK_EXE env override, else walk up to dist\CodeCrack.
    private static string FindPackagedExe()
    {
        var env = Environment.GetEnvironmentVariable("CODECRACK_EXE");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "dist", "CodeCrack", "CodeCrack.exe");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        throw new FileNotFoundException(
            "Packaged CodeCrack.exe not found. Build it first (winapp/make-app.ps1) " +
            "or set CODECRACK_EXE to its path.");
    }
}
