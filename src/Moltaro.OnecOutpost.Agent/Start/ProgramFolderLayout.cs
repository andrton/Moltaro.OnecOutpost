using Moltaro.OnecOutpost.Agent.Connector;

namespace Moltaro.OnecOutpost.Agent.Start;

// The layout of the program folder, named once for the agent and the console: the COM worker of each bitness in x64\
// and x86\, each with the files of its build, and the executor beside the agent's program.
internal static class ProgramFolderLayout
{
    public const string WorkerFile = "Moltaro.OnecOutpost.ComWorker.exe";

    public const string ExecutorFile = "OutpostExecutor.epf";

    // {programFolder}\x64\ or \x86\, then WorkerFile.
    public static string WorkerOf(string programFolder, ConnectorBitnessEnum bitness)
    {
        string folder = bitness == ConnectorBitnessEnum.X64 ? "x64" : "x86";

        return Path.Combine(programFolder, folder, WorkerFile);
    }

    public static string ExecutorOf(string programFolder)
    {
        return Path.Combine(programFolder, ExecutorFile);
    }
}
