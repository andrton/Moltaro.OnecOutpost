using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Console;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.OneC.Tests.Agent;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.TestSupport;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Testing;

namespace Moltaro.OnecOutpost.OneC.Tests.Console;

// The console's check and metadata on a LabAgent (the S10b brief, "Lab tests", and "Internal of S10c"), through
// CommandAppTester with ConsoleApp.Configure, as Program runs it: the parts that Program.MachineParts makes for the
// agent's program folder, in the x64\ layout, and its data folder, with the account that runs the tests as the agent's
// account, AgentSid, as the agent's tests make their access lists for it, the agent's fake clock, and a LabServer of the
// agent's keys, so that the server's line reaches a stub in the process. check needs a console run as administrator,
// and its probe needs "Impersonate a client after authentication", which only an elevated token holds, so the lab's
// tests run elevated. Each run checks that the program folder can be removed at once and that the server saw no poll.
// metadata runs on the clock of the machine so the policy's run limit applies as in use, and the lab records the
// halvings it causes.
[SupportedOSPlatform("windows")]
internal static class LabConsole
{
    // The longest a run of check takes on the lab: a worker of each account of the base, each of which may take twenty
    // seconds to open on a Windows account that never used 1C (LabLimits), and the probe.
    public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(10);

    // The longest a run of metadata takes on the lab: the walk of a copy of a typical base, of thousands of objects with
    // their storage, outlasts check's deadline by far.
    public static readonly TimeSpan MetadataDeadline = TimeSpan.FromHours(2);

    private const string ServiceName = "MoltaroOnecOutpost";

    // The parts of the machine for the agent, its server the one given.
    public static ConsoleParts Parts(LabAgent agent, LabServer server)
    {
        ConsoleParts machine = Program.MachineParts(ServiceName, AppContext.BaseDirectory, agent.ProgramsPath, agent.DataPath);

        Assert.True(machine.Elevated, "check needs a console run as administrator: run the tests of the lab in an elevated terminal.");

        return machine with
        {
            AgentSid = AccessListEdits.TestAccount.Value,
            Clock = agent.Clock,
            Handlers = (_, _) => server,
        };
    }

    // Runs check on the agent to its end.
    public static async Task<LabCheck> CheckAsync(LabAgent agent)
    {
        LabServer server = new(agent.ServerKey, agent.AgentKey, agent.Agent, agent.Clock);

        // The connection of check owns its handler, the server, and disposes it.
        CommandAppTester tester = new(console: new TestConsole().Width(int.MaxValue));
        tester.Configure(config => ConsoleApp.Configure(config, Parts(agent, server)));

        CommandAppResult result = await tester
            .RunAsync(["check"], TestContext.Current.CancellationToken)
            .WaitAsync(Deadline, TestContext.Current.CancellationToken);

        agent.RemoveProgramsAfterCommand();
        Assert.Equal(0, server.Polls);

        return new LabCheck(result);
    }

    // Runs metadata of the agent's base to its end, its report the file given.
    public static async Task<LabMetadata> MetadataAsync(LabAgent agent, string file)
    {
        using LabServer server = new(agent.ServerKey, agent.AgentKey, agent.Agent, agent.Clock);
        ConsoleParts parts = Parts(agent, server) with { Clock = TimeProvider.System };

        CommandAppTester tester = new(console: new TestConsole().Width(int.MaxValue));
        tester.Configure(config => ConsoleApp.Configure(config, parts));

        CommandAppResult result = await tester
            .RunAsync(["metadata", agent.Key, file], TestContext.Current.CancellationToken)
            .WaitAsync(MetadataDeadline, TestContext.Current.CancellationToken);

        agent.RemoveProgramsAfterCommand();
        Assert.Equal(0, server.Polls);

        return new LabMetadata(result, file, named: agent.Key == LabTestBase.Key);
    }
}
