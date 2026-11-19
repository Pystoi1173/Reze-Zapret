using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal sealed class Session : IDisposable
{
    private readonly SafeFileHandle job;

    private Session(SafeFileHandle job) { this.job = job; }

    public static Session Start(string folder, string script)
    {
        if (!ProfileFiles.IsProfile(script)) throw new ArgumentException("Недопустимое имя профиля.");
        if (!File.Exists(Path.Combine(folder, script))) throw new FileNotFoundException("Профиль не найден в выбранной папке.");
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception();
        try
        {
            var limits = new ExtendedLimits();
            limits.Basic.LimitFlags = 0x2000; // Close the launcher and the entire session is stopped.
            if (!SetInformationJobObject(job, limitsClass, ref limits, Marshal.SizeOf(typeof(ExtendedLimits))))
                throw new Win32Exception();

            var startup = new StartupInfo { Size = Marshal.SizeOf(typeof(StartupInfo)) };
            var command = new StringBuilder("\"" + cmd + "\" /d /v:off /c \"\".\\" + script + "\"\"");
            ProcessInfo process;
            if (!CreateProcess(cmd, command, IntPtr.Zero, IntPtr.Zero, false, 0x14, IntPtr.Zero,
                               folder, ref startup, out process))
                throw new Win32Exception();
            using (var handle = new SafeFileHandle(process.Process, true))
            using (var thread = new SafeFileHandle(process.Thread, true))
            {
                if (!AssignProcessToJobObject(job, handle) || ResumeThread(thread) == uint.MaxValue)
                {
                    var error = new Win32Exception();
                    TerminateProcess(handle, 1);
                    throw error;
                }
            }
            return new Session(job);
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    public bool Contains(Process process)
    {
        bool result;
        return IsProcessInJob(process.Handle, job, out result) && result;
    }

    public bool IsActive
    {
        get
        {
            Accounting data;
            if (!QueryInformationJobObject(job, accountingClass, out data, Marshal.SizeOf(typeof(Accounting)), IntPtr.Zero))
                throw new Win32Exception();
            return data.ActiveProcesses != 0;
        }
    }

    public void Dispose() { job.Dispose(); }

    private const int limitsClass = 9;
    private const int accountingClass = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations;
        public ulong ReadBytes, WriteBytes, OtherBytes;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemory, PeakJobMemory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting
    {
        public long TotalUserTime, TotalKernelTime, PeriodUserTime, PeriodKernelTime;
        public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedData, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int type, ref ExtendedLimits data, int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int type, out Accounting data, int size, IntPtr written);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment,
        string directory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(IntPtr process, SafeFileHandle job, out bool result);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(SafeFileHandle process, uint code);
}
