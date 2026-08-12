using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace CodeCrack.App.Core.Engine;

/// Confines the untrusted analyze/run child in a Win32 Job Object with a memory cap, an
/// active-process cap, and kill-on-close. Disposing the scope closes the job handle,
/// which — via <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> — terminates every descendant,
/// including detached / re-parented children, with no PID walk.
///
/// Defense-in-depth on top of the engine's own wall-clock timeout — not a replacement.
/// Off Windows, or if any Win32 call fails (old OS, nested-job conflict), it degrades to
/// a no-op so analysis never fails merely because confinement was unavailable.
///
/// The type itself is OS-neutral (the P/Invoke declarations compile everywhere and are
/// only ever called under an <see cref="OperatingSystem.IsWindows"/> guard), so
/// CodeCrack.App.Core still builds and runs on Linux.
///
/// NOTE (pre-assignment race): the child is assigned to the job immediately after
/// Start(), leaving a microscopic window in which it could spawn an escapee. Creating the
/// process CREATE_SUSPENDED and resuming only after assignment would close it fully;
/// deferred because the plumbing uses System.Diagnostics.Process, which starts running.
public sealed partial class JobObjectProcessConfiner : IProcessConfiner
{
    public const long DefaultMemoryCapBytes = 2L * 1024 * 1024 * 1024; // 2 GiB
    public const uint DefaultActiveProcessCap = 16;

    private readonly long _memoryCap;
    private readonly uint _activeProcessCap;

    public JobObjectProcessConfiner(
        long memoryCapBytes = DefaultMemoryCapBytes,
        uint activeProcessCap = DefaultActiveProcessCap)
    {
        _memoryCap = memoryCapBytes;
        _activeProcessCap = activeProcessCap;
    }

    public IDisposable Confine(Process process)
    {
        if (!OperatingSystem.IsWindows())
            return NullConfinementScope.Instance;
        try
        {
            return ConfineWindows(process, _memoryCap, _activeProcessCap);
        }
        catch
        {
            return NullConfinementScope.Instance; // best-effort: run unconfined
        }
    }

    [SupportedOSPlatform("windows")]
    private static IDisposable ConfineWindows(Process process, long memoryCap, uint activeProcessCap)
    {
        IntPtr job = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
        if (job == IntPtr.Zero)
            return NullConfinementScope.Instance;
        try
        {
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_JOB_MEMORY
                               | JOB_OBJECT_LIMIT_ACTIVE_PROCESS
                               | JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                    ActiveProcessLimit = activeProcessCap,
                },
                JobMemoryLimit = (UIntPtr)(ulong)memoryCap,
            };

            int len = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr buf = Marshal.AllocHGlobal(len);
            try
            {
                Marshal.StructureToPtr(info, buf, false);
                if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buf, (uint)len))
                    throw new InvalidOperationException("SetInformationJobObject failed");
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }

            // Assign as early as possible after Start() to minimize the pre-assignment race.
            if (!AssignProcessToJobObject(job, process.Handle))
                throw new InvalidOperationException("AssignProcessToJobObject failed");

            return new JobScope(job);
        }
        catch
        {
            CloseHandle(job);
            throw;
        }
    }

    private const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
    private const uint JOB_OBJECT_LIMIT_JOB_MEMORY = 0x00000200;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const int JobObjectExtendedLimitInformation = 9;

    /// Closing the job handle triggers kill-on-close, reaping the whole tree. Idempotent.
    private sealed partial class JobScope : IDisposable
    {
        private IntPtr _job;
        public JobScope(IntPtr job) => _job = job;

        public void Dispose()
        {
            IntPtr h = Interlocked.Exchange(ref _job, IntPtr.Zero);
            if (h != IntPtr.Zero)
                CloseHandle(h);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr CreateJobObjectW(IntPtr lpJobAttributes, IntPtr lpName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
