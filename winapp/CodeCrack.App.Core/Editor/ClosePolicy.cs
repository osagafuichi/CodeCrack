using System.Collections.Generic;
using System.Linq;

namespace CodeCrack.App.Core.Editor;

/// <summary>What a close/quit gesture should do before the buffer(s) actually go away.</summary>
public enum CloseAction
{
    /// Nothing unsaved is at stake; close immediately with no prompt.
    CloseImmediately,
    /// Unsaved work would be lost; the view must prompt Save / Discard / Cancel first.
    Prompt,
}

/// <summary>Pure dirty-close/quit decision rules, kept UI-free so they are unit-testable.
/// The view maps <see cref="CloseAction.Prompt"/> onto a Save/Discard/Cancel MessageBox and only
/// prompts when there is genuinely unsaved work (respecting the "never nag on a clean doc" non-goal).</summary>
public static class ClosePolicy
{
    /// <summary>Closing a single tab: prompt only when that tab has unsaved changes.</summary>
    public static CloseAction ForClosingTab(OpenDocument? doc) =>
        doc is { IsDirty: true } ? CloseAction.Prompt : CloseAction.CloseImmediately;

    /// <summary>Quitting the app: prompt when any open buffer has unsaved changes.</summary>
    public static CloseAction ForQuit(IEnumerable<OpenDocument> docs) =>
        docs.Any(d => d.IsDirty) ? CloseAction.Prompt : CloseAction.CloseImmediately;

    /// <summary>The still-dirty buffers a quit must resolve, in tab order.</summary>
    public static IReadOnlyList<OpenDocument> DirtyDocuments(IEnumerable<OpenDocument> docs) =>
        docs.Where(d => d.IsDirty).ToList();
}
