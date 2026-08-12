using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeCrack.App.Core.Engine;
using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Services;
using CodeCrack.App.Core.Settings;

namespace CodeCrack.App.Core.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IAnalyzer _analyzer;
    private readonly IAppSettings _settings;
    private readonly StatusBus _status;
    private readonly IFileIO _io;

    public MainViewModel(IAnalyzer analyzer, IAppSettings settings, StatusBus status, IFileIO io)
    {
        _analyzer = analyzer;
        _settings = settings;
        _status = status;
        _io = io;
        Issues.LineActivated += RevealLine;
        Tests.FindingActivated += RevealFinding;
    }

    public EditorViewModel Editor { get; } = new();
    public IssuesViewModel Issues { get; } = new();
    public TestsViewModel Tests { get; } = new();
    public FileTreeViewModel FileTree { get; } = new();

    /// The AvalonEdit-hosting control (set by the view). Forwarded to the EditorViewModel.
    public IEditorHost? EditorHost
    {
        get => Editor.Host;
        set => Editor.Host = value;
    }

    public ObservableCollection<OpenDocument> Documents { get; } = new();

    private OpenDocument? _active;
    public OpenDocument? Active
    {
        get => _active;
        set
        {
            if (ReferenceEquals(_active, value)) return;
            Editor.SyncFromHost();          // persist the outgoing doc's buffer before switching tabs
            Set(ref _active, value);
            Editor.Document = value;
            Raise(nameof(CanSave));
            Raise(nameof(CanRun));
            Raise(nameof(CanAnalyze));
        }
    }

    private bool _isAnalyzing;
    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (Set(ref _isAnalyzing, value))
            {
                Raise(nameof(CanAnalyze));
                Raise(nameof(CanCancelAnalyze));
            }
        }
    }

    /// Hard ceiling on a single analyze so a hung interpreter/generation can't wedge the UI.
    public TimeSpan AnalyzeTimeout { get; set; } = TimeSpan.FromSeconds(60);
    private CancellationTokenSource? _analyzeCts;
    public bool CanCancelAnalyze => IsAnalyzing;

    /// Cancel the in-flight analyze (a Cancel affordance, or a re-Analyze).
    public void CancelAnalyze() => _analyzeCts?.Cancel();

    public bool ShowIssues { get; private set; }

    public bool CanSave => Active is not null;
    public bool CanRun => Active is not null && !IsRunning;
    public bool CanAnalyze => Active is not null && !IsAnalyzing && IsPython;

    private bool IsPython
    {
        get
        {
            if (Active is null) return false;
            var ext = Path.GetExtension(Active.Path).TrimStart('.').ToLowerInvariant();
            return ext == "py" || ext == "pyw";
        }
    }

    public void RevealLine(int line1Indexed) => EditorHost?.RevealLine(line1Indexed);

    private void RevealFinding(string findingId)
    {
        var f = Issues.Findings.FirstOrDefault(x => x.Id == findingId);
        if (f is not null) RevealLine(f.Line);
    }

    /// Atomic UTF-8 (no BOM) save via IFileIO; clears dirty and refreshes stored mtime.
    public string Save()
    {
        if (Active is null) return "";
        Editor.SyncFromHost();
        Active.LastWriteUtc = _io.AtomicWrite(Active.Path, Active.Text);
        Active.IsDirty = false;
        var msg = $"Saved {Active.Name}";
        _status.Set(msg);
        return msg;
    }

    /// Sync wrapper for the Analyze command (fire-and-forget; CanAnalyze guards re-entry).
    public void Analyze() => _ = AnalyzeAsync();

    /// Opens (or re-focuses) the document at <paramref name="path"/>, loading its text and
    /// stored mtime via IFileIO. Dialogs live in the view; this takes a resolved path.
    public OpenDocument OpenPath(string path)
    {
        var full = Path.GetFullPath(path);
        var existing = Documents.FirstOrDefault(
            d => string.Equals(d.Path, full, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { Active = existing; return existing; }

        var doc = new OpenDocument
        {
            Path = full,
            Text = _io.Read(full),
            IsDirty = false,
            LastWriteUtc = _io.GetLastWriteUtc(full),
        };
        Documents.Add(doc);
        Active = doc;
        FileTree.BuildFrom(full); // rebuild the sidebar from the opened file's directory
        return doc;
    }

    /// Creates a fresh untitled buffer and makes it active.
    public OpenDocument NewDocument()
    {
        var doc = new OpenDocument { Path = string.Empty, Text = string.Empty, IsDirty = false };
        Documents.Add(doc);
        Active = doc;
        return doc;
    }

    /// Save As: point the active buffer at <paramref name="path"/>, then persist via Save().
    public string SaveToPath(string path)
    {
        if (Active is null) return "";
        Active.Path = Path.GetFullPath(path);
        var msg = Save();
        FileTree.BuildFrom(Active.Path); // Save-As creates a file; refresh the sidebar
        return msg;
    }

    /// Closes the active document, choosing the next active by the close-neighbor rule
    /// (prefer left, else right, else none). mac discards unsaved changes silently.
    public void CloseActive()
    {
        if (Active is null) return;
        int idx = Documents.IndexOf(Active);
        if (idx < 0) return;
        int? next = TabManagement.SelectAfterClose(Documents.Count, idx);
        Documents.RemoveAt(idx);
        Active = next is int n ? Documents[n] : null;
    }

    /// Save the current file, run the engine, and surface findings/tests.
    public async Task AnalyzeAsync(CancellationToken ct = default)
    {
        if (Active is null) return;
        Save(); // engine reads from disk
        var name = Active.Name;
        var path = Active.Path;   // capture: the active tab may change during the await
        ShowIssues = true;
        Issues.ErrorMessage = null;
        IsAnalyzing = true;
        _status.Set($"Analyzing {name}…");

        using var timeout = new CancellationTokenSource(AnalyzeTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        _analyzeCts = linked;
        try
        {
            var outcome = await _analyzer.AnalyzeAsync(path, linked.Token).ConfigureAwait(true);
            if (outcome.IsSuccess && outcome.Result is not null)
            {
                Issues.SetFindings(outcome.Result.Findings);
                Tests.SetResult(outcome.Result.Tests, outcome.Result.Summary);
                Issues.ErrorMessage = null;
                int n = outcome.Result.Summary.Findings;
                _status.Set($"Analysis found {n} issue{(n == 1 ? "" : "s")} in {name}");
            }
            else
            {
                Issues.SetFindings(Array.Empty<Finding>());
                Tests.SetResult(Array.Empty<GeneratedTest>(), null);
                Issues.ErrorMessage = outcome.Error?.Message;
                _status.Set("Analysis failed");
            }
        }
        catch (OperationCanceledException)
        {
            Issues.SetFindings(Array.Empty<Finding>());
            Tests.SetResult(Array.Empty<GeneratedTest>(), null);
            bool timedOut = timeout.IsCancellationRequested && !ct.IsCancellationRequested;
            var msg = timedOut
                ? $"Analysis timed out after {(int)AnalyzeTimeout.TotalSeconds}s"
                : "Analysis cancelled";
            Issues.ErrorMessage = msg;
            _status.Set(msg);
        }
        finally
        {
            _analyzeCts = null;
            IsAnalyzing = false;
        }
    }

    // ---- Run / console (Task 3.5) ----

    public ConsoleViewModel Console { get; } = new();
    public bool ShowConsole { get; private set; }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set { if (Set(ref _isRunning, value)) Raise(nameof(CanRun)); }
    }

    private IRunSession? _session;

    /// Injection seam for tests; defaults to the real process Runner.
    public Func<RunCommand, Action<string>, Action<int>, IRunSession?> RunnerFactory { get; set; }
        = Runner.Start;

    public string Run()
    {
        if (Active is null) return "";
        Save(); // run reads from disk
        ShowConsole = true;

        var cmd = RunCommandTable.For(Active.Path);
        if (cmd is null)
        {
            var ext = Path.GetExtension(Active.Path).TrimStart('.');
            Console.Clear();
            Console.Append($"Don't know how to run .{ext} files yet.\n");
            var noRun = $"No run configuration for .{ext}";
            _status.Set(noRun);
            return noRun;
        }

        _session?.Stop();
        Console.Clear();
        Console.Append($"$ {cmd.Display}\n\n");
        IsRunning = true;
        Console.IsRunning = true;

        _session = RunnerFactory(cmd,
            text => Console.Append(text),
            code =>
            {
                Console.Append($"\n[exited with code {code}]\n");
                IsRunning = false;
                Console.IsRunning = false;
                _session = null;
            });

        return cmd.Display;
    }

    public void SendInput(string text)
    {
        _session?.Send(text);
        Console.Append(text + "\n");
    }
}
