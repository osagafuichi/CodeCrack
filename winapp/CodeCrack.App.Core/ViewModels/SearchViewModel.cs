using System;
using System.Collections.ObjectModel;
using CodeCrack.App.Core.Search;

namespace CodeCrack.App.Core.ViewModels;

/// <summary>
/// Drives the project-wide Find in Files panel. UI-free: it resolves the project root through an
/// injected provider (the file-tree root), runs <see cref="ProjectSearch"/>, and raises
/// <see cref="RevealRequested"/> when the user selects a hit so the shell can open the file and
/// reveal the line via <c>IEditorHost.RevealLine</c>. Replace-All uses the case-SENSITIVE
/// semantics baked into <see cref="ProjectSearch.ReplaceAll"/>.
/// </summary>
public sealed class SearchViewModel : ObservableObject
{
    private readonly Func<string?> _rootProvider;

    public SearchViewModel(Func<string?> rootProvider)
        => _rootProvider = rootProvider ?? throw new ArgumentNullException(nameof(rootProvider));

    public ObservableCollection<SearchHit> Results { get; } = new();

    private string _query = string.Empty;
    public string Query
    {
        get => _query;
        set => Set(ref _query, value ?? string.Empty);
    }

    private string _replacement = string.Empty;
    public string Replacement
    {
        get => _replacement;
        set => Set(ref _replacement, value ?? string.Empty);
    }

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    private SearchHit? _selectedHit;
    /// Selecting a hit requests that the shell open the file and reveal the line.
    public SearchHit? SelectedHit
    {
        get => _selectedHit;
        set
        {
            if (Set(ref _selectedHit, value) && value is not null)
                RevealRequested?.Invoke(value);
        }
    }

    /// Raised when a hit is selected: (path, 1-based line) should be opened and revealed.
    public event Action<SearchHit>? RevealRequested;

    /// Run the search across the project root and populate <see cref="Results"/>.
    public void Search()
    {
        Results.Clear();
        SelectedHit = null;

        if (string.IsNullOrEmpty(Query))
        {
            Status = "Type something to search for.";
            return;
        }

        var root = _rootProvider();
        if (string.IsNullOrEmpty(root))
        {
            Status = "Open a file or folder first.";
            return;
        }

        var hits = ProjectSearch.Search(Query, root);
        foreach (var hit in hits) Results.Add(hit);

        var capped = hits.Count >= ProjectSearch.MaxHits ? $"{hits.Count}+" : hits.Count.ToString();
        Status = hits.Count == 0
            ? $"No results for “{Query}”"
            : $"{capped} result{(hits.Count == 1 ? "" : "s")} for “{Query}”";
    }

    /// Replace every case-sensitive occurrence of <see cref="Query"/> with <see cref="Replacement"/>
    /// across the project, then refresh the results and report how many files changed.
    public void ReplaceAll()
    {
        if (string.IsNullOrEmpty(Query))
        {
            Status = "Type something to search for.";
            return;
        }

        var root = _rootProvider();
        if (string.IsNullOrEmpty(root))
        {
            Status = "Open a file or folder first.";
            return;
        }

        var changed = ProjectSearch.ReplaceAll(Query, Replacement, root);
        Search(); // refresh the hit list against the rewritten files
        Status = changed.Count == 0
            ? "No case-sensitive matches to replace."
            : $"Replaced in {changed.Count} file{(changed.Count == 1 ? "" : "s")}.";
    }
}
