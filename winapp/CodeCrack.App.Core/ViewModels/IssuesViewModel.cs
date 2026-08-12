using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using CodeCrack.App.Core.Engine;

namespace CodeCrack.App.Core.ViewModels;

public sealed class IssuesViewModel : ObservableObject
{
    public ObservableCollection<Finding> Findings { get; } = new();

    private bool _hasAnalyzed;

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set { if (Set(ref _errorMessage, value)) Raise(nameof(ShowEmptyState)); }
    }

    /// True when there are no findings to list and no error banner: the panel would otherwise
    /// be a confusing blank, so the view shows <see cref="EmptyStateMessage"/> instead.
    public bool ShowEmptyState => Findings.Count == 0 && string.IsNullOrEmpty(_errorMessage);

    /// Friendly placeholder: guidance before the first analyze, reassurance after a clean one.
    public string EmptyStateMessage =>
        _hasAnalyzed ? "No issues found." : "Run Analyze to find issues.";

    /// Raised when a finding row is activated; MainViewModel maps it to editor RevealLine.
    public event Action<int>? LineActivated;

    /// high before medium before anything else (low/unknown). LINQ OrderBy is stable,
    /// so equal-severity findings keep engine order.
    public static int SeverityRank(string severity) => severity.ToLowerInvariant() switch
    {
        "high" => 0,
        "medium" => 1,
        _ => 2,
    };

    public void SetFindings(IEnumerable<Finding> findings)
    {
        _hasAnalyzed = true;
        Findings.Clear();
        foreach (var f in findings.OrderBy(f => SeverityRank(f.Severity)))
            Findings.Add(f);
        Raise(nameof(ShowEmptyState));
        Raise(nameof(EmptyStateMessage));
    }

    public void Activate(Finding finding) => LineActivated?.Invoke(finding.Line);
}
