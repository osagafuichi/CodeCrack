using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CodeCrack.App.Core.Engine;

namespace CodeCrack.App.Core.ViewModels;

public sealed class TestsViewModel : ObservableObject
{
    public ObservableCollection<GeneratedTest> Tests { get; } = new();

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
        Tests.Clear();
        foreach (var t in tests) Tests.Add(t);
        Summary = summary;
    }

    public void JumpToFinding(GeneratedTest test) => FindingActivated?.Invoke(test.FindingId);
}
