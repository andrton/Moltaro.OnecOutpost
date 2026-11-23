using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.TestSupport.Programs;

namespace Moltaro.OnecOutpost.Agent.Tests.Host;

// The agent program on Windows (the S6 brief, "Program", "AgentService.Run" and "AgentExitCodes"), with
// Requires=Windows: started with service by anything but the service controller, the dispatcher fails with error 1063
// and the program exits with NotService before it touches its data folder, %ProgramData%\Moltaro.OnecOutpost, which
// the test only reads; with any other arguments it exits with Usage and never waits on the service controller. The
// program is the one built beside the tests, run with its standard streams redirected. A real service is the manual
// acceptance of the brief.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class ProgramTests
{
    private static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Moltaro.OnecOutpost");

    [Fact]
    public async Task The_program_started_with_service_outside_the_service_controller_exits_with_NotService_and_changes_nothing_in_its_data_folder()
    {
        string before = BuiltProgram.Describe(DataFolder);

        int exitCode = await RunAsync("service");

        Assert.Equal(AgentExitCodes.NotService, exitCode);
        Assert.Equal(before, BuiltProgram.Describe(DataFolder));
    }

    [Theory]
    [InlineData]
    [InlineData("serve")]
    [InlineData("service", "now")]
    public async Task Any_arguments_but_service_exit_with_Usage(params string[] arguments)
    {
        Assert.Equal(AgentExitCodes.Usage, await RunAsync(arguments));
    }

    private static async Task<int> RunAsync(params string[] arguments)
    {
        return (await BuiltProgram.RunAsync("Moltaro.OnecOutpost.Agent.exe", arguments)).ExitCode;
    }
}
