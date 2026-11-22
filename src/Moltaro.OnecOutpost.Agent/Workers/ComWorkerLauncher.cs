using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The program's launcher of the COM workers, which S9b implements. Until then it starts no process, and the OpenAsync
// of what it starts gives false, so every mode declares free 0.
[SupportedOSPlatform("windows")]
internal sealed partial class ComWorkerLauncher : IWorkerLauncher
{
    // The ProgID of the COM connector that the platform registers.
    public const string ComConnector = "V83.COMConnector";

    // The file of the COM worker in x64\ and x86\ beside the agent's program, each with the files of its build.
    public const string WorkerFile = "Moltaro.OnecOutpost.ComWorker.exe";

    // The two programs of the worker, the ProgID of the connector whose registration chooses between them, and the job
    // that every started process joins.
    public ComWorkerLauncher(string x64Program, string x86Program, string connectorProgId, WorkerJob job)
    {
        throw new NotSupportedException();
    }

    // Reads the registry at each start (concept §5, O8): the x64 program when the connector is registered for 64 bits,
    // otherwise the x86 program when it is for 32 bits, otherwise a NoProcess. Registered for a bitness:
    // HKEY_CLASSES_ROOT, opened with RegistryKey.OpenBaseKey in the view Registry64 or Registry32, gives a class
    // identifier as the default value of {connectorProgId}\CLSID, and CLSID\{that}\InprocServer32 has a default value
    // that is not empty; never 64 bits on a 32-bit Windows. A key that cannot be read is not registered; no file or
    // version is checked. Process.Start of the program with ArgumentList the base key and read or write, the three
    // standard streams redirected, UseShellExecute false and CreateNoWindow true, then WorkerJob.Add. The connection
    // with its password travels only in the first input line, never on the command line or in the environment. A
    // program that does not start, and a process the job does not take, killed at once, give a NoProcess: Start throws
    // nothing. Limitation: a process is in the job from just after it starts; if the agent ends in between, it ends at
    // the end of its input, as S9's worker does.
    public IWorkerProcess Start(string baseKey, OutpostModeEnum mode)
    {
        throw new NotSupportedException();
    }
}
