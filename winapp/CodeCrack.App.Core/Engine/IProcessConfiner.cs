using System;
using System.Diagnostics;

namespace CodeCrack.App.Core.Engine;

/// Confines an already-started child process (and everything it spawns) to bounded
/// resources. Disposing the returned scope reaps the confined process tree.
///
/// Confinement is best-effort and never throws: an implementation that cannot confine
/// (wrong OS, missing privilege) must return a no-op scope so analysis still runs.
public interface IProcessConfiner
{
    IDisposable Confine(Process process);
}

/// Default confiner: does nothing. Used by Core so headless tests and non-Windows
/// builds run unconfined (the real confinement is Windows-only; see
/// <see cref="JobObjectProcessConfiner"/>).
public sealed class NullProcessConfiner : IProcessConfiner
{
    public static readonly NullProcessConfiner Instance = new();
    public IDisposable Confine(Process process) => NullConfinementScope.Instance;
}

/// A confinement scope that reaps nothing.
public sealed class NullConfinementScope : IDisposable
{
    public static readonly NullConfinementScope Instance = new();
    public void Dispose() { }
}
