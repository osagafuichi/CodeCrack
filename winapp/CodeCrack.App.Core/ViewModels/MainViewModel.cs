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
            if (!Set(ref _active, value)) return;
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
        private set { if (Set(ref _isAnalyzing, value)) Raise(nameof(CanAnalyze)); }
    }

    public bool ShowIssues { get; private set; }

    public bool CanSave => Active is not null;
    public bool CanRun => Active is not null;
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

    /// Save the current file, run the engine, and surface findings/tests.
    public async Task AnalyzeAsync(CancellationToken ct = default)
    {
        if (Active is null) return;
        Save(); // engine reads from disk
        var name = Active.Name;
        ShowIssues = true;
        Issues.ErrorMessage = null;
        IsAnalyzing = true;
        _status.Set($"Analyzing {name}…");

        var outcome = await _analyzer.AnalyzeAsync(Active.Path, ct).ConfigureAwait(true);

        IsAnalyzing = false;
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
}
