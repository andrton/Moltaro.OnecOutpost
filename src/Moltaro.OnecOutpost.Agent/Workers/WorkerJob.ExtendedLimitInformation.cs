using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class WorkerJob
{
    // JOBOBJECT_EXTENDED_LIMIT_INFORMATION of winnt.h, with its JOBOBJECT_BASIC_LIMIT_INFORMATION and IO_COUNTERS laid
    // out in place, in their order: the sequential layout aligns each field as the C compiler does, for 64 and for 32
    // bits. Only LimitFlags is set; every other limit stays zero, which sets none.
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        // JOBOBJECT_BASIC_LIMIT_INFORMATION.
        public long PerProcessUserTimeLimit;

        public long PerJobUserTimeLimit;

        public uint LimitFlags;

        public nuint MinimumWorkingSetSize;

        public nuint MaximumWorkingSetSize;

        public uint ActiveProcessLimit;

        public nuint Affinity;

        public uint PriorityClass;

        public uint SchedulingClass;

        // IO_COUNTERS.
        public ulong ReadOperationCount;

        public ulong WriteOperationCount;

        public ulong OtherOperationCount;

        public ulong ReadTransferCount;

        public ulong WriteTransferCount;

        public ulong OtherTransferCount;

        // The memory limits and peaks of the extended information.
        public nuint ProcessMemoryLimit;

        public nuint JobMemoryLimit;

        public nuint PeakProcessMemoryUsed;

        public nuint PeakJobMemoryUsed;
    }
}
