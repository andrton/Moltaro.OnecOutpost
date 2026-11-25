using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Start;

namespace Moltaro.OnecOutpost.Agent.Tests.Start;

// The layout of the program folder, named once for the agent and the console: the agent's program and the executor in
// the folder itself, and the COM
// worker of each bitness in x64\ and x86\, as the archive lays them out and install registers the service.
public sealed class ProgramFolderLayoutTests
{
    private static readonly string Folder = Path.Combine(OperatingSystem.IsWindows() ? @"C:\" : "/", "Program Files", "Moltaro.OnecOutpost");

    [Fact]
    public void The_program_of_the_agent_lies_in_the_program_folder_itself()
    {
        Assert.Equal(Path.Combine(Folder, "Moltaro.OnecOutpost.Agent.exe"), ProgramFolderLayout.AgentOf(Folder));
        Assert.Equal("Moltaro.OnecOutpost.Agent.exe", ProgramFolderLayout.AgentFile);
    }

    [Fact]
    public void The_executor_lies_in_the_program_folder_and_each_worker_in_the_folder_of_its_bitness()
    {
        Assert.Equal(Path.Combine(Folder, "OutpostExecutor.epf"), ProgramFolderLayout.ExecutorOf(Folder));
        Assert.Equal(
            Path.Combine(Folder, "x64", "Moltaro.OnecOutpost.ComWorker.exe"),
            ProgramFolderLayout.WorkerOf(Folder, ConnectorBitnessEnum.X64));
        Assert.Equal(
            Path.Combine(Folder, "x86", "Moltaro.OnecOutpost.ComWorker.exe"),
            ProgramFolderLayout.WorkerOf(Folder, ConnectorBitnessEnum.X86));
    }
}
