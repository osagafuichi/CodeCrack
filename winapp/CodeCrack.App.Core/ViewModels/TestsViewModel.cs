using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CodeCrack.App.Core.Engine;

namespace CodeCrack.App.Core.ViewModels;

public sealed class TestsViewModel : ObservableObject
{
    public ObservableCollection<GeneratedTest> Tests { get; } = new();

    private bool _hasAnalyzed;

    /// True when there are no generated tests to list: the view shows <see cref="EmptyStateMessage"/>
    /// instead of a blank list.
    public bool ShowEmptyState => Tests.Count == 0;

    /// Friendly placeholder: a call to action before the first analyze, a plain result after one.
    public string EmptyStateMessage =>
        _hasAnalyzed ? "No tests generated." : "Run Analyze to generate tests.";

    private Summary? _summary;
    public Summary? Summary
    {
        get => _summary;
        private set
        {
            if (Set(ref _summary, value))
            {
                Raise(nameof(ReproducedCount));
                Raise(nameof(Headline));
                Raise(nameof(HasSummary));
            }
        }
    }

    public bool HasSummary => _summary is not null;
    public int ReproducedCount => _summary?.Reproduced ?? 0;

    /// Load-bearing headline: how many generated tests reproduced a real failure
    /// (engine's summary.reproduced — never re-derived).
    public string Headline
    {
        get
        {
            if (_summary is null) return "";
            int n = _summary.Reproduced;
            return $"{n} test{(n == 1 ? "" : "s")} reproduce a real failure";
        }
    }

    public event Action<string>? FindingActivated;

    public void SetResult(IReadOnlyList<GeneratedTest> tests, Summary? summary)
    {
        _hasAnalyzed = true;
        Tests.Clear();
        foreach (var t in tests) Tests.Add(t);
        Summary = summary;
        Raise(nameof(ShowEmptyState));
        Raise(nameof(EmptyStateMessage));
    }

    public void JumpToFinding(GeneratedTest test) => FindingActivated?.Invoke(test.FindingId);
}
