using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using CodeCrack.App.Core.Engine;

namespace CodeCrack.App.Core.ViewModels;

public sealed class IssuesViewModel : ObservableObject
{
    public ObservableCollection<Finding> Findings { get; } = new();

    private string? _errorMessage;
    public string? ErrorMessage { get => _errorMessage; set => Set(ref _errorMessage, value); }

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
        Findings.Clear();
        foreach (var f in findings.OrderBy(f => SeverityRank(f.Severity)))
            Findings.Add(f);
    }

    public void Activate(Finding finding) => LineActivated?.Invoke(finding.Line);
}
